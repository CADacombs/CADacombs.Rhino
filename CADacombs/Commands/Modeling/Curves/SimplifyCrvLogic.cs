using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class SimplifyCrvLogic
    {
        public static (Curve ResultCurve, string Report) ExecutePipeline(
            Curve input, 
            double devTol, 
            double minLen, 
            bool doNative,
            bool doLines, 
            bool doArcs)
        {
            if (input == null) return (null, "No input.");

            Curve wipCurve = input.DuplicateCurve();
            string report = "Pipeline Started:\n";

            // 1. Native Simplify (Baseline)
            if (doNative)
            {
                Curve nativeSimp = wipCurve.Simplify(
                    CurveSimplifyOptions.Merge | CurveSimplifyOptions.SplitAtFullyMultipleKnots, 
                    devTol, 
                    RhinoDoc.ActiveDoc.ModelAngleToleranceRadians);

                if (nativeSimp != null)
                {
                    wipCurve = nativeSimp;
                    report += $"- Native simplify applied.\n";
                }
            }

            // 2. Custom Convert Spans to Lines
            if (doLines)
            {
                var ncWip = wipCurve.ToNurbsCurve();
                var lineResult = SpanConversionUtils.ConvertBetweenKnots(
                    ncWip,
                    seg => ConvertToLineLogic.GetLineCurve(seg, devTol),
                    ConvertToLineOptions.TolByRatio,
                    ConvertToLineOptions.TolRatio,
                    devTol,
                    minLen);

                if (lineResult != null)
                {
                    wipCurve = JoinSegments(lineResult.Value.Segments, wipCurve);
                    report += $"- Line spans converted (Max Dev: {lineResult.Value.MaxDev:E3}).\n";
                }
            }

            // 3. Custom Convert Spans to Arcs
            if (doArcs)
            {
                var ncWip = wipCurve.ToNurbsCurve();
                var arcResult = SpanConversionUtils.ConvertBetweenKnots(
                    ncWip,
                    seg => ConvertToArcLogic.GetArcCurve(seg, devTol),
                    ConvertToArcOptions.TolByRatio,
                    ConvertToArcOptions.TolRatio,
                    devTol,
                    minLen);

                if (arcResult != null)
                {
                    wipCurve = JoinSegments(arcResult.Value.Segments, wipCurve);
                    report += $"- Arc spans converted (Max Dev: {arcResult.Value.MaxDev:E3}).\n";
                }
            }

            int segCount = (wipCurve is PolyCurve pc) ? pc.SegmentCount : 1;
            report += $"\nFinal Result: {wipCurve.GetType().Name} ({segCount} segment(s)).";

            return (wipCurve, report);
        }

        /// <summary>
        /// Helper to join the returned segments back into a single PolyCurve/PolylineCurve.
        /// </summary>
        private static Curve JoinSegments(Curve[] segments, Curve fallback)
        {
            if (segments == null || segments.Length == 0) return fallback;
            if (segments.Length == 1) return segments[0];

            PolyCurve pcOut = new PolyCurve();
            foreach (var seg in segments)
            {
                pcOut.Append(seg);
            }

            // Clean up to PolylineCurve if possible
            if (pcOut.TryGetPolyline(out Polyline pl))
                return new PolylineCurve(pl);

            return pcOut;
        }
    }
}