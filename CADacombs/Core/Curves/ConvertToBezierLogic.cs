using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace CADacombs.Core.Curves
{
    public static class ConvertToBezierLogic
    {
        public static List<int> ParseDegreesString(string input, int minDegree = 2)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<int>();
            
            return input.Where(c => char.IsDigit(c))
                        .Select(c => int.Parse(c.ToString()))
                        .Where(d => d >= minDegree)
                        .Distinct()
                        .ToList();
        }

        public static (NurbsCurve Bezier, double Deviation, string Log) TryConvert(
            Curve crvIn, List<int> targetDegrees, bool limitDev, double devTol,
            KnotRemovalUtils.PreserveEndType preserveEnds, bool convertToNonrational,
            bool includeLines, bool includeArcs, bool includeOtherConical,
            bool includeRational, bool includeBeziers, bool debug = false)
        {
            if (crvIn == null || targetDegrees == null || targetDegrees.Count == 0) 
                return (null, 0.0, "Input is null or no target degrees provided.");
            
            int minAllowedDeg = preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature ? 5 : 2;
            var activeTargets = targetDegrees.Where(d => d >= minAllowedDeg).ToList();
            if (activeTargets.Count == 0)
                return (null, 0.0, $"No target degrees >= {minAllowedDeg} available for {preserveEnds}.");

            StringBuilder log = new StringBuilder();
            Stopwatch sw = new Stopwatch();
            if (debug) sw.Start();

            double docTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            double activeDevTol = limitDev ? devTol : double.MaxValue;
            double distTol = Math.Min(0.1 * activeDevTol, 0.1 * docTol);

            NurbsCurve ncIn = null;

            // Mutually exclusive classification: once matched, either skip or proceed to conversion.
            if (crvIn is LineCurve || crvIn.IsLinear())
            {
                if (!includeLines) return (null, 0.0, "Skipped linear curve.");
            }
            else if (crvIn is ArcCurve || crvIn.IsArc())
            {
                if (!includeArcs) return (null, 0.0, "Skipped arc curve.");
            }
            else if (crvIn.IsEllipse() || IsParabolaOrHyperbola(crvIn))
            {
                if (!includeOtherConical) return (null, 0.0, "Skipped conical curve.");
            }
            else
            {
                ncIn = crvIn as NurbsCurve ?? crvIn.ToNurbsCurve();
                if (ncIn == null) return (null, 0.0, "Failed to convert to NurbsCurve.");

                if (ncIn.IsRational)
                {
                    if (!includeRational) return (null, 0.0, "Skipped rational curve.");
                }
                else if (ncIn.SpanCount == 1 && activeTargets.Contains(ncIn.Degree))
                {
                    if (!includeBeziers) return (null, 0.0, $"Already a degree {ncIn.Degree} Bezier.");
                }
            }

            if (ncIn == null)
            {
                ncIn = crvIn.ToNurbsCurve();
                if (ncIn == null) return (null, 0.0, "Failed to convert to NurbsCurve.");
            }

            int maxTarget = activeTargets.Max();
            bool isAlreadyValidBezier = ncIn.SpanCount == 1 && (!convertToNonrational || !ncIn.IsRational);
            bool canUseKnotRemoval = ncIn.SpanCount > 1 && (!convertToNonrational || !ncIn.IsRational);

            // 1. HOISTED KNOT REMOVAL: Only for multi-span curves that don't require rational-to-nonrational conversion.
            if (canUseKnotRemoval && ncIn.Degree >= minAllowedDeg && ncIn.Degree <= maxTarget)
            {
                long t0 = sw.ElapsedMilliseconds;
                var optResult = KnotRemovalUtils.OptimizeKnots(ncIn, activeDevTol, preserveEnds, debug);
                
                if (optResult.OptimizedCurve != null && optResult.OptimizedCurve.SpanCount == 1 && optResult.MaxDeviation <= activeDevTol)
                {
                    var successfulCurve = optResult.OptimizedCurve;
                    if (activeTargets.Contains(ncIn.Degree))
                    {
                        if (debug) log.AppendLine($"Hoisted knot removal succeeded in {sw.ElapsedMilliseconds - t0}ms. Dev: {optResult.MaxDeviation:E3}");
                        return (successfulCurve, optResult.MaxDeviation, log.ToString());
                    }
                    else
                    {
                        int nextAllowed = activeTargets.Where(d => d > ncIn.Degree).Min();
                        successfulCurve.IncreaseDegree(nextAllowed);
                        if (debug) log.AppendLine($"Hoisted knot removal + Elevation to {nextAllowed} succeeded in {sw.ElapsedMilliseconds - t0}ms. Dev: {optResult.MaxDeviation:E3}");
                        return (successfulCurve, optResult.MaxDeviation, log.ToString());
                    }
                }
            }

            // 2. Ascending Fallback Loop (minAllowedDeg to maxTarget)
            int maxLoopDeg = isAlreadyValidBezier ? Math.Min(maxTarget, ncIn.Degree - 1) : maxTarget;
            bool preserveTans = preserveEnds != KnotRemovalUtils.PreserveEndType.None;

            for (int deg = minAllowedDeg; deg <= maxLoopDeg; deg++)
            {
                // Skip only if Hoisted Knot Removal already attempted this exact degree
                if (canUseKnotRemoval && deg == ncIn.Degree) continue;

                NurbsCurve successfulCurve = null;
                double successfulDev = 0.0;
                string logMsg = "";

                // Cubic Bulge-Fit (only reached when minAllowedDeg <= 3, i.e., not Curvature)
                if (deg == 3)
                {
                    long t1 = sw.ElapsedMilliseconds;
                    var cubic = TryCubicBulgeFit(crvIn, distTol, out double cubicDev);
                    if (cubic != null && cubicDev <= activeDevTol)
                    {
                        successfulCurve = cubic;
                        successfulDev = cubicDev;
                        logMsg = $"Cubic Fit succeeded in {sw.ElapsedMilliseconds - t1}ms. Dev: {successfulDev:E3}";
                    }
                }

                // Native Rebuild Fallback (always produces a non-rational NurbsCurve)
                if (successfulCurve == null)
                {
                    long t2 = sw.ElapsedMilliseconds;
                    var rebuilt = ncIn.Rebuild(deg + 1, deg, preserveTans);
                    if (rebuilt != null && rebuilt.IsValid)
                    {
                        bool endCondPassed = true;
                        if (preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature)
                        {
                            bool startOk = rebuilt.SetEndCondition(false, NurbsCurve.NurbsCurveEndConditionType.Curvature,
                                rebuilt.PointAtStart, crvIn.TangentAtStart, crvIn.CurvatureAt(crvIn.Domain.T0));
                            bool endOk = rebuilt.SetEndCondition(true, NurbsCurve.NurbsCurveEndConditionType.Curvature,
                                rebuilt.PointAtEnd, crvIn.TangentAtEnd, crvIn.CurvatureAt(crvIn.Domain.T1));
                            endCondPassed = startOk && endOk;
                        }

                        if (endCondPassed && Curve.GetDistancesBetweenCurves(crvIn, rebuilt, distTol, out double dev, out _, out _, out _, out _, out _))
                        {
                            if (dev <= activeDevTol)
                            {
                                bool continuityPassed = true;
                                if (preserveTans)
                                {
                                    double angTol = 0.1 * RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
                                    if (Vector3d.VectorAngle(crvIn.TangentAtStart, rebuilt.TangentAtStart) > angTol ||
                                        Vector3d.VectorAngle(crvIn.TangentAtEnd, rebuilt.TangentAtEnd) > angTol)
                                    {
                                        continuityPassed = false;
                                    }
                                }

                                if (continuityPassed && preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature)
                                {
                                    double curvTol = Math.Max(0.1 * RhinoDoc.ActiveDoc.ModelRelativeTolerance, RhinoMath.ZeroTolerance);
                                    if (!CurvatureMatches(crvIn.CurvatureAt(crvIn.Domain.T0), rebuilt.CurvatureAt(rebuilt.Domain.T0), curvTol) ||
                                        !CurvatureMatches(crvIn.CurvatureAt(crvIn.Domain.T1), rebuilt.CurvatureAt(rebuilt.Domain.T1), curvTol))
                                    {
                                        continuityPassed = false;
                                    }
                                }

                                if (continuityPassed)
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
                    if (activeTargets.Contains(deg))
                    {
                        if (debug) log.AppendLine(logMsg);
                        return (successfulCurve, successfulDev, log.ToString());
                    }
                    else
                    {
                        int nextAllowed = activeTargets.Where(d => d > deg).Min();
                        // Prevent a no-op if elevating back to the input's own degree
                        if (isAlreadyValidBezier && nextAllowed == ncIn.Degree)
                            continue;

                        successfulCurve.IncreaseDegree(nextAllowed);
                        if (debug) log.AppendLine(logMsg + $" -> Elevated to Degree {nextAllowed}");
                        return (successfulCurve, successfulDev, log.ToString());
                    }
                }
            }

            // 3. Exact Degree Elevation for existing valid Beziers whose degree isn't in activeTargets
            if (isAlreadyValidBezier)
            {
                if (activeTargets.Contains(ncIn.Degree))
                {
                    if (debug) log.AppendLine($"Already a valid degree {ncIn.Degree} Bezier; no lower target degree met tolerance.");
                    return (null, 0.0, log.ToString());
                }
                else if (ncIn.Degree < maxTarget)
                {
                    int nextAllowed = activeTargets.Where(d => d > ncIn.Degree).Min();
                    var elevated = ncIn.DuplicateCurve() as NurbsCurve;
                    if (elevated != null && elevated.IncreaseDegree(nextAllowed))
                    {
                        if (debug) log.AppendLine($"Exact degree elevation from {ncIn.Degree} to {nextAllowed} succeeded. Dev: 0.000E+000");
                        return (elevated, 0.0, log.ToString());
                    }
                }
            }

            if (debug) { sw.Stop(); log.AppendLine($"Total process failed after {sw.ElapsedMilliseconds}ms."); }
            return (null, 0.0, log.ToString());
        }

        private static bool IsParabolaOrHyperbola(Curve crvIn)
        {
            // Stage A: Fast-path for single-span degree-2 NurbsCurve
            if (crvIn is NurbsCurve nc && nc.SpanCount == 1 && nc.Degree == 2 && nc.Points.Count == 3)
            {
                double w0 = nc.Points[0].Weight;
                double w1 = nc.Points[1].Weight;
                double w2 = nc.Points[2].Weight;

                if (w0 > RhinoMath.ZeroTolerance && w2 > RhinoMath.ZeroTolerance)
                {
                    double wNorm = w1 / Math.Sqrt(w0 * w2);
                    if (wNorm >= 1.0 - RhinoMath.ZeroTolerance)
                        return true;
                }
            }

            // Stage B: Shape-based fallback (handles degree-elevated or multi-span parabolas/hyperbolas)
            if (!crvIn.TryGetPlane(out Plane plane, RhinoMath.ZeroTolerance))
                return false;

            Point3d p0 = crvIn.PointAtStart;
            Point3d p2 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t2 = crvIn.TangentAtEnd;

            Line line0 = new Line(p0, p0 + t0);
            Line line2 = new Line(p2, p2 + t2);

            if (!Intersection.LineLine(line0, line2, out double a, out double b, RhinoMath.ZeroTolerance, false))
                return false;

            // Apex must lie forward along start tangent and backward along end tangent
            if (a <= RhinoMath.ZeroTolerance || b >= -RhinoMath.ZeroTolerance)
                return false;

            Point3d ptA = line0.PointAt(a);
            Point3d ptB = line2.PointAt(b);
            if (ptA.DistanceTo(ptB) > RhinoMath.ZeroTolerance)
                return false;

            Point3d p1 = 0.5 * (ptA + ptB);
            Point3d mid = crvIn.PointAt(crvIn.Domain.Mid);

            // Compute barycentric coordinates (u, v, w) of mid in triangle (p0, p1, p2) in the curve's plane
            plane.ClosestParameter(p0, out double x0, out double y0);
            plane.ClosestParameter(p1, out double x1, out double y1);
            plane.ClosestParameter(p2, out double x2, out double y2);
            plane.ClosestParameter(mid, out double xm, out double ym);

            double det = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Math.Abs(det) <= RhinoMath.ZeroTolerance)
                return false;

            double u = ((y1 - y2) * (xm - x2) + (x2 - x1) * (ym - y2)) / det;
            double v = ((y2 - y0) * (xm - x2) + (x0 - x2) * (ym - y2)) / det;
            double w = 1.0 - u - v;

            if (u <= RhinoMath.ZeroTolerance || v <= RhinoMath.ZeroTolerance || w <= RhinoMath.ZeroTolerance)
                return false;

            double w1Candidate = v / (2.0 * Math.Sqrt(u * w));
            if (w1Candidate < 1.0 - RhinoMath.ZeroTolerance)
                return false;

            var candidate = new NurbsCurve(3, true, 3, 3);
            candidate.Points.SetPoint(0, p0, 1.0);
            candidate.Points.SetPoint(1, p1, w1Candidate);
            candidate.Points.SetPoint(2, p2, 1.0);
            candidate.Knots.CreateUniformKnots(1.0);

            if (!candidate.IsValid)
                return false;

            if (Curve.GetDistancesBetweenCurves(crvIn, candidate, RhinoMath.ZeroTolerance, out double maxDev, out _, out _, out _, out _, out _))
            {
                return maxDev <= RhinoMath.ZeroTolerance;
            }

            return false;
        }

        private static NurbsCurve TryCubicBulgeFit(Curve crvIn, double distTol, out double minDev)
        {
            var symCurve = FindSymmetrical(crvIn, distTol, out double symDev);
            var asymCurve = FindNonSymmetrical(crvIn, distTol, out double asymDev);

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

        private static NurbsCurve FindSymmetrical(Curve crvIn, double distTol, out double minDev)
        {
            minDev = double.MaxValue;
            
            Point3d p0 = crvIn.PointAtStart;
            Point3d p3 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t1 = crvIn.TangentAtEnd;
            
            double fBulgeUnitDist = p0.DistanceTo(p3);
            if (fBulgeUnitDist < RhinoMath.ZeroTolerance) return null;

            double fmin = 0.0, fmax = 1.0;
            double fDivs = 0.1, res_WIP = 0.1;

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
                    if (Curve.GetDistancesBetweenCurves(crvIn, nc, distTol, out double dev, out _, out _, out _, out _, out _))
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

        private static NurbsCurve FindNonSymmetrical(Curve crvIn, double distTol, out double minDev)
        {
            minDev = double.MaxValue;

            Point3d p0 = crvIn.PointAtStart;
            Point3d p3 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t1 = crvIn.TangentAtEnd;
            
            double fBulgeUnitDist = p0.DistanceTo(p3);
            if (fBulgeUnitDist < RhinoMath.ZeroTolerance) return null;

            double fmin_mA = 0.0, fmax_mA = 1.0;
            double fmin_mB = 0.0, fmax_mB = 1.0;
            double fDivs = 0.1, res_WIP = 0.1;

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
                        if (Curve.GetDistancesBetweenCurves(crvIn, nc, distTol, out double dev, out _, out _, out _, out _, out _))
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

        private static bool CurvatureMatches(Vector3d kIn, Vector3d kOut, double relTol)
        {
            double maxLen = Math.Max(kIn.Length, kOut.Length);
            if (maxLen <= RhinoMath.ZeroTolerance)
                return true;

            return (kIn - kOut).Length / maxLen <= relTol;
        }
    }
}