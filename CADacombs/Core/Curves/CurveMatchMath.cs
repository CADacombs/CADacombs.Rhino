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
            bool maximizeMinRadius, 
            double devTol)
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

            var candidates = new List<NurbsCurve>();
            NurbsCurve ncWip = (NurbsCurve)ncMod.Duplicate();

            if (continuity == 0) // Position
            {
                if (modT1) ncWip.SetEndPoint(ptTarget);
                else ncWip.SetStartPoint(ptTarget);
                candidates.Add(ncWip);
            }
            else if (continuity == 1) // Tangency
            {
                ncWip.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptTarget, tanTarget);
                candidates.Add(ncWip);
            }
            else if (continuity == 2) // Curvature
            {
                Vector3d curvTarget = ncRef.CurvatureAt(tRef);
                ncWip.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptTarget, tanTarget, curvTarget);
                candidates.Add(ncWip);
            }

            if (maximizeMinRadius && continuity < 2)
            {
                var maximizedCrvs = new List<NurbsCurve>();
                foreach (var cand in candidates)
                {
                    var maxRadiusNc = MaximizeMinRadiusOneEnd(cand, modT1, devTol, crvToMod);
                    if (maxRadiusNc != null) maximizedCrvs.Add(maxRadiusNc);
                }
                candidates.AddRange(maximizedCrvs);
            }

            NurbsCurve winner = null;
            double bestDev = double.MaxValue;
            double bestRadius = double.MinValue;

            foreach (var cand in candidates)
            {
                double dev = GetMaximumDeviation(crvToMod, cand);
                if (dev > devTol) 
                {
                    cand.Dispose();
                    continue;
                }

                double? minRad = RadiusMinimaMath.GetMinimumRadius(cand);
                double radVal = minRad ?? double.MinValue;

                if (maximizeMinRadius)
                {
                    if (radVal > bestRadius)
                    {
                        bestRadius = radVal;
                        winner?.Dispose();
                        winner = cand;
                    }
                    else cand.Dispose();
                }
                else
                {
                    if (dev < bestDev)
                    {
                        bestDev = dev;
                        winner?.Dispose();
                        winner = cand;
                    }
                    else cand.Dispose();
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
            bool maximizeMinRadius, 
            double devTol)
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

            Point3d ptA = modT1_A ? ncWipA.PointAtEnd : ncWipA.PointAtStart;
            Point3d ptB = modT1_B ? ncWipB.PointAtEnd : ncWipB.PointAtStart;
            Point3d ptAvg = (Point3d)(0.5 * (Vector3d)ptA + 0.5 * (Vector3d)ptB);

            if (continuity >= 0)
            {
                if (modT1_A) ncWipA.SetEndPoint(ptAvg); else ncWipA.SetStartPoint(ptAvg);
                if (modT1_B) ncWipB.SetEndPoint(ptAvg); else ncWipB.SetStartPoint(ptAvg);
            }

            if (continuity >= 1)
            {
                Vector3d tanA = ncWipA.TangentAt(tA);
                Vector3d tanB = ncWipB.TangentAt(tB);
                
                Vector3d tanTargetB = modT1_A == modT1_B ? (0.5 * -tanA) + (0.5 * tanB) : (0.5 * tanA) + (0.5 * tanB);
                tanTargetB.Unitize();
                Vector3d tanTargetA = modT1_A == modT1_B ? -tanTargetB : tanTargetB;

                ncWipA.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetA);
                ncWipB.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetB);
                
                if (maximizeMinRadius && continuity < 2)
                {
                    var maxA = MaximizeMinRadiusOneEnd(ncWipA, modT1_A, devTol, crvA);
                    if (maxA != null) { ncWipA.Dispose(); ncWipA = maxA; }
                    
                    var maxB = MaximizeMinRadiusOneEnd(ncWipB, modT1_B, devTol, crvB);
                    if (maxB != null) { ncWipB.Dispose(); ncWipB = maxB; }
                }
            }

            if (continuity >= 2)
            {
                Vector3d curvA = ncWipA.CurvatureAt(tA);
                Vector3d curvB = ncWipB.CurvatureAt(tB);
                
                Vector3d curvTargetB = modT1_A == modT1_B ? (0.5 * -curvA) + (0.5 * curvB) : (0.5 * curvA) + (0.5 * curvB);
                Vector3d curvTargetA = modT1_A == modT1_B ? -curvTargetB : curvTargetB;

                ncWipA.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, ncWipA.TangentAt(tA), curvTargetA);
                ncWipB.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, ncWipB.TangentAt(tB), curvTargetB);
            }

            if (GetMaximumDeviation(crvA, ncWipA) > devTol || GetMaximumDeviation(crvB, ncWipB) > devTol)
            {
                ncWipA.Dispose();
                ncWipB.Dispose();
                return (null, null);
            }

            return (ncWipA, ncWipB);
        }

        public static NurbsCurve MaximizeMinRadiusOneEnd(NurbsCurve ncIn, bool isT1, double devTol, Curve refCrvForDev)
        {
            int idxG0 = isT1 ? ncIn.Points.Count - 1 : 0;
            int idxG1 = isT1 ? ncIn.Points.Count - 2 : 1;
            int idxG2 = isT1 ? ncIn.Points.Count - 3 : 2;

            Vector3d vPosToTan = isT1 ? -ncIn.TangentAtEnd : ncIn.TangentAtStart;
            
            double distG0toG1 = ncIn.Points[idxG0].Location.DistanceTo(ncIn.Points[idxG1].Location);
            double distG0toG2 = ncIn.Points[idxG0].Location.DistanceTo(ncIn.Points[idxG2].Location);

            double currentMinRad = RadiusMinimaMath.GetMinimumRadius(ncIn) ?? 0.0;

            double epsilonLoc = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            
            NurbsCurve ncLonger = (NurbsCurve)ncIn.Duplicate();
            ncLonger.Points.SetPoint(idxG1, ncIn.Points[idxG1].Location + epsilonLoc * vPosToTan);
            double radLonger = RadiusMinimaMath.GetMinimumRadius(ncLonger) ?? 0.0;

            NurbsCurve ncShorter = (NurbsCurve)ncIn.Duplicate();
            ncShorter.Points.SetPoint(idxG1, ncIn.Points[idxG1].Location - epsilonLoc * vPosToTan);
            double radShorter = RadiusMinimaMath.GetMinimumRadius(ncShorter) ?? 0.0;

            int dir = 0;
            if (Math.Abs(currentMinRad - radLonger) < 1e-6 && Math.Abs(currentMinRad - radShorter) < 1e-6) dir = 0;
            else if (radLonger > currentMinRad) dir = 1;
            else if (radShorter > currentMinRad) dir = -1;

            if (dir == 0)
            {
                ncLonger.Dispose();
                ncShorter.Dispose();
                return null;
            }

            double distL, distH;
            if (dir == 1)
            {
                distL = distG0toG1;
                distH = 0.99 * distG0toG2;
            }
            else
            {
                distL = 0.01 * distG0toG2;
                distH = distG0toG1;
            }

            NurbsCurve bestCurve = null;
            
            while (Math.Abs(distH - distL) > 1e-6)
            {
                double distM = 0.5 * (distL + distH);
                
                NurbsCurve ncWipM = (NurbsCurve)ncIn.Duplicate();
                ncWipM.Points.SetPoint(idxG1, ncIn.Points[idxG0].Location + distM * vPosToTan);
                double radM = RadiusMinimaMath.GetMinimumRadius(ncWipM) ?? 0.0;

                NurbsCurve ncWipL = (NurbsCurve)ncIn.Duplicate();
                ncWipL.Points.SetPoint(idxG1, ncIn.Points[idxG0].Location + distL * vPosToTan);
                double radL = RadiusMinimaMath.GetMinimumRadius(ncWipL) ?? 0.0;

                NurbsCurve ncWipH = (NurbsCurve)ncIn.Duplicate();
                ncWipH.Points.SetPoint(idxG1, ncIn.Points[idxG0].Location + distH * vPosToTan);
                double radH = RadiusMinimaMath.GetMinimumRadius(ncWipH) ?? 0.0;

                if (Math.Abs(radL - radH) < 1e-6)
                {
                    bestCurve = ncWipM;
                    break;
                }

                if (radM > radH && radH > radL) distL = distM;
                else if (radM > radL && radL > radH) distL = distM;
                else if (radM > radL && radM > radH)
                {
                    NurbsCurve ncMTempLong = (NurbsCurve)ncWipM.Duplicate();
                    ncMTempLong.Points.SetPoint(idxG1, ncWipM.Points[idxG1].Location + epsilonLoc * vPosToTan);
                    if ((RadiusMinimaMath.GetMinimumRadius(ncMTempLong) ?? 0) > radM) distL = distM;
                    else distH = distM;
                }
                else if (radH > radM && radM > radL) distL = distM;
                else if (radL > radM && radM > radH) distH = distM;
            }

            ncLonger.Dispose();
            ncShorter.Dispose();

            if (bestCurve != null)
            {
                if (GetMaximumDeviation(refCrvForDev, bestCurve) <= devTol) return bestCurve;
                bestCurve.Dispose();
            }

            return null;
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
    }
}