using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling
{
    public static class FitSrfToTargetLogic
    {
        private static EscapeTracker _escapeTracker;
        private static Func<bool> _cancelToken;

        public static void CheckEscape()
        {
            if ((_escapeTracker != null && _escapeTracker.IsCanceled) || (_cancelToken?.Invoke() == true))
            {
                throw new OperationCanceledException("User canceled command.");
            }
        }

        private static bool CheckEscapeDelegate()
        {
            return (_escapeTracker != null && _escapeTracker.IsCanceled) || (_cancelToken?.Invoke() == true);
        }

        public static void BakeResults(RhinoDoc doc, List<ProjectionResult> results, List<ObjRef> startingSrfRefs, ObjRef[] targetRefs)
        {
            var attrRed = new ObjectAttributes { ObjectColor = System.Drawing.Color.Red, ColorSource = ObjectColorSource.ColorFromObject };

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] == null || results[i].Surface == null) continue;
                var ns = results[i].Surface;

                bool doReplace = FitSrfToTargetOptions.DeleteStartingSrf && startingSrfRefs != null && i < startingSrfRefs.Count;

                if (doReplace)
                {
                    Guid idToReplace = startingSrfRefs[i].ObjectId;
                    doc.Objects.Replace(idToReplace, ns);
                    
                    if (FitSrfToTargetOptions.OutputLayer != 0) 
                    {
                        var rhObj = doc.Objects.FindId(idToReplace);
                        if (rhObj != null)
                        {
                            var modAttr = rhObj.Attributes.Duplicate();
                            if (FitSrfToTargetOptions.OutputLayer == 1)
                            {
                                modAttr.LayerIndex = doc.Layers.CurrentLayerIndex;
                                modAttr.ColorSource = ObjectColorSource.ColorFromLayer;
                            }
                            else if (FitSrfToTargetOptions.OutputLayer == 2 && targetRefs != null && targetRefs.Length > 0 && targetRefs[0].Object() != null)
                            {
                                var tgtAttr = targetRefs[0].Object().Attributes;
                                modAttr.LayerIndex = tgtAttr.LayerIndex;
                                modAttr.ColorSource = tgtAttr.ColorSource;
                                modAttr.ObjectColor = tgtAttr.ObjectColor;
                            }
                            doc.Objects.ModifyAttributes(rhObj, modAttr, true);
                        }
                    }
                }
                else
                {
                    var attr = new ObjectAttributes();
                    if (FitSrfToTargetOptions.OutputLayer == 0 && startingSrfRefs != null && i < startingSrfRefs.Count && startingSrfRefs[i].Object() != null)
                    {
                        var srcAttr = startingSrfRefs[i].Object().Attributes;
                        attr.LayerIndex = srcAttr.LayerIndex;
                        attr.ColorSource = srcAttr.ColorSource;
                        attr.ObjectColor = srcAttr.ObjectColor;
                    }
                    else if (FitSrfToTargetOptions.OutputLayer == 2 && targetRefs != null && targetRefs.Length > 0 && targetRefs[0].Object() != null)
                    {
                        var tgtAttr = targetRefs[0].Object().Attributes;
                        attr.LayerIndex = tgtAttr.LayerIndex;
                        attr.ColorSource = tgtAttr.ColorSource;
                        attr.ObjectColor = tgtAttr.ObjectColor;
                    }
                    else
                    {
                        attr.LayerIndex = doc.Layers.CurrentLayerIndex;
                        attr.ColorSource = ObjectColorSource.ColorFromLayer;
                    }

                    doc.Objects.AddSurface(ns, attr);
                }

                if (FitSrfToTargetOptions.AddErrorLines && results[i].ErrorLines != null && results[i].ErrorLines.Count > 0)
                {
                    var toBake = results[i].ErrorLines.Where(l => l.Length > FitSrfToTargetOptions.Tolerance).Take(100).ToList();
                    foreach (var l in toBake) doc.Objects.AddLine(l, attrRed);
                    
                    if (results[i].MaxErrorLine.IsValid && results[i].MaxDeviation > FitSrfToTargetOptions.Tolerance)
                    {
                        int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
                        string label = $"Max: {FormatUtils.FormatDistance(results[i].MaxDeviation, prec)}";
                        var dot = new TextDot(label, results[i].MaxErrorLine.PointAt(0.5));
                        doc.Objects.AddTextDot(dot, attrRed);
                    }
                    if (toBake.Count == 100 && results[i].ErrorLines.Count > 100)
                    {
                        RhinoApp.WriteLine($"Only the top 100 error lines out of {results[i].ErrorLines.Count} were added to the document.");
                    }
                }
            }
            doc.Views.Redraw();
        }

        public static Result ExecuteBake(RhinoDoc doc, ObjRef[] targetRefs, List<ObjRef> startingSrfRefs)
        {
            var targetBreps = new List<Brep>();
            var targetMeshes = new List<Mesh>();

            foreach (var r in targetRefs)
            {
                var geom = r.Geometry();
                if (geom is SubD subD) { var lm = Mesh.CreateFromSubD(subD, 3); if (lm != null) targetMeshes.Add(lm); else { var pb = subD.ToBrep(new SubDToBrepOptions()); if (pb != null) targetBreps.Add(pb); } }
                else if (geom is Brep b) targetBreps.Add(b);
                else if (geom is Mesh m) targetMeshes.Add(m);
            }

            var startSrfs = new List<NurbsSurface>();
            foreach (var r in startingSrfRefs)
            {
                Surface srf = r.Surface();
                if (srf == null && r.Brep()?.Faces.Count == 1) srf = r.Brep().Faces[0].UnderlyingSurface();
                startSrfs.Add(srf?.ToNurbsSurface());
            }

            List<ProjectionResult> results;
            List<string> messages;

            try
            {
                results = ComputeFitSurfacesCore(targetBreps, targetMeshes, startSrfs, doc.ModelAbsoluteTolerance, out messages, out _, null, null);
            }
            catch (OperationCanceledException)
            {
                return Result.Cancel;
            }

            if (results == null || results.Count == 0) return Result.Failure;

            foreach (var msg in messages)
            {
                if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
            }

            BakeResults(doc, results, startingSrfRefs, targetRefs);
            return Result.Success;
        }

        public static List<ProjectionResult> ComputeFitSurfacesCore(List<Brep> targetBreps, List<Mesh> targetMeshes, List<NurbsSurface> startingSrfs, double docTol, out List<string> messages, out double maxDeviation, Action<SolverProgress> progressCallback, Func<bool> cancelToken)
        {
            _cancelToken = cancelToken;
            messages = new List<string>();
            maxDeviation = 0.0;
            var outResults = new List<ProjectionResult>();

            using (_escapeTracker = new EscapeTracker())
            {
                if (targetBreps.Count == 0 && targetMeshes.Count == 0)
                {
                    messages.Add("No valid target geometry could be extracted.");
                    return outResults;
                }

                int totalSurfaces = startingSrfs.Count;
                int totalMisses = 0;
                Vector3d rayDir = FitSrfToTargetOptions.FlipDirection ? Vector3d.ZAxis : -Vector3d.ZAxis;

                for (int i = 0; i < totalSurfaces; i++)
                {
                    CheckEscape();
                    NurbsSurface nsWIP = startingSrfs[i];
                    if (nsWIP == null) 
                    {
                        outResults.Add(null);
                        continue;
                    }

                    ProjectionResult res = null;
                    int solverType = FitSrfToTargetOptions.SolverType;
                    int mappingDirection = FitSrfToTargetOptions.MappingDirection;
                    
                    bool effectiveConstrain1D = FitSrfToTargetOptions.Constrain1D && (mappingDirection == 1);

                    if (solverType == 0 || solverType == 1)
                    {
                        Point3d[,] sourcePts;
                        if (solverType == 0)
                            sourcePts = GetControlPointLocations(nsWIP);
                        else
                            sourcePts = GetGrevillePoints(nsWIP);

                        Point3d?[,] targetPts;
                        if (mappingDirection == 0) 
                            targetPts = IntersectionMath.PullPtsToObjs(sourcePts, targetBreps, targetMeshes, CheckEscapeDelegate);
                        else 
                            targetPts = IntersectionMath.RaycastPtsToObjs(sourcePts, targetBreps, targetMeshes, rayDir, CheckEscapeDelegate);

                        bool hitAnything = false;
                        foreach (var pt in targetPts) { if (pt.HasValue) { hitAnything = true; break; } }
                        
                        if (!hitAnything)
                        {
                            outResults.Add(null);
                            totalMisses++;
                            progressCallback?.Invoke(null);
                            continue;
                        }

                        if (solverType == 0)
                            res = ProjectionMath.FitDirectControlPoints(targetPts, nsWIP, targetBreps, targetMeshes, FitSrfToTargetOptions.Tolerance, progressCallback);
                        else
                            res = ProjectionMath.FitDirectGreville(targetPts, nsWIP, targetBreps, targetMeshes, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate, progressCallback);
                    }
                    else if (solverType == 2)
                    {
                        res = ProjectionMath.FitWeightedRelaxed(nsWIP, targetBreps, targetMeshes, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate, progressCallback);
                    }
                    else if (solverType == 3)
                    {
                        res = ProjectionMath.FitGlobalLeastSquares(nsWIP, targetBreps, targetMeshes, effectiveConstrain1D, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate, progressCallback);
                    }
                    else if (solverType == 4)
                    {
                        double stiffness = Math.Pow(10, FitSrfToTargetOptions.EnergyStiffnessExponent);
                        res = ProjectionMath.FitEnergyMinimization(nsWIP, targetBreps, targetMeshes, stiffness, effectiveConstrain1D, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate, progressCallback);
                    }

                    if (res != null)
                    {
                        if (!res.Converged && res.Iterations == 0) 
                        {
                            outResults.Add(null);
                            totalMisses++;
                            progressCallback?.Invoke(null);
                            continue;
                        }

                        maxDeviation = Math.Max(maxDeviation, res.MaxDeviation);
                        if (!string.IsNullOrEmpty(res.Message)) messages.Add(res.Message);
                        outResults.Add(res);
                    }
                    else
                    {
                        outResults.Add(null);
                    }

                    progressCallback?.Invoke(null); 
                }

                if (totalMisses > 0)
                {
                    if (totalSurfaces == 1)
                        messages.Add("The starting surface completely misses the targets.");
                    else
                        messages.Add($"{totalMisses} of {totalSurfaces} starting surfaces completely miss the targets.");
                }

                return outResults;
            }
        }

        private static Point3d[,] GetGrevillePoints(NurbsSurface ns)
        {
            int countU = ns.Points.CountU;
            int countV = ns.Points.CountV;
            var pts = new Point3d[countU, countV];

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    Point2d uv = ns.Points.GetGrevillePoint(u, v);
                    pts[u, v] = ns.PointAt(uv.X, uv.Y);
                }
            }
            return pts;
        }

        private static Point3d[,] GetControlPointLocations(NurbsSurface ns)
        {
            int countU = ns.Points.CountU;
            int countV = ns.Points.CountV;
            var pts = new Point3d[countU, countV];

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    pts[u, v] = ns.Points.GetControlPoint(u, v).Location;
                }
            }
            return pts;
        }
    }
}