using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class PolyCurveSmoothingSolver
    {
        // 1.82501207499443e-08 radians converted to degrees. 
        // Derived from Math.Acos(1.0 - Math.Pow(2, -52)) where 2^-52 is double-precision machine epsilon.
        // At this angle, the dot product of two vectors evaluates mathematically to 1.0.
        private const double PerfectG1TolDeg = 1.0456565158694086e-06;

        public static Curve Solve(
            PolyCurve polyCurve, 
            int continuityMode, // 0 = G1, 1 = G2WithG1Fallback, 2 = G2Only
            double angleTolDeg, 
            double curvatureMagTolPct, 
            double devTol, 
            bool skipIfAlreadyAtContinuity, 
            bool limitCrvDev, 
            bool debug = false)
        {
            if (polyCurve == null) return null;
            if (polyCurve.SegmentCount < 2 && !polyCurve.IsClosed) return polyCurve;

            Curve[] origSegs = polyCurve.DuplicateSegments();
            if (origSegs == null || origSegs.Length == 0) return polyCurve;

            List<Curve> newSegs = new List<Curve>();
            Curve segA = origSegs[0];
            bool someSegsReplaced = false;

            double currentDevTol = limitCrvDev ? devTol : double.MaxValue;
            double posTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            
            bool targetG2 = continuityMode >= 1;
            bool allowG1Fallback = continuityMode <= 1;

            for (int i = 1; i < (polyCurve.IsClosed ? origSegs.Length + 1 : origSegs.Length); i++)
            {
                int segBIndex = i == origSegs.Length ? 0 : i;
                Curve segB = (i == origSegs.Length) ? newSegs[0] : origSegs[segBIndex];

                bool modA = IsCurveOkToMod(segA);
                bool modB = IsCurveOkToMod(segB);

                if (!modA && !modB)
                {
                    if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Neither segment can be modified.");
                    newSegs.Add(segA);
                    segA = segB;
                    continue;
                }

                var vA = ContinuityUtils.GetContinuityVectorsAt(segA, segA.Domain.T1, CurveEvaluationSide.Below);
                var vB = ContinuityUtils.GetContinuityVectorsAt(segB, segB.Domain.T0, CurveEvaluationSide.Above);
                
                // 1. Check against the user's upper limit to protect intentional kinks
                int? userLevel = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvatureMagTolPct);
                if (userLevel == null || userLevel.Value < 1)
                {
                    if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Intentional kink or gap exceeds user AngleTol ({angleTolDeg}°).");
                    newSegs.Add(segA);
                    segA = segB;
                    continue;
                }

                // 2. Check against machine epsilon for true geometric perfection
                int? perfectLevel = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, PerfectG1TolDeg, PerfectG1TolDeg, curvatureMagTolPct);
                int currentCont = perfectLevel ?? 0;

                NurbsCurve resA = null;
                NurbsCurve resB = null;
                bool jointResolved = false;

                bool TryMatch(int targetContinuity, out NurbsCurve rA, out NurbsCurve rB)
                {
                    rA = null; rB = null;
                    if (modA && modB)
                    {
                        var res = CurveMatchMath.MatchBothCurves(segA, segB, true, false, targetContinuity, 2, false, false, 2, currentDevTol, false, false, 2, posTol, angleTolDeg, curvatureMagTolPct, debug);
                        rA = res.ResultA; rB = res.ResultB;
                    }
                    else if (modA && !modB)
                    {
                        var res = CurveMatchMath.MatchOneCurve(segA, segB, true, false, targetContinuity, 2, false, false, 2, currentDevTol, false, false, 2, posTol, angleTolDeg, curvatureMagTolPct, debug);
                        rA = res.Result;
                    }
                    else if (!modA && modB)
                    {
                        var res = CurveMatchMath.MatchOneCurve(segB, segA, false, true, targetContinuity, 2, false, false, 2, currentDevTol, false, false, 2, posTol, angleTolDeg, curvatureMagTolPct, debug);
                        rB = res.Result;
                    }
                    return (rA != null || rB != null);
                }

                if (targetG2)
                {
                    if (skipIfAlreadyAtContinuity && currentCont >= 2)
                    {
                        if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Already at perfect G2.");
                        jointResolved = true;
                    }
                    else if (TryMatch(2, out resA, out resB))
                    {
                        if (debug) RhinoApp.WriteLine($"Joint {i}: G2 smoothing successful.");
                        jointResolved = true;
                    }
                }

                if (!jointResolved && allowG1Fallback)
                {
                    if (skipIfAlreadyAtContinuity && currentCont >= 1)
                    {
                        if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Already at perfect G1.");
                        jointResolved = true;
                    }
                    else if (TryMatch(1, out resA, out resB))
                    {
                        if (debug) RhinoApp.WriteLine($"Joint {i}: G1 smoothing successful.");
                        jointResolved = true;
                    }
                }

                if (!jointResolved && debug)
                {
                    RhinoApp.WriteLine($"Joint {i}: All smoothing attempts failed. Skipping.");
                }

                if (resA != null || resB != null) someSegsReplaced = true;

                newSegs.Add(resA ?? segA);
                segA = resB ?? segB;
            }

            if (polyCurve.IsClosed) newSegs[0] = segA;
            else newSegs.Add(segA);

            if (!someSegsReplaced) return polyCurve;

            var joined = Curve.JoinCurves(newSegs, posTol * 2.1);
            if (joined != null && joined.Length == 1) return joined[0];

            return polyCurve; 
        }

        private static bool IsCurveOkToMod(Curve seg)
        {
            if (seg is LineCurve || seg is ArcCurve || seg.IsLinear(1e-9)) return false;

            NurbsCurve nc = seg as NurbsCurve ?? seg.ToNurbsCurve();
            if (nc == null || nc.IsLinear(1e-9)) return false;

            if (nc.SpanCount > 1)
            {
                int iK = nc.Degree;
                while (iK < nc.Knots.Count - nc.Degree)
                {
                    int m = nc.Knots.KnotMultiplicity(iK);
                    if (m == nc.Degree) return false; 
                    iK += m;
                }
            }
            return true;
        }
    }
}