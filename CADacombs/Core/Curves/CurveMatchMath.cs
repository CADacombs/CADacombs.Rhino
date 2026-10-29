using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class CurveMatchMath
    {
        public static NurbsCurve MatchOneCurve(
            Curve crvToMod, 
            Curve crvRef, 
            bool modT1, 
            bool refT1, 
            int continuity, 
            int preserveOtherEnd, 
            bool deformLines, 
            double devTol,
            bool debug = false)
        {
            NurbsCurve ncMod = crvToMod.ToNurbsCurve();
            NurbsCurve ncRef = crvRef.ToNurbsCurve();
            
            if (ncMod == null || ncRef == null) return null;

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (deformLines && ncMod.Points.Count < minCpNeeded && ncMod.Points.Count == 2)
            {
                var ts = ncMod.DivideByCount(minCpNeeded - 1, true);
                if (ts != null)
                {
                    var pts = ts.Select(t => ncMod.PointAt(t)).ToList();
                    ncMod = NurbsCurve.Create(false, 3, pts);
                }
            }

            if (ncMod.Points.Count < minCpNeeded)
            {
                ncMod.Dispose();
                ncRef.Dispose();
                return null;
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

                // Cand 1: ClosestPoint
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

                // Cand 2: SetEndCondition (Mathematically identical to _Match for One Curve)
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

                if (maxDev > devTol) 
                {
                    cand.Curve.Dispose();
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

            return winner;
        }

        public static (NurbsCurve ResultA, NurbsCurve ResultB) MatchBothCurves(
            Curve crvA, 
            Curve crvB, 
            bool modT1_A, 
            bool modT1_B, 
            int continuity, 
            int preserveOtherEnd, 
            bool deformLines, 
            double devTol,
            bool debug = false)
        {
            NurbsCurve ncWipA = crvA.ToNurbsCurve();
            NurbsCurve ncWipB = crvB.ToNurbsCurve();

            if (ncWipA == null || ncWipB == null) return (null, null);

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (deformLines)
            {
                if (ncWipA.Points.Count < minCpNeeded && ncWipA.Points.Count == 2)
                {
                    var ts = ncWipA.DivideByCount(minCpNeeded - 1, true);
                    if (ts != null) ncWipA = NurbsCurve.Create(false, 3, ts.Select(t => ncWipA.PointAt(t)).ToList());
                }
                if (ncWipB.Points.Count < minCpNeeded && ncWipB.Points.Count == 2)
                {
                    var ts = ncWipB.DivideByCount(minCpNeeded - 1, true);
                    if (ts != null) ncWipB = NurbsCurve.Create(false, 3, ts.Select(t => ncWipB.PointAt(t)).ToList());
                }
            }

            if (ncWipA.Points.Count < minCpNeeded || ncWipB.Points.Count < minCpNeeded)
            {
                ncWipA.Dispose();
                ncWipB.Dispose();
                return (null, null);
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
                Vector3d tanTargetB = modT1_A == modT1_B ? (0.5 * -tanA) + (0.5 * tanB) : (0.5 * tanA) + (0.5 * tanB);
                Vector3d tanTargetA = modT1_A == modT1_B ? -tanTargetB : tanTargetB;

                // Cand 1: ClosestPoint
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

                // Cand 2: SetEndCondition
                NurbsCurve cand2A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand2B = (NurbsCurve)ncWipB.Duplicate();
                cand2A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetA);
                cand2B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetB);
                candidates.Add((cand2A, cand2B, "Both Tangency: SetEndCondition"));

                // Cand 3: Preserve orig P0-P1 (Kept for Both because vectors diverge)
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
                Vector3d curvTargetB = modT1_A == modT1_B ? (0.5 * -curvA) + (0.5 * curvB) : (0.5 * curvA) + (0.5 * curvB);
                Vector3d curvTargetA = modT1_A == modT1_B ? -curvTargetB : curvTargetB;

                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();
                cand1A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, ncWipA.TangentAt(tA), curvTargetA);
                cand1B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, ncWipB.TangentAt(tB), curvTargetB);
                candidates.Add((cand1A, cand1B, "Both Curvature: SetEndCondition"));
            }

            (NurbsCurve ResultA, NurbsCurve ResultB) winner = (null, null);
            double bestAvgDevSum = double.MaxValue;

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

                if (maxDev > devTol) 
                {
                    if (candA != winner.ResultA) candA.Dispose();
                    if (candB != winner.ResultB) candB.Dispose();
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

            return winner;
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