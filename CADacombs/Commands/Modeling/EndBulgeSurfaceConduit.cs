using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class EndBulgeSurfaceConduit : EndBulgeConduit
    {
        public NurbsSurface Surface { get; set; }
        public Brep PreviewBrep { get; set; }
        public Color SurfaceColor { get; set; } = Color.Black;
        public List<(Curve Crv, int Direction, double ConstParam)> CgCurves { get; set; }
        
        public bool IsSwapped { get; set; } = false;
        
        public bool UseNativePreview { get; set; } = false;
        public bool ShowWireframe { get; set; } = true;
        public string ConduitDisplayString { get; set; } = "Shaded";

        private DisplayMaterial _cachedMaterial;
        private Color _cachedMaterialColor = Color.Empty;

        public static readonly MethodInfo DrawZebraPreviewMethod = GetDrawMethod("DrawZebraPreview");
        public static readonly MethodInfo DrawEmapPreviewMethod = GetDrawMethod("DrawEmapPreview");
        public static readonly MethodInfo DrawDraftAnglePreviewMethod = GetDrawMethod("DrawDraftAnglePreview");
        public static readonly MethodInfo DrawCurvaturePreviewMethod = GetDrawMethod("DrawCurvaturePreview");

        public EndBulgeSurfaceConduit()
        {
            CgCurves = new List<(Curve, int, double)>();
        }

        private static MethodInfo GetDrawMethod(string name)
        {
            var methods = typeof(DisplayPipeline).GetMethods().Where(m => m.Name == name);
            foreach (var m in methods)
            {
                var p = m.GetParameters();
                if (p.Length > 0 && p[0].ParameterType == typeof(Brep)) return m;
            }
            return null;
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (Surface == null) return;
            BoundingBox bbox = Surface.GetBoundingBox(false);
            bbox.Inflate(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance * 100.0);
            e.IncludeBoundingBox(bbox);
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (Surface == null) return;

            bool shouldDrawProxy = UseNativePreview ? EndBulgeOptions.ShowGeom : true;

            if (shouldDrawProxy && PreviewBrep != null && !IsSwapped)
            {
                bool isLiveWireframe = e.Display.Viewport.DisplayMode.Id == DisplayModeDescription.WireframeId;
                
                bool drawShaded = UseNativePreview ? !isLiveWireframe : ConduitDisplayString != "No shading";

                if (drawShaded)
                {
                    bool isNativeZebra = UseNativePreview && ConduitDisplayString == "Zebra";

                    if ((!UseNativePreview || isNativeZebra) && ConduitDisplayString == "Zebra" && DrawZebraPreviewMethod != null)
                        DrawReflectedPreview(e.Display, DrawZebraPreviewMethod, PreviewBrep, SurfaceColor);
                    else if (!UseNativePreview && ConduitDisplayString == "EMap" && DrawEmapPreviewMethod != null)
                        DrawReflectedPreview(e.Display, DrawEmapPreviewMethod, PreviewBrep, SurfaceColor);
                    else if (!UseNativePreview && ConduitDisplayString == "Draft angle" && DrawDraftAnglePreviewMethod != null)
                        DrawReflectedPreview(e.Display, DrawDraftAnglePreviewMethod, PreviewBrep, SurfaceColor);
                    else if (!UseNativePreview && ConduitDisplayString == "Curvature" && DrawCurvaturePreviewMethod != null)
                        DrawReflectedPreview(e.Display, DrawCurvaturePreviewMethod, PreviewBrep, SurfaceColor);
                    else
                    {
                        if (_cachedMaterial == null || _cachedMaterialColor != SurfaceColor)
                        {
                            _cachedMaterial?.Dispose();
                            var mat = new Rhino.DocObjects.Material { DiffuseColor = SurfaceColor };
                            _cachedMaterial = new DisplayMaterial(mat);
                            _cachedMaterialColor = SurfaceColor;
                        }
                        e.Display.DrawBrepShaded(PreviewBrep, _cachedMaterial);
                    }
                }
                
                // Pure wireframe logic: strictly relies on the checkbox or live viewport forcing
                if (ShowWireframe || (UseNativePreview && isLiveWireframe))
                {
                    e.Display.DrawBrepWires(PreviewBrep, SurfaceColor);
                }
            }

            if (EndBulgeOptions.ShowPolygon)
            {
                for (int v = 0; v < Surface.Points.CountV; v++)
                {
                    var pts = new List<Point3d>(Surface.Points.CountU);
                    for (int u = 0; u < Surface.Points.CountU; u++)
                        pts.Add(Surface.Points.GetControlPoint(u, v).Location);
                    e.Display.DrawPolyline(pts, FeedbackColor, 1);
                }
                
                for (int u = 0; u < Surface.Points.CountU; u++)
                {
                    var pts = new List<Point3d>(Surface.Points.CountV);
                    for (int v = 0; v < Surface.Points.CountV; v++)
                        pts.Add(Surface.Points.GetControlPoint(u, v).Location);
                    e.Display.DrawPolyline(pts, FeedbackColor, 1);
                }

                var allPts = new List<Point3d>(Surface.Points.CountU * Surface.Points.CountV);
                for (int u = 0; u < Surface.Points.CountU; u++)
                {
                    for (int v = 0; v < Surface.Points.CountV; v++)
                        allPts.Add(Surface.Points.GetControlPoint(u, v).Location);
                }
                e.Display.DrawPoints(allPts, PointStyle.Simple, 3, FeedbackColor);
            }

            foreach (var item in CgCurves)
            {
                if (EndBulgeOptions.ShowGeom && !IsSwapped && UseNativePreview)
                {
                    e.Display.DrawCurve(item.Crv, SurfaceColor, 1);
                }
                
                if (EndBulgeOptions.ShowGraph)
                {
                    DrawSurfaceCurvatureGraph(
                        e.Display, Surface, item.Crv, item.Direction, 
                        item.ConstParam, EndBulgeOptions.GraphScale, 
                        EndBulgeOptions.GraphDensity, FeedbackColor);
                }
            }
        }

        private void DrawReflectedPreview(DisplayPipeline display, MethodInfo method, Brep brep, Color objColor)
        {
            var parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = brep;
            
            for (int i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(Color))
                    args[i] = objColor;
                else if (parameters[i].ParameterType.IsValueType)
                    args[i] = Activator.CreateInstance(parameters[i].ParameterType);
            }
            method.Invoke(display, args);
        }

        public static List<(Curve Crv, int Direction, double ConstParam)> GetCurvatureIsocurves(NurbsSurface ns)
        {
            var crvsInfo = new List<(Curve, int, double)>();
            double tol = RhinoMath.ZeroTolerance;

            Interval vDom = ns.Domain(1);
            var vParams = new List<double> { vDom.Min, vDom.Max };

            var internalV = ns.KnotsV.Where(k => k > vDom.Min + tol && k < vDom.Max - tol).Distinct().ToList();
            if (internalV.Count == 0) vParams.Add(vDom.Mid);
            else vParams.AddRange(internalV);

            foreach (double v in vParams)
            {
                Curve c = ns.IsoCurve(0, v);
                if (c != null) crvsInfo.Add((c, 0, v));
            }

            Interval uDom = ns.Domain(0);
            var uParams = new List<double> { uDom.Min, uDom.Max };

            var internalU = ns.KnotsU.Where(k => k > uDom.Min + tol && k < uDom.Max - tol).Distinct().ToList();
            if (internalU.Count == 0) uParams.Add(uDom.Mid);
            else uParams.AddRange(internalU);

            foreach (double u in uParams)
            {
                Curve c = ns.IsoCurve(1, u);
                if (c != null) crvsInfo.Add((c, 1, u));
            }

            return crvsInfo;
        }

        private void DrawSurfaceCurvatureGraph(DisplayPipeline display, NurbsSurface ns, Curve c, int direction, double constParam, int scale, int density, Color color)
        {
            double unitScale = RhinoMath.UnitScale(UnitSystem.Centimeters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            double minDist = 1e-6 * unitScale;

            if (c.GetLength() < minDist) return;
            double trueScale = Math.Pow(2.0, (scale - 100.0) / 2.0);
            int hairSteps = Math.Max(1, density + 1);
            int multiplier = (density == 0) ? 12 : (76 + hairSteps - 1) / hairSteps;
            int envSteps = hairSteps * multiplier;

            var hairTVals = new List<double>();
            var envTVals = new List<double>();

            for (int i = 0; i < c.SpanCount; i++)
            {
                Interval dom = c.SpanDomain(i);
                for (int j = 0; j < hairSteps; j++) hairTVals.Add(dom.Min + (dom.Length / hairSteps) * j);
                for (int j = 0; j < envSteps; j++) envTVals.Add(dom.Min + (dom.Length / envSteps) * j);
            }
            hairTVals.Add(c.Domain.Max);
            envTVals.Add(c.Domain.Max);

            var envPts = new List<Point3d>();
            foreach (double t in envTVals)
            {
                Point3d P = c.PointAt(t);
                Vector3d cv = c.CurvatureAt(t);
                Vector3d norm = (direction == 0) ? ns.NormalAt(t, constParam) : ns.NormalAt(constParam, t);

                if (!norm.IsValid || norm.Length < minDist || !cv.IsValid || cv.Length > 1e5)
                {
                    envPts.Add(P);
                    continue;
                }
                double kappaN = cv * norm;
                Vector3d hair = norm * (kappaN * trueScale * -1.0);
                envPts.Add(P + hair);
            }

            if (envPts.Count > 1) display.DrawPolyline(envPts, color, 1);

            foreach (double t in hairTVals)
            {
                Point3d P = c.PointAt(t);
                Vector3d cv = c.CurvatureAt(t);
                Vector3d norm = (direction == 0) ? ns.NormalAt(t, constParam) : ns.NormalAt(constParam, t);

                if (!norm.IsValid || norm.Length < minDist || !cv.IsValid || cv.Length > 1e5) continue;
                double kappaN = cv * norm;
                Vector3d hair = norm * (kappaN * trueScale * -1.0);
                display.DrawLine(P, P + hair, color, 1);
            }
        }
    }
}