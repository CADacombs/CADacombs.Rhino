using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace CADacombs.Commands.Modeling
{
    public class DrapeConduit : DisplayConduit
    {
        public List<NurbsSurface> PreviewSurfaces { get; set; } = new List<NurbsSurface>();
        public List<Brep> PreviewBreps { get; set; } = new List<Brep>();
        public List<Color> PreviewColors { get; set; } = new List<Color>();

        public bool ShowSurface { get; set; } = true;
        public bool ShowWireframe { get; set; } = true;
        public bool ShowPolygon { get; set; } = false;

        // Ensures the preview doesn't get clipped if the user pans the camera away from the original targets
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            base.CalculateBoundingBox(e);
            if (PreviewSurfaces != null)
            {
                foreach (var srf in PreviewSurfaces)
                {
                    if (srf != null)
                    {
                        var bbox = srf.GetBoundingBox(false);
                        bbox.Inflate(Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance * 10.0);
                        e.IncludeBoundingBox(bbox);
                    }
                }
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            Color polyColor = Rhino.ApplicationSettings.AppearanceSettings.FeedbackColor;

            if (PreviewBreps != null && ShowSurface)
            {
                for (int i = 0; i < PreviewBreps.Count; i++)
                {
                    if (PreviewBreps[i] != null)
                    {
                        Color baseColor = i < PreviewColors.Count ? PreviewColors[i] : Color.Black;
                        
                        var material = new DisplayMaterial(baseColor);
                        material.Transparency = 0.6; // Explicitly sets it to 60% transparent to prevent obfuscation
                        
                        e.Display.DrawBrepShaded(PreviewBreps[i], material);
                    }
                }
            }

            if (PreviewSurfaces != null)
            {
                for (int i = 0; i < PreviewSurfaces.Count; i++)
                {
                    var ns = PreviewSurfaces[i];
                    if (ns == null) continue;

                    Color wireColor = i < PreviewColors.Count ? PreviewColors[i] : Color.Black;

                    if (ShowWireframe)
                    {
                        e.Display.DrawSurface(ns, wireColor, 1);
                    }

                    if (ShowPolygon)
                    {
                        for (int u = 0; u < ns.Points.CountU; u++)
                        {
                            for (int v = 0; v < ns.Points.CountV - 1; v++)
                            {
                                e.Display.DrawLine(ns.Points.GetControlPoint(u, v).Location, ns.Points.GetControlPoint(u, v + 1).Location, polyColor, 1);
                            }
                        }
                        for (int v = 0; v < ns.Points.CountV; v++)
                        {
                            for (int u = 0; u < ns.Points.CountU - 1; u++)
                            {
                                e.Display.DrawLine(ns.Points.GetControlPoint(u, v).Location, ns.Points.GetControlPoint(u + 1, v).Location, polyColor, 1);
                            }
                        }
                        
                        var allPts = new List<Point3d>();
                        for (int u = 0; u < ns.Points.CountU; u++)
                        {
                            for (int v = 0; v < ns.Points.CountV; v++)
                                allPts.Add(ns.Points.GetControlPoint(u, v).Location);
                        }
                        e.Display.DrawPoints(allPts, PointStyle.Simple, 3, polyColor);
                    }
                }
            }
        }
    }
}