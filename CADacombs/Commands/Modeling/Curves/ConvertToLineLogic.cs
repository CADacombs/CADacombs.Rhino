using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public static class ConvertToLineLogic
    {
        public static (LineCurve lineCurve, double deviation, string log) GetLineCurve(
            GeometryBase geom, 
            double docTol,
            double? overrideDevTol = null,
            double? overrideMinLen = null)
        {
            if (geom == null)
                return (null, 0.0, "Geometry not found! It will be skipped.");

            Rhino.Geometry.Curve crv = geom as Rhino.Geometry.Curve;
            if (geom is BrepEdge edge)
                crv = edge.DuplicateCurve();

            if (crv == null)
                return (null, 0.0, "Geometry is not a curve or BrepEdge.");

            if (crv is LineCurve)
                return (null, 0.0, "Skipped LineCurve.");

            if (crv.IsClosed)
                return (null, 0.0, "Skipped closed curve.");

            if (!ConvertToLineOptions.ProcessArcs && crv is ArcCurve)
                return (null, 0.0, "Skipped ArcCurve.");

            if (!ConvertToLineOptions.ProcessArcs && crv.IsArc(docTol))
                return (null, 0.0, "Skipped NurbsCurve that can be converted to ArcCurve within tolerance.");

            if (crv is NurbsCurve nc && nc.Degree == 1)
            {
                if (!ConvertToLineOptions.Process2PtNurbs && nc.Points.Count == 2)
                    return (null, 0.0, "Skipped degree 1, 2-point NurbsCurve.");
            }

            var lineCurve = new LineCurve(crv.PointAtStart, crv.PointAtEnd);
            double lineLength = lineCurve.GetLength();

            double minLen = overrideMinLen ?? ConvertToLineOptions.MinNewCrvLen;
            if (lineLength < minLen)
                return (null, 0.0, "Curve is too short.");

            bool success = Rhino.Geometry.Curve.GetDistancesBetweenCurves(crv, lineCurve, docTol, out double maxDev, out _, out _, out _, out _, out _);
            if (!success)
                return (null, 0.0, "Deviation could not be determined by GetDistancesBetweenCurves.");

            double devTol = overrideDevTol ?? ConvertToLineOptions.DevTol;

            if (ConvertToLineOptions.TolByRatio)
            {
                double ratio = maxDev > 0.0 ? lineLength / maxDev : double.MaxValue;
                if (ratio < ConvertToLineOptions.TolRatio)
                    return (null, maxDev, $"Ratio ({ratio:F1}) is less than minimum ratio tolerance ({ConvertToLineOptions.TolRatio:F1}).");
            }
            else
            {
                if (maxDev > devTol)
                    return (null, maxDev, $"Required tolerance ({maxDev:E3}) exceeds maximum allowed deviation ({devTol:E3}).");
            }

            return (lineCurve, maxDev, null);
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

                var (lineCurve, deviation, log) = GetLineCurve(objRef.Geometry(), doc.ModelAbsoluteTolerance);

                if (log != null)
                    logs.Add(log);

                if (lineCurve == null)
                    continue;

                devs.Add(deviation);

                if (ConvertToLineOptions.Replace && objRef.ObjectId != Guid.Empty)
                {
                    if (doc.Objects.Replace(objRef.ObjectId, lineCurve))
                        replacedCount++;
                    else
                        logs.Add("Failed to replace document object.");
                }
                else
                {
                    Guid newId = doc.Objects.AddCurve(lineCurve);
                    if (newId != Guid.Empty)
                        addedCount++;
                    else
                        logs.Add("Failed to add new LineCurve to document.");
                }
            }

            if (ConvertToLineOptions.Echo)
            {
                var uniqueLogs = new HashSet<string>(logs);
                foreach (var log in uniqueLogs)
                {
                    int count = logs.FindAll(l => l == log).Count;
                    RhinoApp.WriteLine($"[{count}] {log}");
                }

                if (devs.Count > 0)
                {
                    if (devs.Count == 1)
                        RhinoApp.WriteLine($"Deviation: {devs[0]:E3}");
                    else
                        RhinoApp.WriteLine($"Deviations: [{Math.Min(devs[0], devs[devs.Count - 1]):E3} to {Math.Max(devs[0], devs[devs.Count - 1]):E3}]");
                }

                if (replacedCount > 0) RhinoApp.WriteLine($"{replacedCount} curve(s) replaced.");
                if (addedCount > 0) RhinoApp.WriteLine($"{addedCount} curve(s) added.");
            }

            doc.Views.Redraw();
            return (replacedCount > 0 || addedCount > 0) ? Result.Success : Result.Nothing;
        }
    }
}