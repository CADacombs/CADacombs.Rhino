using System;
using System.Collections.Generic;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvLogic
    {
        public static (Curve ResultCurve, int OriginalSegments, int NewSegments) ExecutePipeline(
            Curve inputCurve, 
            double distTol, 
            double angleTol,
            bool doSpansToLines, 
            bool doSpansToArcs, 
            bool doPolylineOutput)
        {
            if (inputCurve == null) return (null, 0, 0);

            int originalSegs = (inputCurve is PolyCurve pcOriginal) ? pcOriginal.SegmentCount : 1;
            Curve currentCurve = inputCurve.DuplicateCurve();

            // Helper function to process spans across an entire curve or PolyCurve
            Curve ProcessSpans(Curve curve, SpanConversionUtils.CurveEvaluator evaluator, bool tolByRatio, double tolRatio, double devTol, double minLen)
            {
                if (curve is PolyCurve pc)
                {
                    PolyCurve newPc = new PolyCurve();
                    bool modified = false;
                    
                    for (int i = 0; i < pc.SegmentCount; i++)
                    {
                        Curve seg = pc.SegmentCurve(i);
                        var nc = seg.ToNurbsCurve();
                        var result = SpanConversionUtils.ConvertBetweenKnots(nc, evaluator, tolByRatio, tolRatio, devTol, minLen);
                        
                        if (result.HasValue)
                        {
                            modified = true;
                            foreach (var resSeg in result.Value.Segments) newPc.Append(resSeg);
                        }
                        else
                        {
                            newPc.Append(seg);
                        }
                    }
                    return modified ? newPc : curve;
                }
                else
                {
                    var nc = curve.ToNurbsCurve();
                    var result = SpanConversionUtils.ConvertBetweenKnots(nc, evaluator, tolByRatio, tolRatio, devTol, minLen);
                    
                    if (result.HasValue)
                    {
                        if (result.Value.Segments.Length == 1) return result.Value.Segments[0];
                        PolyCurve newPc = new PolyCurve();
                        foreach (var resSeg in result.Value.Segments) newPc.Append(resSeg);
                        return newPc;
                    }
                    return curve;
                }
            }

            // 1. Convert Spans to Lines
            if (doSpansToLines)
            {
                SpanConversionUtils.CurveEvaluator lineEval = c => {
                    var res = ConvertToLineLogic.GetLineCurve(c, distTol); //
                    return (res.lineCurve, res.deviation, res.log); //
                };
                currentCurve = ProcessSpans(currentCurve, lineEval, ConvertToLineOptions.TolByRatio, ConvertToLineOptions.TolRatio, distTol, ConvertToLineOptions.MinNewCrvLen); //[cite: 21]
            }

            // 2. Convert Spans to Arcs
            if (doSpansToArcs)
            {
                SpanConversionUtils.CurveEvaluator arcEval = c => {
                    var res = ConvertToArcLogic.GetArcCurve(c, distTol); //[cite: 15]
                    return (res.arcCurve, res.deviation, res.log); //[cite: 18]
                };
                currentCurve = ProcessSpans(currentCurve, arcEval, ConvertToArcOptions.TolByRatio, ConvertToArcOptions.TolRatio, distTol, ConvertToArcOptions.MinNewCrvLen); //[cite: 20]
            }

            // 3. Polyline Output (Merge contiguous lines)
            if (doPolylineOutput)
            {
                currentCurve = PolylineOutputLogic.Execute(currentCurve); //[cite: 10]
            }

            int newSegs = (currentCurve is PolyCurve pcNew) ? pcNew.SegmentCount : 1;
            if (currentCurve is PolylineCurve) newSegs = 1; // A single polyline acts as 1 segment object

            return (currentCurve, originalSegs, newSegs);
        }
    }
}