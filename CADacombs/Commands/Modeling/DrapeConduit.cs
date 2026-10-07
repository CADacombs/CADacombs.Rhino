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

        public bool ShowSurface { get; set; } = true;
        public bool ShowWireframe { get; set; } = true;
        public bool ShowPolygon { get; set; } = false;

        private readonly Color _srfColor = Color.FromArgb(150, 0, 90, 255);
        private readonly Color _wireColor = Color.Black;
        private readonly Color _polyColor = Color.Gray;

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (PreviewBreps != null && ShowSurface)
            {
                var material = new DisplayMaterial(_srfColor);
                foreach (var brep in PreviewBreps)
                {
                    if (brep != null) e.Display.DrawBrepShaded(brep, material);
                }
            }

            if (PreviewSurfaces != null)
            {
                foreach (var ns in PreviewSurfaces)
                {
                    if (ns == null) continue;

                    if (ShowWireframe)
                    {
                        e.Display.DrawSurface(ns, _wireColor, 1);
                    }

                    if (ShowPolygon)
                    {
                        for (int u = 0; u < ns.Points.CountU; u++)
                        {
                            for (int v = 0; v < ns.Points.CountV - 1; v++)
                            {
                                e.Display.DrawLine(ns.Points.GetControlPoint(u, v).Location, ns.Points.GetControlPoint(u, v + 1).Location, _polyColor, 1);
                            }
                        }
                        for (int v = 0; v < ns.Points.CountV; v++)
                        {
                            for (int u = 0; u < ns.Points.CountU - 1; u++)
                            {
                                e.Display.DrawLine(ns.Points.GetControlPoint(u, v).Location, ns.Points.GetControlPoint(u + 1, v).Location, _polyColor, 1);
                            }
                        }
                    }
                }
            }
        }
    }
}