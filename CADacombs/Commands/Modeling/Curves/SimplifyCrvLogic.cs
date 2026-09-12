using System;
using System.Collections.Generic;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvLogic
    {
        public static (Curve ResultCurve, int OriginalSegments, int NewSegments, double MaxDev) ExecutePipeline(
            Curve inputCurve, 
            double distTol, 
            double angleTol,
            double lineTol,
            double arcBulgeTol,
            double minSegLen,
            bool doSpansToLines, 
            bool doSpansToArcs, 
            bool doAdjustG1,
            bool doSpansToBeziers,     
            List<int> targetDegrees,   
            bool doMakeUniform,
            bool doSplitAllKnots,
            bool doSplitFullyMultiple,
            bool doPolylineOutput)
        {
            if (inputCurve == null) return (null, 0, 0, 0.0);

            int originalSegs = (inputCurve is PolyCurve pcOriginal) ? pcOriginal.SegmentCount : 1;
            Curve currentCurve = inputCurve.DuplicateCurve();
            double globalMaxDev = 0.0;

            double g1DistTol = distTol;
            double g1AngleTol = angleTol;
            double bezierDistTol = distTol;
            double uniformDistTol = distTol;

            if (doAdjustG1 && (doSpansToBeziers || doMakeUniform))
            {
                g1DistTol = distTol / 2.0;
                g1AngleTol = angleTol / 2.0;
                bezierDistTol = distTol / 2.0;
                uniformDistTol = distTol / 2.0;
            }

            (Curve Curve, double Dev) ProcessSpans(Curve curve, SpanConversionUtils.CurveEvaluator evaluator, Func<Curve, bool> skipCheck, bool tolByRatio, double tolRatio, double devTol, double minLen, double bulgeTol, bool onlySplitAtFullyMultipleKnots)
            {
                double localDev = 0.0;
                if (curve is PolyCurve pc)
                {
                    PolyCurve newPc = new PolyCurve();
                    bool modified = false;
                    List<Curve> batch = new List<Curve>();
                    
                    void FlushBatch()
                    {
                        if (batch.Count == 0) return;
                        
                        Curve crvToEval = batch[0];
                        if (batch.Count > 1)
                        {
                            PolyCurve tempPc = new PolyCurve();
                            foreach (var c in batch) tempPc.Append(c);
                            crvToEval = (Curve)tempPc.ToNurbsCurve() ?? tempPc; 
                        }
                        
                        var nc = crvToEval.ToNurbsCurve();
                        if (nc != null)
                        {
                            var result = SpanConversionUtils.ConvertBetweenKnots(nc, evaluator, tolByRatio, tolRatio, devTol, minLen, bulgeTol, onlySplitAtFullyMultipleKnots);
                            if (result.HasValue)
                            {
                                modified = true;
                                localDev = Math.Max(localDev, result.Value.MaxDev);
                                foreach (var resSeg in result.Value.Segments) newPc.Append(resSeg);
                                batch.Clear();
                                return;
                            }
                        }
                        
                        foreach (var c in batch) newPc.Append(c);
                        batch.Clear();
                    }

                    for (int i = 0; i < pc.SegmentCount; i++)
                    {
                        Curve seg = pc.SegmentCurve(i);
                        if (skipCheck(seg))
                        {
                            FlushBatch();
                            newPc.Append(seg); 
                        }
                        else
                        {
                            batch.Add(seg);
                        }
                    }
                    FlushBatch();
                    
                    if (modified && newPc.SegmentCount == 1) return (newPc.SegmentCurve(0), localDev);
                    return (modified ? newPc : curve, localDev);
                }
                else
                {
                    if (skipCheck(curve)) return (curve, 0.0);
                    
                    var nc = curve.ToNurbsCurve();
                    if (nc == null) return (curve, 0.0);
                    
                    var result = SpanConversionUtils.ConvertBetweenKnots(nc, evaluator, tolByRatio, tolRatio, devTol, minLen, bulgeTol, onlySplitAtFullyMultipleKnots);
                    if (result.HasValue)
                    {
                        localDev = result.Value.MaxDev;
                        if (result.Value.Segments.Length == 1) return (result.Value.Segments[0], localDev);
                        
                        PolyCurve newPc = new PolyCurve();
                        foreach (var resSeg in result.Value.Segments) newPc.Append(resSeg);
                        return (newPc, localDev);
                    }
                    return (curve, 0.0);
                }
            }

            // 1. CONVERT SPANS TO LINES
            if (doSpansToLines)
            {
                SpanConversionUtils.CurveEvaluator lineEval = c => {
                    var evalRes = ConvertToLineLogic.GetLineCurve(c, lineTol, lineTol, 0.0); 
                    return (evalRes.lineCurve, evalRes.deviation, evalRes.log); 
                };
                var spanRes = ProcessSpans(currentCurve, lineEval, c => c is LineCurve || c is PolylineCurve, ConvertToLineOptions.TolByRatio, ConvertToLineOptions.TolRatio, lineTol, minSegLen, 0.0, false);
                currentCurve = spanRes.Curve;
                globalMaxDev = Math.Max(globalMaxDev, spanRes.Dev);
            }

            // 2. CONVERT SPANS TO ARCS
            if (doSpansToArcs)
            {
                SpanConversionUtils.CurveEvaluator arcEval = c => {
                    var evalRes = ConvertToArcLogic.GetArcCurve(c, distTol, distTol, 0.0, true); 
                    return (evalRes.arcCurve, evalRes.deviation, evalRes.log); 
                };
                var spanRes = ProcessSpans(currentCurve, arcEval, c => c is LineCurve || c is PolylineCurve || c is ArcCurve, ConvertToArcOptions.TolByRatio, ConvertToArcOptions.TolRatio, distTol, minSegLen, arcBulgeTol, false); 
                currentCurve = spanRes.Curve;
                globalMaxDev = Math.Max(globalMaxDev, spanRes.Dev);
            }

            // 3. ADJUST G1
            if (doAdjustG1) currentCurve = ApplyAdjustG1(currentCurve, g1DistTol, g1AngleTol);

            // 4. CONVERT SECTIONS TO BEZIERS
            if (doSpansToBeziers && targetDegrees != null && targetDegrees.Count > 0)
            {
                SpanConversionUtils.CurveEvaluator bezierEval = c => {
                    var evalRes = ConvertToBezierLogic.TryConvert(c, targetDegrees, true, bezierDistTol, true, false, false, false, false, false);
                    return (evalRes.Bezier, evalRes.Deviation, evalRes.Log);
                };
                var spanRes = ProcessSpans(currentCurve, bezierEval, c => c is LineCurve || c is PolylineCurve || c is ArcCurve, false, 0.0, bezierDistTol, minSegLen, 0.0, true); 
                currentCurve = spanRes.Curve;
                globalMaxDev = Math.Max(globalMaxDev, spanRes.Dev);
            }

            // 5. MAKE UNIFORM
            if (doMakeUniform)
            {
                SpanConversionUtils.CurveEvaluator uniformEval = c => {
                    var evalRes = MakeUniformLogic.TryMakeUniform(c, true, uniformDistTol, true, false);
                    return (evalRes.UniformCurve, evalRes.Deviation, evalRes.Log);
                };
                var spanRes = ProcessSpans(currentCurve, uniformEval, c => c is LineCurve || c is PolylineCurve || c is ArcCurve || (c is NurbsCurve n && n.SpanCount == 1), false, 0.0, uniformDistTol, minSegLen, 0.0, true); 
                currentCurve = spanRes.Curve;
                globalMaxDev = Math.Max(globalMaxDev, spanRes.Dev);
            }

            // 6. SPLIT KNOTS 
            if (doSplitAllKnots || doSplitFullyMultiple) currentCurve = SplitKnots(currentCurve, doSplitAllKnots, doSplitFullyMultiple);

            // 7. POLYLINE OUTPUT
            if (doPolylineOutput) currentCurve = PolylineOutputLogic.Execute(currentCurve, true); 

            int newSegs = (currentCurve is PolyCurve pcNew) ? pcNew.SegmentCount : 1;
            if (currentCurve is PolylineCurve) newSegs = 1; 

            return (currentCurve, originalSegs, newSegs, globalMaxDev);
        }

        private static Curve SplitKnots(Curve curve, bool all, bool fullyMultiple)
        {
            if (curve is LineCurve || curve is ArcCurve || curve is PolylineCurve) return curve;
            if (curve is NurbsCurve bnc && bnc.SpanCount == 1 && !bnc.IsRational) return curve;

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

            HashSet<double> splitParams = new HashSet<double>();
            for (int i = 1; i < nc.Knots.Count - 1; i++)
            {
                int mult = nc.Knots.KnotMultiplicity(i);
                if (all && mult > 1) splitParams.Add(nc.Knots[i]);
                else if (fullyMultiple && mult >= nc.Degree) splitParams.Add(nc.Knots[i]);
            }

            if (splitParams.Count == 0) return curve;

            List<double> tNodes = new List<double> { nc.Domain.Min };
            var sortedSplits = new List<double>(splitParams);
            sortedSplits.Sort();
            tNodes.AddRange(sortedSplits);
            tNodes.Add(nc.Domain.Max);

            PolyCurve resultPc = new PolyCurve();
            for (int i = 0; i < tNodes.Count - 1; i++)
            {
                Curve seg = nc.Trim(new Interval(tNodes[i], tNodes[i + 1]));
                if (seg != null) resultPc.Append(seg);
            }
            
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
                    bool isLineOrArc = seg is LineCurve || seg is PolylineCurve || seg is ArcCurve;
                    bool isBezier = seg is NurbsCurve b && b.SpanCount == 1 && !b.IsRational;
                    
                    if (!isLineOrArc && !isBezier)
                    {
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
                bool isLineOrArc = curve is LineCurve || curve is PolylineCurve || curve is ArcCurve;
                bool isBezier = curve is NurbsCurve b && b.SpanCount == 1 && !b.IsRational;
                
                if (!isLineOrArc && !isBezier) return curve.Simplify(CurveSimplifyOptions.AdjustG1, distTol, angleTol) ?? curve;
                return curve;
            }
        }
    }
}