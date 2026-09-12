using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class ConvertToArcLogic
    {
        public static (ArcCurve arcCurve, double deviation, string log) GetArcCurve(
            GeometryBase geom, 
            double docTol,
            double? overrideDevTol = null,
            double? overrideMinLen = null,
            bool bypassMaxRadius = false)
        {
            if (geom == null)
                return (null, 0.0, "Geometry not found! It will be skipped.");

            Rhino.Geometry.Curve crv = geom as Rhino.Geometry.Curve;
            if (geom is BrepEdge edge)
                crv = edge.DuplicateCurve(); 

            if (crv == null)
                return (null, 0.0, "Geometry is not a curve or BrepEdge.");

            if (crv is ArcCurve)
                return (null, 0.0, "Curve is already an ArcCurve."); 

            if (crv is LineCurve)
                return (null, 0.0, "Skipped LineCurve."); 

            if (crv.IsLinear(1e-9))
                return (null, 0.0, $"Skipped linear {crv.GetType().Name}."); 

            ArcCurve arcCurve;

            if (crv.IsClosed) 
            {
                crv.DivideByCount(3, false, out Point3d[] pts);
                if (pts == null || pts.Length < 2)
                    return (null, 0.0, "Failed to divide closed curve for arc generation.");
                
                arcCurve = new ArcCurve(new Circle(crv.PointAtStart, pts[0], pts[1])); 
            }
            else
            {
                crv.DivideByCount(2, false, out Point3d[] pts);
                if (pts == null || pts.Length < 1)
                    return (null, 0.0, "Failed to divide open curve for arc generation.");
                
                arcCurve = new ArcCurve(new Arc(crv.PointAtStart, pts[0], crv.PointAtEnd)); 
            }

            double arcLength = arcCurve.GetLength();
            List<string> sizeLogs = new List<string>();

            double minLen = overrideMinLen ?? ConvertToArcOptions.MinNewCrvLen;
            if (arcLength < minLen)
                sizeLogs.Add("Curve is too short"); 
            
            if (!bypassMaxRadius && arcCurve.Radius > ConvertToArcOptions.MaxRadius)
                sizeLogs.Add("Radius is too large"); 

            if (sizeLogs.Count > 0)
                return (null, 0.0, string.Join(" and ", sizeLogs) + "."); 

            bool success = Rhino.Geometry.Curve.GetDistancesBetweenCurves(crv, arcCurve, docTol, out double maxDev, out _, out _, out _, out _, out _);
            if (!success)
                return (null, 0.0, "Deviation was not returned from GetDistancesBetweenCurves."); 

            double devTol = overrideDevTol ?? ConvertToArcOptions.DevTol;

            if (ConvertToArcOptions.TolByRatio) 
            {
                double ratio = maxDev > 0.0 ? arcLength / maxDev : double.MaxValue;
                if (ratio < ConvertToArcOptions.TolRatio)
                    return (null, maxDev, "Curve was not converted because its (length / deviation) ratio is too small."); 
            }
            else
            {
                if (maxDev > devTol)
                    return (null, maxDev, $"Required distance deviation to convert: {maxDev:E3}"); 
            }

            double startAngleDeg = RhinoMath.ToDegrees(Vector3d.VectorAngle(arcCurve.TangentAtStart, crv.TangentAtStart));
            if (startAngleDeg > ConvertToArcOptions.TanTol)
                return (null, 0.0, "Tangent vector difference at start was above tolerance."); 

            double endAngleDeg = RhinoMath.ToDegrees(Vector3d.VectorAngle(arcCurve.TangentAtEnd, crv.TangentAtEnd));
            if (endAngleDeg > ConvertToArcOptions.TanTol)
                return (null, 0.0, "Tangent vector difference at end was above tolerance."); 

            return (arcCurve, maxDev, null);
        }

        public static Result Execute(RhinoDoc doc, ObjRef[] objRefs)
        {
            var devs = new List<double>();
            var logs = new List<string>();
            int replacedCount = 0;
            int addedCount = 0;

            for (int i = 0; i < objRefs.Length; i++)
            {
                ObjRef objRef = objRefs[i];
                RhinoApp.SetCommandPrompt($"Processing curve {i + 1} of {objRefs.Length}..."); 

                var (arcCurve, deviation, log) = GetArcCurve(objRef.Geometry(), doc.ModelAbsoluteTolerance);

                if (log != null)
                    logs.Add(log);

                if (arcCurve == null)
                    continue;

                devs.Add(deviation);

                if (ConvertToArcOptions.Replace && objRef.ObjectId != Guid.Empty) 
                {
                    if (doc.Objects.Replace(objRef.ObjectId, arcCurve))
                        replacedCount++;
                    else
                        logs.Add("Curve could not be replaced."); 
                }
                else
                {
                    Guid newId = doc.Objects.AddCurve(arcCurve);
                    if (newId != Guid.Empty)
                        addedCount++;
                    else
                        logs.Add("Curve could not be added."); 
                }
            }

            if (ConvertToArcOptions.Echo)
            {
                if (objRefs.Length > 1)
                    RhinoApp.WriteLine($"Out of {objRefs.Length} total curves:"); 

                var uniqueLogs = new HashSet<string>(logs);
                foreach (var log in uniqueLogs)
                {
                    int count = logs.FindAll(l => l == log).Count;
                    RhinoApp.WriteLine($"[{count}] {log}");
                }

                if (replacedCount > 0 || addedCount > 0)
                {
                    if (devs.Count == 1)
                        RhinoApp.WriteLine($"Deviation: {devs[0]:E3}");
                    else
                        RhinoApp.WriteLine($"Deviations: [{Math.Min(devs[0], devs[devs.Count - 1]):E3} to {Math.Max(devs[0], devs[devs.Count - 1]):E3}]");
                }

                if (replacedCount > 0) RhinoApp.WriteLine($"{replacedCount} curve(s) replaced."); 
                if (addedCount > 0) RhinoApp.WriteLine($"{addedCount} curve(s) added."); 
                if (replacedCount == 0 && addedCount == 0) RhinoApp.WriteLine("No curves were added or replaced."); 
            }

            doc.Views.Redraw();
            return (replacedCount > 0 || addedCount > 0) ? Result.Success : Result.Nothing;
        }
    }
}