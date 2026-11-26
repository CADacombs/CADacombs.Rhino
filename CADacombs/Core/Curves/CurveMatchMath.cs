using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
        private const double MaxWorkingScaleMultiple = 50.0;

        // ==============================================================================
        // BACKWARD COMPATIBILITY OVERLOADS
        // ==============================================================================

        public static (Curve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod, Curve crvRef, bool modT1, bool refT1,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchOneCurve(crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                                 deformLines ? 2 : 0, 0, 2, devTol, false, false, 2,
                                 RhinoMath.ZeroTolerance, MatchCrvOptions.DefaultAngleTolDeg, MatchCrvOptions.DefaultCurvatureMagTolPct, debug);
        }

        public static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA, Curve crvB, bool modT1_A, bool modT1_B,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchBothCurves(crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                                   deformLines ? 2 : 0, 0, 2, devTol, false, false, 2,
                                   RhinoMath.ZeroTolerance, MatchCrvOptions.DefaultAngleTolDeg, MatchCrvOptions.DefaultCurvatureMagTolPct, debug);
        }

        // ==============================================================================
        // PUBLIC ROUTING ENTRY POINTS
        // ==============================================================================

        public static (Curve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod,
            Curve crvRef,
            bool modT1,
            bool refT1,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug = false)
        {
            if (crvToMod == null || crvRef == null)
                return (null, "Input curves cannot be null.");

            if (crvToMod.IsClosed || crvRef.IsClosed)
                return (null, "Result cannot be created because closed curves are not supported.");

            if (IsCompoundCurve(crvToMod) || IsCompoundCurve(crvRef))
            {
                return MatchCompoundOneCurve(
                    crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                    lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                    preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
            }

            return MatchSingleSegmentOneCurve(
                crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
        }

        public static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA,
            Curve crvB,
            bool modT1_A,
            bool modT1_B,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug = false)
        {
            if (crvA == null || crvB == null)
                return (null, null, "Input curves cannot be null.");

            if (crvA.IsClosed || crvB.IsClosed)
                return (null, null, "Result cannot be created because closed curves are not supported.");

            if (IsCompoundCurve(crvA) || IsCompoundCurve(crvB))
            {
                return MatchCompoundBothCurves(
                    crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                    lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                    preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
            }

            return MatchSingleSegmentBothCurves(
                crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
        }

        // ==============================================================================
        // SINGLE-SEGMENT DISPATCH (NON-COMPOUND OR UNWRAPPED END SEGMENTS)
        // ==============================================================================

        private static (Curve Result, string ErrorMsg) MatchSingleSegmentOneCurve(
            Curve crvToMod,
            Curve crvRef,
            bool modT1,
            bool refT1,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug)
        {
            if (TryGetPrimitiveArc(crvToMod, arcMode, out Arc origArc))
            {
                if (arcMode == 0)
                    return (null, "Result cannot be created because the curve is an arc and Arcs is set to Lock.");
                if (arcMode == 1)
                    return SolveAnalyticalArcToFixedTarget(crvToMod, origArc, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }
            else if (TryGetPrimitiveLine(crvToMod, lineMode, out Line origLine))
            {
                if (lineMode == 0)
                    return (null, "Result cannot be created because the curve is a line and Lines is set to Lock.");
                if (lineMode == 1)
                    return SolveAnalyticalLineToFixedTarget(crvToMod, origLine, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            return SolveNurbsOneCurve(crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                                      lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                                      preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchSingleSegmentBothCurves(
            Curve crvA,
            Curve crvB,
            bool modT1_A,
            bool modT1_B,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug)
        {
            bool isArcA = TryGetPrimitiveArc(crvA, arcMode, out Arc arcA);
            bool isArcB = TryGetPrimitiveArc(crvB, arcMode, out Arc arcB);
            Line lineA = Line.Unset;
            Line lineB = Line.Unset;
            bool isLineA = !isArcA && TryGetPrimitiveLine(crvA, lineMode, out lineA);
            bool isLineB = !isArcB && TryGetPrimitiveLine(crvB, lineMode, out lineB);

            if ((isArcA || isArcB) && arcMode == 0)
                return (null, null, "Result cannot be created because a curve is an arc and Arcs is set to Lock.");
            if ((isLineA || isLineB) && lineMode == 0)
                return (null, null, "Result cannot be created because a curve is a line and Lines is set to Lock.");

            bool keepPrimA = (isArcA && arcMode == 1) || (isLineA && lineMode == 1);
            bool keepPrimB = (isArcB && arcMode == 1) || (isLineB && lineMode == 1);

            if (keepPrimA && keepPrimB)
            {
                if (isLineA && isLineB)
                    return SolveAnalyticalBothLines(crvA, lineA, modT1_A, crvB, lineB, modT1_B, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);

                if (isArcA && isLineB)
                {
                    var res = SolveAnalyticalArcAndLine(crvA, arcA, modT1_A, crvB, lineB, modT1_B, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);
                    return (res.ResultArc, res.ResultLine, res.ErrorMsg);
                }

                if (isLineA && isArcB)
                {
                    var res = SolveAnalyticalArcAndLine(crvB, arcB, modT1_B, crvA, lineA, modT1_A, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);
                    return (res.ResultLine, res.ResultArc, res.ErrorMsg);
                }

                if (isArcA && isArcB)
                    return SolveAnalyticalBothArcs(crvA, arcA, modT1_A, crvB, arcB, modT1_B, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            if (keepPrimA && !keepPrimB)
            {
                return SolveHybridPrimitiveAndNurbs(
                    crvA, isArcA, arcA, lineA, modT1_A,
                    crvB, modT1_B,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            if (!keepPrimA && keepPrimB)
            {
                var res = SolveHybridPrimitiveAndNurbs(
                    crvB, isArcB, arcB, lineB, modT1_B,
                    crvA, modT1_A,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
                return (res.ResultNurbs, res.ResultPrim, res.ErrorMsg);
            }

            return SolveNurbsBothCurves(crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                                        lineMode, arcMode, incrAsNeeded, devTol, preventExtraInflections,
                                        preserveInternalContinuity, maxInternalContinuity, posTol, angleTolDeg, curvTolPct, debug);
        }

        // ==============================================================================
        // BIDIRECTIONAL DEVIATION, WORKING VOLUME & CONTINUITY CHECKS
        // ==============================================================================

        public static double GetMaximumDeviation(Curve a, Curve b)
        {
            if (a == null || b == null) return double.MaxValue;

            double maxDist = 0.0;
            double docTol = RhinoDoc.ActiveDoc != null ? 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 1e-4;
            if (docTol <= RhinoMath.ZeroTolerance) docTol = RhinoMath.SqrtEpsilon;

            if (Curve.GetDistancesBetweenCurves(a, b, docTol, out double overlapMax, out _, out _, out _, out _, out _))
            {
                maxDist = overlapMax;
            }

            maxDist = Math.Max(maxDist, SampleOneWayMaxDeviation(a, b, 64));
            maxDist = Math.Max(maxDist, SampleOneWayMaxDeviation(b, a, 64));
            return maxDist;
        }

        public static double GetAverageDeviation(Curve original, Curve modified, int sampleCount = 50)
        {
            if (original == null || modified == null) return double.MaxValue;

            double avgOrigToMod = SampleOneWayAverageDeviation(original, modified, sampleCount);
            double avgModToOrig = SampleOneWayAverageDeviation(modified, original, sampleCount);
            if (avgOrigToMod == double.MaxValue || avgModToOrig == double.MaxValue)
                return double.MaxValue;

            return 0.5 * (avgOrigToMod + avgModToOrig);
        }

        /// <summary>
        /// Evaluates bidirectional average deviation over the modified end segment only when matching
        /// compound curves (PolylineCurve / PolyCurve), or over the full curve for single-segment curves.
        /// </summary>
        public static double GetEndSegmentAverageDeviation(Curve original, Curve modified, bool workIsT1, int sampleCount = 50)
        {
            if (original == null || modified == null) return double.MaxValue;

            if (!IsCompoundCurve(original) && !IsCompoundCurve(modified))
                return GetAverageDeviation(original, modified, sampleCount);

            CompoundEnvelope envOrig = null;
            CompoundEnvelope envMod = null;
            try
            {
                bool splitOrig = TrySplitCompoundCurve(original, workIsT1, out envOrig);
                bool splitMod = TrySplitCompoundCurve(modified, workIsT1, out envMod);

                bool hasJointOrig = splitOrig && envOrig.HasAnyInternalJoint;
                bool hasJointMod = splitMod && envMod.HasAnyInternalJoint;

                // Safety fallback if one curve is multi-segment and the other was collapsed externally
                if (hasJointOrig != hasJointMod)
                    return GetAverageDeviation(original, modified, sampleCount);

                Curve segOrig = splitOrig ? envOrig.LeafEndSegment : original;
                Curve segMod = splitMod ? envMod.LeafEndSegment : modified;
                return GetAverageDeviation(segOrig, segMod, sampleCount);
            }
            finally
            {
                envOrig?.Dispose();
                envMod?.Dispose();
            }
        }

        private static double SampleOneWayMaxDeviation(Curve from, Curve to, int sampleCount)
        {
            double maxD = 0.0;
            var ts = from.DivideByCount(sampleCount, true);
            if (ts != null && ts.Length > 0)
            {
                foreach (double t in ts)
                {
                    Point3d pt = from.PointAt(t);
                    if (to.ClosestPoint(pt, out double tTo))
                        maxD = Math.Max(maxD, pt.DistanceTo(to.PointAt(tTo)));
                }
                return maxD;
            }

            if (to.ClosestPoint(from.PointAtStart, out double tS))
                maxD = Math.Max(maxD, from.PointAtStart.DistanceTo(to.PointAt(tS)));
            if (to.ClosestPoint(from.PointAtEnd, out double tE))
                maxD = Math.Max(maxD, from.PointAtEnd.DistanceTo(to.PointAt(tE)));
            return maxD;
        }

        private static double SampleOneWayAverageDeviation(Curve from, Curve to, int sampleCount)
        {
            var ts = from.DivideByCount(sampleCount, true);
            if (ts == null || ts.Length == 0)
            {
                if (!to.ClosestPoint(from.PointAtStart, out double tS) || !to.ClosestPoint(from.PointAtEnd, out double tE))
                    return double.MaxValue;
                return 0.5 * (from.PointAtStart.DistanceTo(to.PointAt(tS)) + from.PointAtEnd.DistanceTo(to.PointAt(tE)));
            }

            double totalDist = 0.0;
            foreach (double t in ts)
            {
                Point3d pt = from.PointAt(t);
                if (!to.ClosestPoint(pt, out double tTo)) return double.MaxValue;
                totalDist += pt.DistanceTo(to.PointAt(tTo));
            }
            return totalDist / ts.Length;
        }

        private static BoundingBox GetReferenceBounds(Curve a, Curve b)
        {
            BoundingBox box = a.GetBoundingBox(false);
            box.Union(b.GetBoundingBox(false));
            return box;
        }

        private static bool IsWithinWorkingScale(BoundingBox refBounds, Curve candidate)
        {
            if (candidate == null || !candidate.IsValid) return false;
            BoundingBox candBox = candidate.GetBoundingBox(false);
            if (!candBox.IsValid) return false;

            double diag = refBounds.Diagonal.Length;
            if (diag <= RhinoMath.ZeroTolerance) diag = 1.0;
            double maxExcursion = MaxWorkingScaleMultiple * diag;

            return refBounds.ClosestPoint(candBox.Min).DistanceTo(candBox.Min) <= maxExcursion &&
                   refBounds.ClosestPoint(candBox.Max).DistanceTo(candBox.Max) <= maxExcursion;
        }

        private static bool IsMeaningfullyChanged(Curve orig, Curve cand, double maxDev)
        {
            if (maxDev > RhinoMath.ZeroTolerance) return true;
            if (orig.PointAtStart.DistanceTo(cand.PointAtStart) > RhinoMath.ZeroTolerance) return true;
            if (orig.PointAtEnd.DistanceTo(cand.PointAtEnd) > RhinoMath.ZeroTolerance) return true;
            if ((cand is LineCurve && !(orig is LineCurve)) || (cand is ArcCurve && !(orig is ArcCurve))) return true;
            return false;
        }

        public static bool AreCurvesAtContinuity(Curve cA, bool bT1WorkEndA, Curve cB, bool bT1WorkEndB, int targetContinuity, double posTol, double angleTolDeg, double curvatureMagTolPct)
        {
            double tA = bT1WorkEndA ? cA.Domain.T1 : cA.Domain.T0;
            double tB = bT1WorkEndB ? cB.Domain.T1 : cB.Domain.T0;

            CurveEvaluationSide sideA = bT1WorkEndA ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;
            CurveEvaluationSide sideB = bT1WorkEndB ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            var vA = ContinuityUtils.GetContinuityVectorsAt(cA, tA, sideA);
            var vB = ContinuityUtils.GetContinuityVectorsAt(cB, tB, sideB);

            if (bT1WorkEndA == bT1WorkEndB)
            {
                vB.Tangent = -vB.Tangent;
            }

            int? level = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvatureMagTolPct);

            return level.HasValue && level.Value >= targetContinuity;
        }

        public static string PickBestErrorMessage(string errFirst, string errSecond, string errBoth)
        {
            string[] errs = { errBoth, errFirst, errSecond };

            foreach (string e in errs)
            {
                if (!string.IsNullOrEmpty(e) &&
                    (e.Contains("Crv dev limit") || e.Contains("internal continuity") || e.Contains("inflection")))
                {
                    return e;
                }
            }

            foreach (string e in errs)
            {
                if (!string.IsNullOrEmpty(e) && !e.Contains("Lock") && !e.Contains("not yet supported"))
                    return e;
            }

            return errFirst ?? errSecond ?? errBoth ?? "Result cannot be created with the current constraints.";
        }
    }
}
