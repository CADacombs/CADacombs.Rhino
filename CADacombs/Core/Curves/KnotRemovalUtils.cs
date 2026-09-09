using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class KnotRemovalUtils
    {
        public enum PreserveEndType { None = 0, Tangency = 1, Curvature = 2 }

        /// <summary>
        /// Master method: Splits at fully multiple knots, normalizes parametric domains, 
        /// joins, and tests multiple brute-force knot removal strategies.
        /// </summary>
        public static (NurbsCurve OptimizedCurve, double MaxDeviation, string Log) OptimizeKnots(
            NurbsCurve curveIn, 
            double devTol, 
            PreserveEndType preserveEnd = PreserveEndType.Tangency,
            bool debug = false)
        {
            if (curveIn == null) return (null, 0.0, "Invalid input");

            StringBuilder log = new StringBuilder();
            if (debug) log.AppendLine($"--- Starting ccRemoveCrvKnots Optimization (Target Dev: {devTol:E3}) ---");

            // STEP 1: Find fully multiple knots to split the curve (from ccNurbsCrv_removeMultipleKnots.py)
            List<double> splitParams = new List<double>();
            int degree = curveIn.Degree;
            
            for (int i = degree; i < curveIn.Knots.Count - degree; i++)
            {
                int m = curveIn.Knots.KnotMultiplicity(i);
                if (m >= degree && !splitParams.Contains(curveIn.Knots[i]))
                {
                    splitParams.Add(curveIn.Knots[i]);
                }
                i += (m - 1);
            }

            // If no splits needed, just run the strategies on the whole curve
            if (splitParams.Count == 0)
            {
                if (debug) log.AppendLine("No fully multiple interior knots found. Running strategies on whole curve.");
                return RunRemovalStrategies(curveIn, devTol, preserveEnd, debug, log);
            }

            // STEP 2: Split, Normalize, and incrementally Re-Join
            Curve[] segments = curveIn.Split(splitParams);
            if (segments == null || segments.Length == 0) 
                return (curveIn, 0.0, "Split failed.");

            NurbsCurve currentWip = segments[0].ToNurbsCurve();

            for (int i = 1; i < segments.Length; i++)
            {
                NurbsCurve nextSeg = segments[i].ToNurbsCurve();

                // D(A) = D(R) * (L(A) / L(R))
                NormalizeDomain(currentWip, nextSeg, debug, log);

                Curve[] joined = Curve.JoinCurves(new[] { currentWip, nextSeg }, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                if (joined == null || joined.Length != 1)
                {
                    if (debug) log.AppendLine($"Failed to join segment {i}. Aborting optimization.");
                    return (curveIn, 0.0, log.ToString());
                }

                NurbsCurve joinedNc = joined[0].ToNurbsCurve();
                
                // Run all removal strategies on this new sub-assembly
                var optimizedSub = RunRemovalStrategies(joinedNc, devTol, preserveEnd, debug, log);
                currentWip = optimizedSub.OptimizedCurve;
            }

            // Final check against original curve
            double finalDev = GetMaxDeviation(curveIn, currentWip);
            return (currentWip, finalDev, log.ToString());
        }

        /// <summary>
        /// Modifies the domain of ncA to perfectly match the parametric speed of ncR at the joint.
        /// Formula: D(A) = D(R) * (L(A) / L(R))
        /// </summary>
        private static void NormalizeDomain(NurbsCurve ncR, NurbsCurve ncA, bool debug, StringBuilder log)
        {
            double dR = ncR.SpanDomain(ncR.SpanCount - 1).Length;
            double dA = ncA.SpanDomain(0).Length;

            int idxR = ncR.Points.Count - 1;
            double lR = ncR.Points[idxR].Location.DistanceTo(ncR.Points[idxR - 1].Location);
            double lA = ncA.Points[0].Location.DistanceTo(ncA.Points[1].Location);

            if (dR <= 1e-12 || dA <= 1e-12 || lR <= 1e-12 || lA <= 1e-12) return;

            // Core Math: D(A)_Ideal = D(R) * (L(A) / L(R))
            double m = (dR / dA) * (lA / lR);

            if (Math.Abs(m - 1.0) > 1e-6)
            {
                if (debug) log.AppendLine($"Normalizing domain of next segment. Multiplier: {m:F6}");
                double newLength = ncA.Domain.Length * m;
                ncA.Domain = new Interval(ncA.Domain.T0, ncA.Domain.T0 + newLength);
            }
            else
            {
                if (debug) log.AppendLine("Domains already matched within tolerance.");
            }
        }

        /// <summary>
        /// Tests all 6 Python removal strategies and returns the one with the fewest knots.
        /// </summary>
        private static (NurbsCurve OptimizedCurve, double MaxDeviation, string Strategy) RunRemovalStrategies(
            NurbsCurve ncIn, double devTol, PreserveEndType preserveEnd, bool debug, StringBuilder log)
        {
            List<(NurbsCurve Curve, double Dev, int KnotCount, string Strategy)> results = new List<(NurbsCurve, double, int, string)>();

            // Strategy 1: Remove All (from ccNurbsCrv_removeKnots.py)
            var allRemoved = ncIn.DuplicateCurve() as NurbsCurve;
            if (allRemoved.Knots.RemoveKnots(allRemoved.Degree, allRemoved.Knots.Count - allRemoved.Degree))
            {
                ApplyEndConditions(allRemoved, ncIn, preserveEnd);
                double dev = GetMaxDeviation(ncIn, allRemoved);
                LogStrategy("RemoveAll", dev, allRemoved, debug, log);
                if (dev <= devTol) results.Add((allRemoved, dev, allRemoved.Knots.Count, "RemoveAll"));
            }

            // Strategy 2 & 3: Plural Forward / Plural Reverse
            RunIterativeStrategy(ncIn, devTol, preserveEnd, true, true, "PluralForward", results, debug, log);
            RunIterativeStrategy(ncIn, devTol, preserveEnd, false, true, "PluralReverse", results, debug, log);

            // Strategy 4 & 5: Singular Forward / Singular Reverse
            RunIterativeStrategy(ncIn, devTol, preserveEnd, true, false, "SingularForward", results, debug, log);
            RunIterativeStrategy(ncIn, devTol, preserveEnd, false, false, "SingularReverse", results, debug, log);

            // Strategy 6: Bracketed Multiplicity (from ccNurbsCrv_removeMultipleKnots.py)
            for (int minM = 1; minM < ncIn.Degree; minM++)
            {
                for (int maxM = ncIn.Degree + 1; maxM > minM; maxM--)
                {
                    var multiCurve = ncIn.DuplicateCurve() as NurbsCurve;
                    int removed = multiCurve.Knots.RemoveMultipleKnots(minM, maxM, RhinoMath.UnsetValue);
                    
                    if (removed > 0)
                    {
                        ApplyEndConditions(multiCurve, ncIn, preserveEnd);
                        double dev = GetMaxDeviation(ncIn, multiCurve);
                        string sName = $"RemoveMulti[{minM},{maxM}]";
                        LogStrategy(sName, dev, multiCurve, debug, log);
                        if (dev <= devTol) results.Add((multiCurve, dev, multiCurve.Knots.Count, sName));
                    }
                }
            }

            if (results.Count == 0) return (ncIn, 0.0, "None");

            // Pick the winner
            results.Sort((a, b) => {
                int knotCmp = a.KnotCount.CompareTo(b.KnotCount);
                if (knotCmp != 0) return knotCmp;
                return a.Dev.CompareTo(b.Dev);
            });

            var winner = results[0];
            if (debug) log.AppendLine($"Winning Strategy: {winner.Strategy} (Dev: {winner.Dev:E3})");
            
            return (winner.Curve, winner.Dev, winner.Strategy);
        }

        private static void RunIterativeStrategy(NurbsCurve ncRef, double devTol, PreserveEndType preserveEnd, bool forward, bool plural, string name, List<(NurbsCurve, double, int, string)> results, bool debug, StringBuilder log)
        {
            var ncOut = ncRef.DuplicateCurve() as NurbsCurve;
            int degree = ncOut.Degree;
            int iK = forward ? degree : ncOut.Knots.Count - degree - 1;

            while (forward ? (iK < ncOut.Knots.Count - degree) : (iK >= degree))
            {
                int m = ncOut.Knots.KnotMultiplicity(iK);
                int targetToRemove = plural ? m : 1;
                bool success = false;

                // Fallback loop (e.g. Try removing 3, then 2, then 1)
                for (int r = targetToRemove; r >= 1; r--)
                {
                    var saved = ncOut.DuplicateCurve() as NurbsCurve;
                    int startIdx = forward ? iK : (iK - r + 1);
                    
                    if (ncOut.Knots.RemoveKnots(startIdx, startIdx + r))
                    {
                        ApplyEndConditions(ncOut, ncRef, preserveEnd);
                        if (GetMaxDeviation(ncRef, ncOut) <= devTol)
                        {
                            success = true;
                            break;
                        }
                    }
                    ncOut = saved;
                }

                if (success)
                {
                    if (forward) iK += (plural ? 0 : 1);
                    else iK -= (plural ? m : 1);
                }
                else
                {
                    if (forward) iK += m;
                    else iK -= m;
                }
            }

            double finalDev = GetMaxDeviation(ncRef, ncOut);
            LogStrategy(name, finalDev, ncOut, debug, log);
            
            if (finalDev <= devTol && ncOut.Knots.Count < ncRef.Knots.Count)
            {
                results.Add((ncOut, finalDev, ncOut.Knots.Count, name));
            }
        }

        private static void ApplyEndConditions(NurbsCurve ncMod, NurbsCurve ncRef, PreserveEndType type)
        {
            if (type == PreserveEndType.None) return;

            var cond = type == PreserveEndType.Tangency 
                ? NurbsCurve.NurbsCurveEndConditionType.Tangency 
                : NurbsCurve.NurbsCurveEndConditionType.Curvature;

            ncMod.SetEndCondition(false, cond, ncMod.PointAtStart, ncRef.TangentAtStart, ncRef.CurvatureAt(ncRef.Domain.T0));
            ncMod.SetEndCondition(true, cond, ncMod.PointAtEnd, ncRef.TangentAtEnd, ncRef.CurvatureAt(ncRef.Domain.T1));
        }

        private static double GetMaxDeviation(Curve crvA, Curve crvB)
        {
            if (Curve.GetDistancesBetweenCurves(crvA, crvB, 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
                out double maxDev, out _, out _, out _, out _, out _))
            {
                return maxDev;
            }
            return double.MaxValue;
        }

        private static void LogStrategy(string name, double dev, NurbsCurve crv, bool debug, StringBuilder log)
        {
            if (!debug) return;
            List<int> mults = new List<int>();
            for (int i = 0; i < crv.Knots.Count; i += crv.Knots.KnotMultiplicity(i))
                mults.Add(crv.Knots.KnotMultiplicity(i));
                
            log.AppendLine($"{name}  Dev: {dev:E3}  KnotCt: {crv.Knots.Count}  Multies: ({string.Join(",", mults)})");
        }
    }
}