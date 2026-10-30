using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class PolyCurveG1Solver
    {
        public static Curve Solve(
            PolyCurve polyCurve, 
            double angleTolDeg, 
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

                if (skipIfAlreadyAtContinuity)
                {
                    var vA = ContinuityUtils.GetContinuityVectorsAt(segA, segA.Domain.T1, CurveEvaluationSide.Below);
                    var vB = ContinuityUtils.GetContinuityVectorsAt(segB, segB.Domain.T0, CurveEvaluationSide.Above);
                    
                    int? gLevel = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, 5.0);
                    if (gLevel.HasValue && gLevel.Value >= 1)
                    {
                        if (debug) RhinoApp.WriteLine($"Skipping Joint {i}: Already at G1.");
                        newSegs.Add(segA);
                        segA = segB;
                        continue;
                    }
                }

                NurbsCurve resA = null;
                NurbsCurve resB = null;

                // Route to the correct mathematical solver based on lock status
                if (modA && modB)
                {
                    var res = CurveMatchMath.MatchBothCurves(
                        segA, segB, true, false, 1, 2, false, false, 2, currentDevTol, 
                        false, false, 2, posTol, angleTolDeg, 5.0, debug);
                    resA = res.ResultA;
                    resB = res.ResultB;
                }
                else if (modA && !modB)
                {
                    var res = CurveMatchMath.MatchOneCurve(
                        segA, segB, true, false, 1, 2, false, false, 2, currentDevTol, 
                        false, false, 2, posTol, angleTolDeg, 5.0, debug);
                    resA = res.Result;
                }
                else if (!modA && modB)
                {
                    var res = CurveMatchMath.MatchOneCurve(
                        segB, segA, false, true, 1, 2, false, false, 2, currentDevTol, 
                        false, false, 2, posTol, angleTolDeg, 5.0, debug);
                    resB = res.Result;
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

            return polyCurve; // Fallback if join fails
        }

        private static bool IsCurveOkToMod(Curve seg)
        {
            if (seg is LineCurve || seg is ArcCurve || seg.IsLinear(1e-9)) return false;

            NurbsCurve nc = seg as NurbsCurve ?? seg.ToNurbsCurve();
            if (nc == null) return false;

            if (nc.IsLinear(1e-9)) return false;

            // Check for fully-multiple internal knots
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