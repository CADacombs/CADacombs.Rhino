using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class PolyCurveSmoothingSolver
    {
        public static Curve Solve(
            PolyCurve polyCurve, 
            bool smoothG2,
            double angleTolDeg, 
            double curvatureMagTolPct, // NEW
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

                // Analyze current continuity
                var vA = ContinuityUtils.GetContinuityVectorsAt(segA, segA.Domain.T1, CurveEvaluationSide.Below);
                var vB = ContinuityUtils.GetContinuityVectorsAt(segB, segB.Domain.T0, CurveEvaluationSide.Above);
                int? gLevel = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvatureMagTolPct);
                int currentCont = gLevel ?? 0;

                NurbsCurve resA = null;
                NurbsCurve resB = null;
                bool jointResolved = false;

                // Local helper to DRY up the MatchBoth/MatchOne logic
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

                // ==========================================
                // WATERFALL LOGIC
                // ==========================================

                // 1. Attempt G2 if requested
                if (smoothG2)
                {
                    if (skipIfAlreadyAtContinuity && currentCont >= 2)
                    {
                        if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Already at G2.");
                        jointResolved = true;
                    }
                    else if (TryMatch(2, out resA, out resB))
                    {
                        if (debug) RhinoApp.WriteLine($"Joint {i}: G2 smoothing successful.");
                        jointResolved = true;
                    }
                }

                // 2. Fallback to G1
                if (!jointResolved)
                {
                    if (skipIfAlreadyAtContinuity && currentCont >= 1)
                    {
                        if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Already at G1.");
                        jointResolved = true;
                    }
                    else if (TryMatch(1, out resA, out resB))
                    {
                        if (debug) RhinoApp.WriteLine($"Joint {i}: G1 smoothing successful.");
                        jointResolved = true;
                    }
                    else
                    {
                        if (debug) RhinoApp.WriteLine($"Joint {i}: All smoothing attempts failed. Skipping.");
                    }
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