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
            bool doPolylineOutput,
            bool doSplitAllKnots,
            bool doSplitFullyMultiple,
            bool doAdjustG1)
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

            // 1. CONVERT SPANS TO LINES
            if (doSpansToLines)
            {
                SpanConversionUtils.CurveEvaluator lineEval = c => {
                    var res = ConvertToLineLogic.GetLineCurve(c, distTol);
                    return (res.lineCurve, res.deviation, res.log); 
                };
                currentCurve = ProcessSpans(currentCurve, lineEval, ConvertToLineOptions.TolByRatio, ConvertToLineOptions.TolRatio, distTol, ConvertToLineOptions.MinNewCrvLen);
            }

            // 2. CONVERT SPANS TO ARCS
            if (doSpansToArcs)
            {
                SpanConversionUtils.CurveEvaluator arcEval = c => {
                    var res = ConvertToArcLogic.GetArcCurve(c, distTol); 
                    return (res.arcCurve, res.deviation, res.log); 
                };
                currentCurve = ProcessSpans(currentCurve, arcEval, ConvertToArcOptions.TolByRatio, ConvertToArcOptions.TolRatio, distTol, ConvertToArcOptions.MinNewCrvLen); 
            }

            // 3. SPLIT KNOTS (Happens AFTER arcs are safely extracted so we don't break them)
            if (doSplitAllKnots || doSplitFullyMultiple)
            {
                currentCurve = SplitKnots(currentCurve, doSplitAllKnots, doSplitFullyMultiple);
            }

            // 4. ADJUST G1 (Safe: ignores Lines and Arcs)
            if (doAdjustG1)
            {
                currentCurve = ApplyAdjustG1(currentCurve, distTol, angleTol);
            }

            // 5. POLYLINE OUTPUT
            if (doPolylineOutput)
            {
                // Passing 'true' assumes you updated PolylineOutputLogic to accept the strict parameter
                currentCurve = PolylineOutputLogic.Execute(currentCurve, true); 
            }

            int newSegs = (currentCurve is PolyCurve pcNew) ? pcNew.SegmentCount : 1;
            if (currentCurve is PolylineCurve) newSegs = 1; 

            return (currentCurve, originalSegs, newSegs);
        }

        // --- Helper Methods ---

        private static Curve SplitKnots(Curve curve, bool all, bool fullyMultiple)
        {
            // Protect already-simple curves from being converted to NURBS and split
            if (curve is LineCurve || curve is ArcCurve || curve is PolylineCurve) return curve;
            
            // Process segments recursively if it's already a PolyCurve
            if (curve is PolyCurve pc)
            {
                PolyCurve newPc = new PolyCurve();
                bool modified = false;
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    Curve seg = pc.SegmentCurve(i);
                    Curve splitSeg = SplitKnots(seg, all, fullyMultiple);
                    if (splitSeg != seg) modified = true;
                    
                    if (splitSeg is PolyCurve subPc)
                    {
                        for (int j = 0; j < subPc.SegmentCount; j++) newPc.Append(subPc.SegmentCurve(j));
                    }
                    else newPc.Append(splitSeg);
                }
                return modified ? newPc : curve;
            }
            
            var nc = curve.ToNurbsCurve();
            if (nc == null) return curve;

            // HashSet prevents duplicate parameters from crashing the Split method
            HashSet<double> splitParams = new HashSet<double>();
            
            for (int i = 1; i < nc.Knots.Count - 1; i++)
            {
                int mult = nc.Knots.KnotMultiplicity(i);
                if (all && mult > 1) 
                {
                    splitParams.Add(nc.Knots[i]);
                }
                else if (fullyMultiple && mult >= nc.Degree) 
                {
                    splitParams.Add(nc.Knots[i]);
                }
            }

            if (splitParams.Count == 0) return curve;

            var segments = curve.Split(splitParams);
            if (segments == null || segments.Length <= 1) return curve;

            PolyCurve resultPc = new PolyCurve();
            foreach (var seg in segments) resultPc.Append(seg);
            return resultPc;
        }

        private static Curve ApplyAdjustG1(Curve curve, double distTol, double angleTol)
        {
            if (curve is PolyCurve pc)
            {
                PolyCurve newPc = new PolyCurve();
                bool modified = false;
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    Curve seg = pc.SegmentCurve(i);
                    bool isLineOrArc = seg is LineCurve || seg is ArcCurve || seg.IsLinear(distTol) || seg.IsArc(distTol);
                    
                    if (!isLineOrArc)
                    {
                        // Use native Rhino simplify as a placeholder for AdjustG1
                        var simplified = seg.Simplify(CurveSimplifyOptions.AdjustG1, distTol, angleTol);
                        if (simplified != null)
                        {
                            newPc.Append(simplified);
                            modified = true;
                            continue;
                        }
                    }
                    newPc.Append(seg);
                }
                return modified ? newPc : curve;
            }
            else
            {
                bool isLineOrArc = curve is LineCurve || curve is ArcCurve || curve.IsLinear(distTol) || curve.IsArc(distTol);
                if (!isLineOrArc)
                {
                    return curve.Simplify(CurveSimplifyOptions.AdjustG1, distTol, angleTol) ?? curve;
                }
                return curve;
            }
        }
    }
}