using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class SpanConversionUtils
    {
        /// <summary>
        /// A delegate defining how to evaluate and convert a curve segment.
        /// It should return the converted geometry, the calculated deviation, and a log string if it fails.
        /// </summary>
        public delegate (Curve Converted, double Deviation, string Log) CurveEvaluator(Curve input);

        /// <summary>
        /// A generic greedy-expansion routine that finds the longest possible valid continuous 
        /// spans between knots and converts them using the provided evaluator delegate.
        /// </summary>
        public static (Curve[] Segments, double MaxDev)? ConvertBetweenKnots(
            NurbsCurve ncIn,
            CurveEvaluator evaluator,
            bool tolByRatio,
            double tolRatio,
            double devTol,
            double minNewCrvLen)
        {
            if (ncIn == null) return null;

            // 1. Identify strictly interior split parameters based on Rhino's knot array
            List<double> interiorSplits = new List<double>();
            for (int k = ncIn.Degree; k < ncIn.Knots.Count - ncIn.Degree; k++)
            {
                double t = ncIn.Knots[k];
                if (!interiorSplits.Contains(t)) interiorSplits.Add(t);
            }

            // 2. Split into atomic segments
            Curve[] atomicSegs;
            if (interiorSplits.Count == 0)
            {
                // The curve is already a single span
                atomicSegs = new Curve[] { ncIn };
            }
            else
            {
                atomicSegs = ncIn.Split(interiorSplits);
                if (atomicSegs == null || atomicSegs.Length == 0) return null;
            }

            // 3. Build parametric nodes (equivalent to the old tsKnots, but guaranteed safe)
            double[] tNodes = new double[atomicSegs.Length + 1];
            tNodes[0] = atomicSegs[0].Domain.Min;
            for (int i = 0; i < atomicSegs.Length; i++) 
            {
                tNodes[i + 1] = atomicSegs[i].Domain.Max;
            }

            // 4. Test each atomic segment to identify viable starting points
            bool[] isSegWithinTol = new bool[atomicSegs.Length];
            for (int i = 0; i < atomicSegs.Length; i++)
            {
                var eval = evaluator(atomicSegs[i]);
                if (eval.Converted != null && IsWithinDeviationTolerance(atomicSegs[i], eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                {
                    isSegWithinTol[i] = true;
                }
                else
                {
                    isSegWithinTol[i] = false;
                }
            }

            if (!isSegWithinTol.Contains(true))
                return null; 

            // 5. Greedy Expansion Loop
            List<int> validNodeIndices = new List<int>();
            int segIdx = 0;

            while (segIdx < atomicSegs.Length)
            {
                RhinoApp.Wait(); // Keeps Rhino responsive during tight loops

                if (!isSegWithinTol[segIdx])
                {
                    segIdx++;
                    continue;
                }

                int j;
                Curve lastValidSeg = null;

                for (j = segIdx; j < isSegWithinTol.Length; j++)
                {
                    if (!isSegWithinTol[j])
                    {
                        j--;
                        break;
                    }

                    double t0 = tNodes[segIdx];
                    double t1 = tNodes[j + 1];

                    // Safely extract the continuous sub-curve
                    Curve concatSeg = (segIdx == 0 && j == atomicSegs.Length - 1) 
                        ? ncIn 
                        : ncIn.Trim(new Interval(t0, t1));

                    if (concatSeg == null) 
                    {
                        j--; 
                        break; 
                    }

                    var eval = evaluator(concatSeg);
                    if (eval.Converted != null && IsWithinDeviationTolerance(concatSeg, eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                    {
                        lastValidSeg = eval.Converted;
                        continue; 
                    }
                    else
                    {
                        // Failed to expand further, revert to the last successful segment
                        j--; 
                        break;
                    }
                }

                if (lastValidSeg != null)
                {
                    double linearDist = lastValidSeg.PointAtStart.DistanceTo(lastValidSeg.PointAtEnd);
                    if (linearDist >= minNewCrvLen)
                    {
                        if (!validNodeIndices.Contains(segIdx)) validNodeIndices.Add(segIdx);
                        if (!validNodeIndices.Contains(j + 1)) validNodeIndices.Add(j + 1);
                    }
                }

                segIdx = j + 1; // Move past the processed block
            }

            if (validNodeIndices.Count == 0) return null;

            validNodeIndices.Sort();

            // 6. Final Split based exclusively on interior valid boundaries
            List<double> finalInteriorSplits = new List<double>();
            foreach (int idx in validNodeIndices)
            {
                // Prevent passing Domain Min/Max into Rhino's Split algorithm
                if (idx > 0 && idx < tNodes.Length - 1)
                {
                    finalInteriorSplits.Add(tNodes[idx]);
                }
            }

            Curve[] finalSplitSegs;
            if (finalInteriorSplits.Count == 0)
            {
                finalSplitSegs = new Curve[] { ncIn };
            }
            else
            {
                finalSplitSegs = ncIn.Split(finalInteriorSplits);
            }

            if (finalSplitSegs == null || finalSplitSegs.Length == 0) return null;

            // 7. Evaluate and build the final segments array
            List<Curve> finalSegments = new List<Curve>();
            List<double> tolsUsed = new List<double>();

            foreach (var seg in finalSplitSegs)
            {
                var eval = evaluator(seg);
                if (eval.Converted != null && IsWithinDeviationTolerance(seg, eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                {
                    if (eval.Converted.GetLength() >= minNewCrvLen)
                    {
                        finalSegments.Add(eval.Converted);
                        tolsUsed.Add(eval.Deviation);
                        continue;
                    }
                }
                
                // Add unconverted segment as fallback
                finalSegments.Add(seg);
            }

            double maxDev = tolsUsed.Count > 0 ? tolsUsed.Max() : 0.0;
            return (finalSegments.ToArray(), maxDev);
        }

        private static bool IsWithinDeviationTolerance(Curve original, Curve converted, double dev, bool tolByRatio, double tolRatio, double devTol)
        {
            if (tolByRatio)
            {
                double crvLength = converted.GetLength();
                double ratio = dev > 0.0 ? crvLength / dev : double.MaxValue;
                return ratio >= tolRatio;
            }
            else
            {
                return dev <= devTol;
            }
        }
    }
}