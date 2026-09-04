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
        /// <summary>
        /// Evaluates a single curve or edge and attempts to convert it to an ArcCurve.
        /// </summary>
        public static (ArcCurve arcCurve, double deviation, string log) GetArcCurve(
            GeometryBase geom, 
            double docTol)
        {
            if (geom == null)
                return (null, 0.0, "Geometry not found! It will be skipped.");

            Rhino.Geometry.Curve crv = geom as Rhino.Geometry.Curve;
            if (geom is BrepEdge edge)
                crv = edge.DuplicateCurve(); // DuplicateCurve avoids overly long EdgeCurve domains[cite: 7]

            if (crv == null)
                return (null, 0.0, "Geometry is not a curve or BrepEdge.");

            if (crv is ArcCurve)
                return (null, 0.0, "Curve is already an ArcCurve."); //[cite: 7]

            if (crv is LineCurve)
                return (null, 0.0, "Skipped LineCurve."); //[cite: 7]

            if (crv.IsLinear(1e-9))
                return (null, 0.0, $"Skipped linear {crv.GetType().Name}."); //[cite: 7]

            ArcCurve arcCurve;

            if (crv.IsClosed) //[cite: 7]
            {
                // For closed curves, build a circle from Start, 1/3, and 2/3 parameters[cite: 7]
                crv.DivideByCount(3, false, out Point3d[] pts);
                if (pts == null || pts.Length < 2)
                    return (null, 0.0, "Failed to divide closed curve for arc generation.");
                
                arcCurve = new ArcCurve(new Circle(crv.PointAtStart, pts[0], pts[1])); //[cite: 7]
            }
            else
            {
                // For open curves, build an arc from Start, Mid, and End parameters[cite: 7]
                crv.DivideByCount(2, false, out Point3d[] pts);
                if (pts == null || pts.Length < 1)
                    return (null, 0.0, "Failed to divide open curve for arc generation.");
                
                arcCurve = new ArcCurve(new Arc(crv.PointAtStart, pts[0], crv.PointAtEnd)); //[cite: 7]
            }

            // Size Validations[cite: 7]
            double arcLength = arcCurve.GetLength();
            List<string> sizeLogs = new List<string>();

            if (arcLength < ConvertToArcOptions.MinNewCrvLen)
                sizeLogs.Add("Curve is too short"); //[cite: 7]
            
            if (arcCurve.Radius > ConvertToArcOptions.MaxRadius)
                sizeLogs.Add("Radius is too large"); //[cite: 7]

            if (sizeLogs.Count > 0)
                return (null, 0.0, string.Join(" and ", sizeLogs) + "."); //[cite: 7]

            // Distance Deviation Validation[cite: 7]
            bool success = Rhino.Geometry.Curve.GetDistancesBetweenCurves(crv, arcCurve, docTol, out double maxDev, out _, out _, out _, out _, out _);
            if (!success)
                return (null, 0.0, "Deviation was not returned from GetDistancesBetweenCurves."); //[cite: 7]

            if (ConvertToArcOptions.TolByRatio) //[cite: 7]
            {
                double ratio = maxDev > 0.0 ? arcLength / maxDev : double.MaxValue;
                if (ratio < ConvertToArcOptions.TolRatio)
                    return (null, maxDev, "Curve was not converted because its (length / deviation) ratio is too small."); //[cite: 7]
            }
            else
            {
                if (maxDev > ConvertToArcOptions.DevTol)
                    return (null, maxDev, $"Required distance deviation to convert: {maxDev:E3}"); //[cite: 7]
            }

            // Tangency Validations[cite: 7]
            double startAngleDeg = RhinoMath.ToDegrees(Vector3d.VectorAngle(arcCurve.TangentAtStart, crv.TangentAtStart));
            if (startAngleDeg > ConvertToArcOptions.TanTol)
                return (null, 0.0, "Tangent vector difference at start was above tolerance."); //[cite: 7]

            double endAngleDeg = RhinoMath.ToDegrees(Vector3d.VectorAngle(arcCurve.TangentAtEnd, crv.TangentAtEnd));
            if (endAngleDeg > ConvertToArcOptions.TanTol)
                return (null, 0.0, "Tangent vector difference at end was above tolerance."); //[cite: 7]

            return (arcCurve, maxDev, null);
        }

        /// <summary>
        /// Processes selected document objects, converting qualifying curves to ArcCurves.
        /// </summary>
        public static Result Execute(RhinoDoc doc, ObjRef[] objRefs)
        {
            var devs = new List<double>();
            var logs = new List<string>();
            int replacedCount = 0;
            int addedCount = 0;

            for (int i = 0; i < objRefs.Length; i++)
            {
                ObjRef objRef = objRefs[i];
                RhinoApp.SetCommandPrompt($"Processing curve {i + 1} of {objRefs.Length}..."); //[cite: 7]

                var (arcCurve, deviation, log) = GetArcCurve(objRef.Geometry(), doc.ModelAbsoluteTolerance);

                if (log != null)
                    logs.Add(log);

                if (arcCurve == null)
                    continue;

                devs.Add(deviation);

                if (ConvertToArcOptions.Replace && objRef.ObjectId != Guid.Empty) //[cite: 7]
                {
                    if (doc.Objects.Replace(objRef.ObjectId, arcCurve))
                        replacedCount++;
                    else
                        logs.Add("Curve could not be replaced."); //[cite: 7]
                }
                else
                {
                    Guid newId = doc.Objects.AddCurve(arcCurve);
                    if (newId != Guid.Empty)
                        addedCount++;
                    else
                        logs.Add("Curve could not be added."); //[cite: 7]
                }
            }

            // Reporting output[cite: 7]
            if (ConvertToArcOptions.Echo)
            {
                if (objRefs.Length > 1)
                    RhinoApp.WriteLine($"Out of {objRefs.Length} total curves:"); //[cite: 7]

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

                if (replacedCount > 0) RhinoApp.WriteLine($"{replacedCount} curve(s) replaced."); //[cite: 7]
                if (addedCount > 0) RhinoApp.WriteLine($"{addedCount} curve(s) added."); //[cite: 7]
                if (replacedCount == 0 && addedCount == 0) RhinoApp.WriteLine("No curves were added or replaced."); //[cite: 7]
            }

            doc.Views.Redraw();
            return (replacedCount > 0 || addedCount > 0) ? Result.Success : Result.Nothing;
        }
    }
}