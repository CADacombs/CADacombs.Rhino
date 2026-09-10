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
            bool doSpansToLines, 
            bool doSpansToArcs, 
            bool doAdjustG1,
            bool doSpansToBeziers,     
            List<int> targetDegrees,   
            bool doSplitAllKnots,
            bool doSplitFullyMultiple,
            bool doPolylineOutput)
        {
            if (inputCurve == null) return (null, 0, 0, 0.0);

            int originalSegs = (inputCurve is PolyCurve pcOriginal) ? pcOriginal.SegmentCount : 1;
            Curve currentCurve = inputCurve.DuplicateCurve();
            double globalMaxDev = 0.0;

            // --- Tolerance Distribution Logic ---
            double g1DistTol = distTol;
            double g1AngleTol = angleTol;
            double bezierDistTol = distTol;

            if (doAdjustG1 && doSpansToBeziers)
            {
                g1DistTol = distTol / 2.0;
                g1AngleTol = angleTol / 2.0;
                bezierDistTol = distTol / 2.0;
            }
            // ------------------------------------

            (Curve Curve, double Dev) ProcessSpans(Curve curve, SpanConversionUtils.CurveEvaluator evaluator, bool tolByRatio, double tolRatio, double devTol, double minLen)
            {
                var nc = curve.ToNurbsCurve();
                if (nc == null) return (curve, 0.0);

                var result = SpanConversionUtils.ConvertBetweenKnots(nc, evaluator, tolByRatio, tolRatio, devTol, minLen);
                
                if (result.HasValue)
                {
                    double localDev = result.Value.MaxDev;
                    if (result.Value.Segments.Length == 1) return (result.Value.Segments[0], localDev);
                    
                    PolyCurve newPc = new PolyCurve();
                    foreach (var resSeg in result.Value.Segments) newPc.Append(resSeg);
                    return (newPc, localDev);
                }
                return (curve, 0.0);
            }

            // 1. CONVERT SPANS TO LINES
            if (doSpansToLines)
            {
                SpanConversionUtils.CurveEvaluator lineEval = c => {
                    var res = ConvertToLineLogic.GetLineCurve(c, distTol);
                    return (res.lineCurve, res.deviation, res.log); 
                };
                var res = ProcessSpans(currentCurve, lineEval, ConvertToLineOptions.TolByRatio, ConvertToLineOptions.TolRatio, distTol, ConvertToLineOptions.MinNewCrvLen);
                currentCurve = res.Curve;
                globalMaxDev = Math.Max(globalMaxDev, res.Dev);
            }

            // 2. CONVERT SPANS TO ARCS
            if (doSpansToArcs)
            {
                SpanConversionUtils.CurveEvaluator arcEval = c => {
                    var res = ConvertToArcLogic.GetArcCurve(c, distTol); 
                    return (res.arcCurve, res.deviation, res.log); 
                };
                var res = ProcessSpans(currentCurve, arcEval, ConvertToArcOptions.TolByRatio, ConvertToArcOptions.TolRatio, distTol, ConvertToArcOptions.MinNewCrvLen); 
                currentCurve = res.Curve;
                globalMaxDev = Math.Max(globalMaxDev, res.Dev);
            }

            // 3. ADJUST G1 (Evaluates raw NURBS spans before Bezier conversion)
            if (doAdjustG1)
            {
                currentCurve = ApplyAdjustG1(currentCurve, g1DistTol, g1AngleTol);
            }

            // 4. CONVERT SPANS TO BEZIERS
            if (doSpansToBeziers && targetDegrees != null && targetDegrees.Count > 0)
            {
                SpanConversionUtils.CurveEvaluator bezierEval = c => {
                    var res = ConvertToBezierLogic.TryConvert(c, targetDegrees, true, bezierDistTol, true, false, false, false, false, false);
                    return (res.Bezier, res.Deviation, res.Log);
                };
                var res = ProcessSpans(currentCurve, bezierEval, false, 0.0, bezierDistTol, 0.001); 
                currentCurve = res.Curve;
                globalMaxDev = Math.Max(globalMaxDev, res.Dev);
            }

            // 5. SPLIT KNOTS 
            if (doSplitAllKnots || doSplitFullyMultiple)
            {
                currentCurve = SplitKnots(currentCurve, doSplitAllKnots, doSplitFullyMultiple);
            }

            // 6. POLYLINE OUTPUT
            if (doPolylineOutput)
            {
                currentCurve = PolylineOutputLogic.Execute(currentCurve, true); 
            }

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
                bool isLineOrArc = curve is LineCurve || curve is ArcCurve || curve.IsLinear(distTol) || curve.IsArc(distTol);
                bool isBezier = curve is NurbsCurve b && b.SpanCount == 1 && !b.IsRational;
                
                if (!isLineOrArc && !isBezier) return curve.Simplify(CurveSimplifyOptions.AdjustG1, distTol, angleTol) ?? curve;
                return curve;
            }
        }
    }
}