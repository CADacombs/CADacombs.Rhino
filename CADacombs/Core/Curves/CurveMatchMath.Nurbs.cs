using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
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

        /// <summary>
        /// Prepares a NURBS curve to have enough control points (and minimum degree when curving a degree-1 line)
        /// for the requested continuity + opposite-end preservation across any input degree or span count.
        /// </summary>
        private static bool PrepareNurbsForContinuity(
            Curve origCrv, ref NurbsCurve ncWip,
            int continuity, int minCpNeeded, int lineMode, int arcMode, int incrAsNeeded,
            bool isPairMode, out string errorMsg)
        {
            errorMsg = null;
            string noun = isPairMode ? "a curve" : "the curve";
            string lineNoun = isPairMode ? "a line" : "the line";

            if (origCrv is ArcCurve)
            {
                if (arcMode != 2)
                {
                    errorMsg = $"Result cannot be created because {noun} is an arc and Convert to NURBS is disabled.";
                    return false;
                }
                Curve rebuilt = origCrv.Rebuild(6, 5, false);
                if (rebuilt != null)
                {
                    ncWip.Dispose();
                    ncWip = rebuilt.ToNurbsCurve();
                }
                if (ncWip == null)
                {
                    errorMsg = "Failed to convert arc to NURBS.";
                    return false;
                }
            }
            else if (origCrv.IsArc(RhinoMath.ZeroTolerance) && arcMode != 2)
            {
                errorMsg = $"Result cannot be created because {noun} is an arc and Convert to NURBS is disabled.";
                return false;
            }
            else if (origCrv.IsLinear(RhinoMath.ZeroTolerance) && lineMode != 2 && ncWip.Points.Count < minCpNeeded)
            {
                errorMsg = $"Result cannot be created because {noun} is a line and Convert to NURBS is disabled.";
                return false;
            }

            // A degree-1 curve with SpanCt knot insertion alone remains piecewise linear and cannot achieve G1/G2 curvature
            bool needsDegreeForSmoothness = ncWip.Degree == 1 && continuity >= 1;

            if (ncWip.Points.Count < minCpNeeded || needsDegreeForSmoothness)
            {
                if (incrAsNeeded == 2)
                {
                    bool isLine = origCrv.IsLinear(RhinoMath.ZeroTolerance);
                    errorMsg = $"Result cannot be created because {(isLine ? lineNoun : noun)} requires more control points and Incr as needed is disabled.";
                    return false;
                }

                if (incrAsNeeded == 0) // Increase Degree
                {
                    int targetDeg = Math.Max(ncWip.Degree + Math.Max(0, minCpNeeded - ncWip.Points.Count),
                                             needsDegreeForSmoothness ? Math.Max(2, continuity) : ncWip.Degree);
                    if (targetDeg > ncWip.Degree)
                        ncWip.IncreaseDegree(targetDeg);
                }
                else if (incrAsNeeded == 1) // Increase Span Count
                {
                    if (needsDegreeForSmoothness)
                    {
                        int smoothDeg = Math.Min(3, Math.Max(2, minCpNeeded - 1));
                        if (smoothDeg > ncWip.Degree)
                            ncWip.IncreaseDegree(smoothDeg);
                    }

                    int ptsNeeded = minCpNeeded - ncWip.Points.Count;
                    if (ptsNeeded > 0)
                    {
                        InsertInteriorKnotsSafely(ncWip, ptsNeeded);
                    }
                }
            }

            if (ncWip.Points.Count < minCpNeeded)
            {
                errorMsg = $"Result cannot be created because {noun} does not have enough control points for the requested continuity.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Inserts ptsNeeded interior knots strictly inside open knot spans so existing knot multiplicities
        /// and internal continuities are never degraded.
        /// </summary>
        private static void InsertInteriorKnotsSafely(NurbsCurve nc, int ptsNeeded)
        {
            if (ptsNeeded <= 0) return;

            if (nc.SpanCount == 1)
            {
                Interval dom = nc.SpanDomain(0);
                for (int k = 1; k <= ptsNeeded; k++)
                {
                    double t = dom.ParameterAt((double)k / (ptsNeeded + 1));
                    nc.Knots.InsertKnot(t, 1);
                }
                return;
            }

            for (int k = 0; k < ptsNeeded; k++)
            {
                int bestSpan = 0;
                double bestLen = -1.0;
                int spanCount = nc.SpanCount;

                for (int s = 0; s < spanCount; s++)
                {
                    Interval spDom = nc.SpanDomain(s);
                    double chord = nc.PointAt(spDom.T0).DistanceTo(nc.PointAt(spDom.T1));
                    if (chord > bestLen)
                    {
                        bestLen = chord;
                        bestSpan = s;
                    }
                }

                double tMid = nc.SpanDomain(bestSpan).Mid;
                nc.Knots.InsertKnot(tMid, 1);
            }
        }

        private static (Curve Result, string ErrorMsg) SolveNurbsOneCurve(
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
            NurbsCurve ncMod = crvToMod.ToNurbsCurve();
            NurbsCurve ncRef = crvRef.ToNurbsCurve();

            if (ncMod == null || ncRef == null)
            {
                ncMod?.Dispose();
                ncRef?.Dispose();
                return (null, "Failed to convert curves to NURBS.");
            }

            int minCpNeeded = preserveOtherEnd + continuity + 1;
            if (!PrepareNurbsForContinuity(crvToMod, ref ncMod, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, false, out string prepErr))
            {
                ncMod?.Dispose();
                ncRef.Dispose();
                return (null, prepErr);
            }

            double tRef = refT1 ? ncRef.Domain.T1 : ncRef.Domain.T0;
            Point3d ptTarget = refT1 ? ncRef.PointAtEnd : ncRef.PointAtStart;
            Vector3d tanTarget = ncRef.TangentAt(tRef);
            if (modT1 == refT1) tanTarget.Reverse();
            tanTarget.Unitize();

            var candidates = new List<(NurbsCurve Curve, string Desc)>();

            if (continuity == 0)
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;

                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                cand1.Points.SetPoint(idxCpPosOne, ptTarget, cand1.Points.GetWeight(idxCpPosOne));
                candidates.Add((cand1, "Position: Move P0 Only"));

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                bool ok = modT1 ? cand2.SetEndPoint(ptTarget) : cand2.SetStartPoint(ptTarget);
                if (ok) candidates.Add((cand2, "Position: SetEndCondition"));
                else cand2.Dispose();
            }
            else if (continuity == 1)
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;
                int idxCpTanOne = modT1 ? ncMod.Points.Count - 2 : 1;

                // Project P1 strictly onto the forward inward ray so obtuse approaches never create a 180-deg cusp
                Vector3d inwardDir = modT1 ? -tanTarget : tanTarget;
                if (inwardDir.Unitize())
                {
                    double proj = (ncMod.Points[idxCpTanOne].Location - ptTarget) * inwardDir;
                    if (proj > 1e-6)
                    {
                        NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                        cand1.Points.SetPoint(idxCpPosOne, ptTarget, cand1.Points.GetWeight(idxCpPosOne));
                        cand1.Points.SetPoint(idxCpTanOne, ptTarget + proj * inwardDir, cand1.Points.GetWeight(idxCpTanOne));
                        candidates.Add((cand1, "Tangency: ClosestPoint (Slide P1)"));
                    }
                }

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                if (cand2.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptTarget, tanTarget))
                    candidates.Add((cand2, "Tangency: SetEndCondition"));
                else
                    cand2.Dispose();
            }
            else if (continuity == 2)
            {
                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                if (cand1.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptTarget, tanTarget, ncRef.CurvatureAt(tRef)))
                    candidates.Add((cand1, "Curvature: SetEndCondition"));
                else
                    cand1.Dispose();
            }

            NurbsCurve winner = null;
            double bestAvgDev = double.MaxValue;
            double bestMaxDev = double.MaxValue;

            int origInf = crvToMod.InflectionPoints()?.Length ?? 0;
            var origInternalCont = preserveInternalContinuity ? GetInternalContinuities(ncMod, posTol, angleTolDeg, curvTolPct) : null;

            bool rejectedByInflection = false;
            bool rejectedByDev = false;
            bool rejectedByInternalCont = false;

            if (debug) RhinoApp.WriteLine($"--- MatchOneCurve NURBS Candidates Evaluated ({candidates.Count}) ---");

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

            if (winner != null && !IsMeaningfullyChanged(crvToMod, winner, bestMaxDev))
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding result: Maximum deviation ({bestMaxDev:g}) is below ZeroTolerance.");
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

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveNurbsBothCurves(
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
            NurbsCurve ncWipA = crvA.ToNurbsCurve();
            NurbsCurve ncWipB = crvB.ToNurbsCurve();

            if (ncWipA == null || ncWipB == null)
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, "Failed to convert curves to NURBS.");
            }

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (!PrepareNurbsForContinuity(crvA, ref ncWipA, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, true, out string errA))
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, errA);
            }

            if (!PrepareNurbsForContinuity(crvB, ref ncWipB, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, true, out string errB))
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, errB);
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
            Point3d ptAvg = 0.5 * (ptA + ptB);

            Vector3d tanA = ncWipA.TangentAt(tA);
            Vector3d tanB = ncWipB.TangentAt(tB);
            Vector3d curvA = ncWipA.CurvatureAt(tA);
            Vector3d curvB = ncWipB.CurvatureAt(tB);

            Vector3d tanTargetA = Vector3d.Unset;
            Vector3d tanTargetB = Vector3d.Unset;

            if (continuity >= 1)
            {
                GetOrientedEnds(ncWipA, modT1_A, out Point3d pFixA, out _, out _, out Vector3d tWorkOutA);
                GetOrientedEnds(ncWipB, modT1_B, out Point3d pFixB, out _, out _, out Vector3d tWorkOutB);
                Vector3d sharedFlow = ComputeSharedFlowDirection(tWorkOutA, tWorkOutB, pFixA, pFixB, Vector3d.ZAxis);

                tanTargetA = modT1_A ? sharedFlow : -sharedFlow;
                tanTargetB = modT1_B ? -sharedFlow : sharedFlow;

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
                candA.Points.SetPoint(idxCpPosA, ptAvg, candA.Points.GetWeight(idxCpPosA));
                candB.Points.SetPoint(idxCpPosB, ptAvg, candB.Points.GetWeight(idxCpPosB));
                candidates.Add((candA, candB, "Both Position: Move P0"));
            }
            else if (continuity == 1)
            {
                // Project P1 strictly along each curve's forward inward ray to guarantee smooth G1 (no 180-deg cusps)
                Vector3d inwardDirA = modT1_A ? -tanTargetA : tanTargetA;
                Vector3d inwardDirB = modT1_B ? -tanTargetB : tanTargetB;

                if (inwardDirA.Unitize() && inwardDirB.Unitize())
                {
                    double projA = (ncWipA.Points[idxCpTanA].Location - ptAvg) * inwardDirA;
                    double projB = (ncWipB.Points[idxCpTanB].Location - ptAvg) * inwardDirB;

                    if (projA > 1e-6 && projB > 1e-6)
                    {
                        NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                        NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();
                        cand1A.Points.SetPoint(idxCpPosA, ptAvg, cand1A.Points.GetWeight(idxCpPosA));
                        cand1B.Points.SetPoint(idxCpPosB, ptAvg, cand1B.Points.GetWeight(idxCpPosB));
                        cand1A.Points.SetPoint(idxCpTanA, ptAvg + projA * inwardDirA, cand1A.Points.GetWeight(idxCpTanA));
                        cand1B.Points.SetPoint(idxCpTanB, ptAvg + projB * inwardDirB, cand1B.Points.GetWeight(idxCpTanB));
                        candidates.Add((cand1A, cand1B, "Both Tangency: ClosestPoint (Slide P1)"));
                    }
                }

                NurbsCurve cand2A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand2B = (NurbsCurve)ncWipB.Duplicate();
                bool ok2A = cand2A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetA);
                bool ok2B = cand2B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetB);

                if (ok2A && ok2B)
                {
                    candidates.Add((cand2A, cand2B, "Both Tangency: SetEndCondition"));

                    if (trueOrigDistA > RhinoMath.ZeroTolerance && trueOrigDistB > RhinoMath.ZeroTolerance)
                    {
                        NurbsCurve cand3A = (NurbsCurve)cand2A.Duplicate();
                        NurbsCurve cand3B = (NurbsCurve)cand2B.Duplicate();

                        Vector3d newDirA = cand3A.Points[idxCpTanA].Location - cand3A.Points[idxCpPosA].Location;
                        Vector3d newDirB = cand3B.Points[idxCpTanB].Location - cand3B.Points[idxCpPosB].Location;
                        if (newDirA.Unitize() && newDirB.Unitize())
                        {
                            cand3A.Points.SetPoint(idxCpTanA, ptAvg + (newDirA * trueOrigDistA), cand3A.Points.GetWeight(idxCpTanA));
                            cand3B.Points.SetPoint(idxCpTanB, ptAvg + (newDirB * trueOrigDistB), cand3B.Points.GetWeight(idxCpTanB));
                            candidates.Add((cand3A, cand3B, "Both Tangency: Preserve P0-P1 (_Match)"));
                        }
                        else
                        {
                            cand3A.Dispose();
                            cand3B.Dispose();
                        }
                    }
                }
                else
                {
                    cand2A.Dispose();
                    cand2B.Dispose();
                }
            }
            else if (continuity == 2)
            {
                Vector3d curvTarget = (0.5 * curvA) + (0.5 * curvB);
                curvTarget -= (curvTarget * tanTargetB) * tanTargetB;

                if (debug) RhinoApp.WriteLine($"[Debug] Shared Curvature Target: {curvTarget}");

                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();

                bool okA = cand1A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetA, curvTarget);
                bool okB = cand1B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetB, curvTarget);

                if (okA && okB)
                {
                    candidates.Add((cand1A, cand1B, "Both Curvature: SetEndCondition"));
                }
                else
                {
                    cand1A.Dispose();
                    cand1B.Dispose();
                }
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

            if (debug) RhinoApp.WriteLine($"--- MatchBothCurves NURBS Candidates Evaluated ({candidates.Count}) ---");

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

            if (winner.ResultA != null && !IsMeaningfullyChanged(crvA, winner.ResultA, bestMaxDevA))
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding Result A: Max deviation ({bestMaxDevA:g}) is below ZeroTolerance.");
                winner.ResultA.Dispose();
                winner.ResultA = null;
            }
            if (winner.ResultB != null && !IsMeaningfullyChanged(crvB, winner.ResultB, bestMaxDevB))
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
    }
}
