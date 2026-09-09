using System.Collections.Generic;
using System.Drawing;
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
                    // 1. Draw continuity between explicitly separated segments in PolyCurves
                    if (previewCurve is PolyCurve pCurve)
                    {
                        for (int i = 1; i < pCurve.SegmentCount; i++)
                        {
                            Curve segBelow = pCurve.SegmentCurve(i - 1);
                            Curve segAbove = pCurve.SegmentCurve(i);
                            
                            Point3d pt = segAbove.PointAtStart;
                            string label = ContinuityUtils.GetSpbContinuityBetweenSegments(segBelow, segAbove);
                            
                            // Append the "+" for G2 limits
                            if (label == "G2") label = "G2+";
                            
                            Color color = Color.Red; 
                            if (label == "G2+") color = Color.LimeGreen;
                            else if (label == "G1") color = Color.Gold;

                            e.Display.DrawDot(pt, label, color, Color.Black);
                        }
                    }

                    // 2. Draw explicit G0 dots at internal vertices of Polylines
                    DrawInternalPolylineG0Dots(e, previewCurve);
                    
                    // 3. Draw dots at interior knots of raw NURBS curves
                    DrawNurbsInteriorContinuity(e, previewCurve);

                    // 4. Draw the Seam dot for ALL closed curves
                    if (previewCurve.IsClosed)
                    {
                        DrawClosedSeam(e, previewCurve);
                    }
                }
            }
        }

        private void DrawNurbsInteriorContinuity(DrawEventArgs e, Curve curve)
        {
            if (curve == null || curve is PolylineCurve) return;

            // If it's a PolyCurve, dig inside and evaluate its individual segments
            if (curve is PolyCurve pc)
            {
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    DrawNurbsInteriorContinuity(e, pc.SegmentCurve(i));
                }
                return;
            }

            double[] spans = curve.SpanVector();
            if (spans == null || spans.Length <= 2) return; 

            for (int i = 1; i < spans.Length - 1; i++)
            {
                double t = spans[i];
                string label = ContinuityUtils.GetSpbContinuity(curve, t);
                
                // Append the "+" for G2 limits
                if (label == "G2") label = "G2+";
                
                Color color = Color.Red; 
                if (label == "G2+") color = Color.LimeGreen;
                else if (label == "G1") color = Color.Gold;

                e.Display.DrawDot(curve.PointAt(t), label, color, Color.Black);
            }

        }

        private void DrawClosedSeam(DrawEventArgs e, Curve curve)
        {
            string label = "G0"; 
            Color color = Color.Red;

            if (curve is PolylineCurve)
            {
                label = "G0";
            }
            else if (curve is PolyCurve pc && pc.SegmentCount > 1)
            {
                label = ContinuityUtils.GetSpbContinuityBetweenSegments(pc.SegmentCurve(pc.SegmentCount - 1), pc.SegmentCurve(0));
                if (label == "G2") label = "G2+";
            }
            else
            {
                // Manually evaluate the start and end vectors to avoid Rhino's IsContinuous seam quirks
                Vector3d tStart = curve.TangentAt(curve.Domain.Min);
                Vector3d tEnd = curve.TangentAt(curve.Domain.Max);
                
                double angleTol = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
                
                // 1 = parallel (same direction), -1 = anti-parallel, 0 = not parallel
                if (tStart.IsParallelTo(tEnd, angleTol) == 1) 
                {
                    Vector3d kStart = curve.CurvatureAt(curve.Domain.Min);
                    Vector3d kEnd = curve.CurvatureAt(curve.Domain.Max);
                    
                    if (kStart.EpsilonEquals(kEnd, 1e-5)) label = "G2+";
                    else label = "G1";
                }
                else
                {
                    label = "G0";
                }
            }

            if (label == "G-inf" || label == "G3") color = Color.Cyan;
            else if (label == "G2+") color = Color.LimeGreen;
            else if (label == "G1") color = Color.Gold;

            e.Display.DrawDot(curve.PointAtStart, label, color, Color.Black);
        }

        private void DrawInternalPolylineG0Dots(DrawEventArgs e, Curve curve)
        {
            if (curve is PolylineCurve plc)
            {
                for (int i = 1; i < plc.PointCount - 1; i++)
                {
                    e.Display.DrawDot(plc.Point(i), "G0", Color.Red, Color.Black);
                }
            }
            else if (curve is PolyCurve pc)
            {
                for (int i = 0; i < pc.SegmentCount; i++)
                {
                    if (pc.SegmentCurve(i) is PolylineCurve subPlc)
                    {
                        for (int j = 1; j < subPlc.PointCount - 1; j++)
                        {
                            e.Display.DrawDot(subPlc.Point(j), "G0", Color.Red, Color.Black);
                        }
                    }
                }
            }
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
                e.Display.DrawCurve(seg, Color.Cyan, 4);
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