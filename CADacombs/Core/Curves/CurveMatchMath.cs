using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace CADacombs.Core.Curves
{
    public static class CurveMatchMath
    {
        // ==============================================================================
        // BACKWARD COMPATIBILITY OVERLOADS
        // ==============================================================================

        public static (Curve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod, Curve crvRef, bool modT1, bool refT1,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchOneCurve(crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd,
                                 deformLines ? 2 : 0, 0, 2, devTol, false, false, 2,
                                 RhinoMath.ZeroTolerance, RhinoMath.ToRadians(1.0), 5.0, debug);
        }

        public static (Curve ResultA, Curve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA, Curve crvB, bool modT1_A, bool modT1_B,
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchBothCurves(crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd,
                                   deformLines ? 2 : 0, 0, 2, devTol, false, false, 2,
                                   RhinoMath.ZeroTolerance, RhinoMath.ToRadians(1.0), 5.0, debug);
        }

        // ==============================================================================
        // PRIMITIVE DETECTION & ORIENTATION HELPERS
        // ==============================================================================

        private static bool TryGetPrimitiveArc(Curve crv, int arcMode, out Arc arc)
        {
            arc = Arc.Unset;
            if (crv is ArcCurve ac)
            {
                arc = ac.Arc;
                return arc.IsValid;
            }
            // Only test NURBS curves for arc shape when NOT converting to NURBS,
            // preserving intentional control-point layouts when Convert to NURBS is selected.
            if (arcMode != 2 && crv.TryGetArc(out arc, RhinoMath.ZeroTolerance))
            {
                return arc.IsValid;
            }
            return false;
        }

        private static bool TryGetPrimitiveLine(Curve crv, int lineMode, out Line line)
        {
            line = Line.Unset;
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

        /// <summary>
        /// Extracts endpoints and unit tangents independent of underlying Arc AngleDomain.
        /// tFixInto points INTO the curve from pFix toward pWork.
        /// tWorkOut points OUT OF the curve at pWork (continuing past pWork).
        /// </summary>
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
            if ((fromDir * toDir) < -0.999999999)
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

        // ==============================================================================
        // ANALYTICAL PRIMITIVE SOLVERS
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
            // Unit tangent pointing INTO crvRef away from pRef; smooth G1 join requires line to arrive along +tRefInto
            Vector3d tRefInto = refT1 ? -crvRef.TangentAtEnd : crvRef.TangentAtStart;
            tRefInto.Unitize();
            Vector3d curvRef = crvRef.CurvatureAt(tRef);

            double effPosTol = Math.Max(posTol, 1e-8);
            double effAngleTolDeg = Math.Max(angleTolDeg, 1e-4);
            double effAngleTolRad = RhinoMath.ToRadians(effAngleTolDeg);
            double effCurvTolPct = Math.Max(curvTolPct, 1e-4);

            if (continuity == 2 && curvRef.Length > effPosTol)
            {
                return (null, "Result cannot be created: an analytical line cannot achieve Curvature (G2) continuity with a curved target.");
            }

            var candidates = new List<(Curve Crv, string Desc)>();

            void TryAddLineCandidate(LineCurve lc, string desc)
            {
                if (lc == null) return;
                if (continuity >= 1)
                {
                    Vector3d candOut = modT1 ? lc.TangentAtEnd : -lc.TangentAtStart;
                    candOut.Unitize();
                    if (Vector3d.VectorAngle(candOut, tRefInto) > effAngleTolRad)
                    {
                        lc.Dispose();
                        return;
                    }
                }
                if (continuity == 2 && !AreCurvesAtContinuity(lc, modT1, crvRef, refT1, 2, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    lc.Dispose();
                    return;
                }
                candidates.Add((lc, desc));
            }

            // Tier <= 3 & <= 2: Preserve Tangency/Curvature (only extend/trim along existing ray from pFix)
            Vector3d vecToRef = pRef - pFix;
            double distToRef = vecToRef.Length;
            if (distToRef > RhinoMath.ZeroTolerance)
            {
                Vector3d dirToRef = vecToRef / distToRef;
                Line origRay = new Line(pFix, pFix + tFixInto);
                double perpDist = origRay.DistanceTo(pRef, false);

                if ((dirToRef * tFixInto) > 0.0 && perpDist <= effPosTol && Vector3d.VectorAngle(dirToRef, tFixInto) <= effAngleTolRad)
                {
                    TryAddLineCandidate(MakeOrientedLineCurve(pFix, pRef, modT1), "Analytical Line: Extend/Trim (Preserve G1/G2)");
                }
                else if (preserveOtherEnd <= 1)
                {
                    // Tier <= 1: Preserve Position (pivot around pFix to pRef)
                    TryAddLineCandidate(MakeOrientedLineCurve(pFix, pRef, modT1), "Analytical Line: Pivot (Preserve G0)");
                }
            }

            // Tier == 0: Preserve None (allow pFix to translate/rotate)
            if (preserveOtherEnd == 0)
            {
                if (continuity == 0)
                {
                    Point3d pNewFix = pFix + (pRef - pWork);
                    TryAddLineCandidate(MakeOrientedLineCurve(pNewFix, pRef, modT1), "Analytical Line: Translate to G0 (Preserve None)");
                }
                else
                {
                    // Line must arrive at pRef along +tRefInto, so pNewFix lies along pRef - s * tRefInto (s > 0)
                    double projS = (pFix - pRef) * (-tRefInto);
                    if (projS > RhinoMath.ZeroTolerance)
                    {
                        Point3d pProjFix = pRef - projS * tRefInto;
                        TryAddLineCandidate(MakeOrientedLineCurve(pProjFix, pRef, modT1), "Analytical Line: Align & Project to G1 (Preserve None)");
                    }

                    Point3d pRigidFix = pRef - origLine.Length * tRefInto;
                    TryAddLineCandidate(MakeOrientedLineCurve(pRigidFix, pRef, modT1), "Analytical Line: Rigid Move to G1 (Preserve None)");
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

            double effPosTol = Math.Max(posTol, 1e-8);
            double effAngleTolDeg = Math.Max(angleTolDeg, 1e-4);
            double effAngleTolRad = RhinoMath.ToRadians(effAngleTolDeg);
            double effCurvTolPct = Math.Max(curvTolPct, 1e-4);

            if (continuity == 2 && curvRef.Length <= RhinoMath.ZeroTolerance)
            {
                return (null, "Result cannot be created: an analytical arc cannot achieve Curvature (G2) continuity with a zero-curvature target.");
            }

            var candidates = new List<(Curve Crv, string Desc)>();

            void TryAddArcCandidate(ArcCurve ac, string desc)
            {
                if (ac == null) return;
                if (continuity >= 1)
                {
                    Vector3d candOut = modT1 ? ac.TangentAtEnd : -ac.TangentAtStart;
                    candOut.Unitize();
                    if (Vector3d.VectorAngle(candOut, tRefInto) > effAngleTolRad)
                    {
                        ac.Dispose();
                        return;
                    }
                }
                if (continuity == 2 && !AreCurvesAtContinuity(ac, modT1, crvRef, refT1, 2, effPosTol, effAngleTolDeg, effCurvTolPct))
                {
                    ac.Dispose();
                    return;
                }
                candidates.Add((ac, desc));
            }

            // Tier <= 3: Preserve Curvature (locked circle, extend/trim only)
            double distToCircle = Math.Abs(origArc.Center.DistanceTo(pRef) - origArc.Radius);
            double distToPlane = Math.Abs(origArc.Plane.DistanceTo(pRef));
            if (distToCircle <= effPosTol && distToPlane <= effPosTol)
            {
                TryAddArcCandidate(MakeOrientedArcCurve(new Arc(pFix, tFixInto, pRef), modT1), "Analytical Arc: Extend/Trim (Preserve G2)");
            }

            // Tier <= 2: Preserve Tangency (locked pFix and tFixInto; scales radius & rolls in 3D if non-coplanar)
            if (preserveOtherEnd <= 2)
            {
                TryAddArcCandidate(MakeOrientedArcCurve(new Arc(pFix, tFixInto, pRef), modT1), "Analytical Arc: Scale/Roll (Preserve G1)");
            }

            // Tier <= 1: Preserve Position (locked pFix; pivots around pFix and/or scales radius)
            if (preserveOtherEnd <= 1)
            {
                if (continuity == 0)
                {
                    // Exact fixed-radius pivot in rotated plane
                    Vector3d newChord = pRef - pFix;
                    double chordLen = newChord.Length;
                    double r = origArc.Radius;
                    if (chordLen > RhinoMath.ZeroTolerance && chordLen <= 2.0 * r)
                    {
                        Vector3d origChord = pWork - pFix;
                        Transform rot = SafeRotation(origChord, newChord, origArc.Plane.ZAxis, pFix);
                        Vector3d rotNormal = origArc.Plane.ZAxis;
                        rotNormal.Transform(rot);
                        rotNormal.Unitize();

                        Vector3d perp = Vector3d.CrossProduct(rotNormal, newChord);
                        if (perp.Unitize())
                        {
                            Point3d mid = 0.5 * (pFix + pRef);
                            double h = 0.5 * chordLen;
                            double d = Math.Sqrt(Math.Max(0.0, r * r - h * h));
                            Point3d[] centers = { mid + d * perp, mid - d * perp };

                            foreach (Point3d c in centers)
                            {
                                Vector3d radial = pFix - c;
                                Vector3d tan1 = Vector3d.CrossProduct(rotNormal, radial);
                                if (tan1.Unitize())
                                {
                                    TryAddArcCandidate(MakeOrientedArcCurve(new Arc(pFix, tan1, pRef), modT1), "Analytical Arc: Fixed-Radius Pivot + (Preserve G0)");
                                    TryAddArcCandidate(MakeOrientedArcCurve(new Arc(pFix, -tan1, pRef), modT1), "Analytical Arc: Fixed-Radius Pivot - (Preserve G0)");
                                }
                            }
                        }
                    }
                }
                else // continuity == 1 or 2: start at pRef heading -tRefInto back toward pFix
                {
                    Arc rev = new Arc(pRef, -tRefInto, pFix);
                    if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                    {
                        var ac = new ArcCurve(rev);
                        // rev currently runs pRef -> pFix; if modT1 is true we need pFix -> pRef
                        if (modT1) ac.Reverse();
                        TryAddArcCandidate(ac, "Analytical Arc: Pivot/Scale to Target Tangent (Preserve G0)");
                    }
                }
            }

            // Tier == 0: Preserve None (pFix is also free to move)
            if (preserveOtherEnd == 0)
            {
                if (continuity == 0)
                {
                    Arc moved = origArc;
                    moved.Transform(Transform.Translation(pRef - pWork));
                    var ac = new ArcCurve(moved);
                    TryAddArcCandidate(ac, "Analytical Arc: Translate to G0 (Preserve None)");
                }
                else if (continuity == 1)
                {
                    Transform rot = SafeRotation(tWorkOut, tRefInto, origArc.Plane.ZAxis, pWork);
                    Transform xform = Transform.Translation(pRef - pWork) * rot;
                    Arc moved = origArc;
                    moved.Transform(xform);
                    var ac = new ArcCurve(moved);
                    TryAddArcCandidate(ac, "Analytical Arc: Rigid Move to G1 (Preserve None)");
                }
                else if (continuity == 2 && curvRef.Length > RhinoMath.ZeroTolerance)
                {
                    double rOsc = 1.0 / curvRef.Length;
                    Point3d cOsc = pRef + curvRef / curvRef.SquareLength;
                    Vector3d xDir = pRef - cOsc;
                    if (xDir.Unitize())
                    {
                        // Circle starting at pRef (angle 0) and sweeping along -tRefInto back into the arc
                        Plane oscPlane = new Plane(cOsc, xDir, -tRefInto);
                        Circle oscCircle = new Circle(oscPlane, rOsc);

                        double[] sweeps =
                        {
                            Math.Min(Math.Max(origArc.Length / rOsc, 1e-4), 1.95 * Math.PI),
                            Math.Min(Math.Max(origArc.Angle, 1e-4), 1.95 * Math.PI)
                        };

                        foreach (double sw in sweeps)
                        {
                            Arc oscArc = new Arc(oscCircle, sw);
                            if (oscArc.IsValid)
                            {
                                var ac = new ArcCurve(oscArc); // runs pRef -> newFix
                                if (modT1) ac.Reverse();       // orient newFix -> pRef if working end is T1
                                TryAddArcCandidate(ac, "Analytical Arc: Osculating Circle G2 (Preserve None)");
                            }
                        }
                    }
                }
            }

            return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveAnalyticalBothLines(
            Curve crvA, Line lineA, bool modT1_A,
            Curve crvB, Line lineB, bool modT1_B,
            int continuity, int preserveOtherEnd,
            double devTol, double posTol, double angleTolDeg, bool debug)
        {
            GetOrientedEnds(crvA, modT1_A, out Point3d pFixA, out Point3d pWorkA, out Vector3d tFixIntoA, out Vector3d tWorkOutA);
            GetOrientedEnds(crvB, modT1_B, out Point3d pFixB, out Point3d pWorkB, out Vector3d tFixIntoB, out Vector3d tWorkOutB);

            double effPosTol = Math.Max(posTol, 1e-8);
            double effAngleTolRad = RhinoMath.ToRadians(Math.Max(angleTolDeg, 1e-4));
            Point3d ptAvg = 0.5 * (pWorkA + pWorkB);

            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();

            if (continuity == 0)
            {
                // Tier <= 3 & <= 2: Intersect infinite rays along tFixIntoA and tFixIntoB
                Line rayA = new Line(pFixA, pFixA + tFixIntoA);
                Line rayB = new Line(pFixB, pFixB + tFixIntoB);
                if (tFixIntoA.IsParallelTo(tFixIntoB, 1e-6) == 0 &&
                    Intersection.LineLine(rayA, rayB, out double sA, out double sB, effPosTol, false))
                {
                    Point3d iA = rayA.PointAt(sA);
                    Point3d iB = rayB.PointAt(sB);
                    if (sA > RhinoMath.ZeroTolerance && sB > RhinoMath.ZeroTolerance && iA.DistanceTo(iB) <= effPosTol)
                    {
                        Point3d q = 0.5 * (iA + iB);
                        var lcA = MakeOrientedLineCurve(pFixA, q, modT1_A);
                        var lcB = MakeOrientedLineCurve(pFixB, q, modT1_B);
                        if (lcA != null && lcB != null)
                            candidates.Add((lcA, lcB, "Analytical Both Lines G0: Trim/Extend to Intersection"));
                        else { lcA?.Dispose(); lcB?.Dispose(); }
                    }
                }
                else if (tFixIntoA.IsParallelTo(tFixIntoB, effAngleTolRad) == -1 && rayA.DistanceTo(pFixB, false) <= effPosTol)
                {
                    // Collinear opposite rays meeting at midpoint
                    Point3d q = rayA.ClosestPoint(ptAvg, false);
                    if (((q - pFixA) * tFixIntoA) > RhinoMath.ZeroTolerance && ((q - pFixB) * tFixIntoB) > RhinoMath.ZeroTolerance)
                    {
                        var lcA = MakeOrientedLineCurve(pFixA, q, modT1_A);
                        var lcB = MakeOrientedLineCurve(pFixB, q, modT1_B);
                        if (lcA != null && lcB != null)
                            candidates.Add((lcA, lcB, "Analytical Both Lines G0: Collinear Meet"));
                        else { lcA?.Dispose(); lcB?.Dispose(); }
                    }
                }

                // Tier <= 1: Pivot both lines around pFixA, pFixB to ptAvg
                if (preserveOtherEnd <= 1)
                {
                    var lcA = MakeOrientedLineCurve(pFixA, ptAvg, modT1_A);
                    var lcB = MakeOrientedLineCurve(pFixB, ptAvg, modT1_B);
                    if (lcA != null && lcB != null)
                        candidates.Add((lcA, lcB, "Analytical Both Lines G0: Pivot to Midpoint"));
                    else { lcA?.Dispose(); lcB?.Dispose(); }
                }

                // Tier == 0: Pure translation to ptAvg
                if (preserveOtherEnd == 0)
                {
                    var lcA = MakeOrientedLineCurve(pFixA + (ptAvg - pWorkA), ptAvg, modT1_A);
                    var lcB = MakeOrientedLineCurve(pFixB + (ptAvg - pWorkB), ptAvg, modT1_B);
                    if (lcA != null && lcB != null)
                        candidates.Add((lcA, lcB, "Analytical Both Lines G0: Translate to Midpoint"));
                    else { lcA?.Dispose(); lcB?.Dispose(); }
                }
            }
            else // continuity == 1 or 2 (two collinear lines have 0 curvature, so G1 == G2)
            {
                Line rayA = new Line(pFixA, pFixA + tFixIntoA);
                // Tier <= 3 & <= 2: Only if already collinear and pointing toward each other
                if (Vector3d.VectorAngle(tFixIntoA, -tFixIntoB) <= effAngleTolRad && rayA.DistanceTo(pFixB, false) <= effPosTol)
                {
                    Point3d q = rayA.ClosestPoint(ptAvg, false);
                    if (((q - pFixA) * tFixIntoA) > RhinoMath.ZeroTolerance && ((q - pFixB) * tFixIntoB) > RhinoMath.ZeroTolerance)
                    {
                        var lcA = MakeOrientedLineCurve(pFixA, q, modT1_A);
                        var lcB = MakeOrientedLineCurve(pFixB, q, modT1_B);
                        if (lcA != null && lcB != null)
                            candidates.Add((lcA, lcB, "Analytical Both Lines G1/G2: Collinear Extend/Trim"));
                        else { lcA?.Dispose(); lcB?.Dispose(); }
                    }
                }

                // Tier <= 1: Collinearize along pFixA -> pFixB
                if (preserveOtherEnd <= 1)
                {
                    Vector3d span = pFixB - pFixA;
                    double spanLen = span.Length;
                    if (spanLen > 2.0 * RhinoMath.ZeroTolerance)
                    {
                        Vector3d axis = span / spanLen;
                        double s = (ptAvg - pFixA) * axis;
                        s = Math.Max(0.1 * spanLen, Math.Min(0.9 * spanLen, s));
                        Point3d q = pFixA + s * axis;
                        var lcA = MakeOrientedLineCurve(pFixA, q, modT1_A);
                        var lcB = MakeOrientedLineCurve(pFixB, q, modT1_B);
                        if (lcA != null && lcB != null)
                            candidates.Add((lcA, lcB, "Analytical Both Lines G1/G2: Pivot Collinear (Preserve G0)"));
                        else { lcA?.Dispose(); lcB?.Dispose(); }
                    }
                }

                // Tier == 0: Align to averaged tangent through ptAvg
                if (preserveOtherEnd == 0)
                {
                    // Flow direction from A into B: tWorkOutA aligns with -tWorkOutB
                    Vector3d flowDir = tWorkOutA + (-tWorkOutB);
                    if (flowDir.Unitize())
                    {
                        double sA = Math.Max((pFixA - ptAvg) * (-flowDir), lineA.Length);
                        double sB = Math.Max((pFixB - ptAvg) * flowDir, lineB.Length);
                        var lcA = MakeOrientedLineCurve(ptAvg - sA * flowDir, ptAvg, modT1_A);
                        var lcB = MakeOrientedLineCurve(ptAvg + sB * flowDir, ptAvg, modT1_B);
                        if (lcA != null && lcB != null)
                            candidates.Add((lcA, lcB, "Analytical Both Lines G1/G2: Rotate to Shared Tangent (Preserve None)"));
                        else { lcA?.Dispose(); lcB?.Dispose(); }
                    }
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
            {
                return (null, null, "Result cannot be created: an analytical arc and line cannot achieve Curvature (G2) continuity.");
            }

            GetOrientedEnds(origArcCrv, modT1_Arc, out Point3d pFixArc, out Point3d pWorkArc, out Vector3d tFixArcInto, out Vector3d tWorkArcOut);
            GetOrientedEnds(origLineCrv, modT1_Line, out Point3d pFixLine, out Point3d pWorkLine, out Vector3d tFixLineInto, out _);

            double effPosTol = Math.Max(posTol, 1e-8);
            double effAngleTolRad = RhinoMath.ToRadians(Math.Max(angleTolDeg, 1e-4));
            Line lineRay = new Line(pFixLine, pFixLine + tFixLineInto);

            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            string coplanarityHint = null;

            void TryAddArcLinePair(ArcCurve ac, Point3d q, string desc, bool requireSmoothG1)
            {
                if (ac == null) return;
                var lc = MakeOrientedLineCurve(pFixLine, q, modT1_Line);
                if (lc == null)
                {
                    ac.Dispose();
                    return;
                }

                if (requireSmoothG1)
                {
                    Vector3d arcOut = modT1_Arc ? ac.TangentAtEnd : -ac.TangentAtStart;
                    arcOut.Unitize();
                    // Smooth G1 join at q: arc leaves q heading into the line toward pFixLine
                    Vector3d lineIntoFromQ = pFixLine - q;
                    if (!lineIntoFromQ.Unitize() || Vector3d.VectorAngle(arcOut, lineIntoFromQ) > effAngleTolRad)
                    {
                        ac.Dispose();
                        lc.Dispose();
                        return;
                    }
                }

                candidates.Add((ac, lc, desc));
            }

            if (continuity == 0) // Position (G0)
            {
                // Tier <= 3: Preserve Curvature (intersect infinite line ray with existing 3D circle)
                Circle origCircle = new Circle(origArc.Plane, origArc.Radius);
                var rc = Intersection.LineCircle(lineRay, origCircle, out double t1, out Point3d pt1, out double t2, out Point3d pt2);
                if (rc == LineCircleIntersection.Single || rc == LineCircleIntersection.Multiple)
                {
                    if (t1 > RhinoMath.ZeroTolerance && lineRay.PointAt(t1).DistanceTo(pt1) <= effPosTol)
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, pt1), modT1_Arc), pt1, "Analytical Arc-Line G0: Circle-Line Intersection 1 (Preserve G2)", false);

                    if (rc == LineCircleIntersection.Multiple && t2 > RhinoMath.ZeroTolerance && lineRay.PointAt(t2).DistanceTo(pt2) <= effPosTol)
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, pt2), modT1_Arc), pt2, "Analytical Arc-Line G0: Circle-Line Intersection 2 (Preserve G2)", false);
                }

                // Tier <= 2: Preserve Tangency (scale/roll arc to closest point on line ray)
                if (preserveOtherEnd <= 2)
                {
                    double s = (pWorkArc - pFixLine) * tFixLineInto;
                    if (s > RhinoMath.ZeroTolerance)
                    {
                        Point3d q = pFixLine + s * tFixLineInto;
                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc), q, "Analytical Arc-Line G0: Scale/Roll to Line (Preserve G1)", false);
                    }
                }

                // Tier <= 1: Preserve Position (both pivot/scale to midpoint)
                if (preserveOtherEnd <= 1)
                {
                    Point3d ptAvg = 0.5 * (pWorkArc + pWorkLine);
                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, ptAvg), modT1_Arc), ptAvg, "Analytical Arc-Line G0: Meet at Midpoint (Preserve G0)", false);
                }

                // Tier == 0: Preserve None (translate arc to line ray)
                if (preserveOtherEnd == 0)
                {
                    double s = (pWorkArc - pFixLine) * tFixLineInto;
                    if (s > RhinoMath.ZeroTolerance)
                    {
                        Point3d q = pFixLine + s * tFixLineInto;
                        Arc moved = origArc;
                        moved.Transform(Transform.Translation(q - pWorkArc));
                        TryAddArcLinePair(new ArcCurve(moved), q, "Analytical Arc-Line G0: Translate Arc to Line (Preserve None)", false);
                    }
                }
            }
            else if (continuity == 1) // Tangency (G1)
            {
                // Tier <= 3: Preserve Curvature (only if infinite line ray is already tangent to existing circle)
                double planeDist = Math.Abs(origArc.Plane.DistanceTo(pFixLine));
                bool lineInArcPlane = planeDist <= effPosTol && Math.Abs(origArc.Plane.ZAxis * tFixLineInto) <= Math.Max(1e-6, Math.Sin(effAngleTolRad));
                if (lineInArcPlane)
                {
                    double s = (origArc.Center - pFixLine) * tFixLineInto;
                    if (s > RhinoMath.ZeroTolerance)
                    {
                        Point3d q = pFixLine + s * tFixLineInto;
                        if (Math.Abs(origArc.Center.DistanceTo(q) - origArc.Radius) <= effPosTol)
                        {
                            TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc), q, "Analytical Arc-Line G1: Extend/Trim to Existing Tangent (Preserve G2)", true);
                        }
                    }
                }

                // Orthogonal basis for plane containing lineRay and pFixArc
                Point3d projFixOnLine = lineRay.ClosestPoint(pFixArc, false);
                Vector3d perpToLine = pFixArc - projFixOnLine;
                double perpDist = perpToLine.Length;
                Plane commonPlane = perpDist > effPosTol ? new Plane(projFixOnLine, tFixLineInto, perpToLine) : Plane.Unset;

                // Tier <= 2: Preserve Tangency (tFixArcInto and lineRay are both locked in direction)
                if (preserveOtherEnd <= 2)
                {
                    if (!commonPlane.IsValid)
                    {
                        coplanarityHint = "Result cannot be created: arc opposite end lies on the infinite target line.";
                    }
                    else
                    {
                        bool coplanar = Math.Abs(commonPlane.ZAxis * tFixArcInto) <= Math.Max(1e-6, Math.Sin(effAngleTolRad));
                        if (!coplanar)
                        {
                            coplanarityHint = "Result cannot be created: arc opposite tangent and target line are skew (not coplanar) for Preserve Tangency.";
                        }
                        else
                        {
                            // Project tFixArcInto cleanly into commonPlane to eliminate tiny numerical noise
                            Vector3d tInPlane = tFixArcInto - (tFixArcInto * commonPlane.ZAxis) * commonPlane.ZAxis;
                            tInPlane.Unitize();

                            bool isParallel = tInPlane.IsParallelTo(tFixLineInto, 1e-6) != 0;
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
                                        TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc), q, "Analytical Arc-Line G1: Tangent Kiss (Preserve G1)", true);
                                    }
                                }
                            }
                            else
                            {
                                // Parallel tangent ray and target line -> 180-degree semicircle
                                Point3d q = projFixOnLine;
                                if (((q - pFixLine) * tFixLineInto) > RhinoMath.ZeroTolerance)
                                {
                                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc), q, "Analytical Arc-Line G1: Parallel Semicircle Kiss (Preserve G1)", true);
                                }
                            }
                        }
                    }
                }

                // Tier <= 1: Preserve Position (pFixArc and pFixLine are locked in position; tangents may pivot)
                if (preserveOtherEnd <= 1)
                {
                    // 1A: Keep line direction, pivot arc around pFixArc with fixed original radius
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

                                // Walking backwards from q into the arc toward pFixArc leaves q along +tFixLineInto
                                Arc rev = new Arc(q, tFixLineInto, pFixArc);
                                if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                                {
                                    var ac = new ArcCurve(rev); // runs q -> pFixArc
                                    if (modT1_Arc) ac.Reverse(); // orient pFixArc -> q if working end is T1
                                    TryAddArcLinePair(ac, q, "Analytical Arc-Line G1: Fixed-Radius Arc Pivot (Preserve G0)", true);
                                }
                            }
                        }
                    }

                    // 1B: Keep arc circle/radius, pivot line around pFixLine to kiss existing circle (coplanar)
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
                                {
                                    TryAddArcLinePair(MakeOrientedArcCurve(new Arc(pFixArc, tFixArcInto, q), modT1_Arc), q, "Analytical Arc-Line G1: Pivot Line to Existing Circle (Preserve G0)", true);
                                }
                            }
                        }
                    }

                    // 1C: 3D/Fallback pivot + scale arc to closest point of pWorkArc on lineRay (works even when skew or d > 2R)
                    double sClosest = (pWorkArc - pFixLine) * tFixLineInto;
                    if (sClosest > RhinoMath.ZeroTolerance)
                    {
                        Point3d q = pFixLine + sClosest * tFixLineInto;
                        Arc rev = new Arc(q, tFixLineInto, pFixArc);
                        if (rev.IsValid && rev.Length > RhinoMath.ZeroTolerance)
                        {
                            var ac = new ArcCurve(rev);
                            if (modT1_Arc) ac.Reverse();
                            TryAddArcLinePair(ac, q, "Analytical Arc-Line G1: 3D Pivot & Scale to Line (Preserve G0)", true);
                        }
                    }
                }

                // Tier == 0: Preserve None (rigid move of arc to kiss lineRay at closest point)
                if (preserveOtherEnd == 0)
                {
                    double s = (pWorkArc - pFixLine) * tFixLineInto;
                    if (s > RhinoMath.ZeroTolerance)
                    {
                        Point3d q = pFixLine + s * tFixLineInto;
                        Vector3d lineIntoFromQ = -tFixLineInto;
                        Transform rot = SafeRotation(tWorkArcOut, lineIntoFromQ, origArc.Plane.ZAxis, pWorkArc);
                        Transform xform = Transform.Translation(q - pWorkArc) * rot;
                        Arc moved = origArc;
                        moved.Transform(xform);
                        TryAddArcLinePair(new ArcCurve(moved), q, "Analytical Arc-Line G1: Rigid Move Arc to Line (Preserve None)", true);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return (null, null, coplanarityHint ?? "Result cannot be created analytically with the current constraints.");
            }

            return EvaluateAnalyticalPairCandidates(origArcCrv, origLineCrv, candidates, devTol, debug);
        }

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

                bool isBetter = Math.Abs(avgDev - bestAvgDev) <= 1e-12 ? (maxDev < bestMaxDev) : (avgDev < bestAvgDev);
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

            if (winner != null && bestMaxDev <= RhinoMath.ZeroTolerance)
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

                bool isBetter = Math.Abs(avgDevSum - bestAvgDevSum) <= 1e-12 ? (maxDev < bestMaxDev) : (avgDevSum < bestAvgDevSum);
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

            if (winA != null && bestMaxDevA <= RhinoMath.ZeroTolerance)
            {
                winA.Dispose();
                winA = null;
            }
            if (winB != null && bestMaxDevB <= RhinoMath.ZeroTolerance)
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

        // ==============================================================================
        // CORE ENGINE
        // ==============================================================================

        private static Dictionary<double, int> GetInternalContinuities(NurbsCurve nc, double posTol, double angleTolDeg, double curvTolPct)
        {
            var dict = new Dictionary<double, int>();
            var uniqueKnots = new HashSet<double>();

            for (int i = nc.Degree; i < nc.Knots.Count - nc.Degree; i++)
            {
                uniqueKnots.Add(nc.Knots[i]);
            }

            foreach (double t in uniqueKnots)
            {
                var vBelow = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Below);
                var vAbove = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Above);

                int level = ContinuityUtils.GetContinuityLevel(vBelow, vAbove, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;
                dict[t] = level;
            }
            return dict;
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
            // --- PRIMITIVE INTERCEPTOR ---
            if (TryGetPrimitiveArc(crvToMod, arcMode, out Arc origArc))
            {
                if (arcMode == 0)
                    return (null, "Result cannot be created because the curve is an arc and Arcs mode is set to Lock.");
                if (arcMode == 1)
                    return SolveAnalyticalArcToFixedTarget(crvToMod, origArc, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }
            else if (TryGetPrimitiveLine(crvToMod, lineMode, out Line origLine))
            {
                if (lineMode == 0)
                    return (null, "Result cannot be created because the curve is a line and Lines mode is set to Lock.");
                if (lineMode == 1)
                    return SolveAnalyticalLineToFixedTarget(crvToMod, origLine, modT1, crvRef, refT1, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, debug);
            }

            // --- STANDARD NURBS SOLVER ---
            NurbsCurve ncMod = crvToMod.ToNurbsCurve();
            NurbsCurve ncRef = crvRef.ToNurbsCurve();

            if (ncMod == null || ncRef == null) return (null, "Failed to convert curves to NURBS.");

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (crvToMod is ArcCurve)
            {
                Curve rebuilt = crvToMod.Rebuild(6, 5, false);
                if (rebuilt != null) { ncMod.Dispose(); ncMod = rebuilt.ToNurbsCurve(); }
            }
            else if (crvToMod.IsLinear(RhinoMath.ZeroTolerance) && ncMod.Points.Count < minCpNeeded)
            {
                if (lineMode != 2)
                {
                    ncMod.Dispose(); ncRef.Dispose();
                    return (null, "Result cannot be created because the curve is a line and Convert to NURBS is disabled.");
                }
                int ptsNeeded = minCpNeeded - ncMod.Points.Count;

                if (incrAsNeeded == 0) ncMod.IncreaseDegree(ncMod.Degree + ptsNeeded);
                else if (incrAsNeeded == 1)
                {
                    var ts = ncMod.DivideByCount(ptsNeeded + 1, true);
                    if (ts != null) for (int k = 1; k < ts.Length - 1; k++) ncMod.Knots.InsertKnot(ts[k], 1);
                }
                else
                {
                    ncMod.Dispose(); ncRef.Dispose();
                    return (null, "Result cannot be created because the line requires more control points and Incr as needed is disabled.");
                }
            }
            else if (ncMod.SpanCount == 1 && ncMod.Degree == 3 && ncMod.Points.Count < minCpNeeded)
            {
                int ptsNeeded = minCpNeeded - ncMod.Points.Count;
                if (incrAsNeeded == 0) ncMod.IncreaseDegree(ncMod.Degree + ptsNeeded);
                else if (incrAsNeeded == 1)
                {
                    var ts = ncMod.DivideByCount(ptsNeeded + 1, true);
                    if (ts != null) for (int k = 1; k < ts.Length - 1; k++) ncMod.Knots.InsertKnot(ts[k], 1);
                }
                else
                {
                    ncMod.Dispose(); ncRef.Dispose();
                    return (null, "Result cannot be created because the curve requires more control points and Incr as needed is disabled.");
                }
            }

            if (ncMod.Points.Count < minCpNeeded)
            {
                ncMod.Dispose();
                ncRef.Dispose();
                return (null, "Result cannot be created because the curve does not have enough control points for the requested continuity.");
            }

            double tRef = refT1 ? ncRef.Domain.T1 : ncRef.Domain.T0;
            Point3d ptTarget = refT1 ? ncRef.PointAtEnd : ncRef.PointAtStart;
            Vector3d tanTarget = ncRef.TangentAt(tRef);
            if (modT1 == refT1) tanTarget.Reverse();

            var candidates = new List<(NurbsCurve Curve, string Desc)>();

            if (continuity == 0) // Position
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;
                int idxCpPosRef = refT1 ? ncRef.Points.Count - 1 : 0;

                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                cand1.Points.SetPoint(idxCpPosOne, ncRef.Points[idxCpPosRef].Location);
                candidates.Add((cand1, "Position: Move P0 Only"));

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                if (modT1) cand2.SetEndPoint(ncRef.Points[idxCpPosRef].Location);
                else cand2.SetStartPoint(ncRef.Points[idxCpPosRef].Location);
                candidates.Add((cand2, "Position: SetEndCondition"));
            }
            else if (continuity == 1) // Tangency
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;
                int idxCpTanOne = modT1 ? ncMod.Points.Count - 2 : 1;
                int idxCpPosRef = refT1 ? ncRef.Points.Count - 1 : 0;
                int idxCpTanRef = refT1 ? ncRef.Points.Count - 2 : 1;

                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                cand1.Points.SetPoint(idxCpPosOne, ncRef.Points[idxCpPosRef].Location);
                Line tanLine = new Line(ncRef.Points[idxCpPosRef].Location, ncRef.Points[idxCpTanRef].Location);
                Point3d ptTargetTan = tanLine.ClosestPoint(cand1.Points[idxCpTanOne].Location, false);

                if (ptTargetTan.DistanceTo(ncRef.Points[idxCpPosRef].Location) > 1e-6)
                {
                    cand1.Points.SetPoint(idxCpTanOne, ptTargetTan);
                    candidates.Add((cand1, "Tangency: ClosestPoint (Slide P1)"));
                }
                else
                {
                    cand1.Dispose();
                }

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                cand2.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptTarget, tanTarget);
                candidates.Add((cand2, "Tangency: SetEndCondition"));
            }
            else if (continuity == 2) // Curvature
            {
                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                cand1.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptTarget, tanTarget, ncRef.CurvatureAt(tRef));
                candidates.Add((cand1, "Curvature: SetEndCondition"));
            }

            NurbsCurve winner = null;
            double bestAvgDev = double.MaxValue;
            double bestMaxDev = double.MaxValue;

            int origInf = crvToMod.InflectionPoints()?.Length ?? 0;
            var origInternalCont = preserveInternalContinuity ? GetInternalContinuities(ncMod, posTol, angleTolDeg, curvTolPct) : null;

            bool rejectedByInflection = false;
            bool rejectedByDev = false;
            bool rejectedByInternalCont = false;

            if (debug) RhinoApp.WriteLine($"--- MatchOneCurve Candidates Evaluated ({candidates.Count}) ---");

            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                double maxDev = GetMaximumDeviation(crvToMod, cand.Curve);
                double avgDev = GetAverageDeviation(crvToMod, cand.Curve);
                double? minRad = RadiusMinimaMath.GetMinimumRadius(cand.Curve);

                int idxPos = modT1 ? cand.Curve.Points.Count - 1 : 0;
                int idxTan = modT1 ? cand.Curve.Points.Count - 2 : 1;
                double p0p1Dist = continuity > 0 ? cand.Curve.Points[idxPos].Location.DistanceTo(cand.Curve.Points[idxTan].Location) : 0;

                if (debug)
                    RhinoApp.WriteLine($" {cand.Desc}\n    AvgDev: {avgDev:g} | MaxDev: {maxDev:g} | MinRad: {(minRad.HasValue ? minRad.Value.ToString("g") : "N/A")} | P0-P1 Dist: {p0p1Dist:g}");

                if (preserveInternalContinuity && origInternalCont != null)
                {
                    bool degraded = false;
                    foreach (var kvp in origInternalCont)
                    {
                        var vB = ContinuityUtils.GetContinuityVectorsAt(cand.Curve, kvp.Key, CurveEvaluationSide.Below);
                        var vA = ContinuityUtils.GetContinuityVectorsAt(cand.Curve, kvp.Key, CurveEvaluationSide.Above);
                        int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                        int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                        if (newCont < targetCont)
                        {
                            if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                            degraded = true;
                            break;
                        }
                    }
                    if (degraded)
                    {
                        cand.Curve.Dispose();
                        rejectedByInternalCont = true;
                        continue;
                    }
                }

                if (preventExtraInflections)
                {
                    int candInf = cand.Curve.InflectionPoints()?.Length ?? 0;
                    if (candInf > origInf)
                    {
                        if (debug) RhinoApp.WriteLine($"    [Rejected] Added extra inflection points (Original: {origInf}, New: {candInf})");
                        cand.Curve.Dispose();
                        rejectedByInflection = true;
                        continue;
                    }
                }

                if (maxDev > devTol)
                {
                    cand.Curve.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                if (avgDev < bestAvgDev)
                {
                    bestAvgDev = avgDev;
                    bestMaxDev = maxDev;
                    winner?.Dispose();
                    winner = cand.Curve;
                }
                else
                {
                    cand.Curve.Dispose();
                }
            }

            if (winner != null && bestMaxDev <= RhinoMath.ZeroTolerance)
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding result: Maximum deviation ({bestMaxDev:g}) is below ZeroTolerance. No meaningful change made.");
                winner.Dispose();
                winner = null;
            }

            ncMod.Dispose();
            ncRef.Dispose();

            string errorMsg = null;
            if (winner == null)
            {
                if (rejectedByDev) errorMsg = "Result cannot be created because it exceeds the Crv dev limit.";
                else if (rejectedByInternalCont) errorMsg = "Result cannot be created because it would degrade internal continuity.";
                else if (rejectedByInflection) errorMsg = "Result cannot be created because it would add extra inflection points.";
                else errorMsg = "Result cannot be created with the current constraints.";
            }

            return (winner, errorMsg);
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
            // --- PRIMITIVE INTERCEPTOR ---
            bool isArcA = TryGetPrimitiveArc(crvA, arcMode, out Arc arcA);
            bool isArcB = TryGetPrimitiveArc(crvB, arcMode, out Arc arcB);
            Line lineA = Line.Unset;
            Line lineB = Line.Unset;
            bool isLineA = !isArcA && TryGetPrimitiveLine(crvA, lineMode, out lineA);
            bool isLineB = !isArcB && TryGetPrimitiveLine(crvB, lineMode, out lineB);

            if ((isArcA || isArcB) && arcMode == 0)
                return (null, null, "Result cannot be created because a curve is an arc and Arcs mode is set to Lock.");
            if ((isLineA || isLineB) && lineMode == 0)
                return (null, null, "Result cannot be created because a curve is a line and Lines mode is set to Lock.");

            if (isLineA && isLineB && lineMode == 1)
            {
                return SolveAnalyticalBothLines(crvA, lineA, modT1_A, crvB, lineB, modT1_B, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);
            }

            if (isArcA && isLineB && arcMode == 1 && lineMode >= 1)
            {
                if (continuity <= 1 || lineMode == 1)
                {
                    var res = SolveAnalyticalArcAndLine(crvA, arcA, modT1_A, crvB, lineB, modT1_B, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);
                    return (res.ResultArc, res.ResultLine, res.ErrorMsg);
                }
            }
            if (isLineA && isArcB && arcMode == 1 && lineMode >= 1)
            {
                if (continuity <= 1 || lineMode == 1)
                {
                    var res = SolveAnalyticalArcAndLine(crvB, arcB, modT1_B, crvA, lineA, modT1_A, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, debug);
                    return (res.ResultLine, res.ResultArc, res.ErrorMsg);
                }
            }

            if (((isArcA || isArcB) && arcMode == 1) || ((isLineA || isLineB) && lineMode == 1))
            {
                return (null, null, "Result cannot be created: analytical Average (both-curve) matching for this curve combination is not yet supported.");
            }

            // --- STANDARD NURBS SOLVER ---
            NurbsCurve ncWipA = crvA.ToNurbsCurve();
            NurbsCurve ncWipB = crvB.ToNurbsCurve();

            if (ncWipA == null || ncWipB == null) return (null, null, "Failed to convert curves to NURBS.");

            int minCpNeeded = preserveOtherEnd + continuity + 1;
            string localErr = null;

            void ProcessCrv(Curve origCrv, ref NurbsCurve ncWip)
            {
                if (origCrv is ArcCurve)
                {
                    if (arcMode != 2) { localErr = "Result cannot be created because a curve is an arc and Convert to NURBS is disabled."; ncWip.Dispose(); ncWip = null; return; }
                    Curve rebuilt = origCrv.Rebuild(6, 5, false);
                    if (rebuilt != null) { ncWip.Dispose(); ncWip = rebuilt.ToNurbsCurve(); }
                }
                else if (origCrv.IsLinear(RhinoMath.ZeroTolerance) && ncWip.Points.Count < minCpNeeded)
                {
                    if (lineMode != 2) { localErr = "Result cannot be created because a curve is a line and Convert to NURBS is disabled."; ncWip.Dispose(); ncWip = null; return; }
                    int ptsNeeded = minCpNeeded - ncWip.Points.Count;
                    if (incrAsNeeded == 0) ncWip.IncreaseDegree(ncWip.Degree + ptsNeeded);
                    else if (incrAsNeeded == 1)
                    {
                        var ts = ncWip.DivideByCount(ptsNeeded + 1, true);
                        if (ts != null) for (int k = 1; k < ts.Length - 1; k++) ncWip.Knots.InsertKnot(ts[k], 1);
                    }
                    else { localErr = "Result cannot be created because a line requires more control points and Incr as needed is disabled."; ncWip.Dispose(); ncWip = null; return; }
                }
                else if (ncWip.SpanCount == 1 && ncWip.Degree == 3 && ncWip.Points.Count < minCpNeeded)
                {
                    int ptsNeeded = minCpNeeded - ncWip.Points.Count;
                    if (incrAsNeeded == 0) ncWip.IncreaseDegree(ncWip.Degree + ptsNeeded);
                    else if (incrAsNeeded == 1)
                    {
                        var ts = ncWip.DivideByCount(ptsNeeded + 1, true);
                        if (ts != null) for (int k = 1; k < ts.Length - 1; k++) ncWip.Knots.InsertKnot(ts[k], 1);
                    }
                    else { localErr = "Result cannot be created because a curve requires more control points and Incr as needed is disabled."; ncWip.Dispose(); ncWip = null; return; }
                }
            }

            ProcessCrv(crvA, ref ncWipA);
            if (localErr != null) { ncWipB?.Dispose(); return (null, null, localErr); }

            ProcessCrv(crvB, ref ncWipB);
            if (localErr != null) { ncWipA?.Dispose(); return (null, null, localErr); }

            if (ncWipA.Points.Count < minCpNeeded || ncWipB.Points.Count < minCpNeeded)
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, "Result cannot be created because a curve does not have enough control points for the requested continuity.");
            }

            double tA = modT1_A ? ncWipA.Domain.T1 : ncWipA.Domain.T0;
            double tB = modT1_B ? ncWipB.Domain.T1 : ncWipB.Domain.T0;

            int idxCpPosA = modT1_A ? ncWipA.Points.Count - 1 : 0;
            int idxCpPosB = modT1_B ? ncWipB.Points.Count - 1 : 0;
            int idxCpTanA = modT1_A ? ncWipA.Points.Count - 2 : 1;
            int idxCpTanB = modT1_B ? ncWipB.Points.Count - 2 : 1;

            double trueOrigDistA = ncWipA.Points.Count > 1 ? ncWipA.Points[idxCpPosA].Location.DistanceTo(ncWipA.Points[idxCpTanA].Location) : 0;
            double trueOrigDistB = ncWipB.Points.Count > 1 ? ncWipB.Points[idxCpPosB].Location.DistanceTo(ncWipB.Points[idxCpTanB].Location) : 0;

            Point3d ptA = modT1_A ? ncWipA.PointAtEnd : ncWipA.PointAtStart;
            Point3d ptB = modT1_B ? ncWipB.PointAtEnd : ncWipB.PointAtStart;
            Point3d ptAvg = (Point3d)(0.5 * (Vector3d)ptA + 0.5 * (Vector3d)ptB);

            Vector3d tanA = ncWipA.TangentAt(tA);
            Vector3d tanB = ncWipB.TangentAt(tB);
            Vector3d curvA = ncWipA.CurvatureAt(tA);
            Vector3d curvB = ncWipB.CurvatureAt(tB);

            Vector3d tanTargetA = Vector3d.Unset;
            Vector3d tanTargetB = Vector3d.Unset;

            if (continuity >= 1)
            {
                tanTargetB = modT1_A == modT1_B ? (0.5 * -tanA) + (0.5 * tanB) : (0.5 * tanA) + (0.5 * tanB);
                tanTargetA = modT1_A == modT1_B ? -tanTargetB : tanTargetB;

                if (debug)
                {
                    RhinoApp.WriteLine($"[Debug] Shared Tangent Target A: {tanTargetA}");
                    RhinoApp.WriteLine($"[Debug] Shared Tangent Target B: {tanTargetB}");
                }
            }

            var candidates = new List<(NurbsCurve CandA, NurbsCurve CandB, string Desc)>();

            if (continuity == 0)
            {
                NurbsCurve candA = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve candB = (NurbsCurve)ncWipB.Duplicate();
                candA.Points.SetPoint(idxCpPosA, ptAvg);
                candB.Points.SetPoint(idxCpPosB, ptAvg);
                candidates.Add((candA, candB, "Both Position: Move P0"));
            }
            else if (continuity == 1)
            {
                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();
                cand1A.Points.SetPoint(idxCpPosA, ptAvg);
                cand1B.Points.SetPoint(idxCpPosB, ptAvg);
                Line tanLine = new Line(ptAvg, ptAvg + tanTargetB);

                Point3d ptTanA = tanLine.ClosestPoint(cand1A.Points[idxCpTanA].Location, false);
                Point3d ptTanB = tanLine.ClosestPoint(cand1B.Points[idxCpTanB].Location, false);

                if (ptTanA.DistanceTo(ptAvg) > 1e-6 && ptTanB.DistanceTo(ptAvg) > 1e-6)
                {
                    cand1A.Points.SetPoint(idxCpTanA, ptTanA);
                    cand1B.Points.SetPoint(idxCpTanB, ptTanB);
                    candidates.Add((cand1A, cand1B, "Both Tangency: ClosestPoint (Slide P1)"));
                }
                else
                {
                    cand1A.Dispose(); cand1B.Dispose();
                }

                NurbsCurve cand2A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand2B = (NurbsCurve)ncWipB.Duplicate();
                cand2A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetA);
                cand2B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetB);
                candidates.Add((cand2A, cand2B, "Both Tangency: SetEndCondition"));

                NurbsCurve cand3A = (NurbsCurve)cand2A.Duplicate();
                NurbsCurve cand3B = (NurbsCurve)cand2B.Duplicate();

                Vector3d newDirA = cand3A.Points[idxCpTanA].Location - cand3A.Points[idxCpPosA].Location;
                Vector3d newDirB = cand3B.Points[idxCpTanB].Location - cand3B.Points[idxCpPosB].Location;
                newDirA.Unitize(); newDirB.Unitize();

                cand3A.Points.SetPoint(idxCpTanA, ptAvg + (newDirA * trueOrigDistA));
                cand3B.Points.SetPoint(idxCpTanB, ptAvg + (newDirB * trueOrigDistB));
                candidates.Add((cand3A, cand3B, "Both Tangency: Preserve P0-P1 (_Match)"));
            }
            else if (continuity == 2)
            {
                Vector3d curvTarget = (0.5 * curvA) + (0.5 * curvB);

                if (debug) RhinoApp.WriteLine($"[Debug] Shared Curvature Target: {curvTarget}");

                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();

                cand1A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetA, curvTarget);
                cand1B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetB, curvTarget);

                candidates.Add((cand1A, cand1B, "Both Curvature: SetEndCondition"));
            }

            (NurbsCurve ResultA, NurbsCurve ResultB) winner = (null, null);
            double bestAvgDevSum = double.MaxValue;
            double bestMaxDevA = double.MaxValue;
            double bestMaxDevB = double.MaxValue;

            int origInfA = crvA.InflectionPoints()?.Length ?? 0;
            int origInfB = crvB.InflectionPoints()?.Length ?? 0;

            var origInternalContA = preserveInternalContinuity ? GetInternalContinuities(ncWipA, posTol, angleTolDeg, curvTolPct) : null;
            var origInternalContB = preserveInternalContinuity ? GetInternalContinuities(ncWipB, posTol, angleTolDeg, curvTolPct) : null;

            bool rejectedByInflection = false;
            bool rejectedByDev = false;
            bool rejectedByInternalCont = false;

            if (debug) RhinoApp.WriteLine($"--- MatchBothCurves Candidates Evaluated ({candidates.Count}) ---");

            for (int i = 0; i < candidates.Count; i++)
            {
                var candA = candidates[i].CandA;
                var candB = candidates[i].CandB;
                string desc = candidates[i].Desc;

                double maxDevA = GetMaximumDeviation(crvA, candA);
                double maxDevB = GetMaximumDeviation(crvB, candB);
                double maxDev = Math.Max(maxDevA, maxDevB);

                double avgDevA = GetAverageDeviation(crvA, candA);
                double avgDevB = GetAverageDeviation(crvB, candB);
                double avgDevSum = avgDevA + avgDevB;

                double radA = RadiusMinimaMath.GetMinimumRadius(candA) ?? double.MinValue;
                double radB = RadiusMinimaMath.GetMinimumRadius(candB) ?? double.MinValue;
                double minRad = Math.Min(radA, radB);

                double p0p1A = continuity > 0 ? candA.Points[idxCpPosA].Location.DistanceTo(candA.Points[idxCpTanA].Location) : 0;
                double p0p1B = continuity > 0 ? candB.Points[idxCpPosB].Location.DistanceTo(candB.Points[idxCpTanB].Location) : 0;

                if (debug)
                    RhinoApp.WriteLine($" {desc}\n    AvgDev Sum: {avgDevSum:g} (A:{avgDevA:g}, B:{avgDevB:g}) | MaxDev: {maxDev:g} | MinRad: {(minRad != double.MinValue ? minRad.ToString("g") : "N/A")} | P0-P1 Dist: (A:{p0p1A:g}, B:{p0p1B:g})");

                if (preserveInternalContinuity && (origInternalContA != null || origInternalContB != null))
                {
                    bool degraded = false;
                    if (origInternalContA != null)
                    {
                        foreach (var kvp in origInternalContA)
                        {
                            var vB = ContinuityUtils.GetContinuityVectorsAt(candA, kvp.Key, CurveEvaluationSide.Below);
                            var vA = ContinuityUtils.GetContinuityVectorsAt(candA, kvp.Key, CurveEvaluationSide.Above);
                            int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                            int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                            if (newCont < targetCont)
                            {
                                if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded Curve A internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                                degraded = true;
                                break;
                            }
                        }
                    }
                    if (!degraded && origInternalContB != null)
                    {
                        foreach (var kvp in origInternalContB)
                        {
                            var vB = ContinuityUtils.GetContinuityVectorsAt(candB, kvp.Key, CurveEvaluationSide.Below);
                            var vA = ContinuityUtils.GetContinuityVectorsAt(candB, kvp.Key, CurveEvaluationSide.Above);
                            int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                            int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                            if (newCont < targetCont)
                            {
                                if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded Curve B internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                                degraded = true;
                                break;
                            }
                        }
                    }

                    if (degraded)
                    {
                        candA.Dispose();
                        candB.Dispose();
                        rejectedByInternalCont = true;
                        continue;
                    }
                }

                if (preventExtraInflections)
                {
                    int candInfA = candA.InflectionPoints()?.Length ?? 0;
                    int candInfB = candB.InflectionPoints()?.Length ?? 0;
                    if (candInfA > origInfA || candInfB > origInfB)
                    {
                        if (debug) RhinoApp.WriteLine($"    [Rejected] Added extra inflection points (OrigA: {origInfA}, OrigB: {origInfB} | CandA: {candInfA}, CandB: {candInfB})");
                        candA.Dispose();
                        candB.Dispose();
                        rejectedByInflection = true;
                        continue;
                    }
                }

                if (maxDev > devTol)
                {
                    if (candA != winner.ResultA) candA.Dispose();
                    if (candB != winner.ResultB) candB.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                if (avgDevSum < bestAvgDevSum)
                {
                    bestAvgDevSum = avgDevSum;
                    bestMaxDevA = maxDevA;
                    bestMaxDevB = maxDevB;
                    if (winner.ResultA != null && winner.ResultA != candA) winner.ResultA.Dispose();
                    if (winner.ResultB != null && winner.ResultB != candB) winner.ResultB.Dispose();
                    winner = (candA, candB);
                }
                else
                {
                    candA.Dispose();
                    candB.Dispose();
                }
            }

            ncWipA.Dispose();
            ncWipB.Dispose();

            if (winner.ResultA != null && bestMaxDevA <= RhinoMath.ZeroTolerance)
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding Result A: Max deviation ({bestMaxDevA:g}) is below ZeroTolerance.");
                winner.ResultA.Dispose();
                winner.ResultA = null;
            }
            if (winner.ResultB != null && bestMaxDevB <= RhinoMath.ZeroTolerance)
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding Result B: Max deviation ({bestMaxDevB:g}) is below ZeroTolerance.");
                winner.ResultB.Dispose();
                winner.ResultB = null;
            }

            string errorMsg = null;
            if (winner.ResultA == null && winner.ResultB == null)
            {
                if (rejectedByDev) errorMsg = "Result cannot be created because it exceeds the Crv dev limit.";
                else if (rejectedByInternalCont) errorMsg = "Result cannot be created because it would degrade internal continuity.";
                else if (rejectedByInflection) errorMsg = "Result cannot be created because it would add extra inflection points.";
                else errorMsg = "Result cannot be created with the current constraints (curves may already be optimized).";
            }

            return (winner.ResultA, winner.ResultB, errorMsg);
        }

        public static string PickBestErrorMessage(string errFirst, string errSecond, string errBoth)
        {
            string[] errs = { errFirst, errSecond, errBoth };
            foreach (string e in errs)
            {
                if (!string.IsNullOrEmpty(e) && !e.Contains("Lock") && !e.Contains("not yet supported"))
                    return e;
            }
            return errFirst ?? errSecond ?? errBoth ?? "Result cannot be created with the current constraints.";
        }

        public static double GetMaximumDeviation(Curve a, Curve b)
        {
            if (Curve.GetDistancesBetweenCurves(a, b, 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance,
                out double maxDist, out _, out _, out _, out _, out _))
            {
                return maxDist;
            }
            return double.MaxValue;
        }

        public static double GetAverageDeviation(Curve original, Curve modified, int sampleCount = 50)
        {
            var ts = original.DivideByCount(sampleCount, true);
            if (ts == null || ts.Length == 0) return double.MaxValue;

            double totalDist = 0.0;
            foreach (double t in ts)
            {
                Point3d ptOrig = original.PointAt(t);
                modified.ClosestPoint(ptOrig, out double tMod);
                totalDist += ptOrig.DistanceTo(modified.PointAt(tMod));
            }

            return totalDist / ts.Length;
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
    }
}