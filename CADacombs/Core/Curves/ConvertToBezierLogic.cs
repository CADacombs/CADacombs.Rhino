using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class ConvertToBezierLogic
    {
        public static List<int> ParseDegreesString(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<int>();
            
            return input.Where(c => char.IsDigit(c) && c != '0')
                        .Select(c => int.Parse(c.ToString()))
                        .Distinct()
                        .ToList();
        }

        public static (NurbsCurve Bezier, double Deviation, string Log) TryConvert(
            Curve crvIn, List<int> targetDegrees, bool limitDev, double devTol,
            bool preserveTangents, bool skipBeziers, bool skipLines, 
            bool skipArcs, bool skipOtherConical, bool debug = false)
        {
            if (crvIn == null || targetDegrees == null || targetDegrees.Count == 0) 
                return (null, 0.0, "Input is null or no target degrees provided.");
            
            StringBuilder log = new StringBuilder();
            Stopwatch sw = new Stopwatch();
            if (debug) sw.Start();

            double docTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            double activeDevTol = limitDev ? devTol : double.MaxValue;

            if (skipLines && crvIn.IsLinear(docTol)) return (null, 0.0, "Skipped linear curve.");
            if (skipArcs && crvIn.IsArc(docTol)) return (null, 0.0, "Skipped arc curve.");

            var ncIn = crvIn.ToNurbsCurve();
            if (ncIn == null) return (null, 0.0, "Failed to convert to NurbsCurve.");

            if (skipOtherConical && ncIn.IsRational) return (null, 0.0, "Skipped rational curve.");

            if (skipBeziers && ncIn.SpanCount == 1 && !ncIn.IsRational)
            {
                if (targetDegrees.Contains(ncIn.Degree))
                    return (null, 0.0, $"Already a degree {ncIn.Degree} Bezier.");
            }

            int maxTarget = targetDegrees.Max();

            // 1. HOISTED KNOT REMOVAL: Do this BEFORE any brute-force fitting.
            if (ncIn.Degree <= maxTarget)
            {
                long t0 = sw.ElapsedMilliseconds;
                var preserve = preserveTangents ? KnotRemovalUtils.PreserveEndType.Tangency : KnotRemovalUtils.PreserveEndType.None;
                var optResult = KnotRemovalUtils.OptimizeKnots(ncIn, activeDevTol, preserve, debug);
                
                if (optResult.OptimizedCurve != null && optResult.OptimizedCurve.SpanCount == 1 && optResult.MaxDeviation <= activeDevTol)
                {
                    var successfulCurve = optResult.OptimizedCurve;
                    if (targetDegrees.Contains(ncIn.Degree))
                    {
                        if (debug) log.AppendLine($"Hoisted knot removal succeeded in {sw.ElapsedMilliseconds - t0}ms. Dev: {optResult.MaxDeviation:E3}");
                        return (successfulCurve, optResult.MaxDeviation, log.ToString());
                    }
                    else
                    {
                        int nextAllowed = targetDegrees.Where(d => d > ncIn.Degree).Min();
                        successfulCurve.IncreaseDegree(nextAllowed);
                        if (debug) log.AppendLine($"Hoisted knot removal + Elevation to {nextAllowed} succeeded in {sw.ElapsedMilliseconds - t0}ms. Dev: {optResult.MaxDeviation:E3}");
                        return (successfulCurve, optResult.MaxDeviation, log.ToString());
                    }
                }
            }

            // 2. Ascending Fallback Loop (Degree 2 to maxTarget)
            for (int deg = 2; deg <= maxTarget; deg++)
            {
                if (deg == ncIn.Degree) continue; // Already tried Knot Removal above

                NurbsCurve successfulCurve = null;
                double successfulDev = 0.0;
                string logMsg = "";

                // Cubic Bulge-Fit
                if (deg == 3)
                {
                    long t1 = sw.ElapsedMilliseconds;
                    var cubic = TryCubicBulgeFit(crvIn, activeDevTol, out double cubicDev);
                    if (cubic != null && cubicDev <= activeDevTol)
                    {
                        successfulCurve = cubic;
                        successfulDev = cubicDev;
                        logMsg = $"Cubic Fit succeeded in {sw.ElapsedMilliseconds - t1}ms. Dev: {successfulDev:E3}";
                    }
                }

                // Native Rebuild Fallback
                if (successfulCurve == null)
                {
                    long t2 = sw.ElapsedMilliseconds;
                    var rebuilt = ncIn.Rebuild(deg + 1, deg, preserveTangents);
                    if (rebuilt != null && rebuilt.IsValid)
                    {
                        if (Curve.GetDistancesBetweenCurves(crvIn, rebuilt, 0.1 * docTol, out double dev, out _, out _, out _, out _, out _))
                        {
                            if (dev <= activeDevTol)
                            {
                                bool tangencyPassed = true;
                                if (preserveTangents)
                                {
                                    double angTol = RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
                                    if (Vector3d.VectorAngle(crvIn.TangentAtStart, rebuilt.TangentAtStart) > angTol ||
                                        Vector3d.VectorAngle(crvIn.TangentAtEnd, rebuilt.TangentAtEnd) > angTol)
                                    {
                                        tangencyPassed = false;
                                    }
                                }

                                if (tangencyPassed)
                                {
                                    successfulCurve = rebuilt;
                                    successfulDev = dev;
                                    logMsg = $"Rebuild deg {deg} succeeded in {sw.ElapsedMilliseconds - t2}ms. Dev: {successfulDev:E3}";
                                }
                            }
                        }
                    }
                }

                if (successfulCurve != null)
                {
                    if (targetDegrees.Contains(deg))
                    {
                        if (debug) log.AppendLine(logMsg);
                        return (successfulCurve, successfulDev, log.ToString());
                    }
                    else
                    {
                        int nextAllowed = targetDegrees.Where(d => d > deg).Min();
                        successfulCurve.IncreaseDegree(nextAllowed);
                        if (debug) log.AppendLine(logMsg + $" -> Elevated to Degree {nextAllowed}");
                        return (successfulCurve, successfulDev, log.ToString());
                    }
                }
            }

            if (debug) { sw.Stop(); log.AppendLine($"Total process failed after {sw.ElapsedMilliseconds}ms."); }
            return (null, 0.0, log.ToString());
        }

        private static NurbsCurve TryCubicBulgeFit(Curve crvIn, double devTol, out double minDev)
        {
            var symCurve = FindSymmetrical(crvIn, out double symDev);
            var asymCurve = FindNonSymmetrical(crvIn, out double asymDev);

            if (symCurve == null && asymCurve == null) 
            { 
                minDev = double.MaxValue; 
                return null; 
            }

            if (symCurve == null) { minDev = asymDev; return asymCurve; }
            if (asymCurve == null) { minDev = symDev; return symCurve; }

            if (symDev <= asymDev) { minDev = symDev; return symCurve; }
            
            minDev = asymDev; 
            return asymCurve;
        }

        private static NurbsCurve FindSymmetrical(Curve crvIn, out double minDev)
        {
            minDev = double.MaxValue;
            
            Point3d p0 = crvIn.PointAtStart;
            Point3d p3 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t1 = crvIn.TangentAtEnd;
            
            double fBulgeUnitDist = p0.DistanceTo(p3);
            if (fBulgeUnitDist < 1e-12) return null;

            double fmin = 0.0, fmax = 1.0;
            double fDivs = 0.1, res_WIP = 0.1;
            double docTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            while (true)
            {
                List<NurbsCurve> ncs_Res = new List<NurbsCurve>();
                List<double> mABs = new List<double>();
                List<double> devs = new List<double>();

                int steps = (int)(1.0 / fDivs) + 1;
                for (int i = 0; i < steps; i++)
                {
                    double mAB = fmin * (1.0 - i * fDivs) + fmax * (i * fDivs);
                    if (mAB == 0.0) continue;

                    var nc = BuildCubic(p0, p3, t0, t1, mAB, mAB, fBulgeUnitDist);
                    if (Curve.GetDistancesBetweenCurves(crvIn, nc, docTol, out double dev, out _, out _, out _, out _, out _))
                    {
                        ncs_Res.Add(nc);
                        mABs.Add(mAB);
                        devs.Add(dev);
                    }
                }

                if (ncs_Res.Count == 0)
                {
                    fmin *= 10.0; fmax *= 10.0;
                    if (fmax > 1000) break; 
                    continue;
                }

                double currentMinDev = devs.Min();
                int idx_Winner = devs.IndexOf(currentMinDev);

                if (res_WIP < 1.1e-6)
                {
                    minDev = currentMinDev;
                    return ncs_Res[idx_Winner];
                }

                fmin = mABs[idx_Winner] - res_WIP;
                fmax = mABs[idx_Winner] + res_WIP;

                res_WIP *= 0.1;
                fDivs = 0.05;
            }
            return null;
        }

        private static NurbsCurve FindNonSymmetrical(Curve crvIn, out double minDev)
        {
            minDev = double.MaxValue;

            Point3d p0 = crvIn.PointAtStart;
            Point3d p3 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t1 = crvIn.TangentAtEnd;
            
            double fBulgeUnitDist = p0.DistanceTo(p3);
            if (fBulgeUnitDist < 1e-12) return null;

            double fmin_mA = 0.0, fmax_mA = 1.0;
            double fmin_mB = 0.0, fmax_mB = 1.0;
            double fDivs = 0.1, res_WIP = 0.1;
            double docTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            double? min_devs_Prev = null;

            while (true)
            {
                List<NurbsCurve> ncs_Res = new List<NurbsCurve>();
                List<double> mAs = new List<double>();
                List<double> mBs = new List<double>();
                List<double> devs = new List<double>();

                int steps = (int)(1.0 / fDivs) + 1;
                for (int iA = 0; iA < steps; iA++)
                {
                    double mA = fmin_mA * (1.0 - iA * fDivs) + fmax_mA * (iA * fDivs);
                    if (mA == 0.0) continue;

                    for (int iB = 0; iB < steps; iB++)
                    {
                        double mB = fmin_mB * (1.0 - iB * fDivs) + fmax_mB * (iB * fDivs);
                        if (mB == 0.0) continue;

                        var nc = BuildCubic(p0, p3, t0, t1, mA, mB, fBulgeUnitDist);
                        if (Curve.GetDistancesBetweenCurves(crvIn, nc, docTol, out double dev, out _, out _, out _, out _, out _))
                        {
                            ncs_Res.Add(nc);
                            mAs.Add(mA);
                            mBs.Add(mB);
                            devs.Add(dev);
                        }
                    }
                }

                if (ncs_Res.Count == 0)
                {
                    fmin_mA *= 10.0; fmax_mA *= 10.0;
                    fmin_mB *= 10.0; fmax_mB *= 10.0;
                    if (fmax_mA > 1000) break; 
                    continue;
                }

                double currentMinDev = devs.Min();
                int idx_Winner = devs.IndexOf(currentMinDev);

                if (min_devs_Prev.HasValue && Math.Abs(min_devs_Prev.Value - currentMinDev) < 1e-6)
                {
                    minDev = currentMinDev;
                    return ncs_Res[idx_Winner];
                }
                min_devs_Prev = currentMinDev;

                if (res_WIP < 0.00101 && Math.Abs(mAs[idx_Winner] - mBs[idx_Winner]) <= 0.001)
                {
                    return null;
                }

                fmin_mA = mAs[idx_Winner] - res_WIP;
                fmax_mA = mAs[idx_Winner] + res_WIP;
                fmin_mB = mBs[idx_Winner] - res_WIP;
                fmax_mB = mBs[idx_Winner] + res_WIP;

                res_WIP *= 0.1;
                fDivs = 0.05;
            }
            return null;
        }

        private static NurbsCurve BuildCubic(Point3d p0, Point3d p3, Vector3d t0, Vector3d t1, double mA, double mB, double dist)
        {
            return NurbsCurve.CreateControlPointCurve(new[] { 
                p0, 
                p0 + (mA * dist * t0), 
                p3 - (mB * dist * t1), 
                p3 
            }, 3) as NurbsCurve;
        }
    }
}