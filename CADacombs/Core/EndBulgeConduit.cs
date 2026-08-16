using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace CADacombs.Core
{
    /// <summary>
    /// Handles the real-time preview drawing of the modified curve, 
    /// control polygon, and curvature graph.
    /// </summary>
    public class EndBulgeConduit : DisplayConduit
    {
        public Color FeedbackColor { get; set; }
        public NurbsCurve Crv { get; set; }

        public EndBulgeConduit()
        {
            FeedbackColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (Crv == null) return;
            
            BoundingBox bbox = Crv.GetBoundingBox(false);
            bbox.Inflate(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance * 100.0);
            e.IncludeBoundingBox(bbox);
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (Crv == null) return;

            // 1. Draw the Curve Geometry
            if (EndBulgeOptions.ShowGeom)
            {
                var displayMode = e.Display.Viewport.DisplayMode;
                int crvThk = displayMode.DisplayAttributes.CurveThickness + 1;
                e.Display.DrawCurve(Crv, FeedbackColor, crvThk);
            }

            // 2. Draw the Control Polygon and Points
            if (EndBulgeOptions.ShowPolygon)
            {
                List<Point3d> cpLocations = new List<Point3d>(Crv.Points.Count);
                foreach (var pt in Crv.Points)
                {
                    cpLocations.Add(pt.Location);
                }

                // 0x00001111 hex pattern creates the dashed line effect
                e.Display.DrawPatternedPolyline(cpLocations, FeedbackColor, 0x00001111, 1, false);
                e.Display.DrawPoints(cpLocations, PointStyle.Simple, 3, FeedbackColor);
            }

            // 3. Draw the Curvature Graph
            if (EndBulgeOptions.ShowGraph)
            {
                e.Display.DrawCurvatureGraph(
                    Crv, 
                    FeedbackColor, 
                    EndBulgeOptions.GraphScale, 
                    EndBulgeOptions.GraphDensity, 
                    2);
            }
        }
    }
}