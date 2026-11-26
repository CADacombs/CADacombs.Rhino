using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
        // ==============================================================================
        // PRIMITIVE DETECTION & ORIENTATION HELPERS
        // ==============================================================================

        private static bool TryGetPrimitiveArc(Curve crv, int arcMode, out Arc arc)
        {
            arc = Arc.Unset;
            if (crv == null || crv.IsClosed) return false;

            if (crv is ArcCurve ac)
            {
                arc = ac.Arc;
                if (!arc.IsValid || arc.IsCircle) return false;
                if (arc.StartPoint.DistanceTo(crv.PointAtEnd) < arc.StartPoint.DistanceTo(crv.PointAtStart))
                    arc.Reverse();
                return true;
            }
            if (arcMode != 2 && crv.TryGetArc(out arc, RhinoMath.ZeroTolerance))
            {
                if (!arc.IsValid || arc.IsCircle) return false;
                if (arc.StartPoint.DistanceTo(crv.PointAtEnd) < arc.StartPoint.DistanceTo(crv.PointAtStart))
                    arc.Reverse();
                return true;
            }
            return false;
        }

        private static bool TryGetPrimitiveLine(Curve crv, int lineMode, out Line line)
        {
            line = Line.Unset;
            if (crv == null || crv.IsClosed) return false;

            if (crv is LineCurve lc)
            {
                line = lc.Line;
                return line.IsValid && line.Length > RhinoMath.ZeroTolerance;
            }
            if (lineMode != 2 && crv.IsLinear(RhinoMath.ZeroTolerance))
            {
                line = new Line(crv.PointAtStart, crv.PointAtEnd);
                return line.IsValid && line.Length > RhinoMath.ZeroTolerance;
            }
            return false;
        }

        private static void GetOrientedEnds(
            Curve crv, bool workIsT1,
            out Point3d pFix, out Point3d pWork,
            out Vector3d tFixInto, out Vector3d tWorkOut)
        {
            pFix = workIsT1 ? crv.PointAtStart : crv.PointAtEnd;
            pWork = workIsT1 ? crv.PointAtEnd : crv.PointAtStart;
            tFixInto = workIsT1 ? crv.TangentAtStart : -crv.TangentAtEnd;
            tFixInto.Unitize();
            tWorkOut = workIsT1 ? crv.TangentAtEnd : -crv.TangentAtStart;
            tWorkOut.Unitize();
        }

        private static ArcCurve MakeOrientedArcCurve(Arc arcFromFixToWork, bool modT1)
        {
            if (!arcFromFixToWork.IsValid || arcFromFixToWork.Length <= RhinoMath.ZeroTolerance)
                return null;
            var ac = new ArcCurve(arcFromFixToWork);
            if (!modT1) ac.Reverse();
            return ac;
        }

        private static LineCurve MakeOrientedLineCurve(Point3d pFix, Point3d pWork, bool modT1)
        {
            Line ln = modT1 ? new Line(pFix, pWork) : new Line(pWork, pFix);
            if (!ln.IsValid || ln.Length <= RhinoMath.ZeroTolerance)
                return null;
            return new LineCurve(ln);
        }

        private static Transform SafeRotation(Vector3d fromDir, Vector3d toDir, Vector3d fallbackAxis, Point3d pivot)
        {
            fromDir.Unitize();
            toDir.Unitize();
            if ((fromDir * toDir) < -1.0 + RhinoMath.SqrtEpsilon)
            {
                if (!fallbackAxis.Unitize() || Math.Abs(fallbackAxis * fromDir) > 0.999)
                {
                    fallbackAxis = Vector3d.CrossProduct(fromDir, Math.Abs(fromDir.X) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis);
                    fallbackAxis.Unitize();
                }
                return Transform.Rotation(Math.PI, fallbackAxis, pivot);
            }
            return Transform.Rotation(fromDir, toDir, pivot);
        }

        private static Vector3d ComputeSharedFlowDirection(
            Vector3d tWorkOutA, Vector3d tWorkOutB,
            Point3d pFixA, Point3d pFixB, Vector3d fallbackNormal)
        {
            Vector3d sharedFlow = tWorkOutA + (-tWorkOutB);
            if (sharedFlow.Unitize())
                return sharedFlow;

            Vector3d span = pFixB - pFixA;
            span -= (span * tWorkOutA) * tWorkOutA;
            if (span.Unitize())
                return span;

            Vector3d perp = Vector3d.CrossProduct(fallbackNormal, tWorkOutA);
            if (perp.Unitize())
                return perp;

            perp = Vector3d.CrossProduct(Math.Abs(tWorkOutA.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis, tWorkOutA);
            perp.Unitize();
            return perp;
        }

        /// <summary>
        /// Rigidly transforms origArc (pure rotation + translation; exact Radius & Length preserved)
        /// so its working end lands at qTarget with outward tangent aligned to flowTarget.
        /// </summary>
        private static ArcCurve MakeRigidAlignedArcCurve(
            Arc origArc, bool modT1,
            Point3d pFixOrig, Point3d pWorkOrig, Vector3d tWorkOutOrig,
            Point3d qTarget, Vector3d flowTarget, bool optimizeRoll)
        {
            Transform rot = SafeRotation(tWorkOutOrig, flowTarget, origArc.Plane.ZAxis, pWorkOrig);
            Arc moved = origArc;
            moved.Transform(Transform.Translation(qTarget - pWorkOrig) * rot);
            if (!moved.IsValid) return null;

            if (optimizeRoll)
            {
                Point3d pOther = modT1 ? moved.StartPoint : moved.EndPoint;
                Vector3d v0 = (pOther - qTarget) - ((pOther - qTarget) * flowTarget) * flowTarget;
                Vector3d vf = (pFixOrig - qTarget) - ((pFixOrig - qTarget) * flowTarget) * flowTarget;
                if (v0.Length > RhinoMath.ZeroTolerance && vf.Length > RhinoMath.ZeroTolerance)
                {
                    Transform roll = SafeRotation(v0, vf, flowTarget, qTarget);
                    moved.Transform(roll);
                }
            }

            return moved.IsValid ? new ArcCurve(moved) : null;
        }

        /// <summary>
        /// Constructs an ArcCurve on a target osculating circle (radius rOsc, center cOsc, end at pJoin,
        /// outward tangent outFlow at pJoin) while preserving origArc.Length (falling back to origArc.Angle
        /// only if origArc.Length would wrap >= 1.95*pi around a small osculating circle).
        /// </summary>
        private static ArcCurve MakeLengthPreservingOsculatingArcCurve(
            Arc origArc, bool modT1,
            Point3d pJoin, Vector3d outFlow, Point3d cOsc, double rOsc)
        {
            if (rOsc <= RhinoMath.ZeroTolerance) return null;
            Vector3d xDir = pJoin - cOsc;
            if (!xDir.Unitize()) return null;

            Vector3d backFlow = -outFlow;
            if (!backFlow.Unitize()) return null;

            double sweep = origArc.Length / rOsc;
            if (sweep > 1.95 * Math.PI)
                sweep = Math.Min(origArc.Angle, 1.95 * Math.PI);
            sweep = Math.Max(sweep, 1e-4);

            Plane oscPlane = new Plane(cOsc, xDir, backFlow);
            if (!oscPlane.IsValid) return null;

            Arc oscArc = new Arc(new Circle(oscPlane, rOsc), sweep);
            if (!oscArc.IsValid || oscArc.Length <= RhinoMath.ZeroTolerance) return null;

            var ac = new ArcCurve(oscArc); // runs pJoin (T0) -> newOtherEnd (T1)
            if (modT1) ac.Reverse();       // orient newOtherEnd (T0) -> pJoin (T1) when working end is T1
            return ac;
        }

        /// <summary>
        /// Rotates and uniformly scales origArc around pFix so pWork maps directly onto pTarget,
        /// preserving pFix and the arc's sweep angle for any non-zero chord distance.
        /// </summary>
        private static ArcCurve MakeSimilarityPivotedArcCurve(
            Arc origArc, Point3d pFix, Point3d pWork, Point3d pTarget)
        {
            Vector3d oldChord = pWork - pFix;
            Vector3d newChord = pTarget - pFix;
            double oldLen = oldChord.Length;
            double newLen = newChord.Length;
            if (oldLen <= RhinoMath.ZeroTolerance || newLen <= RhinoMath.ZeroTolerance)
                return null;

            Transform rot = SafeRotation(oldChord, newChord, origArc.Plane.ZAxis, pFix);
            Transform scale = Transform.Scale(pFix, newLen / oldLen);
            Arc transformed = origArc;
            transformed.Transform(scale * rot);
            return (transformed.IsValid && transformed.Length > RhinoMath.ZeroTolerance)
                ? new ArcCurve(transformed)
                : null;
        }

        private static List<ArcCurve> MakeFixedRadiusPivotedArcs(
            Arc origArc, bool modT1, Point3d pFix, Point3d pWork, Point3d pTarget)
        {
            var list = new List<ArcCurve>();
            Vector3d newChord = pTarget - pFix;
            double chordLen = newChord.Length;
            double r = origArc.Radius;
            if (chordLen <= RhinoMath.ZeroTolerance || chordLen > 2.0 * r)
                return list;

            Transform rot = SafeRotation(pWork - pFix, newChord, origArc.Plane.ZAxis, pFix);
            Vector3d rotNormal = origArc.Plane.ZAxis;
            rotNormal.Transform(rot);
            if (!rotNormal.Unitize()) return list;

            Vector3d perp = Vector3d.CrossProduct(rotNormal, newChord);
            if (!perp.Unitize()) return list;

            Point3d mid = 0.5 * (pFix + pTarget);
            double h = 0.5 * chordLen;
            double d = Math.Sqrt(Math.Max(0.0, r * r - h * h));
            Point3d[] centers = { mid + d * perp, mid - d * perp };
            foreach (Point3d c in centers)
            {
                Vector3d tan1 = Vector3d.CrossProduct(rotNormal, pFix - c);
                if (tan1.Unitize())
                {
                    var acPlus = MakeOrientedArcCurve(new Arc(pFix, tan1, pTarget), modT1);
                    if (acPlus != null) list.Add(acPlus);
                    var acMinus = MakeOrientedArcCurve(new Arc(pFix, -tan1, pTarget), modT1);
                    if (acMinus != null) list.Add(acMinus);
                }
            }
            return list;
        }

        // ==============================================================================
        // SINGLE-CURVE ANALYTICAL SOLVERS
        // ==============================================================================

        private static (Curve Result, string ErrorMsg) SolveAnalyticalLineToFixedTarget(
            Curve origCrv, Line origLine, bool modT1,
            Curve crvRef, bool refT1,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            GetOrientedEnds(origCrv, modT1, out Point3d pFix, out Point3d pWork, out Vector3d tFixInto, out _);

            double tRef = refT1 ? crvRef.Domain.T1 : crvRef.Domain.T0;
            Point3d pRef = refT1 ? crvRef.PointAtEnd : crvRef.PointAtStart;
            Vector3d tRefInto = refT1 ? -crvRef.TangentAtEnd : crvRef.TangentAtStart;
            tRefInto.Unitize();
            Vector3d curvRef = crvRef.CurvatureAt(tRef);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolRad = Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon);
            double effAngleTolDeg = RhinoMath.ToDegrees(effAngleTolRad);
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            if (continuity == 2 && !curvRef.IsTiny())
                return (null, "Result cannot be created: an analytical line cannot achieve Curvature (G2) continuity with a curved target.");

            BoundingBox refBounds = GetReferenceBounds(origCrv, crvRef);
            var candidates = new List<(Curve Crv, string Desc)>();

            void TryAddLine(LineCurve lc, string desc)
            {
                if (lc == null) return;
                if (!IsWithinWorkingScale(refBounds, lc)) { lc.Dispose(); return; }
                if (continuity >= 1)
                {
                    Vector3d candOut = modT1 ? lc.TangentAtEnd : -lc.TangentAtStart;
                    candOut.Unitize();
                    if (Vector3d.VectorAngle(candOut, tRefInto) > effAngleTolRad) { lc.Dispose(); return; }
                }
                if (continuity == 2 && !AreCurvesAtContinuity(lc, modT1, crvRef, refT1, 2, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    lc.Dispose();
                    return;
                }
                candidates.Add((lc, desc));
            }

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: strict rigid transformations (exact origLine.Length preserved)
                if (continuity == 0)
                {
                    TryAddLine(MakeOrientedLineCurve(pFix + (pRef - pWork), pRef, modT1), "Analytical Line: Rigid Translate to G0 (Preserve None)");

                    Vector3d chordDir = pRef - pFix;
                    if (chordDir.Unitize())
                        TryAddLine(MakeOrientedLineCurve(pRef - origLine.Length * chordDir, pRef, modT1), "Analytical Line: Rigid Rotate & Shift to G0 (Preserve None)");
                }

                TryAddLine(MakeOrientedLineCurve(pRef - origLine.Length * tRefInto, pRef, modT1), "Analytical Line: Rigid Move to Target Tangent (Preserve None)");
                return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
            }

            Vector3d vecToRef = pRef - pFix;
            double distToRef = vecToRef.Length;
            if (distToRef > RhinoMath.ZeroTolerance)
            {
                Vector3d dirToRef = vecToRef / distToRef;
                Line origRay = new Line(pFix, pFix + tFixInto);
                double perpDist = origRay.DistanceTo(pRef, false);

                if ((dirToRef * tFixInto) > 0.0 && perpDist <= effPosTol && Vector3d.VectorAngle(dirToRef, tFixInto) <= effAngleTolRad)
                {
                    TryAddLine(MakeOrientedLineCurve(pFix, pRef, modT1), "Analytical Line: Extend/Trim (Preserve G1/G2)");
                }
                else if (preserveOtherEnd == 1)
                {
                    TryAddLine(MakeOrientedLineCurve(pFix, pRef, modT1), "Analytical Line: Pivot (Preserve G0)");
                }
            }

            return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
        }

        private static (Curve Result, string ErrorMsg) SolveAnalyticalArcToFixedTarget(
            Curve origCrv, Arc origArc, bool modT1,
            Curve crvRef, bool refT1,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            GetOrientedEnds(origCrv, modT1, out Point3d pFix, out Point3d pWork, out Vector3d tFixInto, out Vector3d tWorkOut);

            double tRef = refT1 ? crvRef.Domain.T1 : crvRef.Domain.T0;
            Point3d pRef = refT1 ? crvRef.PointAtEnd : crvRef.PointAtStart;
            Vector3d tRefInto = refT1 ? -crvRef.TangentAtEnd : crvRef.TangentAtStart;
            tRefInto.Unitize();
            Vector3d curvRef = crvRef.CurvatureAt(tRef);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolRad = Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon);
            double effAngleTolDeg = RhinoMath.ToDegrees(effAngleTolRad);
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            if (continuity == 2 && curvRef.IsTiny())
                return (null, "Result cannot be created: an analytical arc cannot achieve Curvature (G2) continuity with a zero-curvature target.");

            BoundingBox refBounds = GetReferenceBounds(origCrv, crvRef);
            var candidates = new List<(Curve Crv, string Desc)>();

            void TryAddArc(ArcCurve ac, string desc)
            {
                if (ac == null) return;
                if (!IsWithinWorkingScale(refBounds, ac)) { ac.Dispose(); return; }
                if (continuity >= 1)
                {
                    Vector3d candOut = modT1 ? ac.TangentAtEnd : -ac.TangentAtStart;
                    candOut.Unitize();
                    if (Vector3d.VectorAngle(candOut, tRefInto) > effAngleTolRad) { ac.Dispose(); return; }
                }
                if (continuity == 2 && !AreCurvesAtContinuity(ac, modT1, crvRef, refT1, 2, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    ac.Dispose();
                    return;
                }
                candidates.Add((ac, desc));
            }

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: strict rigid transformations for G0/G1; length-preserving osculating radius match for G2
                if (continuity == 0)
                {
                    Arc transArc = origArc;
                    transArc.Transform(Transform.Translation(pRef - pWork));
                    TryAddArc(new ArcCurve(transArc), "Analytical Arc: Rigid Translate to G0 (Preserve None)");

                    Vector3d oldChord = pWork - pFix;
                    Vector3d newChord = pRef - pFix;
                    if (oldChord.Length > RhinoMath.ZeroTolerance && newChord.Length > RhinoMath.ZeroTolerance)
                    {
                        Transform rot = SafeRotation(oldChord, newChord, origArc.Plane.ZAxis, pWork);
                        Arc rotArc = origArc;
                        rotArc.Transform(Transform.Translation(pRef - pWork) * rot);
                        TryAddArc(new ArcCurve(rotArc), "Analytical Arc: Rigid Chord-Aligned Move to G0 (Preserve None)");
                    }

                    TryAddArc(MakeRigidAlignedArcCurve(origArc, modT1, pFix, pWork, tWorkOut, pRef, tRefInto, false),
                              "Analytical Arc: Rigid Tangent-Aligned Move to G0 (Preserve None)");
                }
                else if (continuity == 1)
                {
                    TryAddArc(MakeRigidAlignedArcCurve(origArc, modT1, pFix, pWork, tWorkOut, pRef, tRefInto, false),
                              "Analytical Arc: Rigid Move to G1 (Preserve None)");
                    TryAddArc(MakeRigidAlignedArcCurve(origArc, modT1, pFix, pWork, tWorkOut, pRef, tRefInto, true),
                              "Analytical Arc: Rigid Move + 3D Roll to G1 (Preserve None)");
                }
                else if (continuity == 2 && !curvRef.IsTiny())
                {
                    Vector3d curvPerp = curvRef - (curvRef * tRefInto) * tRefInto;
                    if (!curvPerp.IsTiny())
                    {
                        double kLen = curvPerp.Length;
                        double rOsc = 1.0 / kLen;
                        Point3d cOsc = pRef + curvPerp / (kLen * kLen);

                        // 1. Exact rigid move if radii already match within curvature tolerance
                        Vector3d cOrig = origArc.Center - pWork;
                        Vector3d cTarget = curvPerp;
                        if (cOrig.Unitize() && cTarget.Unitize())
                        {
                            Plane srcFrame = new Plane(pWork, tWorkOut, cOrig);
                            Plane dstFrame = new Plane(pRef, tRefInto, cTarget);
                            Arc moved = origArc;
                            moved.Transform(Transform.PlaneToPlane(srcFrame, dstFrame));
                            TryAddArc(new ArcCurve(moved), "Analytical Arc: Rigid Osculating-Frame Move to G2 (Preserve None)");
                        }

                        // 2. Match target osculating circle radius while preserving original arc length (unextended)
                        if (candidates.Count == 0)
                        {
                            TryAddArc(MakeLengthPreservingOsculatingArcCurve(origArc, modT1, pRef, tRefInto, cOsc, rOsc),
                                      "Analytical Arc: Length-Preserving Osculating Circle G2 (Preserve None)");
                        }
                    }
                }

                return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
            }

            // Tier <= 3: Preserve Curvature (locked circle, extend/trim only)
            if (Math.Abs(origArc.Center.DistanceTo(pRef) - origArc.Radius) <= effPosTol &&
                Math.Abs(origArc.Plane.DistanceTo(pRef)) <= effPosTol)
            {
                TryAddArc(MakeOrientedArcCurve(new Arc(pFix, tFixInto, pRef), modT1), "Analytical Arc: Extend/Trim (Preserve G2)");
            }

            // Tier <= 2: Preserve Tangency (scales radius & rolls around tFixInto in 3D)
            if (preserveOtherEnd <= 2)
            {
                TryAddArc(MakeOrientedArcCurve(new Arc(pFix, tFixInto, pRef), modT1), "Analytical Arc: Scale/Roll (Preserve G1)");
            }

            // Tier == 1: Preserve Position (pivots around pFix and/or scales radius)
            if (preserveOtherEnd == 1)
            {
                Arc rev = new Arc(pRef, -tRefInto, pFix);
                if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                {
                    var ac = new ArcCurve(rev);
                    if (modT1) ac.Reverse();
                    TryAddArc(ac, "Analytical Arc: Pivot/Scale to Target Tangent (Preserve G0)");
                }

                if (continuity == 0)
                {
                    TryAddArc(MakeSimilarityPivotedArcCurve(origArc, pFix, pWork, pRef),
                              "Analytical Arc: Similarity Pivot to G0 (Preserve G0)");

                    foreach (var pivArc in MakeFixedRadiusPivotedArcs(origArc, modT1, pFix, pWork, pRef))
                        TryAddArc(pivArc, "Analytical Arc: Fixed-Radius Pivot to G0 (Preserve G0)");
                }
            }

            return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
        }

        // ==============================================================================
        // BOTH-CURVE ANALYTICAL & HYBRID SOLVERS
        // ==============================================================================

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveAnalyticalBothLines(
            Curve crvA, Line lineA, bool modT1_A,
            Curve crvB, Line lineB, bool modT1_B,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, bool debug)
        {
            GetOrientedEnds(crvA, modT1_A, out Point3d pFixA, out Point3d pWorkA, out Vector3d tFixIntoA, out Vector3d tWorkOutA);
            GetOrientedEnds(crvB, modT1_B, out Point3d pFixB, out Point3d pWorkB, out Vector3d tFixIntoB, out Vector3d tWorkOutB);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolRad = Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon);
            Point3d ptAvg = 0.5 * (pWorkA + pWorkB);
            BoundingBox refBounds = GetReferenceBounds(crvA, crvB);

            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();

            void TryAddPair(LineCurve lcA, LineCurve lcB, string desc)
            {
                if (lcA == null || lcB == null) { lcA?.Dispose(); lcB?.Dispose(); return; }
                if (!IsWithinWorkingScale(refBounds, lcA) || !IsWithinWorkingScale(refBounds, lcB))
                {
                    lcA.Dispose(); lcB.Dispose();
                    return;
                }
                candidates.Add((lcA, lcB, desc));
            }

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: strict rigid transformations (exact lineA.Length & lineB.Length preserved)
                if (continuity == 0)
                {
                    TryAddPair(MakeOrientedLineCurve(pFixA + (ptAvg - pWorkA), ptAvg, modT1_A),
                               MakeOrientedLineCurve(pFixB + (ptAvg - pWorkB), ptAvg, modT1_B),
                               "Analytical Both Lines G0: Rigid Translate to Midpoint (Preserve None)");

                    Vector3d dirA = ptAvg - pFixA;
                    Vector3d dirB = ptAvg - pFixB;
                    if (dirA.Unitize() && dirB.Unitize())
                    {
                        TryAddPair(MakeOrientedLineCurve(ptAvg - lineA.Length * dirA, ptAvg, modT1_A),
                                   MakeOrientedLineCurve(ptAvg - lineB.Length * dirB, ptAvg, modT1_B),
                                   "Analytical Both Lines G0: Rigid Rotate & Shift to Midpoint (Preserve None)");
                    }
                }

                Vector3d flowDir = ComputeSharedFlowDirection(tWorkOutA, tWorkOutB, pFixA, pFixB, Vector3d.ZAxis);
                TryAddPair(MakeOrientedLineCurve(ptAvg - lineA.Length * flowDir, ptAvg, modT1_A),
                           MakeOrientedLineCurve(ptAvg + lineB.Length * flowDir, ptAvg, modT1_B),
                           "Analytical Both Lines G1/G2: Rigid Rotate & Shift to Shared Tangent (Preserve None)");

                return EvaluateAnalyticalPairCandidates(crvA, crvB, candidates, devTol, debug);
            }

            Line rayA = new Line(pFixA, pFixA + tFixIntoA);
            Line rayB = new Line(pFixB, pFixB + tFixIntoB);

            // Collinear facing lines (valid across G0, G1, G2 and Preserve <= 3)
            if (Vector3d.VectorAngle(tFixIntoA, -tFixIntoB) <= effAngleTolRad && rayA.DistanceTo(pFixB, false) <= effPosTol)
            {
                Point3d q = rayA.ClosestPoint(ptAvg, false);
                if (((q - pFixA) * tFixIntoA) > RhinoMath.ZeroTolerance && ((q - pFixB) * tFixIntoB) > RhinoMath.ZeroTolerance)
                {
                    TryAddPair(MakeOrientedLineCurve(pFixA, q, modT1_A),
                               MakeOrientedLineCurve(pFixB, q, modT1_B),
                               "Analytical Both Lines: Collinear Extend/Trim");
                }
            }

            Point3d? qRayInt = null;
            if (continuity == 0)
            {
                if (tFixIntoA.IsParallelTo(tFixIntoB, RhinoMath.SqrtEpsilon) == 0 &&
                    Intersection.LineLine(rayA, rayB, out double sA, out double sB, effPosTol, false))
                {
                    Point3d iA = rayA.PointAt(sA);
                    Point3d iB = rayB.PointAt(sB);
                    if (sA > RhinoMath.ZeroTolerance && sB > RhinoMath.ZeroTolerance && iA.DistanceTo(iB) <= effPosTol)
                    {
                        Point3d q = 0.5 * (iA + iB);
                        qRayInt = q;
                        TryAddPair(MakeOrientedLineCurve(pFixA, q, modT1_A),
                                   MakeOrientedLineCurve(pFixB, q, modT1_B),
                                   "Analytical Both Lines G0: Trim/Extend to Intersection");
                    }
                }

                if (preserveOtherEnd == 1)
                {
                    TryAddPair(MakeOrientedLineCurve(pFixA, ptAvg, modT1_A),
                               MakeOrientedLineCurve(pFixB, ptAvg, modT1_B),
                               "Analytical Both Lines G0: Pivot to Midpoint");

                    // Optimize 3D meet vertex Q so Preserve = Position minimizes bidirectional deviation
                    var qSeeds = new List<Point3d> { ptAvg };
                    if (qRayInt.HasValue) qSeeds.Add(0.5 * (ptAvg + qRayInt.Value));
                    double charLen = Math.Max(lineA.Length, lineB.Length);
                    if (charLen <= RhinoMath.ZeroTolerance) charLen = 1.0;

                    foreach (Point3d qSeed in qSeeds)
                    {
                        Point3d bestQ = qSeed;
                        double step = 0.08 * charLen;

                        double ScoreQ(Point3d candQ)
                        {
                            var lA = MakeOrientedLineCurve(pFixA, candQ, modT1_A);
                            var lB = MakeOrientedLineCurve(pFixB, candQ, modT1_B);
                            if (lA == null || lB == null) { lA?.Dispose(); lB?.Dispose(); return double.MaxValue; }
                            double sc = GetAverageDeviation(crvA, lA, 20) + GetAverageDeviation(crvB, lB, 20);
                            lA.Dispose(); lB.Dispose();
                            return sc;
                        }

                        double bestSc = ScoreQ(bestQ);
                        Vector3d[] axes = { Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis };
                        for (int iter = 0; iter < 16; iter++)
                        {
                            bool improved = false;
                            foreach (Vector3d ax in axes)
                            {
                                foreach (double sgn in new[] { 1.0, -1.0 })
                                {
                                    Point3d testQ = bestQ + (sgn * step) * ax;
                                    double sc = ScoreQ(testQ);
                                    if (sc < bestSc - RhinoMath.ZeroTolerance)
                                    {
                                        bestSc = sc;
                                        bestQ = testQ;
                                        improved = true;
                                        break;
                                    }
                                }
                            }
                            if (!improved)
                            {
                                step *= 0.5;
                                if (step < 1e-4 * charLen) break;
                            }
                        }

                        TryAddPair(MakeOrientedLineCurve(pFixA, bestQ, modT1_A),
                                   MakeOrientedLineCurve(pFixB, bestQ, modT1_B),
                                   "Analytical Both Lines G0: Optimized Pivot Vertex (Preserve G0)");
                    }
                }
            }

            if (preserveOtherEnd == 1)
            {
                Vector3d span = pFixB - pFixA;
                double spanLen = span.Length;
                if (spanLen > 2.0 * RhinoMath.ZeroTolerance)
                {
                    Vector3d axis = span / spanLen;
                    double s = Math.Max(0.1 * spanLen, Math.Min(0.9 * spanLen, (ptAvg - pFixA) * axis));
                    Point3d q = pFixA + s * axis;
                    TryAddPair(MakeOrientedLineCurve(pFixA, q, modT1_A),
                               MakeOrientedLineCurve(pFixB, q, modT1_B),
                               "Analytical Both Lines: Pivot Collinear (Preserve G0)");
                }
            }

            return EvaluateAnalyticalPairCandidates(crvA, crvB, candidates, devTol, debug);
        }

        private static (Curve ResultArc, Curve ResultLine, string ErrorMsg) SolveAnalyticalArcAndLine(
            Curve origArcCrv, Arc origArc, bool modT1_Arc,
            Curve origLineCrv, Line origLine, bool modT1_Line,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, bool debug)
        {
            if (continuity == 2)
                return (null, null, "Result cannot be created: an analytical arc and line cannot achieve Curvature (G2) continuity.");

            GetOrientedEnds(origArcCrv, modT1_Arc, out Point3d pFixArc, out Point3d pWorkArc, out Vector3d tFixArcInto, out Vector3d tWorkArcOut);
            GetOrientedEnds(origLineCrv, modT1_Line, out Point3d pFixLine, out Point3d pWorkLine, out Vector3d tFixLineInto, out Vector3d tWorkLineOut);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolRad = Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon);
            double effAngleTolDeg = RhinoMath.ToDegrees(effAngleTolRad);
            Point3d ptAvg = 0.5 * (pWorkArc + pWorkLine);
            BoundingBox refBounds = GetReferenceBounds(origArcCrv, origLineCrv);

            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: strict rigid transformations for BOTH Arc and Line (no scaling, no extending)
                void TryAddRigidPair(ArcCurve ac, LineCurve lc, string desc)
                {
                    if (ac == null || lc == null) { ac?.Dispose(); lc?.Dispose(); return; }
                    if (!IsWithinWorkingScale(refBounds, ac) || !IsWithinWorkingScale(refBounds, lc) ||
                        !AreCurvesAtContinuity(ac, modT1_Arc, lc, modT1_Line, continuity, effPosTol, effAngleTolDeg, 5.0))
                    {
                        ac.Dispose(); lc.Dispose();
                        return;
                    }
                    candidates.Add((ac, lc, desc));
                }

                if (continuity == 0)
                {
                    Arc transArc = origArc;
                    transArc.Transform(Transform.Translation(ptAvg - pWorkArc));
                    LineCurve transLine = MakeOrientedLineCurve(pFixLine + (ptAvg - pWorkLine), ptAvg, modT1_Line);
                    TryAddRigidPair(new ArcCurve(transArc), transLine, "Analytical Arc-Line G0: Rigid Translate Both to Midpoint (Preserve None)");
                }

                Vector3d flowDir = ComputeSharedFlowDirection(tWorkArcOut, tWorkLineOut, pFixArc, pFixLine, origArc.Plane.ZAxis);
                LineCurve rigidLine1 = MakeOrientedLineCurve(ptAvg + origLine.Length * flowDir, ptAvg, modT1_Line);
                ArcCurve rigidArc1 = MakeRigidAlignedArcCurve(origArc, modT1_Arc, pFixArc, pWorkArc, tWorkArcOut, ptAvg, flowDir, false);
                TryAddRigidPair(rigidArc1, rigidLine1, "Analytical Arc-Line G1: Rigid Move Both to Shared Tangent (Preserve None)");

                LineCurve rigidLine2 = MakeOrientedLineCurve(ptAvg + origLine.Length * flowDir, ptAvg, modT1_Line);
                ArcCurve rigidArc2 = MakeRigidAlignedArcCurve(origArc, modT1_Arc, pFixArc, pWorkArc, tWorkArcOut, ptAvg, flowDir, true);
                TryAddRigidPair(rigidArc2, rigidLine2, "Analytical Arc-Line G1: Rigid Move Both + 3D Roll (Preserve None)");

                return EvaluateAnalyticalPairCandidates(origArcCrv, origLineCrv, candidates, devTol, debug);
            }

            Line lineRay = new Line(pFixLine, pFixLine + tFixLineInto);
            string coplanarityHint = null;
            Point3d? bestKissQ = null;

            void TryAddArcLinePair(ArcCurve ac, Point3d q, string desc, bool requireSmoothG1)
            {
                if (ac == null) return;
                var lc = MakeOrientedLineCurve(pFixLine, q, modT1_Line);
                if (lc == null || !IsWithinWorkingScale(refBounds, ac) || !IsWithinWorkingScale(refBounds, lc))
                {
                    ac.Dispose(); lc?.Dispose();
                    return;
                }

                if (requireSmoothG1)
                {
                    Vector3d arcOut = modT1_Arc ? ac.TangentAtEnd : -ac.TangentAtStart;
                    arcOut.Unitize();
                    Vector3d lineIntoFromQ = pFixLine - q;
                    if (!lineIntoFromQ.Unitize() || Vector3d.VectorAngle(arcOut, lineIntoFromQ) > effAngleTolRad)
                    {
                        ac.Dispose(); lc.Dispose();
                        return;
                    }
                }

                candidates.Add((ac, lc, desc));
            }

            // Tier <= 3 (shared across G0 & G1): Existing circle already tangent to lineRay
            bool lineInArcPlane = Math.Abs(origArc.Plane.DistanceTo(pFixLine)) <= effPosTol &&
                                  Math.Abs(origArc.Plane.ZAxis * tFixLineInto) <= Math.Sin(effAngleTolRad);
            if (lineInArcPlane)
            {
                double sTan = (origArc.Center - pFixLine) * tFixLineInto;
                if (sTan > RhinoMath.ZeroTolerance)
                {
                    Point3d qTan = pFixLine + sTan * tFixLineInto;
                    if (Math.Abs(origArc.Center.DistanceTo(qTan) - origArc.Radius) <= effPosTol)
                    {
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, qTan), modT1_Arc),
                                          qTan, "Analytical Arc-Line: Extend/Trim to Existing Tangent (Preserve G2)", continuity >= 1);
                    }
                }
            }

            Point3d projFixOnLine = lineRay.ClosestPoint(pFixArc, false);
            Vector3d perpToLine = pFixArc - projFixOnLine;
            double perpDist = perpToLine.Length;
            Plane commonPlane = perpDist > effPosTol ? new Plane(projFixOnLine, tFixLineInto, perpToLine) : Plane.Unset;

            // Tier <= 2 helper (shared so G0 always includes G1 tangent-kiss solutions)
            void AddTangentKissCandidates(bool requireSmooth)
            {
                if (!commonPlane.IsValid)
                {
                    coplanarityHint = "Result cannot be created: arc opposite end lies on the infinite target line.";
                    return;
                }
                if (Math.Abs(commonPlane.ZAxis * tFixArcInto) > Math.Sin(effAngleTolRad))
                {
                    coplanarityHint = "Result cannot be created: arc opposite tangent and target line are skew (not coplanar) for Preserve Tangency.";
                    return;
                }

                Vector3d tInPlane = tFixArcInto - (tFixArcInto * commonPlane.ZAxis) * commonPlane.ZAxis;
                tInPlane.Unitize();

                bool isParallel = tInPlane.IsParallelTo(tFixLineInto, RhinoMath.SqrtEpsilon) != 0;
                Line tanRay = new Line(pFixArc, pFixArc + tInPlane);

                if (!isParallel && Intersection.LineLine(tanRay, lineRay, out double a, out _, RhinoMath.ZeroTolerance, false))
                {
                    Point3d ptVertex = tanRay.PointAt(a);
                    double dist = ptVertex.DistanceTo(pFixArc);
                    if (dist > RhinoMath.ZeroTolerance)
                    {
                        Point3d[] kissPts = { ptVertex + tFixLineInto * dist, ptVertex - tFixLineInto * dist };
                        foreach (Point3d q in kissPts)
                        {
                            if (((q - pFixLine) * tFixLineInto) <= RhinoMath.ZeroTolerance) continue;
                            int prevCount = candidates.Count;
                            TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc),
                                              q, "Analytical Arc-Line: Tangent Kiss (Preserve G1)", requireSmooth);
                            if (candidates.Count > prevCount && !bestKissQ.HasValue)
                                bestKissQ = q;
                        }
                    }
                }
                else if (((projFixOnLine - pFixLine) * tFixLineInto) > RhinoMath.ZeroTolerance)
                {
                    int prevCount = candidates.Count;
                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, projFixOnLine), modT1_Arc),
                                      projFixOnLine, "Analytical Arc-Line: Parallel Semicircle Kiss (Preserve G1)", requireSmooth);
                    if (candidates.Count > prevCount && !bestKissQ.HasValue)
                        bestKissQ = projFixOnLine;
                }
            }

            if (continuity == 0)
            {
                Circle origCircle = new Circle(origArc.Plane, origArc.Radius);
                var rc = Intersection.LineCircle(lineRay, origCircle, out double t1, out Point3d pt1, out double t2, out Point3d pt2);
                if (rc == LineCircleIntersection.Single || rc == LineCircleIntersection.Multiple)
                {
                    if (t1 > RhinoMath.ZeroTolerance && lineRay.PointAt(t1).DistanceTo(pt1) <= effPosTol)
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, pt1), modT1_Arc), pt1, "Analytical Arc-Line G0: Circle-Line Intersection 1 (Preserve G2)", false);
                    if (rc == LineCircleIntersection.Multiple && t2 > RhinoMath.ZeroTolerance && lineRay.PointAt(t2).DistanceTo(pt2) <= effPosTol)
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, pt2), modT1_Arc), pt2, "Analytical Arc-Line G0: Circle-Line Intersection 2 (Preserve G2)", false);
                }

                if (preserveOtherEnd <= 2)
                {
                    double[] sCands =
                    {
                        (pWorkArc - pFixLine) * tFixLineInto,
                        (ptAvg - pFixLine) * tFixLineInto,
                        (pWorkLine - pFixLine) * tFixLineInto
                    };
                    foreach (double s in sCands)
                    {
                        if (s > RhinoMath.ZeroTolerance)
                        {
                            Point3d q = pFixLine + s * tFixLineInto;
                            TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc),
                                              q, "Analytical Arc-Line G0: Scale/Roll to Line (Preserve G1)", false);
                        }
                    }
                    AddTangentKissCandidates(false);
                }

                if (preserveOtherEnd == 1)
                {
                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, ptAvg), modT1_Arc),
                                      ptAvg, "Analytical Arc-Line G0: Scale/Roll to Midpoint (Preserve G0)", false);
                    TryAddArcLinePair(MakeSimilarityPivotedArcCurve(origArc, pFixArc, pWorkArc, ptAvg),
                                      ptAvg, "Analytical Arc-Line G0: Similarity Pivot to Midpoint (Preserve G0)", false);

                    foreach (var pivArc in MakeFixedRadiusPivotedArcs(origArc, modT1_Arc, pFixArc, pWorkArc, ptAvg))
                        TryAddArcLinePair(pivArc, ptAvg, "Analytical Arc-Line G0: Fixed-Radius Pivot to Midpoint (Preserve G0)", false);
                }
            }
            else if (continuity == 1)
            {
                if (preserveOtherEnd <= 2)
                {
                    AddTangentKissCandidates(true);
                }

                if (preserveOtherEnd == 1)
                {
                    // 1A: Fixed-radius arc pivot around pFixArc to existing lineRay
                    if (commonPlane.IsValid && perpDist <= 2.0 * origArc.Radius + effPosTol)
                    {
                        double r = origArc.Radius;
                        Vector3d unitPerp = perpToLine / perpDist;
                        Circle cFix = new Circle(new Plane(pFixArc, commonPlane.XAxis, commonPlane.YAxis), r);
                        Line[] offsetLines =
                        {
                            new Line(projFixOnLine + unitPerp * r, projFixOnLine + unitPerp * r + tFixLineInto),
                            new Line(projFixOnLine - unitPerp * r, projFixOnLine - unitPerp * r + tFixLineInto)
                        };

                        foreach (Line offLine in offsetLines)
                        {
                            var rc = Intersection.LineCircle(offLine, cFix, out double ta, out Point3d c1, out double tb, out Point3d c2);
                            var centers = new List<Point3d>();
                            if ((rc == LineCircleIntersection.Single || rc == LineCircleIntersection.Multiple) && offLine.PointAt(ta).DistanceTo(c1) <= 1e-5)
                                centers.Add(c1);
                            if (rc == LineCircleIntersection.Multiple && offLine.PointAt(tb).DistanceTo(c2) <= 1e-5)
                                centers.Add(c2);

                            foreach (Point3d center in centers)
                            {
                                Point3d q = lineRay.ClosestPoint(center, false);
                                if (((q - pFixLine) * tFixLineInto) <= RhinoMath.ZeroTolerance) continue;

                                Arc rev = new Arc(q, tFixLineInto, pFixArc);
                                if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                                {
                                    var ac = new ArcCurve(rev);
                                    if (modT1_Arc) ac.Reverse();
                                    TryAddArcLinePair(ac, q, "Analytical Arc-Line G1: Fixed-Radius Arc Pivot (Preserve G0)", true);
                                }
                            }
                        }
                    }

                    // 1B: Pivot line around pFixLine to kiss existing circle
                    if (Math.Abs(origArc.Plane.DistanceTo(pFixLine)) <= effPosTol)
                    {
                        double r = origArc.Radius;
                        Vector3d u = pFixLine - origArc.Center;
                        double d = u.Length;
                        if (d > r + RhinoMath.ZeroTolerance)
                        {
                            u /= d;
                            Vector3d v = Vector3d.CrossProduct(origArc.Plane.ZAxis, u);
                            if (v.Unitize())
                            {
                                double cosA = r / d;
                                double sinA = Math.Sqrt(Math.Max(0.0, 1.0 - cosA * cosA));
                                Point3d[] touchPts =
                                {
                                    origArc.Center + r * (cosA * u + sinA * v),
                                    origArc.Center + r * (cosA * u - sinA * v)
                                };
                                foreach (Point3d q in touchPts)
                                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc),
                                                      q, "Analytical Arc-Line G1: Pivot Line to Existing Circle (Preserve G0)", true);
                            }
                        }
                    }

                    // 1C: 3D Pivot & Scale arc to lineRay
                    double[] sCands = { (pWorkArc - pFixLine) * tFixLineInto, (ptAvg - pFixLine) * tFixLineInto, (pWorkLine - pFixLine) * tFixLineInto };
                    foreach (double sVal in sCands)
                    {
                        if (sVal > RhinoMath.ZeroTolerance)
                        {
                            Point3d q = pFixLine + sVal * tFixLineInto;
                            Arc rev = new Arc(q, tFixLineInto, pFixArc);
                            if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                            {
                                var ac = new ArcCurve(rev);
                                if (modT1_Arc) ac.Reverse();
                                TryAddArcLinePair(ac, q, "Analytical Arc-Line G1: 3D Pivot & Scale to Line (Preserve G0)", true);
                            }
                        }
                    }

                    // 1D: Simultaneous 2D/3D optimization of join point Q (unlocks both line angle and arc start tangent)
                    AddOptimizedG1PositionArcLinePairs(
                        origArcCrv, origArc, modT1_Arc, pFixArc, pWorkArc,
                        origLineCrv, origLine, modT1_Line, pFixLine, pWorkLine, tFixLineInto,
                        commonPlane, effPosTol, bestKissQ, TryAddArcLinePair);
                }
            }

            if (candidates.Count == 0)
                return (null, null, coplanarityHint ?? "Result cannot be created analytically with the current constraints.");

            return EvaluateAnalyticalPairCandidates(origArcCrv, origLineCrv, candidates, devTol, debug);
        }

        private static void AddOptimizedG1PositionArcLinePairs(
            Curve origArcCrv, Arc origArc, bool modT1_Arc, Point3d pFixArc, Point3d pWorkArc,
            Curve origLineCrv, Line origLine, bool modT1_Line, Point3d pFixLine, Point3d pWorkLine, Vector3d tFixLineInto,
            Plane commonPlane, double effPosTol, Point3d? kissSeed,
            Action<ArcCurve, Point3d, string, bool> addArcLinePair)
        {
            Point3d ptAvg = 0.5 * (pWorkArc + pWorkLine);
            var seeds = new List<Point3d> { ptAvg };
            if (kissSeed.HasValue) seeds.Insert(0, kissSeed.Value);

            double sProj = (pWorkArc - pFixLine) * tFixLineInto;
            if (sProj > RhinoMath.ZeroTolerance)
                seeds.Add(pFixLine + sProj * tFixLineInto);

            bool isPlanar = commonPlane.IsValid &&
                            Math.Abs(commonPlane.DistanceTo(pWorkArc)) <= effPosTol &&
                            Math.Abs(origArc.Plane.ZAxis * commonPlane.ZAxis) >= 1.0 - 1e-4;

            Vector3d e1 = tFixLineInto;
            Vector3d e2 = isPlanar ? commonPlane.YAxis : Vector3d.CrossProduct(origArc.Plane.ZAxis, e1);
            if (!e2.Unitize())
            {
                e2 = Vector3d.CrossProduct(Math.Abs(e1.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis, e1);
                e2.Unitize();
            }
            Vector3d e3 = Vector3d.CrossProduct(e1, e2);
            e3.Unitize();

            double charLen = Math.Max(pFixArc.DistanceTo(pFixLine), Math.Max(origArc.Length, origLine.Length));
            if (charLen <= RhinoMath.ZeroTolerance) charLen = 1.0;

            foreach (Point3d qSeed in seeds)
            {
                Vector3d seedIntoArc = qSeed - pFixLine;
                if (seedIntoArc.Unitize())
                {
                    Arc initRev = new Arc(qSeed, seedIntoArc, pFixArc);
                    if (initRev.IsValid && initRev.Length > RhinoMath.ZeroTolerance)
                    {
                        var acInit = new ArcCurve(initRev);
                        if (modT1_Arc) acInit.Reverse();
                        addArcLinePair(acInit, qSeed, "Analytical Arc-Line G1: Seed Simultaneous Pivot (Preserve G0)", true);
                    }
                }

                int dim = isPlanar ? 2 : 3;
                double[] bestX = new double[dim];
                double step = 0.08 * charLen;

                (ArcCurve ArcC, LineCurve LineC, Point3d Q) BuildAt(double[] x)
                {
                    Point3d q = isPlanar
                        ? qSeed + x[0] * e1 + x[1] * e2
                        : qSeed + x[0] * e1 + x[1] * e2 + x[2] * e3;

                    Vector3d intoArc = q - pFixLine;
                    if (!intoArc.Unitize()) return (null, null, Point3d.Unset);

                    Arc rev = new Arc(q, intoArc, pFixArc);
                    if (!rev.IsValid || rev.Length <= RhinoMath.ZeroTolerance) return (null, null, Point3d.Unset);

                    var ac = new ArcCurve(rev);
                    if (modT1_Arc) ac.Reverse();
                    var lc = MakeOrientedLineCurve(pFixLine, q, modT1_Line);
                    if (lc == null) { ac.Dispose(); return (null, null, Point3d.Unset); }
                    return (ac, lc, q);
                }

                double EvalAt(double[] x)
                {
                    var built = BuildAt(x);
                    if (built.ArcC == null || built.LineC == null)
                    {
                        built.ArcC?.Dispose(); built.LineC?.Dispose();
                        return double.MaxValue;
                    }
                    double sc = GetAverageDeviation(origArcCrv, built.ArcC, 20) + GetAverageDeviation(origLineCrv, built.LineC, 20);
                    built.ArcC.Dispose();
                    built.LineC.Dispose();
                    return sc;
                }

                double bestScore = EvalAt(bestX);
                for (int iter = 0; iter < 18; iter++)
                {
                    bool improved = false;
                    for (int k = 0; k < dim; k++)
                    {
                        double origVal = bestX[k];
                        foreach (double sgn in new[] { 1.0, -1.0 })
                        {
                            bestX[k] = origVal + sgn * step;
                            double sc = EvalAt(bestX);
                            if (sc < bestScore - RhinoMath.ZeroTolerance)
                            {
                                bestScore = sc;
                                origVal = bestX[k];
                                improved = true;
                                break;
                            }
                            bestX[k] = origVal;
                        }
                    }
                    if (!improved)
                    {
                        step *= 0.5;
                        if (step < 1e-4 * charLen) break;
                    }
                }

                var optBuilt = BuildAt(bestX);
                if (optBuilt.ArcC != null)
                {
                    optBuilt.LineC?.Dispose();
                    addArcLinePair(optBuilt.ArcC, optBuilt.Q, "Analytical Arc-Line G1: Optimized Simultaneous Pivot (Preserve G0)", true);
                }
            }
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveAnalyticalBothArcs(
            Curve crvA, Arc arcA, bool modT1_A,
            Curve crvB, Arc arcB, bool modT1_B,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            GetOrientedEnds(crvA, modT1_A, out Point3d pFixA, out Point3d pWorkA, out Vector3d tFixIntoA, out Vector3d tWorkOutA);
            GetOrientedEnds(crvB, modT1_B, out Point3d pFixB, out Point3d pWorkB, out Vector3d tFixIntoB, out Vector3d tWorkOutB);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolRad = Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon);
            double effAngleTolDeg = RhinoMath.ToDegrees(effAngleTolRad);
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            Point3d ptAvg = 0.5 * (pWorkA + pWorkB);
            BoundingBox refBounds = GetReferenceBounds(crvA, crvB);
            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();

            void TryAddArcPair(ArcCurve acA, ArcCurve acB, string desc)
            {
                if (acA == null || acB == null) { acA?.Dispose(); acB?.Dispose(); return; }
                if (!IsWithinWorkingScale(refBounds, acA) || !IsWithinWorkingScale(refBounds, acB) ||
                    !AreCurvesAtContinuity(acA, modT1_A, acB, modT1_B, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    acA.Dispose(); acB.Dispose();
                    return;
                }
                candidates.Add((acA, acB, desc));
            }

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: strict rigid transformations for G0/G1; length-preserving shared osculating circle for G2
                Vector3d sharedFlow = ComputeSharedFlowDirection(tWorkOutA, tWorkOutB, pFixA, pFixB, arcA.Plane.ZAxis);

                if (continuity == 0)
                {
                    Arc mA = arcA; mA.Transform(Transform.Translation(ptAvg - pWorkA));
                    Arc mB = arcB; mB.Transform(Transform.Translation(ptAvg - pWorkB));
                    TryAddArcPair(new ArcCurve(mA), new ArcCurve(mB), "Analytical Both Arcs G0: Rigid Translate to Midpoint (Preserve None)");

                    TryAddArcPair(
                        MakeRigidAlignedArcCurve(arcA, modT1_A, pFixA, pWorkA, tWorkOutA, ptAvg, sharedFlow, false),
                        MakeRigidAlignedArcCurve(arcB, modT1_B, pFixB, pWorkB, tWorkOutB, ptAvg, -sharedFlow, false),
                        "Analytical Both Arcs G0: Rigid Tangent-Aligned Move (Preserve None)");
                }
                else if (continuity == 1)
                {
                    TryAddArcPair(
                        MakeRigidAlignedArcCurve(arcA, modT1_A, pFixA, pWorkA, tWorkOutA, ptAvg, sharedFlow, false),
                        MakeRigidAlignedArcCurve(arcB, modT1_B, pFixB, pWorkB, tWorkOutB, ptAvg, -sharedFlow, false),
                        "Analytical Both Arcs G1: Rigid Move to Shared Tangent (Preserve None)");

                    TryAddArcPair(
                        MakeRigidAlignedArcCurve(arcA, modT1_A, pFixA, pWorkA, tWorkOutA, ptAvg, sharedFlow, true),
                        MakeRigidAlignedArcCurve(arcB, modT1_B, pFixB, pWorkB, tWorkOutB, ptAvg, -sharedFlow, true),
                        "Analytical Both Arcs G1: Rigid Move + 3D Roll to Shared Tangent (Preserve None)");
                }
                else if (continuity == 2)
                {
                    Vector3d vCurvA = crvA.CurvatureAt(modT1_A ? crvA.Domain.T1 : crvA.Domain.T0);
                    Vector3d vCurvB = crvB.CurvatureAt(modT1_B ? crvB.Domain.T1 : crvB.Domain.T0);

                    // 1. Exact rigid co-osculating move when radii already match
                    double rMax = Math.Max(arcA.Radius, arcB.Radius);
                    if (rMax > RhinoMath.ZeroTolerance && Math.Abs(arcA.Radius - arcB.Radius) / rMax <= (effCurvTolPct / 100.0))
                    {
                        Vector3d cOrigA = arcA.Center - pWorkA; cOrigA.Unitize();
                        Vector3d cOrigB = arcB.Center - pWorkB; cOrigB.Unitize();
                        Vector3d cShared = cOrigA + cOrigB;
                        cShared -= (cShared * sharedFlow) * sharedFlow;
                        if (!cShared.Unitize())
                        {
                            cShared = cOrigA - (cOrigA * sharedFlow) * sharedFlow;
                            if (!cShared.Unitize())
                            {
                                cShared = Vector3d.CrossProduct(arcA.Plane.ZAxis, sharedFlow);
                                cShared.Unitize();
                            }
                        }

                        Plane srcA = new Plane(pWorkA, tWorkOutA, cOrigA);
                        Plane srcB = new Plane(pWorkB, -tWorkOutB, cOrigB);
                        Plane dst = new Plane(ptAvg, sharedFlow, cShared);

                        Arc mA = arcA; mA.Transform(Transform.PlaneToPlane(srcA, dst));
                        Arc mB = arcB; mB.Transform(Transform.PlaneToPlane(srcB, dst));
                        TryAddArcPair(new ArcCurve(mA), new ArcCurve(mB), "Analytical Both Arcs G2: Rigid Co-Osculating Move (Preserve None)");
                    }

                    // 2. Length-preserving shared osculating circle when radii differ
                    if (candidates.Count == 0)
                    {
                        Vector3d kAvg = 0.5 * (vCurvA + vCurvB);
                        Vector3d kPerp = kAvg - (kAvg * sharedFlow) * sharedFlow;

                        var centerDirs = new List<Vector3d>();
                        var radii = new List<double>();

                        if (!kPerp.IsTiny())
                        {
                            Vector3d cDir = kPerp / kPerp.Length;
                            centerDirs.Add(cDir);
                            radii.Add(1.0 / kPerp.Length);
                            radii.Add(0.5 * (arcA.Radius + arcB.Radius));
                        }
                        else
                        {
                            Vector3d cFallback = (arcA.Center - pWorkA) - ((arcA.Center - pWorkA) * sharedFlow) * sharedFlow;
                            if (!cFallback.Unitize())
                            {
                                cFallback = Vector3d.CrossProduct(arcA.Plane.ZAxis, sharedFlow);
                                cFallback.Unitize();
                            }
                            centerDirs.Add(cFallback);
                            centerDirs.Add(-cFallback);
                            radii.Add(0.5 * (arcA.Radius + arcB.Radius));
                        }

                        foreach (Vector3d cDir in centerDirs)
                        {
                            foreach (double rShared in radii)
                            {
                                if (rShared <= RhinoMath.ZeroTolerance) continue;
                                Point3d cShared = ptAvg + rShared * cDir;
                                ArcCurve acA = MakeLengthPreservingOsculatingArcCurve(arcA, modT1_A, ptAvg, sharedFlow, cShared, rShared);
                                ArcCurve acB = MakeLengthPreservingOsculatingArcCurve(arcB, modT1_B, ptAvg, -sharedFlow, cShared, rShared);
                                TryAddArcPair(acA, acB, "Analytical Both Arcs G2: Length-Preserving Shared Osculating Circle (Preserve None)");
                            }
                        }
                    }
                }

                return EvaluateAnalyticalPairCandidates(crvA, crvB, candidates, devTol, debug);
            }

            bool coplanarCircles = Math.Abs(arcA.Plane.DistanceTo(arcB.Center)) <= effPosTol &&
                                   arcA.Plane.ZAxis.IsParallelTo(arcB.Plane.ZAxis, effAngleTolRad) != 0;

            // Tier <= 3 (Hoisted across G0, G1, G2): Co-circular arcs sharing the exact same circle
            if (coplanarCircles && arcA.Center.DistanceTo(arcB.Center) <= effPosTol && Math.Abs(arcA.Radius - arcB.Radius) <= effPosTol)
            {
                Vector3d xA = pFixA - arcA.Center;
                if (xA.Unitize())
                {
                    Plane sweepPlane = new Plane(arcA.Center, xA, tFixIntoA);
                    Circle sharedCircle = new Circle(sweepPlane, arcA.Radius);

                    if (sharedCircle.ClosestParameter(pFixB, out double angFixB))
                    {
                        if (angFixB <= RhinoMath.SqrtEpsilon) angFixB = 2.0 * Math.PI;
                        double angMid = 0.5 * (angFixB + arcA.Angle - arcB.Angle);
                        if (angMid > RhinoMath.SqrtEpsilon && angMid < angFixB - RhinoMath.SqrtEpsilon)
                        {
                            Point3d qAng = sharedCircle.PointAt(angMid);
                            TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, qAng), modT1_A),
                                          MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, qAng), modT1_B),
                                          "Analytical Both Arcs: Co-Circular Angular Midpoint (Preserve G2)");
                        }
                    }

                    if (ptAvg.DistanceTo(arcA.Center) > RhinoMath.SqrtEpsilon && sharedCircle.ClosestParameter(ptAvg, out double tMid))
                    {
                        Point3d qProj = sharedCircle.PointAt(tMid);
                        TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, qProj), modT1_A),
                                      MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, qProj), modT1_B),
                                      "Analytical Both Arcs: Co-Circular Meet (Preserve G2)");
                    }
                }
            }

            void AddCircleTangencyCandidates()
            {
                if (!coplanarCircles) return;
                Vector3d cc = arcB.Center - arcA.Center;
                double distC = cc.Length;
                if (distC <= RhinoMath.ZeroTolerance) return;

                Vector3d dirC = cc / distC;
                if (Math.Abs(distC - (arcA.Radius + arcB.Radius)) <= effPosTol)
                {
                    Point3d q = arcA.Center + arcA.Radius * dirC;
                    TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, q), modT1_A),
                                  MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, q), modT1_B),
                                  "Analytical Both Arcs: External Circle Tangency (Preserve G2)");
                }
                else if (Math.Abs(distC - Math.Abs(arcA.Radius - arcB.Radius)) <= effPosTol)
                {
                    Point3d q = arcA.Radius >= arcB.Radius ? arcA.Center + arcA.Radius * dirC : arcA.Center - arcA.Radius * dirC;
                    TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, q), modT1_A),
                                  MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, q), modT1_B),
                                  "Analytical Both Arcs: Internal Circle Tangency (Preserve G2)");
                }
            }

            bool TryComputeBiArcKiss(out Point3d qKiss, out Vector3d flowKiss)
            {
                qKiss = Point3d.Unset;
                flowKiss = Vector3d.Unset;

                Vector3d w = pFixB - pFixA;
                Vector3d s = tFixIntoB - tFixIntoA;
                double a = 4.0 - s.SquareLength;
                double ws = w * s;
                double ww = w.SquareLength;
                double d = -1.0;

                if (a > RhinoMath.SqrtEpsilon)
                    d = (ws + Math.Sqrt(ws * ws + a * ww)) / a;
                else if (ws < -RhinoMath.ZeroTolerance)
                    d = -ww / (2.0 * ws);

                if (d <= RhinoMath.ZeroTolerance) return false;

                Point3d mA = pFixA + d * tFixIntoA;
                Point3d mB = pFixB + d * tFixIntoB;
                qKiss = 0.5 * (mA + mB);

                Arc testA = new Arc(pFixA, tFixIntoA, qKiss);
                if (!testA.IsValid || testA.Length <= RhinoMath.ZeroTolerance) return false;

                var acTest = new ArcCurve(testA);
                flowKiss = acTest.TangentAtEnd;
                flowKiss.Unitize();
                acTest.Dispose();
                return true;
            }

            // Tier <= 2 (shared across G0, G1, G2): Single circle tangent to BOTH (pFixA, tFixIntoA) and (pFixB, tFixIntoB)
            if (preserveOtherEnd <= 2 && pFixA.DistanceTo(pFixB) > effPosTol)
            {
                Arc lockedSpan = new Arc(pFixA, tFixIntoA, pFixB);
                if (lockedSpan.IsValid && lockedSpan.Angle > 1e-4)
                {
                    var acSpan = new ArcCurve(lockedSpan);
                    Vector3d endIntoFromB = -acSpan.TangentAtEnd;
                    endIntoFromB.Unitize();
                    acSpan.Dispose();

                    if (Vector3d.VectorAngle(endIntoFromB, tFixIntoB) <= effAngleTolRad)
                    {
                        Circle lockedCircle = new Circle(lockedSpan.Plane, lockedSpan.Radius);
                        if (lockedCircle.ClosestParameter(ptAvg, out double tMid))
                        {
                            Point3d qLocked = lockedCircle.PointAt(tMid);
                            TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, qLocked), modT1_A),
                                          MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, qLocked), modT1_B),
                                          "Analytical Both Arcs: Symmetric Shared Circle (Preserve G1)");
                        }
                    }
                }
            }

            if (continuity == 0)
            {
                if (coplanarCircles)
                {
                    Circle cA = new Circle(arcA.Plane, arcA.Radius);
                    Circle cB = new Circle(new Plane(arcB.Center, arcA.Plane.XAxis, arcA.Plane.YAxis), arcB.Radius);
                    var rc = Intersection.CircleCircle(cA, cB, out Point3d pt1, out Point3d pt2);
                    if (rc == CircleCircleIntersection.Single || rc == CircleCircleIntersection.Multiple)
                    {
                        TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, pt1), modT1_A),
                                      MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, pt1), modT1_B),
                                      "Analytical Both Arcs G0: Circle-Circle Intersection 1 (Preserve G2)");
                        if (rc == CircleCircleIntersection.Multiple)
                        {
                            TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, pt2), modT1_A),
                                          MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, pt2), modT1_B),
                                          "Analytical Both Arcs G0: Circle-Circle Intersection 2 (Preserve G2)");
                        }
                    }
                }
                AddCircleTangencyCandidates();

                if (preserveOtherEnd <= 2)
                {
                    TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, ptAvg), modT1_A),
                                  MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, ptAvg), modT1_B),
                                  "Analytical Both Arcs G0: Scale/Roll to Midpoint (Preserve G1)");

                    if (TryComputeBiArcKiss(out Point3d qBi, out _))
                    {
                        TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, qBi), modT1_A),
                                      MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, qBi), modT1_B),
                                      "Analytical Both Arcs G0: Bi-Arc Kiss (Preserve G1)");
                    }
                }

                if (preserveOtherEnd == 1)
                {
                    TryAddArcPair(MakeSimilarityPivotedArcCurve(arcA, pFixA, pWorkA, ptAvg),
                                  MakeSimilarityPivotedArcCurve(arcB, pFixB, pWorkB, ptAvg),
                                  "Analytical Both Arcs G0: Similarity Pivot to Midpoint (Preserve G0)");

                    var pivListA = MakeFixedRadiusPivotedArcs(arcA, modT1_A, pFixA, pWorkA, ptAvg);
                    var pivListB = MakeFixedRadiusPivotedArcs(arcB, modT1_B, pFixB, pWorkB, ptAvg);
                    foreach (var pA in pivListA)
                    {
                        foreach (var pB in pivListB)
                        {
                            TryAddArcPair((ArcCurve)pA.DuplicateCurve(), (ArcCurve)pB.DuplicateCurve(),
                                          "Analytical Both Arcs G0: Fixed-Radius Pivot to Midpoint (Preserve G0)");
                        }
                    }
                    foreach (var pA in pivListA) pA.Dispose();
                    foreach (var pB in pivListB) pB.Dispose();
                }
            }
            else if (continuity == 1)
            {
                AddCircleTangencyCandidates();

                bool hasBiArc = TryComputeBiArcKiss(out Point3d qBiArc, out Vector3d flowBiArc);
                if (preserveOtherEnd <= 2 && hasBiArc)
                {
                    TryAddArcPair(MakeOrientedArcCurve(new Arc(pFixA, tFixIntoA, qBiArc), modT1_A),
                                  MakeOrientedArcCurve(new Arc(pFixB, tFixIntoB, qBiArc), modT1_B),
                                  "Analytical Both Arcs G1: 3D Bi-Arc Kiss (Preserve G1)");
                }

                if (preserveOtherEnd == 1)
                {
                    AddOptimizedG1PositionArcPairs(
                        crvA, arcA, modT1_A, pFixA, pWorkA, tWorkOutA,
                        crvB, arcB, modT1_B, pFixB, pWorkB, tWorkOutB,
                        coplanarCircles, effPosTol, hasBiArc, qBiArc, flowBiArc, TryAddArcPair);
                }
            }
            else if (continuity == 2)
            {
                if (preserveOtherEnd == 1)
                {
                    AddSharedCircleG2ArcPairs(
                        crvA, arcA, modT1_A, pFixA, pWorkA,
                        crvB, arcB, modT1_B, pFixB, pWorkB,
                        coplanarCircles, effPosTol, TryAddArcPair);
                }
            }

            return EvaluateAnalyticalPairCandidates(crvA, crvB, candidates, devTol, debug);
        }

        /// <summary>
        /// Optimizes the join point Q and shared tangent direction for Arc-Arc G1 + Preserve Position,
        /// unlocking both outer tangents to achieve lower deviation than the Preserve Tangency bi-arc.
        /// </summary>
        private static void AddOptimizedG1PositionArcPairs(
            Curve crvA, Arc arcA, bool modT1_A, Point3d pFixA, Point3d pWorkA, Vector3d tWorkOutA,
            Curve crvB, Arc arcB, bool modT1_B, Point3d pFixB, Point3d pWorkB, Vector3d tWorkOutB,
            bool coplanarCircles, double effPosTol,
            bool hasBiArc, Point3d qBiArc, Vector3d flowBiArc,
            Action<ArcCurve, ArcCurve, string> addPair)
        {
            Point3d ptAvg = 0.5 * (pWorkA + pWorkB);
            Vector3d avgFlow = ComputeSharedFlowDirection(tWorkOutA, tWorkOutB, pFixA, pFixB, arcA.Plane.ZAxis);

            var seeds = new List<(Point3d Q, Vector3d Flow)>();
            if (hasBiArc) seeds.Add((qBiArc, flowBiArc));
            seeds.Add((ptAvg, avgFlow));

            bool isPlanar = coplanarCircles ||
                            (Math.Abs(arcA.Plane.DistanceTo(pFixB)) <= effPosTol &&
                             Math.Abs(arcA.Plane.DistanceTo(pWorkB)) <= effPosTol);

            double charLen = Math.Max(pFixA.DistanceTo(pFixB), Math.Max(arcA.Length, arcB.Length));
            if (charLen <= RhinoMath.ZeroTolerance) charLen = 1.0;

            foreach (var seed in seeds)
            {
                // Always include the unoptimized seed first (both arcs start at seed.Q and end at pFix, so reverse iff modT1 is true)
                Arc initRevA = new Arc(seed.Q, -seed.Flow, pFixA);
                Arc initRevB = new Arc(seed.Q, seed.Flow, pFixB);
                if (initRevA.IsValid && initRevB.IsValid &&
                    initRevA.Length > RhinoMath.ZeroTolerance && initRevB.Length > RhinoMath.ZeroTolerance)
                {
                    var acInitA = new ArcCurve(initRevA); if (modT1_A) acInitA.Reverse();
                    var acInitB = new ArcCurve(initRevB); if (modT1_B) acInitB.Reverse();
                    addPair(acInitA, acInitB, "Analytical Both Arcs G1: Seed Pivot to Shared Tangent (Preserve G0)");
                }

                // Orthonormal frame at seed: e1 = seed.Flow, e3 = plane normal, e2 = e3 x e1 (in-plane)
                Vector3d e1 = seed.Flow;
                e1.Unitize();
                Vector3d refNormal = arcA.Plane.ZAxis;
                Vector3d e2 = Vector3d.CrossProduct(refNormal, e1);
                if (!e2.Unitize())
                {
                    e2 = Vector3d.CrossProduct(Math.Abs(e1.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis, e1);
                    e2.Unitize();
                }
                Vector3d e3 = Vector3d.CrossProduct(e1, e2);
                e3.Unitize();

                int dim = isPlanar ? 3 : 5; // [du, dv, dYaw] or [du, dv, dw, dYaw, dPitch]
                double[] bestX = new double[dim];
                double[] step = isPlanar
                    ? new[] { 0.08 * charLen, 0.08 * charLen, 0.20 }
                    : new[] { 0.08 * charLen, 0.08 * charLen, 0.08 * charLen, 0.20, 0.20 };

                (ArcCurve A, ArcCurve B) BuildFromState(double[] x)
                {
                    Point3d q;
                    Vector3d flow;
                    if (isPlanar)
                    {
                        q = seed.Q + x[0] * e1 + x[1] * e2;
                        flow = Math.Cos(x[2]) * e1 + Math.Sin(x[2]) * e2;
                    }
                    else
                    {
                        q = seed.Q + x[0] * e1 + x[1] * e2 + x[2] * e3;
                        double cy = Math.Cos(x[3]), sy = Math.Sin(x[3]);
                        double cp = Math.Cos(x[4]), sp = Math.Sin(x[4]);
                        flow = (cp * cy) * e1 + (cp * sy) * e2 + sp * e3;
                    }
                    if (!flow.Unitize()) return (null, null);

                    Arc rA = new Arc(q, -flow, pFixA);
                    Arc rB = new Arc(q, flow, pFixB);
                    if (!rA.IsValid || !rB.IsValid || rA.Length <= RhinoMath.ZeroTolerance || rB.Length <= RhinoMath.ZeroTolerance)
                        return (null, null);

                    var cA = new ArcCurve(rA); if (modT1_A) cA.Reverse();
                    var cB = new ArcCurve(rB); if (modT1_B) cB.Reverse();
                    return (cA, cB);
                }

                double EvalState(double[] x)
                {
                    var pair = BuildFromState(x);
                    if (pair.A == null || pair.B == null)
                    {
                        pair.A?.Dispose(); pair.B?.Dispose();
                        return double.MaxValue;
                    }
                    double score = GetAverageDeviation(crvA, pair.A, 20) + GetAverageDeviation(crvB, pair.B, 20);
                    pair.A.Dispose();
                    pair.B.Dispose();
                    return score;
                }

                double bestScore = EvalState(bestX);
                for (int iter = 0; iter < 18; iter++)
                {
                    bool improved = false;
                    for (int k = 0; k < dim; k++)
                    {
                        double origVal = bestX[k];
                        foreach (double sgn in new[] { 1.0, -1.0 })
                        {
                            bestX[k] = origVal + sgn * step[k];
                            double s = EvalState(bestX);
                            if (s < bestScore - RhinoMath.ZeroTolerance)
                            {
                                bestScore = s;
                                origVal = bestX[k];
                                improved = true;
                                break;
                            }
                            bestX[k] = origVal;
                        }
                    }
                    if (!improved)
                    {
                        for (int k = 0; k < dim; k++) step[k] *= 0.5;
                        if (step[0] < 1e-4 * charLen && step[dim - 1] < 1e-4) break;
                    }
                }

                var optPair = BuildFromState(bestX);
                if (optPair.A != null && optPair.B != null)
                {
                    addPair(optPair.A, optPair.B, "Analytical Both Arcs G1: Optimized Pivot & Scale (Preserve G0)");
                }
                else
                {
                    optPair.A?.Dispose();
                    optPair.B?.Dispose();
                }
            }
        }

        /// <summary>
        /// Solves for the minimum-deviation shared circle passing through pFixA and pFixB
        /// so two ArcCurves achieve exact Curvature (G2) continuity under Preserve = Position.
        /// </summary>
        private static void AddSharedCircleG2ArcPairs(
            Curve crvA, Arc arcA, bool modT1_A, Point3d pFixA, Point3d pWorkA,
            Curve crvB, Arc arcB, bool modT1_B, Point3d pFixB, Point3d pWorkB,
            bool coplanarCircles, double effPosTol,
            Action<ArcCurve, ArcCurve, string> addPair)
        {
            Vector3d chord = pFixB - pFixA;
            double chordLen = chord.Length;
            Point3d ptAvg = 0.5 * (pWorkA + pWorkB);

            if (chordLen <= 2.0 * effPosTol)
            {
                if (pFixA.DistanceTo(ptAvg) > effPosTol)
                {
                    Vector3d n = arcA.Plane.ZAxis;
                    Vector3d u = ptAvg - pFixA;
                    u.Unitize();
                    Vector3d tan = Vector3d.CrossProduct(n, u);
                    if (tan.Unitize())
                    {
                        addPair(MakeOrientedArcCurve(new Arc(pFixA, tan, ptAvg), modT1_A),
                                MakeOrientedArcCurve(new Arc(pFixB, -tan, ptAvg), modT1_B),
                                "Analytical Both Arcs G2: Closed-Loop Shared Circle (Preserve G0)");
                    }
                }
                return;
            }

            Point3d mid = 0.5 * (pFixA + pFixB);
            double h = 0.5 * chordLen;
            Vector3d uHat = chord / chordLen;

            bool isPlanar = coplanarCircles ||
                            (Math.Abs(arcA.Plane.DistanceTo(pFixB)) <= effPosTol &&
                             Math.Abs(arcA.Plane.DistanceTo(pWorkB)) <= effPosTol);

            Vector3d vHat0 = Vector3d.Unset;
            if (isPlanar)
            {
                Vector3d n = arcA.Plane.ZAxis;
                n -= (n * uHat) * uHat;
                if (n.Unitize())
                    vHat0 = Vector3d.CrossProduct(n, uHat);
            }

            if (!vHat0.IsValid || !vHat0.Unitize())
            {
                Vector3d vA = (pWorkA - mid) - ((pWorkA - mid) * uHat) * uHat;
                Vector3d vB = (pWorkB - mid) - ((pWorkB - mid) * uHat) * uHat;
                vHat0 = (vA * vB) >= 0.0 ? (vA + vB) : (vA - vB);
                if (!vHat0.Unitize())
                {
                    Vector3d nFallback = arcA.Plane.ZAxis - (arcA.Plane.ZAxis * uHat) * uHat;
                    if (!nFallback.Unitize())
                        nFallback = Vector3d.CrossProduct(uHat, Math.Abs(uHat.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis);
                    nFallback.Unitize();
                    vHat0 = Vector3d.CrossProduct(nFallback, uHat);
                    vHat0.Unitize();
                }
            }

            Vector3d nPlane0 = Vector3d.CrossProduct(uHat, vHat0);
            nPlane0.Unitize();

            (ArcCurve A, ArcCurve B) BuildSharedCirclePair(double sagitta, double splitDelta, double rollRad)
            {
                if (Math.Abs(sagitta) <= 1e-5 * h) return (null, null);

                Vector3d vHat = isPlanar ? vHat0 : (Math.Cos(rollRad) * vHat0 + Math.Sin(rollRad) * nPlane0);
                Vector3d nPlane = Vector3d.CrossProduct(uHat, vHat);
                if (!nPlane.Unitize()) return (null, null);

                double yc = (sagitta * sagitta - h * h) / (2.0 * sagitta);
                Point3d center = mid + yc * vHat;
                double r = (h * h + sagitta * sagitta) / (2.0 * Math.Abs(sagitta));

                Vector3d tA, tB;
                if (sagitta > 0.0)
                {
                    tA = Vector3d.CrossProduct(pFixA - center, nPlane);
                    tB = Vector3d.CrossProduct(nPlane, pFixB - center);
                }
                else
                {
                    tA = Vector3d.CrossProduct(nPlane, pFixA - center);
                    tB = Vector3d.CrossProduct(pFixB - center, nPlane);
                }
                if (!tA.Unitize() || !tB.Unitize()) return (null, null);

                Arc fullArc = new Arc(pFixA, tA, pFixB);
                if (!fullArc.IsValid || fullArc.Angle <= 1e-4) return (null, null);

                Vector3d xAxis = (pFixA - center) / r;
                Vector3d yAxis = tA;
                double thetaTotal = fullArc.Angle;

                double ProjectAngle(Point3d pt)
                {
                    Vector3d rel = pt - center;
                    double ang = Math.Atan2(rel * yAxis, rel * xAxis);
                    if (ang < 0.0) ang += 2.0 * Math.PI;
                    return Math.Max(0.03 * thetaTotal, Math.Min(0.97 * thetaTotal, ang));
                }

                double baseAngQ = 0.5 * (ProjectAngle(pWorkA) + ProjectAngle(pWorkB));
                double angQ = Math.Max(0.03 * thetaTotal, Math.Min(0.97 * thetaTotal, baseAngQ + splitDelta * thetaTotal));
                Point3d q = center + r * (Math.Cos(angQ) * xAxis + Math.Sin(angQ) * yAxis);

                ArcCurve acA = MakeOrientedArcCurve(new Arc(pFixA, tA, q), modT1_A);
                ArcCurve acB = MakeOrientedArcCurve(new Arc(pFixB, tB, q), modT1_B);
                return (acA, acB);
            }

            double EvalShared(double sagitta, double splitDelta, double rollRad)
            {
                var pair = BuildSharedCirclePair(sagitta, splitDelta, rollRad);
                if (pair.A == null || pair.B == null)
                {
                    pair.A?.Dispose(); pair.B?.Dispose();
                    return double.MaxValue;
                }
                double score = GetAverageDeviation(crvA, pair.A, 20) + GetAverageDeviation(crvB, pair.B, 20);
                pair.A.Dispose();
                pair.B.Dispose();
                return score;
            }

            var sagittaSeeds = new List<double>();
            Point3d[] fitPts = { pWorkA, pWorkB, ptAvg };
            foreach (Point3d fp in fitPts)
            {
                double x = (fp - mid) * uHat;
                double y = (fp - mid) * vHat0;
                if (Math.Abs(y) > 1e-4 * h)
                {
                    double yc = (x * x + y * y - h * h) / (2.0 * y);
                    double r = Math.Sqrt(h * h + yc * yc);
                    sagittaSeeds.Add(yc + Math.Sign(y) * r);
                }
            }

            for (int i = 1; i <= 24; i++)
            {
                double frac = 0.03 * Math.Pow(180.0, (i - 1) / 23.0);
                sagittaSeeds.Add(frac * h);
                sagittaSeeds.Add(-frac * h);
            }

            double bestPosB = 0.5 * h, bestPosScore = double.MaxValue;
            double bestNegB = -0.5 * h, bestNegScore = double.MaxValue;

            foreach (double b in sagittaSeeds)
            {
                double sc = EvalShared(b, 0.0, 0.0);
                if (b > 0.0 && sc < bestPosScore) { bestPosScore = sc; bestPosB = b; }
                else if (b < 0.0 && sc < bestNegScore) { bestNegScore = sc; bestNegB = b; }
            }

            foreach (double initB in new[] { bestPosB, bestNegB })
            {
                double curB = initB;
                double curSplit = 0.0;
                double curRoll = 0.0;
                double curScore = EvalShared(curB, curSplit, curRoll);
                if (curScore == double.MaxValue) continue;

                double stepB = 0.15 * Math.Max(Math.Abs(curB), 0.2 * h);
                double stepSplit = 0.08;
                double stepRoll = 0.20;

                for (int iter = 0; iter < 20; iter++)
                {
                    bool improved = false;
                    foreach (double sgn in new[] { 1.0, -1.0 })
                    {
                        double candB = curB + sgn * stepB;
                        if (Math.Sign(candB) == Math.Sign(initB) && Math.Abs(candB) > 1e-4 * h)
                        {
                            double s = EvalShared(candB, curSplit, curRoll);
                            if (s < curScore - RhinoMath.ZeroTolerance)
                            {
                                curScore = s; curB = candB; improved = true; break;
                            }
                        }
                    }
                    foreach (double sgn in new[] { 1.0, -1.0 })
                    {
                        double candSplit = Math.Max(-0.45, Math.Min(0.45, curSplit + sgn * stepSplit));
                        double s = EvalShared(curB, candSplit, curRoll);
                        if (s < curScore - RhinoMath.ZeroTolerance)
                        {
                            curScore = s; curSplit = candSplit; improved = true; break;
                        }
                    }
                    if (!isPlanar)
                    {
                        foreach (double sgn in new[] { 1.0, -1.0 })
                        {
                            double candRoll = curRoll + sgn * stepRoll;
                            double s = EvalShared(curB, curSplit, candRoll);
                            if (s < curScore - RhinoMath.ZeroTolerance)
                            {
                                curScore = s; curRoll = candRoll; improved = true; break;
                            }
                        }
                    }
                    if (!improved)
                    {
                        stepB *= 0.5;
                        stepSplit *= 0.5;
                        stepRoll *= 0.5;
                        if (stepB < 1e-4 * h && stepSplit < 1e-4 && (isPlanar || stepRoll < 1e-4)) break;
                    }
                }

                var optPair = BuildSharedCirclePair(curB, curSplit, curRoll);
                if (optPair.A != null && optPair.B != null)
                {
                    addPair(optPair.A, optPair.B, "Analytical Both Arcs G2: Shared Circle (Preserve G0)");
                }
                else
                {
                    optPair.A?.Dispose();
                    optPair.B?.Dispose();
                }
            }
        }

        private static (Curve ResultPrim, Curve ResultNurbs, string ErrorMsg) SolveHybridPrimitiveAndNurbs(
            Curve primCrv, bool isArc, Arc origArc, Line origLine, bool modT1_Prim,
            Curve nurbsCrv, bool modT1_Nurbs,
            int continuity, int preserveOtherEnd, int lineMode, int arcMode, int incrAsNeeded, double devTol,
            bool preventExtraInflections, bool preserveInternalContinuity, int maxInternalContinuity,
            double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            GetOrientedEnds(primCrv, modT1_Prim, out Point3d pFixPrim, out Point3d pWorkPrim, out Vector3d tFixPrimInto, out Vector3d tWorkPrimOut);
            GetOrientedEnds(nurbsCrv, modT1_Nurbs, out Point3d pFixNurbs, out Point3d pWorkNurbs, out _, out Vector3d tWorkNurbsOut);

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            Point3d ptAvg = 0.5 * (pWorkPrim + pWorkNurbs);
            Point3d[] blendPts =
            {
                ptAvg,
                pWorkPrim + 0.25 * (pWorkNurbs - pWorkPrim),
                pWorkPrim + 0.75 * (pWorkNurbs - pWorkPrim)
            };
            BoundingBox refBounds = GetReferenceBounds(primCrv, nurbsCrv);
            var primTargets = new List<(Curve CandPrim, string Desc)>();
            var fallbackPrimTargets = new List<(Curve CandPrim, string Desc)>();

            void AddPrimTarget(Curve cand, string desc, bool isFallback = false)
            {
                if (cand != null && IsWithinWorkingScale(refBounds, cand))
                {
                    if (isFallback) fallbackPrimTargets.Add((cand, desc));
                    else primTargets.Add((cand, desc));
                }
                else
                {
                    cand?.Dispose();
                }
            }

            if (preserveOtherEnd == 0)
            {
                // Preserve = None: primitive undergoes rigid transformations first (exact Length & Radius preserved)
                Vector3d sharedFlow = ComputeSharedFlowDirection(
                    tWorkPrimOut, tWorkNurbsOut, pFixPrim, pFixNurbs, isArc ? origArc.Plane.ZAxis : Vector3d.ZAxis);

                if (!isArc)
                {
                    if (continuity == 0)
                    {
                        AddPrimTarget(MakeOrientedLineCurve(pFixPrim + (ptAvg - pWorkPrim), ptAvg, modT1_Prim),
                                      "Hybrid Line-NURBS G0: Rigid Translate Line (Preserve None)");
                    }
                    AddPrimTarget(MakeOrientedLineCurve(ptAvg - origLine.Length * sharedFlow, ptAvg, modT1_Prim),
                                  "Hybrid Line-NURBS: Rigid Align Line (Preserve None)");
                }
                else
                {
                    if (continuity == 0)
                    {
                        Arc moved = origArc;
                        moved.Transform(Transform.Translation(ptAvg - pWorkPrim));
                        AddPrimTarget(new ArcCurve(moved), "Hybrid Arc-NURBS G0: Rigid Translate Arc (Preserve None)");
                    }
                    AddPrimTarget(MakeRigidAlignedArcCurve(origArc, modT1_Prim, pFixPrim, pWorkPrim, tWorkPrimOut, ptAvg, sharedFlow, false),
                                  "Hybrid Arc-NURBS: Rigid Move Arc (Preserve None)");
                    AddPrimTarget(MakeRigidAlignedArcCurve(origArc, modT1_Prim, pFixPrim, pWorkPrim, tWorkPrimOut, ptAvg, sharedFlow, true),
                                  "Hybrid Arc-NURBS: Rigid Move + 3D Roll Arc (Preserve None)");

                    if (continuity == 2)
                    {
                        // Length-preserving osculating fallback if NURBS constraints reject matching 1/origArc.Radius
                        Vector3d kArc = primCrv.CurvatureAt(modT1_Prim ? primCrv.Domain.T1 : primCrv.Domain.T0);
                        Vector3d kNurbs = nurbsCrv.CurvatureAt(modT1_Nurbs ? nurbsCrv.Domain.T1 : nurbsCrv.Domain.T0);
                        Vector3d kAvg = 0.5 * (kArc + kNurbs);
                        Vector3d kPerp = kAvg - (kAvg * sharedFlow) * sharedFlow;
                        if (!kPerp.IsTiny())
                        {
                            double rOsc = 1.0 / kPerp.Length;
                            Point3d cOsc = ptAvg + (kPerp / kPerp.Length) * rOsc;
                            AddPrimTarget(MakeLengthPreservingOsculatingArcCurve(origArc, modT1_Prim, ptAvg, sharedFlow, cOsc, rOsc),
                                          "Hybrid Arc-NURBS G2: Length-Preserving Shared Osculating Arc (Preserve None)", true);
                        }
                    }
                }
            }
            else if (!isArc) // Line (Keep as line) + NURBS (Preserve >= 1)
            {
                double[] sVals =
                {
                    (ptAvg - pFixPrim) * tFixPrimInto,
                    (pWorkNurbs - pFixPrim) * tFixPrimInto,
                    (blendPts[1] - pFixPrim) * tFixPrimInto,
                    (blendPts[2] - pFixPrim) * tFixPrimInto
                };
                foreach (double s in sVals)
                {
                    if (s > RhinoMath.ZeroTolerance)
                        AddPrimTarget(MakeOrientedLineCurve(pFixPrim, pFixPrim + s * tFixPrimInto, modT1_Prim),
                                      "Hybrid Line-NURBS: Extend/Trim Line");
                }

                if (preserveOtherEnd == 1)
                {
                    foreach (Point3d bp in blendPts)
                        AddPrimTarget(MakeOrientedLineCurve(pFixPrim, bp, modT1_Prim),
                                      "Hybrid Line-NURBS: Pivot Line (Preserve G0)");
                }
            }
            else // Arc (Keep as arc) + NURBS (Preserve >= 1)
            {
                Circle origCircle = new Circle(origArc.Plane, origArc.Radius);
                Point3d[] refPts = { ptAvg, pWorkNurbs, blendPts[1], blendPts[2] };
                foreach (Point3d rp in refPts)
                {
                    if (origCircle.ClosestParameter(rp, out double tC))
                        AddPrimTarget(MakeOrientedArcCurve(new Arc(pFixPrim, tFixPrimInto, origCircle.PointAt(tC)), modT1_Prim),
                                      "Hybrid Arc-NURBS: Extend/Trim Arc (Preserve G2)");
                }

                if (preserveOtherEnd <= 2)
                {
                    foreach (Point3d bp in blendPts)
                        AddPrimTarget(MakeOrientedArcCurve(new Arc(pFixPrim, tFixPrimInto, bp), modT1_Prim),
                                      "Hybrid Arc-NURBS: Scale/Roll Arc (Preserve G1)");
                }

                if (preserveOtherEnd == 1)
                {
                    if (continuity == 0)
                    {
                        AddPrimTarget(MakeSimilarityPivotedArcCurve(origArc, pFixPrim, pWorkPrim, ptAvg),
                                      "Hybrid Arc-NURBS: Similarity Pivot Arc to Midpoint (Preserve G0)");
                        foreach (var pivArc in MakeFixedRadiusPivotedArcs(origArc, modT1_Prim, pFixPrim, pWorkPrim, ptAvg))
                            AddPrimTarget(pivArc, "Hybrid Arc-NURBS: Fixed-Radius Pivot Arc to Midpoint (Preserve G0)");
                    }

                    Vector3d sharedFlow = ComputeSharedFlowDirection(tWorkPrimOut, tWorkNurbsOut, pFixPrim, pFixNurbs, origArc.Plane.ZAxis);
                    foreach (Point3d bp in blendPts)
                    {
                        Arc rev = new Arc(bp, -sharedFlow, pFixPrim);
                        if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                        {
                            var ac = new ArcCurve(rev);
                            if (modT1_Prim) ac.Reverse();
                            AddPrimTarget(ac, "Hybrid Arc-NURBS: Pivot Arc to Shared Tangent (Preserve G0)");
                        }
                    }
                }
            }

            var pairCandidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            string lastNurbsErr = null;

            void EvaluatePrimPool(List<(Curve CandPrim, string Desc)> pool)
            {
                foreach (var pt in pool)
                {
                    if (GetMaximumDeviation(primCrv, pt.CandPrim) > devTol)
                    {
                        pt.CandPrim.Dispose();
                        lastNurbsErr = "Result cannot be created because it exceeds the Crv dev limit.";
                        continue;
                    }

                    // If extending/pivoting the primitive alone already meets the untouched NURBS curve, include that zero-NURBS-dev pair
                    if (AreCurvesAtContinuity(pt.CandPrim, modT1_Prim, nurbsCrv, modT1_Nurbs, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                    {
                        pairCandidates.Add((pt.CandPrim.DuplicateCurve(), nurbsCrv.DuplicateCurve(), pt.Desc + " (Exact Meet)"));
                    }

                    var nurbsRes = SolveNurbsOneCurve(
                        nurbsCrv, pt.CandPrim, modT1_Nurbs, modT1_Prim,
                        continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                        preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                        posTol, angleTolDeg, curvTolPct, debug);

                    if (nurbsRes.Result != null)
                    {
                        pairCandidates.Add((pt.CandPrim, nurbsRes.Result, pt.Desc));
                    }
                    else
                    {
                        pt.CandPrim.Dispose();
                        if (!string.IsNullOrEmpty(nurbsRes.ErrorMsg)) lastNurbsErr = nurbsRes.ErrorMsg;
                    }
                }
            }

            EvaluatePrimPool(primTargets);
            if (pairCandidates.Count == 0 && fallbackPrimTargets.Count > 0)
            {
                EvaluatePrimPool(fallbackPrimTargets);
            }
            else
            {
                foreach (var fb in fallbackPrimTargets) fb.CandPrim.Dispose();
            }

            if (pairCandidates.Count == 0)
                return (null, null, lastNurbsErr ?? "Result cannot be created with the current constraints.");

            return EvaluateAnalyticalPairCandidates(primCrv, nurbsCrv, pairCandidates, devTol, debug);
        }

        // ==============================================================================
        // ANALYTICAL CANDIDATE EVALUATORS
        // ==============================================================================

        private static (Curve Result, string ErrorMsg) EvaluateAnalyticalSingleCandidates(
            Curve origCrv, List<(Curve Crv, string Desc)> candidates, double devTol, bool debug)
        {
            if (candidates.Count == 0)
                return (null, "Result cannot be created analytically with the current constraints.");

            Curve winner = null;
            double bestAvgDev = double.MaxValue;
            double bestMaxDev = double.MaxValue;
            bool rejectedByDev = false;

            foreach (var cand in candidates)
            {
                double maxDev = GetMaximumDeviation(origCrv, cand.Crv);
                double avgDev = GetAverageDeviation(origCrv, cand.Crv);

                if (debug) RhinoApp.WriteLine($" {cand.Desc} | AvgDev: {avgDev:g} | MaxDev: {maxDev:g}");

                if (maxDev > devTol)
                {
                    cand.Crv.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                bool isBetter = Math.Abs(avgDev - bestAvgDev) <= RhinoMath.ZeroTolerance
                    ? (maxDev < bestMaxDev)
                    : (avgDev < bestAvgDev);

                if (isBetter)
                {
                    bestAvgDev = avgDev;
                    bestMaxDev = maxDev;
                    winner?.Dispose();
                    winner = cand.Crv;
                }
                else
                {
                    cand.Crv.Dispose();
                }
            }

            if (winner != null && !IsMeaningfullyChanged(origCrv, winner, bestMaxDev))
            {
                winner.Dispose();
                winner = null;
            }

            if (winner == null)
            {
                return (null, rejectedByDev
                    ? "Result cannot be created because it exceeds the Crv dev limit."
                    : "Result cannot be created analytically with the current constraints.");
            }

            return (winner, null);
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) EvaluateAnalyticalPairCandidates(
            Curve origA, Curve origB,
            List<(Curve CandA, Curve CandB, string Desc)> candidates,
            double devTol, bool debug)
        {
            if (candidates.Count == 0)
                return (null, null, "Result cannot be created analytically with the current constraints.");

            Curve winA = null;
            Curve winB = null;
            double bestAvgDevSum = double.MaxValue;
            double bestMaxDev = double.MaxValue;
            double bestMaxDevA = double.MaxValue;
            double bestMaxDevB = double.MaxValue;
            bool rejectedByDev = false;

            foreach (var cand in candidates)
            {
                double maxDevA = GetMaximumDeviation(origA, cand.CandA);
                double maxDevB = GetMaximumDeviation(origB, cand.CandB);
                double maxDev = Math.Max(maxDevA, maxDevB);

                if (maxDev > devTol)
                {
                    cand.CandA.Dispose();
                    cand.CandB.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                double avgDevSum = GetAverageDeviation(origA, cand.CandA) + GetAverageDeviation(origB, cand.CandB);
                if (debug) RhinoApp.WriteLine($" {cand.Desc} | AvgDevSum: {avgDevSum:g} | MaxDev: {maxDev:g}");

                bool isBetter = Math.Abs(avgDevSum - bestAvgDevSum) <= RhinoMath.ZeroTolerance
                    ? (maxDev < bestMaxDev)
                    : (avgDevSum < bestAvgDevSum);

                if (isBetter)
                {
                    bestAvgDevSum = avgDevSum;
                    bestMaxDev = maxDev;
                    bestMaxDevA = maxDevA;
                    bestMaxDevB = maxDevB;
                    winA?.Dispose();
                    winB?.Dispose();
                    winA = cand.CandA;
                    winB = cand.CandB;
                }
                else
                {
                    cand.CandA.Dispose();
                    cand.CandB.Dispose();
                }
            }

            if (winA != null && !IsMeaningfullyChanged(origA, winA, bestMaxDevA))
            {
                winA.Dispose();
                winA = null;
            }
            if (winB != null && !IsMeaningfullyChanged(origB, winB, bestMaxDevB))
            {
                winB.Dispose();
                winB = null;
            }

            if (winA == null && winB == null)
            {
                return (null, null, rejectedByDev
                    ? "Result cannot be created because it exceeds the Crv dev limit."
                    : "Result cannot be created analytically with the current constraints.");
            }

            return (winA, winB, null);
        }
    }
}
