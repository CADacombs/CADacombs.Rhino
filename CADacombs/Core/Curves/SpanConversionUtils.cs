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

            // If the curve is already a single span, it doesn't need between-knot expansion.
            if (ncIn.Points.Count == ncIn.Degree + 1)
                return null; //[cite: 16]

            // 1. Get unique interior knots
            List<double> tsKnots = new List<double>();
            int startIdx = ncIn.Degree - 1; //[cite: 16]
            int endIdx = ncIn.Knots.Count - ncIn.Degree + 1; //[cite: 16]

            for (int k = startIdx; k < endIdx; k++)
            {
                double t = ncIn.Knots[k];
                if (!tsKnots.Contains(t))
                    tsKnots.Add(t); //[cite: 16]
            }

            if (tsKnots.Count < 2) return null;

            // 2. Split curve into atomic segments by unique knots
            Curve[] atomicSegs = ncIn.Split(tsKnots); //[cite: 16]
            if (atomicSegs == null || atomicSegs.Length == 0)
                return null;

            // 3. Test each atomic segment to identify viable starting points
            bool[] isSegWithinTol = new bool[atomicSegs.Length];
            for (int i = 0; i < atomicSegs.Length; i++)
            {
                var eval = evaluator(atomicSegs[i]); //[cite: 16]
                if (eval.Converted != null && IsWithinDeviationTolerance(atomicSegs[i], eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                {
                    isSegWithinTol[i] = true; //[cite: 16]
                }
                else
                {
                    isSegWithinTol[i] = false; //[cite: 16]
                }
            }

            if (!isSegWithinTol.Contains(true))
                return null; //[cite: 16]

            // 4. Greedy Expansion Loop
            List<int> validKnotIndices = new List<int>();
            int segIdx = 0;

            while (segIdx < atomicSegs.Length) //[cite: 16]
            {
                RhinoApp.Wait(); // Keeps Rhino responsive during tight loops

                if (!isSegWithinTol[segIdx])
                {
                    segIdx++; //[cite: 16]
                    continue;
                }

                int j = segIdx;
                Curve lastValidSeg = atomicSegs[segIdx];

                for (j = segIdx + 1; j < isSegWithinTol.Length; j++) //[cite: 16]
                {
                    if (!isSegWithinTol[j])
                    {
                        j--; //[cite: 16]
                        break;
                    }

                    // Try concatenating from segIdx to j
                    double t0 = tsKnots[segIdx];
                    double t1 = tsKnots[j + 1];
                    
                    // Use Trim to safely extract the continuous sub-curve
                    Curve concatSeg = ncIn.Trim(new Interval(t0, t1));
                    if (concatSeg == null) 
                    {
                        j--; 
                        break; 
                    }

                    var eval = evaluator(concatSeg);
                    if (eval.Converted != null && IsWithinDeviationTolerance(concatSeg, eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                    {
                        lastValidSeg = eval.Converted; // Concatentation of segments deviates within tolerance[cite: 16]
                        continue; 
                    }
                    else
                    {
                        j--; // Revert to last valid j and break out of j loop[cite: 16]
                        break;
                    }
                }

                // Check minimum length for the expanded group based on its Start/End distance
                double linearDist = lastValidSeg.PointAtStart.DistanceTo(lastValidSeg.PointAtEnd); //[cite: 16]
                if (linearDist >= minNewCrvLen) //[cite: 16]
                {
                    if (!validKnotIndices.Contains(segIdx)) validKnotIndices.Add(segIdx); //[cite: 16]
                    if (!validKnotIndices.Contains(j + 1)) validKnotIndices.Add(j + 1); //[cite: 16]
                }

                segIdx = j + 1; // Move past the processed block[cite: 16]
            }

            if (validKnotIndices.Count == 0)
                return null;

            validKnotIndices.Sort();

            // 5. Final Split based exclusively on the found valid boundaries
            List<double> finalSplitParams = new List<double>();
            foreach (int idx in validKnotIndices)
            {
                if (idx >= 0 && idx < tsKnots.Count)
                    finalSplitParams.Add(tsKnots[idx]);
            }

            Curve[] finalSplitSegs = ncIn.Split(finalSplitParams); //[cite: 16]
            if (finalSplitSegs == null || finalSplitSegs.Length == 0)
                return null;

            // 6. Evaluate and build the final segments array
            List<Curve> finalSegments = new List<Curve>();
            List<double> tolsUsed = new List<double>();

            foreach (var seg in finalSplitSegs) //[cite: 16]
            {
                var eval = evaluator(seg);
                if (eval.Converted != null)
                {
                    if (IsWithinDeviationTolerance(seg, eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol) && 
                        eval.Converted.GetLength() >= minNewCrvLen) //[cite: 16]
                    {
                        finalSegments.Add(eval.Converted); //[cite: 16]
                        tolsUsed.Add(eval.Deviation); //[cite: 16]
                        continue;
                    }
                }
                
                // Add unconverted segment as fallback[cite: 16]
                finalSegments.Add(seg); //[cite: 16]
            }

            double maxDev = tolsUsed.Count > 0 ? tolsUsed.Max() : 0.0;
            return (finalSegments.ToArray(), maxDev); //[cite: 16]
        }

        private static bool IsWithinDeviationTolerance(Curve original, Curve converted, double dev, bool tolByRatio, double tolRatio, double devTol)
        {
            if (tolByRatio) //[cite: 16]
            {
                double crvLength = converted.GetLength(); //[cite: 16]
                double ratio = dev > 0.0 ? crvLength / dev : double.MaxValue; //[cite: 16]
                return ratio >= tolRatio; //[cite: 16]
            }
            else
            {
                return dev <= devTol; //[cite: 16]
            }
        }
    }
}