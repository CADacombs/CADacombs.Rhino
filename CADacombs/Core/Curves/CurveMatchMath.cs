using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
        private const double MaxWorkingScaleMultiple = 50.0;

        private enum SegmentAction
        {
            Locked,
            KeepPrimitive,
            ExtendTrimNurbs,
            DeformNurbs
        }

        // ==============================================================================
        // BACKWARD COMPATIBILITY OVERLOADS
        // ==============================================================================

        public static (Curve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod, Curve crvRef, bool modT1, bool refT1,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchOneCurve(
                crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                deformLines ? 2 : 0, 0, 2, 2, devTol, false, false, 2,
                RhinoMath.ZeroTolerance, MatchCrvOptions.DefaultAngleTolDeg, MatchCrvOptions.DefaultCurvatureMagTolPct, debug);
        }

        public static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA, Curve crvB, bool modT1_A, bool modT1_B,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchBothCurves(
                crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                deformLines ? 2 : 0, 0, 2, 2, devTol, false, false, 2,
                RhinoMath.ZeroTolerance, MatchCrvOptions.DefaultAngleTolDeg, MatchCrvOptions.DefaultCurvatureMagTolPct, debug);
        }

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
            return MatchOneCurve(
                crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                lineMode, arcMode, 2, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
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
            return MatchBothCurves(
                crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                lineMode, arcMode, 2, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
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
            int nurbsMode,
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
                    lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            return MatchSingleSegmentOneCurve(
                crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
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
            int nurbsMode,
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
                    lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            return MatchSingleSegmentBothCurves(
                crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
        }

        // ==============================================================================
        // INPUT-TYPE CLASSIFICATION & SINGLE-SEGMENT DISPATCH
        // ==============================================================================

        private static SegmentAction ClassifyInputSegment(
            Curve crv, int lineMode, int arcMode, int nurbsMode, bool isPairMode,
            out bool isArc, out Arc arc, out bool isLine, out Line line, out string lockError)
        {
            isArc = false;
            arc = Arc.Unset;
            isLine = false;
            line = Line.Unset;
            lockError = null;
            string noun = isPairMode ? "a curve" : "the curve";

            if (TryGetInputArc(crv, out arc))
            {
                isArc = true;
                if (arcMode == 0)
                {
                    lockError = $"Result cannot be created because {noun} is an arc and Arcs is set to Lock.";
                    return SegmentAction.Locked;
                }
                return arcMode == 1 ? SegmentAction.KeepPrimitive : SegmentAction.DeformNurbs;
            }

            if (TryGetInputLine(crv, out line))
            {
                isLine = true;
                if (lineMode == 0)
                {
                    lockError = $"Result cannot be created because {noun} is a line and Lines is set to Lock.";
                    return SegmentAction.Locked;
                }
                return lineMode == 1 ? SegmentAction.KeepPrimitive : SegmentAction.DeformNurbs;
            }

            // General NURBS curve (not an exact line or circular arc): strictly controlled by nurbsMode
            if (nurbsMode == 0)
            {
                lockError = $"Result cannot be created because {noun} is a NURBS curve and NURBS is set to Lock.";
                return SegmentAction.Locked;
            }

            return nurbsMode == 1 ? SegmentAction.ExtendTrimNurbs : SegmentAction.DeformNurbs;
        }

        private static (Curve Result, string ErrorMsg) MatchSingleSegmentOneCurve(
            Curve crvToMod,
            Curve crvRef,
            bool modT1,
            bool refT1,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int nurbsMode,
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
            bool isArc;
            Arc origArc;
            bool isLine;
            Line origLine;
            string lockErr;

            SegmentAction action = ClassifyInputSegment(
                crvToMod, lineMode, arcMode, nurbsMode, false,
                out isArc, out origArc, out isLine, out origLine, out lockErr);

            if (action == SegmentAction.Locked)
                return (null, lockErr);

            if (action == SegmentAction.KeepPrimitive)
            {
                if (isArc)
                    return SolveAnalyticalArcToFixedTarget(crvToMod, origArc, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);

                return SolveAnalyticalLineToFixedTarget(crvToMod, origLine, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            if (action == SegmentAction.ExtendTrimNurbs)
            {
                return SolveExtendTrimNurbsToFixedTarget(
                    crvToMod, modT1, crvRef, refT1,
                    continuity, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            return SolveNurbsOneCurve(
                crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                lineMode, arcMode, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
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
            int nurbsMode,
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
            bool isArcA, isLineA;
            Arc arcA;
            Line lineA;
            string lockErrA;
            SegmentAction actA = ClassifyInputSegment(
                crvA, lineMode, arcMode, nurbsMode, true,
                out isArcA, out arcA, out isLineA, out lineA, out lockErrA);

            if (actA == SegmentAction.Locked)
                return (null, null, lockErrA);

            bool isArcB, isLineB;
            Arc arcB;
            Line lineB;
            string lockErrB;
            SegmentAction actB = ClassifyInputSegment(
                crvB, lineMode, arcMode, nurbsMode, true,
                out isArcB, out arcB, out isLineB, out lineB, out lockErrB);

            if (actB == SegmentAction.Locked)
                return (null, null, lockErrB);

            // 1. KeepPrimitive + KeepPrimitive
            if (actA == SegmentAction.KeepPrimitive && actB == SegmentAction.KeepPrimitive)
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

            // 2. ExtendTrimNurbs + ExtendTrimNurbs
            if (actA == SegmentAction.ExtendTrimNurbs && actB == SegmentAction.ExtendTrimNurbs)
            {
                return SolveBothExtendTrimNurbs(
                    crvA, modT1_A, crvB, modT1_B,
                    continuity, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            // 3. ExtendTrimNurbs + KeepPrimitive (or vice versa)
            if (actA == SegmentAction.ExtendTrimNurbs && actB == SegmentAction.KeepPrimitive)
            {
                return SolveExtendTrimNurbsAndPrimitive(
                    crvA, modT1_A,
                    crvB, isArcB, arcB, lineB, modT1_B,
                    continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            if (actA == SegmentAction.KeepPrimitive && actB == SegmentAction.ExtendTrimNurbs)
            {
                var res = SolveExtendTrimNurbsAndPrimitive(
                    crvB, modT1_B,
                    crvA, isArcA, arcA, lineA, modT1_A,
                    continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
                return (res.ResultPrim, res.ResultExtNurbs, res.ErrorMsg);
            }

            // 4. ExtendTrimNurbs + DeformNurbs (e.g., general NURBS in Extend/Trim + converted Line/Arc)
            if (actA == SegmentAction.ExtendTrimNurbs && actB == SegmentAction.DeformNurbs)
            {
                return SolveExtendTrimAndDeformNurbs(
                    crvA, modT1_A, crvB, modT1_B,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            if (actA == SegmentAction.DeformNurbs && actB == SegmentAction.ExtendTrimNurbs)
            {
                var res = SolveExtendTrimAndDeformNurbs(
                    crvB, modT1_B, crvA, modT1_A,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
                return (res.ResultDeformNurbs, res.ResultExtNurbs, res.ErrorMsg);
            }

            // 5. KeepPrimitive + DeformNurbs (or vice versa)
            if (actA == SegmentAction.KeepPrimitive && actB == SegmentAction.DeformNurbs)
            {
                return SolveHybridPrimitiveAndNurbs(
                    crvA, isArcA, arcA, lineA, modT1_A,
                    crvB, modT1_B,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            if (actA == SegmentAction.DeformNurbs && actB == SegmentAction.KeepPrimitive)
            {
                var res = SolveHybridPrimitiveAndNurbs(
                    crvB, isArcB, arcB, lineB, modT1_B,
                    crvA, modT1_A,
                    continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
                return (res.ResultNurbs, res.ResultPrim, res.ErrorMsg);
            }

            // 6. DeformNurbs + DeformNurbs
            return SolveNurbsBothCurves(
                crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                lineMode, arcMode, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);
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

            double overlapMax, pA, pB, minD, minPA, minPB;
            if (Curve.GetDistancesBetweenCurves(a, b, docTol, out overlapMax, out pA, out pB, out minD, out minPA, out minPB))
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
                    double tTo;
                    if (to.ClosestPoint(pt, out tTo))
                        maxD = Math.Max(maxD, pt.DistanceTo(to.PointAt(tTo)));
                }
                return maxD;
            }

            double tS, tE;
            if (to.ClosestPoint(from.PointAtStart, out tS))
                maxD = Math.Max(maxD, from.PointAtStart.DistanceTo(to.PointAt(tS)));
            if (to.ClosestPoint(from.PointAtEnd, out tE))
                maxD = Math.Max(maxD, from.PointAtEnd.DistanceTo(to.PointAt(tE)));
            return maxD;
        }

        private static double SampleOneWayAverageDeviation(Curve from, Curve to, int sampleCount)
        {
            var ts = from.DivideByCount(sampleCount, true);
            if (ts == null || ts.Length == 0)
            {
                double tS, tE;
                if (!to.ClosestPoint(from.PointAtStart, out tS) || !to.ClosestPoint(from.PointAtEnd, out tE))
                    return double.MaxValue;
                return 0.5 * (from.PointAtStart.DistanceTo(to.PointAt(tS)) + from.PointAtEnd.DistanceTo(to.PointAt(tE)));
            }

            double totalDist = 0.0;
            foreach (double t in ts)
            {
                Point3d pt = from.PointAt(t);
                double tTo;
                if (!to.ClosestPoint(pt, out tTo)) return double.MaxValue;
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
