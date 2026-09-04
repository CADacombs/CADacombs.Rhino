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

                // 1. Draw Segments (Green = Line, Cyan = Arc, White = Other)
                if (previewCurve is PolyCurve pc)
                {
                    for (int i = 0; i < pc.SegmentCount; i++)
                    {
                        Curve seg = pc.SegmentCurve(i);
                        Color segColor = Color.White;
                        
                        if (seg is LineCurve || seg is PolylineCurve || seg.IsLinear()) 
                            segColor = Color.LimeGreen;
                        else if (seg is ArcCurve || seg.IsArc()) 
                            segColor = Color.Cyan;
                        
                        e.Display.DrawCurve(seg, segColor, 3);
                    }
                }
                else
                {
                    Color segColor = Color.White;
                    if (previewCurve is LineCurve || previewCurve is PolylineCurve || previewCurve.IsLinear()) 
                        segColor = Color.LimeGreen;
                    else if (previewCurve is ArcCurve || previewCurve.IsArc()) 
                        segColor = Color.Cyan;
                        
                    e.Display.DrawCurve(previewCurve, segColor, 3);
                }

                // 2. Draw Control Polygon
                if (ShowControlPolygon && previewCurve is NurbsCurve nc)
                {
                    for (int i = 0; i < nc.Points.Count - 1; i++)
                    {
                        e.Display.DrawLine(nc.Points[i].Location, nc.Points[i + 1].Location, Color.DarkGray);
                        e.Display.DrawPoint(nc.Points[i].Location, PointStyle.ControlPoint, 3, Color.White);
                    }
                    e.Display.DrawPoint(nc.Points[nc.Points.Count - 1].Location, PointStyle.ControlPoint, 3, Color.White);
                }

                // 3. Draw SPB Continuity Marks at PolyCurve seams
                if (ShowContinuity && previewCurve is PolyCurve pCurve)
                {
                    for (int i = 1; i < pCurve.SegmentCount; i++)
                    {
                        Curve segBelow = pCurve.SegmentCurve(i - 1);
                        Curve segAbove = pCurve.SegmentCurve(i);
                        
                        Point3d pt = segAbove.PointAtStart;

                        string label = ContinuityUtils.GetSpbContinuityBetweenSegments(segBelow, segAbove);
                        
                        Color color = Color.Red; 
                        if (label == "G2") color = Color.LimeGreen;
                        else if (label == "G1") color = Color.Gold;

                        e.Display.DrawDot(pt, label, color, Color.Black);
                    }
                }
            }
        }
    }
}