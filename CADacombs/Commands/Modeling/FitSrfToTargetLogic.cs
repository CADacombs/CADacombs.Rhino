using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

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

        public static void BakeResults(RhinoDoc doc, List<NurbsSurface> results, List<ObjRef> startingSrfRefs, ObjRef[] targetRefs)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var ns = results[i];
                if (ns == null) continue;

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
            }
            doc.Views.Redraw();
        }

        public static Result ExecuteBake(RhinoDoc doc, ObjRef[] targetRefs, List<ObjRef> startingSrfRefs)
        {
            List<NurbsSurface> results;
            List<string> messages;

            try
            {
                results = ComputeFitSurfaces(doc, targetRefs, startingSrfRefs, out messages, out _, null, null);
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

        public static List<NurbsSurface> ComputeFitSurfaces(RhinoDoc doc, ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, out List<string> messages, out double maxDeviation, Action progressCallback, Func<bool> cancelToken)
        {
            _cancelToken = cancelToken;
            messages = new List<string>();
            maxDeviation = 0.0;
            var outSurfaces = new List<NurbsSurface>();

            using (_escapeTracker = new EscapeTracker())
            {
                var targetBreps = new List<Brep>();
                var targetMeshes = new List<Mesh>();

                // Extract SubD / Brep / Mesh
                foreach (var r in targetRefs)
                {
                    var geom = r.Geometry();
                    if (geom is SubD subD)
                    {
                        var limitMesh = Mesh.CreateFromSubD(subD, 3);
                        if (limitMesh != null) targetMeshes.Add(limitMesh);
                        else
                        {
                            var proxyBrep = subD.ToBrep(new SubDToBrepOptions());
                            if (proxyBrep != null) targetBreps.Add(proxyBrep);
                        }
                    }
                    else if (geom is Brep b) targetBreps.Add(b);
                    else if (geom is Mesh m) targetMeshes.Add(m);
                }

                if (targetBreps.Count == 0 && targetMeshes.Count == 0)
                {
                    RhinoApp.WriteLine("No valid target geometry could be extracted.");
                    return outSurfaces;
                }

                int totalSurfaces = startingSrfRefs.Count;

                for (int i = 0; i < totalSurfaces; i++)
                {
                    CheckEscape();
                    Surface srf = startingSrfRefs[i].Surface();
                    if (srf == null && startingSrfRefs[i].Brep()?.Faces.Count == 1)
                        srf = startingSrfRefs[i].Brep().Faces[0].UnderlyingSurface();

                    NurbsSurface nsWIP = srf?.ToNurbsSurface();
                    if (nsWIP == null) 
                    {
                        outSurfaces.Add(null);
                        continue;
                    }

                    NurbsSurface nsOut = null;
                    ProjectionResult res = null;

                    int solverType = FitSrfToTargetOptions.SolverType;
                    int mappingDirection = FitSrfToTargetOptions.MappingDirection;

                    // Standard Solvers 0 & 1 (1:1 Point Mapping)
                    if (solverType == 0 || solverType == 1)
                    {
                        Point3d[,] sourcePts;
                        if (solverType == 0)
                            sourcePts = GetControlPointLocations(nsWIP);
                        else
                            sourcePts = GetGrevillePoints(nsWIP);

                        Point3d?[,] targetPts;
                        if (mappingDirection == 0) // Closest Point
                            targetPts = IntersectionMath.PullPtsToObjs(sourcePts, targetBreps, targetMeshes, CheckEscapeDelegate);
                        else // Z-Raycast
                            targetPts = IntersectionMath.ProjectPtsToObjs(sourcePts, targetBreps, targetMeshes, doc.ModelAbsoluteTolerance, false, CheckEscapeDelegate);

                        if (solverType == 0)
                            res = ProjectionMath.FitDirectControlPoints(targetPts, nsWIP, FitSrfToTargetOptions.Tolerance);
                        else
                            res = ProjectionMath.FitDirectGreville(targetPts, nsWIP, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate);
                    }
                    // Advanced Solvers 2 & 3 (Dense Sampling & Complex Math)
                    else if (solverType == 2)
                    {
                        res = ProjectionMath.FitPythonLegacy(nsWIP, targetBreps, targetMeshes, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate);
                    }
                    else if (solverType == 3)
                    {
                        res = ProjectionMath.FitWeightedRelaxed(nsWIP, targetBreps, targetMeshes, FitSrfToTargetOptions.Tolerance, FitSrfToTargetOptions.SolverTimeout, CheckEscapeDelegate);
                    }

                    if (res != null)
                    {
                        nsOut = res.Surface;
                        maxDeviation = Math.Max(maxDeviation, res.MaxDeviation);
                        if (!string.IsNullOrEmpty(res.Message)) messages.Add(res.Message);
                    }

                    outSurfaces.Add(nsOut);
                    progressCallback?.Invoke();
                }

                return outSurfaces;
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