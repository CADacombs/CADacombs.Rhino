using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class SpanConversionUtils
    {
        public delegate (Curve Converted, double Deviation, string Log) CurveEvaluator(Curve input);

        private class Block
        {
            public int StartIdx { get; set; }
            public int EndIdx { get; set; }
            public bool IsValid { get; set; }
            public Curve ConvertedCurve { get; set; }
            public double Dev { get; set; }
        }

        public static (Curve[] Segments, double MaxDev)? ConvertBetweenKnots(
            NurbsCurve ncIn,
            CurveEvaluator evaluator,
            bool tolByRatio,
            double tolRatio,
            double devTol,
            double minNewCrvLen,
            double arcBulgeTol,
            bool onlySplitAtFullyMultipleKnots)
        {
            if (ncIn == null) return null;

            List<double> tNodesList = new List<double> { ncIn.Domain.Min };

            for (int k = ncIn.Degree; k < ncIn.Knots.Count - ncIn.Degree; k++)
            {
                double t = ncIn.Knots[k];
                if (onlySplitAtFullyMultipleKnots)
                {
                    if (ncIn.Knots.KnotMultiplicity(k) < ncIn.Degree) continue; 
                }
                if (!tNodesList.Contains(t)) tNodesList.Add(t);
            }

            if (!tNodesList.Contains(ncIn.Domain.Max)) tNodesList.Add(ncIn.Domain.Max);
            double[] tNodes = tNodesList.ToArray();

            Curve[] atomicSegs = new Curve[tNodes.Length - 1];
            for (int i = 0; i < atomicSegs.Length; i++)
            {
                atomicSegs[i] = ncIn.Trim(new Interval(tNodes[i], tNodes[i + 1]));
            }

            bool[] isSegWithinTol = new bool[atomicSegs.Length];
            for (int i = 0; i < atomicSegs.Length; i++)
            {
                if (atomicSegs[i] == null) continue;
                var eval = evaluator(atomicSegs[i]);
                isSegWithinTol[i] = (eval.Converted != null && IsWithinDeviationTolerance(atomicSegs[i], eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol));
            }

            if (!isSegWithinTol.Contains(true)) return null; 

            List<Block> blocks = new List<Block>();
            int segIdx = 0;

            while (segIdx < atomicSegs.Length)
            {
                RhinoApp.Wait(); 

                if (!isSegWithinTol[segIdx] || atomicSegs[segIdx] == null)
                {
                    blocks.Add(new Block { StartIdx = segIdx, EndIdx = segIdx + 1, IsValid = false, ConvertedCurve = atomicSegs[segIdx], Dev = 0.0 });
                    segIdx++;
                    continue;
                }

                int j;
                Curve lastValidSeg = null;
                double lastValidDev = 0.0;

                for (j = segIdx; j < atomicSegs.Length; j++)
                {
                    if (!isSegWithinTol[j] || atomicSegs[j] == null) break;

                    Curve concatSeg = ncIn.Trim(new Interval(tNodes[segIdx], tNodes[j + 1]));
                    if (concatSeg == null) break; 

                    var eval = evaluator(concatSeg);
                    if (eval.Converted != null && IsWithinDeviationTolerance(concatSeg, eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                    {
                        lastValidSeg = eval.Converted;
                        lastValidDev = eval.Deviation;
                    }
                    else break;
                }

                if (lastValidSeg != null)
                {
                    // Fixed Indexing: Maps strictly to j without overflowing
                    blocks.Add(new Block { StartIdx = segIdx, EndIdx = j, IsValid = true, ConvertedCurve = lastValidSeg, Dev = lastValidDev });
                    segIdx = j;
                }
                else
                {
                    blocks.Add(new Block { StartIdx = segIdx, EndIdx = segIdx + 1, IsValid = false, ConvertedCurve = atomicSegs[segIdx], Dev = 0.0 });
                    segIdx++;
                }
            }

            // Closed Curve Seam Healing
            if (ncIn.IsClosed && blocks.Count > 1 && blocks[0].IsValid && blocks[blocks.Count - 1].IsValid)
            {
                Curve startOriginal = ncIn.Trim(new Interval(tNodes[blocks[0].StartIdx], tNodes[blocks[0].EndIdx]));
                Curve endOriginal = ncIn.Trim(new Interval(tNodes[blocks[blocks.Count - 1].StartIdx], tNodes[blocks[blocks.Count - 1].EndIdx]));
                
                if (startOriginal != null && endOriginal != null)
                {
                    var joined = Curve.JoinCurves(new[] { endOriginal, startOriginal });
                    if (joined != null && joined.Length == 1)
                    {
                        var eval = evaluator(joined[0]);
                        if (eval.Converted != null && IsWithinDeviationTolerance(joined[0], eval.Converted, eval.Deviation, tolByRatio, tolRatio, devTol))
                        {
                            blocks[0].StartIdx = blocks[blocks.Count - 1].StartIdx;
                            blocks[0].ConvertedCurve = eval.Converted;
                            blocks[0].Dev = eval.Deviation;
                            blocks.RemoveAt(blocks.Count - 1);
                        }
                    }
                }
            }

            List<Curve> finalSegments = new List<Curve>();
            List<double> tolsUsed = new List<double>();

            foreach (var block in blocks)
            {
                // Final Threshold Checks applied ONLY to the fully assembled segments
                bool accept = block.IsValid && block.ConvertedCurve.GetLength() >= minNewCrvLen;

                if (accept && arcBulgeTol > 0.0 && block.ConvertedCurve is ArcCurve arc)
                {
                    double sagitta = arc.Radius * (1.0 - Math.Cos(arc.AngleRadians / 2.0));
                    if (sagitta < arcBulgeTol) accept = false;
                }

                if (accept)
                {
                    finalSegments.Add(block.ConvertedCurve);
                    tolsUsed.Add(block.Dev);
                }
                else
                {
                    Curve fallback;
                    if (block.StartIdx > block.EndIdx) // It wrapped across the seam
                    {
                        Curve p1 = ncIn.Trim(new Interval(tNodes[block.StartIdx], tNodes[tNodes.Length - 1]));
                        Curve p2 = ncIn.Trim(new Interval(tNodes[0], tNodes[block.EndIdx]));
                        fallback = Curve.JoinCurves(new[] { p1, p2 })?[0] ?? p1;
                    }
                    else
                    {
                        fallback = ncIn.Trim(new Interval(tNodes[block.StartIdx], tNodes[block.EndIdx]));
                    }
                    
                    if (fallback != null) finalSegments.Add(fallback);
                }
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