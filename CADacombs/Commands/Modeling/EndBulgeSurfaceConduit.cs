using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class EndBulgeSurfaceConduit : EndBulgeConduit
    {
        public NurbsSurface Surface { get; set; }
        public List<(Curve Crv, int Direction, double ConstParam)> CgCurves { get; set; }
        
        public bool IsSwapped { get; set; } = false;

        public EndBulgeSurfaceConduit()
        {
            CgCurves = new List<(Curve, int, double)>();
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
                if (EndBulgeOptions.ShowGeom && !IsSwapped)
                {
                    e.Display.DrawCurve(item.Crv, FeedbackColor, 1);
                }
                
                if (EndBulgeOptions.ShowGraph)
                {
                    DrawSurfaceCurvatureGraph(
                        e.Display, 
                        Surface, 
                        item.Crv, 
                        item.Direction, 
                        item.ConstParam, 
                        EndBulgeOptions.GraphScale, 
                        EndBulgeOptions.GraphDensity, 
                        FeedbackColor);
                }
            }
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
                for (int j = 0; j < hairSteps; j++)
                    hairTVals.Add(dom.Min + (dom.Length / hairSteps) * j);
                for (int j = 0; j < envSteps; j++)
                    envTVals.Add(dom.Min + (dom.Length / envSteps) * j);
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

            if (envPts.Count > 1)
            {
                display.DrawPolyline(envPts, color, 1);
            }

            foreach (double t in hairTVals)
            {
                Point3d P = c.PointAt(t);
                Vector3d cv = c.CurvatureAt(t);

                Vector3d norm = (direction == 0) ? ns.NormalAt(t, constParam) : ns.NormalAt(constParam, t);

                if (!norm.IsValid || norm.Length < minDist || !cv.IsValid || cv.Length > 1e5)
                    continue;

                double kappaN = cv * norm;
                Vector3d hair = norm * (kappaN * trueScale * -1.0);
                display.DrawLine(P, P + hair, color, 1);
            }
        }
    }
}