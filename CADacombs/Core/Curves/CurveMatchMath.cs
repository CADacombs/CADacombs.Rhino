using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class CurveMatchMath
    {
        // ==============================================================================
        // BACKWARD COMPATIBILITY OVERLOADS (For older commands/scripts calling this API)
        // ==============================================================================
        
        public static (NurbsCurve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod, Curve crvRef, bool modT1, bool refT1, 
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchOneCurve(crvToMod, crvRef, modT1, refT1, continuity, preserveOtherEnd, 
                                 deformLines, false, 2, devTol, false, false, 2, 
                                 RhinoMath.ZeroTolerance, RhinoMath.ToRadians(1.0), 5.0, debug);
        }

        public static (NurbsCurve ResultA, NurbsCurve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA, Curve crvB, bool modT1_A, bool modT1_B, 
            int continuity, int preserveOtherEnd, bool deformLines, double devTol, bool debug = false)
        {
            return MatchBothCurves(crvA, crvB, modT1_A, modT1_B, continuity, preserveOtherEnd, 
                                   deformLines, false, 2, devTol, false, false, 2, 
                                   RhinoMath.ZeroTolerance, RhinoMath.ToRadians(1.0), 5.0, debug);
        }

        // ==============================================================================
        // NEW CORE ENGINE
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

        public static (NurbsCurve Result, string ErrorMsg) MatchOneCurve(
            Curve crvToMod, 
            Curve crvRef, 
            bool modT1, 
            bool refT1, 
            int continuity, 
            int preserveOtherEnd, 
            bool deformLines, 
            bool deformArcs,
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
            NurbsCurve ncMod = crvToMod.ToNurbsCurve();
            NurbsCurve ncRef = crvRef.ToNurbsCurve();
            
            if (ncMod == null || ncRef == null) return (null, "Failed to convert curves to NURBS.");

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (crvToMod.IsArc())
            {
                if (!deformArcs) { ncMod.Dispose(); ncRef.Dispose(); return (null, "Result cannot be created because the curve is an arc and Deform arcs is disabled."); }
                Curve rebuilt = crvToMod.Rebuild(6, 5, false);
                if (rebuilt != null) { ncMod.Dispose(); ncMod = rebuilt.ToNurbsCurve(); }
            }
            else if (crvToMod.IsLinear() && ncMod.Points.Count < minCpNeeded)
            {
                if (!deformLines) { ncMod.Dispose(); ncRef.Dispose(); return (null, "Result cannot be created because the curve is a line and Deform lines is disabled."); }
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
                    RhinoApp.WriteLine($" {cand.Desc}\n    AvgDev: {avgDev:F6} | MaxDev: {maxDev:F6} | MinRad: {(minRad.HasValue ? minRad.Value.ToString("F6") : "N/A")} | P0-P1 Dist: {p0p1Dist:F6}");

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
                    winner?.Dispose();
                    winner = cand.Curve;
                }
                else
                {
                    cand.Curve.Dispose();
                }
            }

            if (winner != null && winner.EpsilonEquals(ncMod, 1e-12))
            {
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

        public static (NurbsCurve ResultA, NurbsCurve ResultB, string ErrorMsg) MatchBothCurves(
            Curve crvA, 
            Curve crvB, 
            bool modT1_A, 
            bool modT1_B, 
            int continuity, 
            int preserveOtherEnd, 
            bool deformLines, 
            bool deformArcs,
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
            NurbsCurve ncWipA = crvA.ToNurbsCurve();
            NurbsCurve ncWipB = crvB.ToNurbsCurve();

            if (ncWipA == null || ncWipB == null) return (null, null, "Failed to convert curves to NURBS.");

            int minCpNeeded = preserveOtherEnd + continuity + 1;
            string localErr = null;

            void ProcessCrv(Curve origCrv, ref NurbsCurve ncWip)
            {
                if (origCrv.IsArc())
                {
                    if (!deformArcs) { localErr = "Result cannot be created because a curve is an arc and Deform arcs is disabled."; ncWip.Dispose(); ncWip = null; return; }
                    Curve rebuilt = origCrv.Rebuild(6, 5, false);
                    if (rebuilt != null) { ncWip.Dispose(); ncWip = rebuilt.ToNurbsCurve(); }
                }
                else if (origCrv.IsLinear() && ncWip.Points.Count < minCpNeeded)
                {
                    if (!deformLines) { localErr = "Result cannot be created because a curve is a line and Deform lines is disabled."; ncWip.Dispose(); ncWip = null; return; }
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

                if (debug)
                {
                    RhinoApp.WriteLine($"[Debug] Shared Curvature Target: {curvTarget}");
                }

                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();
                
                cand1A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetA, curvTarget);
                cand1B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetB, curvTarget);
                
                candidates.Add((cand1A, cand1B, "Both Curvature: SetEndCondition"));
            }

            (NurbsCurve ResultA, NurbsCurve ResultB) winner = (null, null);
            double bestAvgDevSum = double.MaxValue;
            
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
                    RhinoApp.WriteLine($" {desc}\n    AvgDev Sum: {avgDevSum:F6} (A:{avgDevA:F6}, B:{avgDevB:F6}) | MaxDev: {maxDev:F6} | MinRad: {(minRad != double.MinValue ? minRad.ToString("F6") : "N/A")} | P0-P1 Dist: (A:{p0p1A:F6}, B:{p0p1B:F6})");

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

            string errorMsg = null;
            if (winner.ResultA == null)
            {
                if (rejectedByDev) errorMsg = "Result cannot be created because it exceeds the Crv dev limit.";
                else if (rejectedByInternalCont) errorMsg = "Result cannot be created because it would degrade internal continuity.";
                else if (rejectedByInflection) errorMsg = "Result cannot be created because it would add extra inflection points.";
                else errorMsg = "Result cannot be created with the current constraints.";
            }

            return (winner.ResultA, winner.ResultB, errorMsg);
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
    }
}