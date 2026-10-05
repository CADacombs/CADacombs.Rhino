using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace CADacombs.Commands.Modeling
{
    public class DrapeConduit : DisplayConduit
    {
        public NurbsSurface PreviewSurface { get; set; }
        public Brep PreviewBrep { get; set; }
        
        public bool ShowSurface { get; set; } = true;
        public bool ShowWireframe { get; set; } = true;
        public bool ShowPolygon { get; set; } = false;

        private DisplayMaterial _material;

        public DrapeConduit()
        {
            Color baseColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;
            _material = new DisplayMaterial(baseColor) { Transparency = 0.2 };
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            base.CalculateBoundingBox(e);
            if (PreviewSurface != null)
            {
                e.IncludeBoundingBox(PreviewSurface.GetBoundingBox(false));
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            base.PostDrawObjects(e);

            if (PreviewSurface == null) return;

            if (ShowSurface && PreviewBrep != null)
            {
                e.Display.DrawBrepShaded(PreviewBrep, _material);
            }

            if (ShowWireframe)
            {
                e.Display.DrawSurface(PreviewSurface, Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor, 2);
            }

            if (ShowPolygon)
            {
                List<Point3d> cpLocations = new List<Point3d>(PreviewSurface.Points.CountU * PreviewSurface.Points.CountV);
                for (int u = 0; u < PreviewSurface.Points.CountU; u++)
                {
                    for (int v = 0; v < PreviewSurface.Points.CountV; v++)
                    {
                        var pt = PreviewSurface.Points.GetControlPoint(u, v).Location;
                        cpLocations.Add(pt);
                        
                        // Draw grid lines
                        if (u > 0) e.Display.DrawLine(PreviewSurface.Points.GetControlPoint(u - 1, v).Location, pt, Color.DarkGray);
                        if (v > 0) e.Display.DrawLine(PreviewSurface.Points.GetControlPoint(u, v - 1).Location, pt, Color.DarkGray);
                    }
                }
                e.Display.DrawPoints(cpLocations, PointStyle.ControlPoint, 3, Color.White);
            }
        }
    }
}