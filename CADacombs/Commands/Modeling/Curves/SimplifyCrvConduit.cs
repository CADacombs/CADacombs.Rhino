using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class SimplifyCrvConduit : DisplayConduit
    {
        public List<Curve> PreviewCurves { get; set; } = new List<Curve>();
        
        public bool ShowControlPolygon { get; set; } = true;
        public bool ShowContinuity { get; set; } = true;

        public bool HighlightLines { get; set; } = true;
        public bool HighlightArcs { get; set; } = true;

        // Default tolerances for the conduit preview
        private double _distTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        private double _g1AngleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private double _g2PlusAngleTolDeg = 2.0;
        private double _vectMagTolPct = 5.0;

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            base.CalculateBoundingBox(e);
            foreach (var crv in PreviewCurves)
            {
                if (crv != null) e.IncludeBoundingBox(crv.GetBoundingBox(false));
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            base.PostDrawObjects(e);

            List<(Point3d Pt, string Label, Color Color)> previewDots = new List<(Point3d, string, Color)>();

            foreach (var previewCurve in PreviewCurves)
            {
                if (previewCurve == null) continue;

                if (previewCurve is PolyCurve pc)
                {
                    for (int i = 0; i < pc.SegmentCount; i++)
                    {
                        DrawSingleSegment(e, pc.SegmentCurve(i));
                    }
                }
                else
                {
                    DrawSingleSegment(e, previewCurve);
                }

                if (ShowContinuity)
                {
                    EvaluateCurve(previewCurve, previewDots);
                }
            }

            // Draw all compiled continuity dots
            foreach (var dot in previewDots)
            {
                e.Display.DrawDot(dot.Pt, dot.Label, dot.Color, Color.Black);
            }
        }

        private void EvaluateCurve(Curve crv, List<(Point3d, string, Color)> outDots)
        {
            if (crv is PolylineCurve plc)
            {
                for (int i = 1; i < plc.PointCount - 1; i++) AddDotRecord(outDots, plc.Point(i), "G0");
            }
            else if (crv is PolyCurve pc)
            {
                for (int i = 1; i < pc.SegmentCount; i++)
                {
                    Curve segB = pc.SegmentCurve(i - 1);
                    Curve segA = pc.SegmentCurve(i);
                    ProcessJoin(segB, segB.Domain.T1, segA, segA.Domain.T0, outDots);
                }
                
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    if (pc.SegmentCurve(i) is PolylineCurve subPlc)
                    {
                        for (int j = 1; j < subPlc.PointCount - 1; j++)
                            AddDotRecord(outDots, subPlc.Point(j), "G0");
                    }
                    else
                    {
                        EvaluateInnerKnots(pc.SegmentCurve(i), outDots);
                    }
                }
            }
            else
            {
                EvaluateInnerKnots(crv, outDots);
            }

            if (crv.IsClosed)
            {
                ProcessJoin(crv, crv.Domain.Max, crv, crv.Domain.Min, outDots);
            }
        }

        private void EvaluateInnerKnots(Curve crv, List<(Point3d, string, Color)> outDots)
        {
            if (crv is PolylineCurve || crv is LineCurve || crv is ArcCurve) return;

            NurbsCurve nc = crv.ToNurbsCurve();
            if (nc == null) return;

            double[] spans = nc.SpanVector();
            if (spans == null || spans.Length <= 2) return;

            for (int i = 1; i < spans.Length - 1; i++)
            {
                ProcessJoin(nc, spans[i], nc, spans[i], outDots);
            }
        }

        private void ProcessJoin(Curve cB, double tB, Curve cA, double tA, List<(Point3d, string, Color)> outDots)
        {
            NurbsCurve ncB = cB.ToNurbsCurve();
            NurbsCurve ncA = cA.ToNurbsCurve();
            if (ncB == null || ncA == null) return;

            bool isGInf = ContinuityUtils.IsGInfinity(ncA, tA, CurveEvaluationSide.Above, ncB, tB, CurveEvaluationSide.Below, _g1AngleTolDeg);
            
            var vB = ContinuityUtils.GetContinuityVectorsAt(ncB, tB, CurveEvaluationSide.Below);
            var vA = ContinuityUtils.GetContinuityVectorsAt(ncA, tA, CurveEvaluationSide.Above);

            if (isGInf)
            {
                AddDotRecord(outDots, vA.Pt, "G∞");
                return;
            }

            int? gLevel = ContinuityUtils.GetContinuityLevel(vB, vA, _distTol, _g1AngleTolDeg, _g2PlusAngleTolDeg, _vectMagTolPct);
            AddDotRecord(outDots, vA.Pt, ContinuityUtils.FormatContinuityString(gLevel));
        }

        private void AddDotRecord(List<(Point3d, string, Color)> outDots, Point3d pt, string label)
        {
            Color color = Color.Gray;
            if (label == "Gap") color = Color.DarkRed;
            else if (label == "G0") color = Color.Red;
            else if (label == "G1") color = Color.Gold;
            else if (label == "G2") color = Color.YellowGreen;
            else if (label == "G3+") color = Color.LimeGreen;
            else if (label == "G∞") color = Color.Cyan;

            outDots.Add((pt, label, color));
        }

        private void DrawSingleSegment(DrawEventArgs e, Curve seg)
        {
            if (seg == null) return;

            bool isLineSegment = seg is LineCurve || seg is PolylineCurve || seg.IsLinear();
            bool isArcSegment = !isLineSegment && (seg is ArcCurve || seg.IsArc());

            if (isLineSegment && HighlightLines)
            {
                e.Display.DrawCurve(seg, Color.LimeGreen, 4);
            }
            else if (isArcSegment && HighlightArcs)
            {
                // Utilize Rhino's native feedback color for arc previews
                Color feedbackColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;
                e.Display.DrawCurve(seg, feedbackColor, 4);
            }
            else
            {
                e.Display.DrawCurve(seg, Color.White, 3);

                if (ShowControlPolygon)
                {
                    NurbsCurve nc = seg as NurbsCurve ?? seg.ToNurbsCurve();
                    if (nc != null)
                    {
                        for (int k = 0; k < nc.Points.Count - 1; k++)
                        {
                            e.Display.DrawLine(nc.Points[k].Location, nc.Points[k + 1].Location, Color.DarkGray);
                            e.Display.DrawPoint(nc.Points[k].Location, PointStyle.ControlPoint, 3, Color.White);
                        }
                        e.Display.DrawPoint(nc.Points[nc.Points.Count - 1].Location, PointStyle.ControlPoint, 3, Color.White);
                    }
                }
            }
        }
    }
}