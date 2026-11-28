using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
        // ==============================================================================
        // COMPOUND CURVE (POLYLINECURVE & POLYCURVE) UNWRAP / RE-WRAP SUPPORT
        // ==============================================================================

        private sealed class CompoundEnvelope : IDisposable
        {
            public bool IsPolyline { get; set; }
            public bool WorkIsT1 { get; set; }

            public bool HasOuterJoint { get; set; }
            public Point3d OuterJointPoint { get; set; }

            public bool HasAnyInternalJoint => HasOuterJoint || (InnerEnvelope != null && InnerEnvelope.HasAnyInternalJoint);

            public Point3d[] PolylinePoints { get; set; }
            public PolyCurve FlattenedPolyCurve { get; set; }
            public int EndSegmentIndex { get; set; }
            public CompoundEnvelope InnerEnvelope { get; set; }
            public Curve LeafEndSegment { get; set; }

            public Curve Rewrap(Curve modifiedLeafSegment, double posTol)
            {
                if (modifiedLeafSegment == null || !modifiedLeafSegment.IsValid)
                    return null;

                Curve segToInsert = InnerEnvelope != null
                    ? InnerEnvelope.Rewrap(modifiedLeafSegment, posTol)
                    : modifiedLeafSegment;

                if (segToInsert == null || !segToInsert.IsValid)
                    return null;

                try
                {
                    if (HasOuterJoint)
                    {
                        Point3d fixedEndPt = WorkIsT1 ? segToInsert.PointAtStart : segToInsert.PointAtEnd;
                        double jointTol = Math.Max(posTol, RhinoMath.SqrtEpsilon);
                        if (fixedEndPt.DistanceTo(OuterJointPoint) > jointTol)
                            return null;
                    }

                    if (IsPolyline)
                        return RewrapPolyline(segToInsert);

                    return RewrapPolyCurve(segToInsert);
                }
                finally
                {
                    if (InnerEnvelope != null && !ReferenceEquals(segToInsert, modifiedLeafSegment))
                        segToInsert.Dispose();
                }
            }

            private Curve RewrapPolyline(Curve modEndSeg)
            {
                int n = PolylinePoints.Length;

                if (modEndSeg is LineCurve modLine)
                {
                    var pts = new Point3d[n];
                    Array.Copy(PolylinePoints, pts, n);
                    if (WorkIsT1)
                        pts[n - 1] = modLine.PointAtEnd;
                    else
                        pts[0] = modLine.PointAtStart;

                    var plOut = new PolylineCurve(pts);
                    return plOut.IsValid ? plOut : null;
                }

                Curve untouchedPortion = null;
                try
                {
                    if (WorkIsT1)
                    {
                        if (n == 3)
                        {
                            untouchedPortion = new LineCurve(PolylinePoints[0], PolylinePoints[1]);
                        }
                        else
                        {
                            var headPts = new Point3d[n - 1];
                            Array.Copy(PolylinePoints, 0, headPts, 0, n - 1);
                            untouchedPortion = new PolylineCurve(headPts);
                        }
                        return BuildPolyCurvePreservingSegments(new[] { untouchedPortion, modEndSeg });
                    }
                    else
                    {
                        if (n == 3)
                        {
                            untouchedPortion = new LineCurve(PolylinePoints[1], PolylinePoints[2]);
                        }
                        else
                        {
                            var tailPts = new Point3d[n - 1];
                            Array.Copy(PolylinePoints, 1, tailPts, 0, n - 1);
                            untouchedPortion = new PolylineCurve(tailPts);
                        }
                        return BuildPolyCurvePreservingSegments(new[] { modEndSeg, untouchedPortion });
                    }
                }
                finally
                {
                    untouchedPortion?.Dispose();
                }
            }

            private Curve RewrapPolyCurve(Curve modEndSeg)
            {
                int segCount = FlattenedPolyCurve.SegmentCount;
                if (segCount == 1)
                    return modEndSeg.DuplicateCurve();

                var segs = new List<Curve>(segCount);
                var ownedCopies = new List<Curve>(segCount - 1);
                try
                {
                    for (int i = 0; i < segCount; i++)
                    {
                        if (i == EndSegmentIndex)
                        {
                            segs.Add(modEndSeg);
                        }
                        else
                        {
                            Curve untouched = FlattenedPolyCurve.SegmentCurve(i);
                            if (untouched == null || !untouched.IsValid)
                            {
                                untouched?.Dispose();
                                return null;
                            }
                            ownedCopies.Add(untouched);
                            segs.Add(untouched);
                        }
                    }

                    return BuildPolyCurvePreservingSegments(segs);
                }
                finally
                {
                    foreach (Curve c in ownedCopies)
                        c.Dispose();
                }
            }

            private static PolyCurve BuildPolyCurvePreservingSegments(IList<Curve> segments)
            {
                var pc = new PolyCurve();
                bool allAppended = true;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i] == null || !segments[i].IsValid || !pc.AppendSegment(segments[i]))
                    {
                        allAppended = false;
                        break;
                    }
                }

                if (allAppended)
                {
                    pc.RemoveNesting();
                    if (pc.IsValid)
                        return pc;
                }

                pc.Dispose();

                var fallbackPc = new PolyCurve();
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i] == null || !segments[i].IsValid || !fallbackPc.Append(segments[i]))
                    {
                        fallbackPc.Dispose();
                        return null;
                    }
                }

                fallbackPc.RemoveNesting();
                if (!fallbackPc.IsValid)
                {
                    fallbackPc.Dispose();
                    return null;
                }
                return fallbackPc;
            }

            public void Dispose()
            {
                bool hadInner = InnerEnvelope != null;
                InnerEnvelope?.Dispose();
                InnerEnvelope = null;

                if (!hadInner)
                {
                    LeafEndSegment?.Dispose();
                }
                LeafEndSegment = null;

                FlattenedPolyCurve?.Dispose();
                FlattenedPolyCurve = null;
            }
        }

        private static bool IsCompoundCurve(Curve crv)
        {
            if (crv == null) return false;
            if (crv is PolylineCurve pl) return pl.PointCount >= 3;
            if (crv is PolyCurve) return true;
            return false;
        }

        private static bool TrySplitCompoundCurve(Curve crv, bool workIsT1, out CompoundEnvelope envelope)
        {
            envelope = null;
            if (crv == null) return false;

            if (crv is PolylineCurve pl && pl.PointCount >= 3)
            {
                int n = pl.PointCount;
                var pts = new Point3d[n];
                for (int i = 0; i < n; i++) pts[i] = pl.Point(i);

                LineCurve leaf = workIsT1
                    ? new LineCurve(pts[n - 2], pts[n - 1])
                    : new LineCurve(pts[0], pts[1]);

                envelope = new CompoundEnvelope
                {
                    IsPolyline = true,
                    WorkIsT1 = workIsT1,
                    HasOuterJoint = true,
                    OuterJointPoint = workIsT1 ? pts[n - 2] : pts[1],
                    PolylinePoints = pts,
                    LeafEndSegment = leaf
                };
                return true;
            }

            if (crv is PolyCurve origPc)
            {
                var flat = (PolyCurve)origPc.DuplicateCurve();
                flat.RemoveNesting();

                int segCount = flat.SegmentCount;
                if (segCount <= 0)
                {
                    flat.Dispose();
                    return false;
                }

                int endIdx = workIsT1 ? segCount - 1 : 0;
                Curve endSeg = flat.SegmentCurve(endIdx);
                if (endSeg == null || !endSeg.IsValid)
                {
                    endSeg?.Dispose();
                    flat.Dispose();
                    return false;
                }

                bool hasOuterJoint = segCount >= 2;
                Point3d outerJointPt = workIsT1 ? endSeg.PointAtStart : endSeg.PointAtEnd;

                CompoundEnvelope inner = null;
                Curve leafSeg = endSeg;

                if (endSeg is PolylineCurve endPl && endPl.PointCount >= 3 &&
                    TrySplitCompoundCurve(endPl, workIsT1, out inner))
                {
                    endSeg.Dispose();
                    leafSeg = inner.LeafEndSegment;
                }

                envelope = new CompoundEnvelope
                {
                    IsPolyline = false,
                    WorkIsT1 = workIsT1,
                    HasOuterJoint = hasOuterJoint,
                    OuterJointPoint = outerJointPt,
                    FlattenedPolyCurve = flat,
                    EndSegmentIndex = endIdx,
                    InnerEnvelope = inner,
                    LeafEndSegment = leafSeg
                };
                return true;
            }

            return false;
        }

        // ==============================================================================
        // COMPOUND MATCH ENTRY POINTS
        // ==============================================================================

        private static (Curve Result, string ErrorMsg) MatchCompoundOneCurve(
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
            CompoundEnvelope envMod;
            CompoundEnvelope envRef;
            bool splitMod = TrySplitCompoundCurve(crvToMod, modT1, out envMod);
            bool splitRef = TrySplitCompoundCurve(crvRef, refT1, out envRef);

            try
            {
                Curve workMod = splitMod ? envMod.LeafEndSegment : crvToMod;
                Curve workRef = splitRef ? envRef.LeafEndSegment : crvRef;

                int effPreserve = (splitMod && envMod.HasAnyInternalJoint)
                    ? Math.Max(preserveOtherEnd, 1)
                    : preserveOtherEnd;

                var segRes = MatchSingleSegmentOneCurve(
                    workMod, workRef, modT1, refT1, continuity, effPreserve,
                    lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);

                if (segRes.Result == null)
                    return (null, segRes.ErrorMsg);

                if (!splitMod)
                    return segRes;

                Curve wrapped = envMod.Rewrap(segRes.Result, posTol);
                segRes.Result.Dispose();

                if (wrapped == null || !wrapped.IsValid)
                    return (null, "Result cannot be created without breaking the compound curve's internal joint.");

                return (wrapped, null);
            }
            finally
            {
                envMod?.Dispose();
                envRef?.Dispose();
            }
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchCompoundBothCurves(
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
            CompoundEnvelope envA;
            CompoundEnvelope envB;
            bool splitA = TrySplitCompoundCurve(crvA, modT1_A, out envA);
            bool splitB = TrySplitCompoundCurve(crvB, modT1_B, out envB);

            try
            {
                Curve workA = splitA ? envA.LeafEndSegment : crvA;
                Curve workB = splitB ? envB.LeafEndSegment : crvB;

                bool hasJointA = splitA && envA.HasAnyInternalJoint;
                bool hasJointB = splitB && envB.HasAnyInternalJoint;

                int effPresA = hasJointA ? Math.Max(preserveOtherEnd, 1) : preserveOtherEnd;
                int effPresB = hasJointB ? Math.Max(preserveOtherEnd, 1) : preserveOtherEnd;

                (Curve ResultA, Curve ResultB, string ErrorMsg) segRes;

                if (effPresA == effPresB)
                {
                    segRes = MatchSingleSegmentBothCurves(
                        workA, workB, modT1_A, modT1_B, continuity, effPresA,
                        lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                        preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                        posTol, angleTolDeg, curvTolPct, debug);
                }
                else if (effPresA > effPresB)
                {
                    segRes = SolveAsymmetricCompoundAndSimple(
                        workA, modT1_A, workB, modT1_B,
                        continuity, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                        preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                        posTol, angleTolDeg, curvTolPct, debug);
                }
                else
                {
                    var swapped = SolveAsymmetricCompoundAndSimple(
                        workB, modT1_B, workA, modT1_A,
                        continuity, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                        preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                        posTol, angleTolDeg, curvTolPct, debug);
                    segRes = (swapped.ResultSimple, swapped.ResultCompound, swapped.ErrorMsg);
                }

                if (segRes.ResultA == null && segRes.ResultB == null)
                    return (null, null, segRes.ErrorMsg);

                Curve outA = segRes.ResultA;
                Curve outB = segRes.ResultB;

                if (splitA && outA != null)
                {
                    Curve wrappedA = envA.Rewrap(outA, posTol);
                    outA.Dispose();
                    if (wrappedA == null || !wrappedA.IsValid)
                    {
                        outB?.Dispose();
                        return (null, null, "Result cannot be created without breaking the first compound curve's internal joint.");
                    }
                    outA = wrappedA;
                }

                if (splitB && outB != null)
                {
                    Curve wrappedB = envB.Rewrap(outB, posTol);
                    outB.Dispose();
                    if (wrappedB == null || !wrappedB.IsValid)
                    {
                        outA?.Dispose();
                        return (null, null, "Result cannot be created without breaking the second compound curve's internal joint.");
                    }
                    outB = wrappedB;
                }

                return (outA, outB, null);
            }
            finally
            {
                envA?.Dispose();
                envB?.Dispose();
            }
        }

        private static (Curve ResultCompound, Curve ResultSimple, string ErrorMsg) SolveAsymmetricCompoundAndSimple(
            Curve compSeg, bool modT1_Comp,
            Curve simpleCrv, bool modT1_Simple,
            int continuity,
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
            bool isArcComp, isLineComp;
            Arc arcComp;
            Line lineComp;
            string lockErrComp;
            SegmentAction actComp = ClassifyInputSegment(
                compSeg, lineMode, arcMode, nurbsMode, true,
                out isArcComp, out arcComp, out isLineComp, out lineComp, out lockErrComp);

            if (actComp == SegmentAction.Locked)
                return (null, null, lockErrComp);

            bool isArcSimple, isLineSimple;
            Arc arcSimple;
            Line lineSimple;
            string lockErrSimple;
            SegmentAction actSimple = ClassifyInputSegment(
                simpleCrv, lineMode, arcMode, nurbsMode, true,
                out isArcSimple, out arcSimple, out isLineSimple, out lineSimple, out lockErrSimple);

            if (actSimple == SegmentAction.Locked)
                return (null, null, lockErrSimple);

            // ExtendTrimNurbs never moves its opposite end, so Preserve=None and Preserve=Position are identical
            if (actSimple == SegmentAction.ExtendTrimNurbs)
            {
                return MatchSingleSegmentBothCurves(
                    compSeg, simpleCrv, modT1_Comp, modT1_Simple,
                    continuity, 1, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);
            }

            Point3d pFixComp, pWorkComp, pFixSimple, pWorkSimple;
            Vector3d tFixCompInto, tWorkCompOut, dummyIntoSimple, tWorkSimpleOut;
            GetOrientedEnds(compSeg, modT1_Comp, out pFixComp, out pWorkComp, out tFixCompInto, out tWorkCompOut);
            GetOrientedEnds(simpleCrv, modT1_Simple, out pFixSimple, out pWorkSimple, out dummyIntoSimple, out tWorkSimpleOut);

            Point3d ptAvg = 0.5 * (pWorkComp + pWorkSimple);
            BoundingBox refBounds = GetReferenceBounds(compSeg, simpleCrv);
            var pairCandidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            string lastErr = null;

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            var compPartwaySeeds = new List<(Curve CandComp, string Desc)>();

            var symAtPos = MatchSingleSegmentBothCurves(
                compSeg, simpleCrv, modT1_Comp, modT1_Simple,
                continuity, 1, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                posTol, angleTolDeg, curvTolPct, debug);

            if (symAtPos.ResultA != null)
            {
                compPartwaySeeds.Add((symAtPos.ResultA, "Asymmetric Compound(PresG0) + Simple(PresNone): Seed from PresG0"));
            }
            symAtPos.ResultB?.Dispose();

            if (actComp == SegmentAction.KeepPrimitive && isLineComp)
            {
                var lcMid = MakeOrientedLineCurve(pFixComp, ptAvg, modT1_Comp);
                if (lcMid != null && IsWithinWorkingScale(refBounds, lcMid))
                    compPartwaySeeds.Add((lcMid, "Asymmetric Compound Line(PresG0) + Simple(PresNone): Pivot to Midpoint"));
                else lcMid?.Dispose();

                double sProj = (ptAvg - pFixComp) * tFixCompInto;
                if (sProj > RhinoMath.ZeroTolerance)
                {
                    var lcExt = MakeOrientedLineCurve(pFixComp, pFixComp + sProj * tFixCompInto, modT1_Comp);
                    if (lcExt != null && IsWithinWorkingScale(refBounds, lcExt))
                        compPartwaySeeds.Add((lcExt, "Asymmetric Compound Line(PresG0) + Simple(PresNone): Extend to Midpoint"));
                    else lcExt?.Dispose();
                }
            }
            else if (actComp == SegmentAction.KeepPrimitive && isArcComp)
            {
                var acScale = MakeOrientedArcCurve(new Arc(pFixComp, tFixCompInto, ptAvg), modT1_Comp);
                if (acScale != null && IsWithinWorkingScale(refBounds, acScale))
                    compPartwaySeeds.Add((acScale, "Asymmetric Compound Arc(PresG0) + Simple(PresNone): Scale/Roll to Midpoint"));
                else acScale?.Dispose();

                var acSim = MakeSimilarityPivotedArcCurve(arcComp, pFixComp, pWorkComp, ptAvg);
                if (acSim != null && IsWithinWorkingScale(refBounds, acSim))
                    compPartwaySeeds.Add((acSim, "Asymmetric Compound Arc(PresG0) + Simple(PresNone): Similarity Pivot to Midpoint"));
                else acSim?.Dispose();
            }

            foreach (var seed in compPartwaySeeds)
            {
                if (GetMaximumDeviation(compSeg, seed.CandComp) > devTol)
                {
                    seed.CandComp.Dispose();
                    lastErr = "Result cannot be created because it exceeds the Crv dev limit.";
                    continue;
                }

                if (AreCurvesAtContinuity(seed.CandComp, modT1_Comp, simpleCrv, modT1_Simple, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    pairCandidates.Add((seed.CandComp.DuplicateCurve(), simpleCrv.DuplicateCurve(), seed.Desc + " (Exact Meet)"));
                }

                var simpleRes = MatchSingleSegmentOneCurve(
                    simpleCrv, seed.CandComp, modT1_Simple, modT1_Comp,
                    continuity, 0, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);

                if (simpleRes.Result != null)
                {
                    pairCandidates.Add((seed.CandComp, simpleRes.Result, seed.Desc));
                }
                else
                {
                    seed.CandComp.Dispose();
                    if (!string.IsNullOrEmpty(simpleRes.ErrorMsg)) lastErr = simpleRes.ErrorMsg;
                }
            }

            var simplePartwaySeeds = new List<(Curve CandSimple, string Desc)>();
            Vector3d sharedFlow = ComputeSharedFlowDirection(
                tWorkCompOut, tWorkSimpleOut, pFixComp, pFixSimple,
                isArcSimple ? arcSimple.Plane.ZAxis : (isArcComp ? arcComp.Plane.ZAxis : Vector3d.ZAxis));

            if (actSimple == SegmentAction.KeepPrimitive && isLineSimple)
            {
                if (continuity == 0)
                {
                    var lcTrans = MakeOrientedLineCurve(pFixSimple + (ptAvg - pWorkSimple), ptAvg, modT1_Simple);
                    if (lcTrans != null && IsWithinWorkingScale(refBounds, lcTrans))
                        simplePartwaySeeds.Add((lcTrans, "Asymmetric Compound(PresG0) + Simple Line(PresNone): Rigid Translate"));
                    else lcTrans?.Dispose();
                }

                var lcRigid = MakeOrientedLineCurve(ptAvg + lineSimple.Length * sharedFlow, ptAvg, modT1_Simple);
                if (lcRigid != null && IsWithinWorkingScale(refBounds, lcRigid))
                    simplePartwaySeeds.Add((lcRigid, "Asymmetric Compound(PresG0) + Simple Line(PresNone): Rigid Align"));
                else lcRigid?.Dispose();
            }
            else if (actSimple == SegmentAction.KeepPrimitive && isArcSimple)
            {
                if (continuity == 0)
                {
                    Arc moved = arcSimple;
                    moved.Transform(Transform.Translation(ptAvg - pWorkSimple));
                    var acTrans = new ArcCurve(moved);
                    if (acTrans.IsValid && IsWithinWorkingScale(refBounds, acTrans))
                        simplePartwaySeeds.Add((acTrans, "Asymmetric Compound(PresG0) + Simple Arc(PresNone): Rigid Translate"));
                    else acTrans.Dispose();
                }

                var acRigid1 = MakeRigidAlignedArcCurve(arcSimple, modT1_Simple, pFixSimple, pWorkSimple, tWorkSimpleOut, ptAvg, -sharedFlow, false);
                if (acRigid1 != null && IsWithinWorkingScale(refBounds, acRigid1))
                    simplePartwaySeeds.Add((acRigid1, "Asymmetric Compound(PresG0) + Simple Arc(PresNone): Rigid Align"));
                else acRigid1?.Dispose();

                var acRigid2 = MakeRigidAlignedArcCurve(arcSimple, modT1_Simple, pFixSimple, pWorkSimple, tWorkSimpleOut, ptAvg, -sharedFlow, true);
                if (acRigid2 != null && IsWithinWorkingScale(refBounds, acRigid2))
                    simplePartwaySeeds.Add((acRigid2, "Asymmetric Compound(PresG0) + Simple Arc(PresNone): Rigid Align + Roll"));
                else acRigid2?.Dispose();
            }
            else
            {
                Curve movedNurbs = simpleCrv.DuplicateCurve();
                movedNurbs.Transform(Transform.Translation(ptAvg - pWorkSimple));
                if (movedNurbs.IsValid && IsWithinWorkingScale(refBounds, movedNurbs))
                    simplePartwaySeeds.Add((movedNurbs, "Asymmetric Compound(PresG0) + Simple NURBS(PresNone): Rigid Translate"));
                else movedNurbs.Dispose();
            }

            foreach (var seed in simplePartwaySeeds)
            {
                if (GetMaximumDeviation(simpleCrv, seed.CandSimple) > devTol)
                {
                    seed.CandSimple.Dispose();
                    lastErr = "Result cannot be created because it exceeds the Crv dev limit.";
                    continue;
                }

                if (AreCurvesAtContinuity(compSeg, modT1_Comp, seed.CandSimple, modT1_Simple, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    pairCandidates.Add((compSeg.DuplicateCurve(), seed.CandSimple.DuplicateCurve(), seed.Desc + " (Exact Meet)"));
                }

                var compRes = MatchSingleSegmentOneCurve(
                    compSeg, seed.CandSimple, modT1_Comp, modT1_Simple,
                    continuity, 1, lineMode, arcMode, nurbsMode, incrAsNeeded, devTol,
                    preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                    posTol, angleTolDeg, curvTolPct, debug);

                if (compRes.Result != null)
                {
                    pairCandidates.Add((compRes.Result, seed.CandSimple, seed.Desc));
                }
                else
                {
                    seed.CandSimple.Dispose();
                    if (!string.IsNullOrEmpty(compRes.ErrorMsg)) lastErr = compRes.ErrorMsg;
                }
            }

            if (pairCandidates.Count == 0)
                return (null, null, lastErr ?? symAtPos.ErrorMsg ?? "Result cannot be created with the current constraints.");

            return EvaluateAnalyticalPairCandidates(compSeg, simpleCrv, pairCandidates, devTol, debug);
        }
    }
}
