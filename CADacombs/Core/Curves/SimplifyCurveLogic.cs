using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Core.Curves
{
    public static class SimplifyCrvLogic
    {
        public static (Curve ResultCurve, int OriginalSegments, int NewSegments, double MaxDev) ExecutePipeline(
            Curve inputCurve, 
            double distTol, 
            double minKinkAngleDeg, 
            double lineTol,
            double arcBulgeTol,
            double minSegLen,
            bool doSpansToLines, 
            bool doSpansToArcs, 
            bool doSmoothJoints,
            int smoothJointsMode,
            bool doSpansToBeziers,     
            List<int> targetDegrees,   
            bool doMakeUniform,
            int knotSplitMode,
            bool doPolylineOutput)
        {
            if (inputCurve == null) return (null, 0, 0, 0.0);

            int originalSegs = (inputCurve is PolyCurve pcOriginal) ? pcOriginal.SegmentCount : 1;
            Curve currentCurve = inputCurve.DuplicateCurve();
            double globalMaxDev = 0.0;
            double docTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            void ApplyStepIfValid(Func<Curve, Curve> stepAction)
            {
                Curve proposedCurve = stepAction(currentCurve);
                if (proposedCurve == null || proposedCurve == currentCurve) return;

                if (Curve.GetDistancesBetweenCurves(inputCurve, proposedCurve, docTol, out double stepDev, out _, out _, out _, out _, out _))
                {
                    if (stepDev <= distTol)
                    {
                        currentCurve = proposedCurve;
                        globalMaxDev = Math.Max(globalMaxDev, stepDev);
                    }
                }
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
                ApplyStepIfValid(c => ProcessSpans(c, lineEval, cv => cv is LineCurve || cv is PolylineCurve, ConvertToLineOptions.TolByRatio, ConvertToLineOptions.TolRatio, lineTol, minSegLen, 0.0, false).Curve);
            }

            // 2. CONVERT SPANS TO ARCS
            if (doSpansToArcs)
            {
                SpanConversionUtils.CurveEvaluator arcEval = c => {
                    var evalRes = ConvertToArcLogic.GetArcCurve(c, distTol, distTol, 0.0, true); 
                    return (evalRes.arcCurve, evalRes.deviation, evalRes.log); 
                };
                ApplyStepIfValid(c => ProcessSpans(c, arcEval, cv => cv is LineCurve || cv is PolylineCurve || cv is ArcCurve, ConvertToArcOptions.TolByRatio, ConvertToArcOptions.TolRatio, distTol, minSegLen, arcBulgeTol, false).Curve);
            }

            // 3. CONVERT SECTIONS TO BEZIERS
            if (doSpansToBeziers && targetDegrees != null && targetDegrees.Count > 0)
            {
                SpanConversionUtils.CurveEvaluator bezierEval = c => {
                    var evalRes = ConvertToBezierLogic.TryConvert(c, targetDegrees, true, distTol, true, false, false, false, false, false);
                    return (evalRes.Bezier, evalRes.Deviation, evalRes.Log);
                };
                ApplyStepIfValid(c => ProcessSpans(c, bezierEval, cv => cv is LineCurve || cv is PolylineCurve || cv is ArcCurve, false, 0.0, distTol, minSegLen, 0.0, true).Curve);
            }

            // 4. MAKE UNIFORM
            if (doMakeUniform)
            {
                SpanConversionUtils.CurveEvaluator uniformEval = c => {
                    var evalRes = ConvertToUniformCurveLogic.TryMakeUniform(c, true, distTol, true, false);
                    return (evalRes.UniformCurve, evalRes.Deviation, evalRes.Log);
                };
                ApplyStepIfValid(c => ProcessSpans(c, uniformEval, cv => cv is LineCurve || cv is PolylineCurve || cv is ArcCurve || (cv is NurbsCurve n && n.SpanCount == 1), false, 0.0, distTol, minSegLen, 0.0, true).Curve);
            }
            
            // 5. SPLIT KNOTS 
            if (knotSplitMode > 0) 
            {
                currentCurve = SplitKnots(currentCurve, knotSplitMode, distTol, minKinkAngleDeg);
            }

            // 6. SMOOTH JOINTS
            if (doSmoothJoints) 
            {
                ApplyStepIfValid(c => ApplyAdjustContinuity(c, distTol, minKinkAngleDeg, smoothJointsMode));
            }

            // 7. POLYLINE OUTPUT
            if (doPolylineOutput) 
            {
                currentCurve = PolylineOutputLogic.Execute(currentCurve, true); 
            }

            int newSegs = (currentCurve is PolyCurve pcNew) ? pcNew.SegmentCount : 1;
            if (currentCurve is PolylineCurve) newSegs = 1; 

            if (Curve.GetDistancesBetweenCurves(inputCurve, currentCurve, docTol, out double finalDev, out _, out _, out _, out _, out _))
                globalMaxDev = finalDev;

            return (currentCurve, originalSegs, newSegs, globalMaxDev);
        }

        private static Curve SplitKnots(Curve curve, int splitMode, double distTol, double minKinkAngleDeg)
        {
            if (splitMode == 0) return curve;
            if (curve is LineCurve || curve is ArcCurve || curve is PolylineCurve) return curve;
            if (curve is NurbsCurve bnc && bnc.SpanCount == 1 && !bnc.IsRational) return curve;

            if (curve is PolyCurve pc)
            {
                PolyCurve newPc = new PolyCurve();
                bool modified = false;
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    Curve seg = pc.SegmentCurve(i);
                    Curve splitSeg = SplitKnots(seg, splitMode, distTol, minKinkAngleDeg);
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
            double[] spans = nc.SpanVector();
            
            if (spans != null && spans.Length > 2)
            {
                double strictPosTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance; 
                double strictAngleTol = 1.2074182697257333e-06; 
                double strictCurvTol = 1e-8;

                for (int i = 1; i < spans.Length - 1; i++)
                {
                    double t = spans[i];
                    
                    bool isGInf = ContinuityUtils.IsGInfinity(nc, t, CurveEvaluationSide.Above, nc, t, CurveEvaluationSide.Below, minKinkAngleDeg);
                    if (isGInf) continue;

                    var vB = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Below);
                    var vA = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Above);
                    
                    int? gLevel = ContinuityUtils.GetContinuityLevel(vB, vA, strictPosTol, strictAngleTol, strictAngleTol, strictCurvTol);
                    int currentLevel = gLevel ?? 0; 

                    if (splitMode == 1 && currentLevel < 1) splitParams.Add(t);      
                    else if (splitMode == 2 && currentLevel < 2) splitParams.Add(t); 
                }
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

        private static Curve ApplyAdjustContinuity(Curve curve, double distTol, double minKinkAngleDeg, int mode)
        {
            if (curve is PolyCurve pc)
            {
                return PolyCurveSmoothingSolver.Solve(
                    pc, 
                    continuityMode: mode, 
                    minKinkAngle: minKinkAngleDeg, 
                    skipAngleTolDeg: 1.2074182697257333e-06, 
                    skipCurvTolPct: 1e-8, 
                    devTol: distTol, 
                    skipIfAlreadyAtContinuity: true, 
                    limitCrvDev: true, 
                    debug: false
                );
            }
            else
            {
                return curve;
            }
        }
    }
}