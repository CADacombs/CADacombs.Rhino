using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public static class DrapeLogic
    {
        private static EscapeTracker _escapeTracker;

        public static void CheckEscape()
        {
            if (_escapeTracker != null && _escapeTracker.IsCanceled)
            {
                throw new OperationCanceledException("User canceled command.");
            }
        }

        public static Result ExecuteBake(RhinoDoc doc, ObjRef[] targetRefs, List<ObjRef> startingSrfRefs)
        {
            var results = ComputeDrapeSurfaces(doc, targetRefs, startingSrfRefs, out _, out List<string> messages, out _);
            if (results == null || results.Count == 0) return Result.Failure;

            foreach (var msg in messages)
            {
                if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
            }

            if (DrapeOptions.UserProvidesStartingSrf && DrapeOptions.DeleteStartingSrf && startingSrfRefs != null)
            {
                foreach (var srfRef in startingSrfRefs)
                {
                    doc.Objects.Delete(srfRef.ObjectId, true);
                }
            }

            foreach (var ns in results)
            {
                doc.Objects.AddSurface(ns);
            }
            doc.Views.Redraw();
            return Result.Success;
        }

        public static List<NurbsSurface> ComputeDrapeSurfaces(RhinoDoc doc, ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, out bool anyPartialMisses, out List<string> messages, out double maxDeviation, Action progressCallback = null)
        {
            anyPartialMisses = false;
            messages = new List<string>();
            maxDeviation = 0.0;
            var outSurfaces = new List<NurbsSurface>();

            using (_escapeTracker = new EscapeTracker())
            {
                try
                {
                    var targetBreps = new List<Brep>();
                    var targetMeshes = new List<Mesh>();

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

                    var view = doc.Views.ActiveView;
                    if (view == null) return outSurfaces;
                    
                    Plane cPlane = view.ActiveViewport.ConstructionPlane();
                    if (DrapeOptions.FlipCPlane) cPlane.Flip();

                    Transform xformToW = Transform.Identity;
                    Transform xformFromW = Transform.Identity;

                    if (!cPlane.Equals(Plane.WorldXY))
                    {
                        xformToW = Transform.PlaneToPlane(cPlane, Plane.WorldXY);
                        xformFromW = Transform.PlaneToPlane(Plane.WorldXY, cPlane);

                        foreach (var b in targetBreps) b.Transform(xformToW);
                        foreach (var m in targetMeshes) m.Transform(xformToW);
                    }

                    int totalSurfaces = (startingSrfRefs != null && startingSrfRefs.Count > 0) ? startingSrfRefs.Count : 1;
                    int totalMisses = 0;

                    for (int i = 0; i < totalSurfaces; i++)
                    {
                        NurbsSurface nsWIP = null;

                        if (startingSrfRefs == null || startingSrfRefs.Count == 0)
                        {
                            var allGeom = new List<GeometryBase>();
                            allGeom.AddRange(targetBreps);
                            allGeom.AddRange(targetMeshes);
                            nsWIP = CreateStartingSurface(allGeom, DrapeOptions.SpanSpacing, DrapeOptions.SpansBeyondEachSide);
                        }
                        else
                        {
                            Surface srf = startingSrfRefs[i].Surface();
                            if (srf == null && startingSrfRefs[i].Brep()?.Faces.Count == 1)
                                srf = startingSrfRefs[i].Brep().Faces[0].UnderlyingSurface();
                            
                            nsWIP = srf?.ToNurbsSurface();
                            if (nsWIP != null && !xformToW.IsIdentity) nsWIP.Transform(xformToW);
                        }

                        if (nsWIP == null) continue;

                        Point3d[,] raycastPts;
                        if (DrapeOptions.FitMethod == 3)
                            raycastPts = GetControlPointLocations(nsWIP);
                        else
                            raycastPts = GetGrevillePoints(nsWIP);

                        Point3d?[,] targetPts = ProjectPtsToObjs(raycastPts, targetBreps, targetMeshes, doc.ModelAbsoluteTolerance);
                        if (targetPts == null) continue;

                        bool hitAnything = false;
                        foreach (var pt in targetPts) 
                        { 
                            if (pt.HasValue) { hitAnything = true; break; } 
                        }

                        if (!hitAnything) 
                        {
                            totalMisses++;
                            progressCallback?.Invoke();
                            continue;
                        }

                        bool hasMisses = HasMissingPoints(targetPts);
                        if (hasMisses) anyPartialMisses = true;

                        double zMax = HighestElevation(targetPts);
                        
                        for (int u = 0; u < nsWIP.Points.CountU; u++)
                        {
                            for (int v = 0; v < nsWIP.Points.CountV; v++)
                            {
                                ControlPoint cp = nsWIP.Points.GetControlPoint(u, v);
                                nsWIP.Points.SetPoint(u, v, cp.Location.X, cp.Location.Y, zMax);
                            }
                        }

                        if (hasMisses)
                        {
                            targetPts = ResolveMissingPoints(targetPts, raycastPts, nsWIP, DrapeOptions.TargetMisses);
                        }

                        NurbsSurface nsOut;
                        if (DrapeOptions.FitMethod == 3)
                        {
                            nsOut = ProjectionMath.FitDirectControlPoints(targetPts, nsWIP);
                        }
                        else if (DrapeOptions.FitMethod == 2)
                        {
                            var res = ProjectionMath.FitDirectGreville(targetPts, nsWIP, DrapeOptions.Tolerance, DrapeOptions.SolverTimeout);
                            nsOut = res.Surface;
                            maxDeviation = Math.Max(maxDeviation, res.MaxDeviation);
                            if (!string.IsNullOrEmpty(res.Message)) messages.Add(res.Message);
                        }
                        else
                        {
                            nsOut = FitIterTranslHighToLow9Pts(targetPts, nsWIP, DrapeOptions.Tolerance, DrapeOptions.Debug, DrapeOptions.FitMethod == 0);
                        }

                        if (!xformFromW.IsIdentity) nsOut.Transform(xformFromW);

                        outSurfaces.Add(nsOut);
                        progressCallback?.Invoke();
                    }

                    if (totalMisses > 0)
                    {
                        if (totalSurfaces == 1)
                            RhinoApp.WriteLine("The starting surface completely misses the targets.");
                        else
                            RhinoApp.WriteLine($"{totalMisses} of {totalSurfaces} starting surfaces completely miss the targets.");
                    }

                    return outSurfaces;
                }
                catch (OperationCanceledException)
                {
                    return outSurfaces;
                }
            }
        }

        private static NurbsSurface CreateStartingSurface(List<GeometryBase> geometries, double spanSpacing, int spansBeyond)
        {
            BoundingBox bb = BoundingBox.Unset;
            foreach (var geom in geometries)
            {
                bb.Union(geom.GetBoundingBox(true));
            }

            int degree = 3;
            double dimX = bb.Diagonal.X;
            double startingSrfXDim = Math.Round(dimX + 2.0 * spansBeyond * spanSpacing, 0);
            Interval uInterval = new Interval(0.0, startingSrfXDim);
            int uPointCount = (int)(startingSrfXDim / spanSpacing) + degree;

            double dimY = bb.Diagonal.Y;
            double startingSrfYDim = Math.Round(dimY + 2.0 * spansBeyond * spanSpacing, 0);
            Interval vInterval = new Interval(0.0, startingSrfYDim);
            int vPointCount = (int)(startingSrfYDim / spanSpacing) + degree;

            Point3d origin = new Point3d(
                bb.Center.X - startingSrfXDim / 2.0,
                bb.Center.Y - startingSrfYDim / 2.0,
                bb.Max.Z);

            Plane plane = new Plane(origin, Vector3d.ZAxis);

            return NurbsSurface.CreateFromPlane(plane, uInterval, vInterval, degree, degree, uPointCount, vPointCount);
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

        private static Point3d?[,] ProjectPtsToObjs(Point3d[,] ptsIn, List<Brep> breps, List<Mesh> meshes, double docTol)
        {
            int countU = ptsIn.GetLength(0);
            int countV = ptsIn.GetLength(1);
            var ptsOut = new Point3d?[countU, countV];
            double rayTol = 0.1 * docTol;

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    CheckEscape();
                    var projectedPts = new List<Point3d>();
                    Point3d pt = ptsIn[u, v];

                    if (meshes.Count > 0)
                    {
                        var meshHits = Intersection.ProjectPointsToMeshes(meshes, new[] { pt }, Vector3d.ZAxis, rayTol);
                        if (meshHits != null) projectedPts.AddRange(meshHits);
                    }

                    if (breps.Count > 0)
                    {
                        var brepHits = Intersection.ProjectPointsToBreps(breps, new[] { pt }, Vector3d.ZAxis, rayTol);
                        if (brepHits != null) projectedPts.AddRange(brepHits);
                    }

                    if (projectedPts.Count == 0)
                    {
                        ptsOut[u, v] = null;
                    }
                    else if (projectedPts.Count == 1)
                    {
                        ptsOut[u, v] = projectedPts[0];
                    }
                    else
                    {
                        ptsOut[u, v] = projectedPts.OrderByDescending(p => p.Z).First();
                    }
                }
            }
            return ptsOut;
        }

        private static double HighestElevation(Point3d?[,] targetPts)
        {
            double maxZ = double.NegativeInfinity;
            foreach (var pt in targetPts)
            {
                if (pt.HasValue && pt.Value.Z > maxZ)
                {
                    maxZ = pt.Value.Z;
                }
            }
            return maxZ;
        }

        private static bool HasMissingPoints(Point3d?[,] targetPts)
        {
            foreach (var pt in targetPts)
            {
                if (!pt.HasValue) return true;
            }
            return false;
        }

        private static Point3d?[,] ResolveMissingPoints(Point3d?[,] targetPts, Point3d[,] raycastPts, NurbsSurface nsWIP, int iTargetMisses)
        {
            int countU = targetPts.GetLength(0);
            int countV = targetPts.GetLength(1);
            var ptsOut = (Point3d?[,])targetPts.Clone();

            if (iTargetMisses == 0) 
            {
                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        if (!ptsOut[u, v].HasValue)
                        {
                            ptsOut[u, v] = raycastPts[u, v]; 
                        }
                    }
                }
            }
            else if (iTargetMisses == 1) 
            {
                bool pointsAdded;
                do
                {
                    CheckEscape();
                    pointsAdded = AddMissingPointsLowestNeighborsBorder(ptsOut, raycastPts);
                } while (pointsAdded);
            }
            else if (iTargetMisses == 2) 
            {
                bool bLineExts2 = false; 

                while (HasMissingPoints(ptsOut))
                {
                    CheckEscape();
                    bool modified = AddMissingPointsAlongBorder(ptsOut, raycastPts, false, bLineExts2);
                    if (!modified) break; 
                }

                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        if (!ptsOut[u, v].HasValue)
                        {
                            ControlPoint cp = nsWIP.Points.GetControlPoint(u, v);
                            ControlPoint cp00 = nsWIP.Points.GetControlPoint(0, 0);
                            ptsOut[u, v] = new Point3d(cp.Location.X, cp.Location.Y, cp00.Location.Z);
                        }
                    }
                }
            }

            return ptsOut;
        }

        private static bool AddMissingPointsLowestNeighborsBorder(Point3d?[,] pts, Point3d[,] raycastPts)
        {
            int countU = pts.GetLength(0);
            int countV = pts.GetLength(1);
            var modifications = new List<(int u, int v, Point3d pt)>();

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (pts[u, v].HasValue) continue;

                    var zs = new List<double>();
                    if (u - 1 >= 0 && pts[u - 1, v].HasValue) zs.Add(pts[u - 1, v].Value.Z);
                    if (u + 1 < countU && pts[u + 1, v].HasValue) zs.Add(pts[u + 1, v].Value.Z);
                    if (v - 1 >= 0 && pts[u, v - 1].HasValue) zs.Add(pts[u, v - 1].Value.Z);
                    if (v + 1 < countV && pts[u, v + 1].HasValue) zs.Add(pts[u, v + 1].Value.Z);

                    if (zs.Count > 0)
                    {
                        Point3d newPt = raycastPts[u, v];
                        newPt.Z = zs.Min();
                        modifications.Add((u, v, newPt));
                    }
                }
            }

            foreach (var mod in modifications)
            {
                pts[mod.u, mod.v] = mod.pt;
            }

            return modifications.Count > 0;
        }

        private static bool AddMissingPointsAlongBorder(Point3d?[,] pts, Point3d[,] raycastPts, bool bDiag, bool bLineExts)
        {
            int countU = pts.GetLength(0);
            int countV = pts.GetLength(1);
            var ptsCopy = (Point3d?[,])pts.Clone();
            bool modified = false;

            var dirs = new[]
            {
                (-1, 0, false), (1, 0, false), (0, -1, false), (0, 1, false),
                (-1, -1, true), (1, -1, true), (-1, 1, true), (1, 1, true)
            };

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (ptsCopy[u, v].HasValue) continue;

                    Line zLine = new Line(raycastPts[u, v], Vector3d.ZAxis);
                    var closestPts = new List<Point3d>();

                    foreach (var (du, dv, isDiag) in dirs)
                    {
                        if (isDiag && !bDiag) continue;

                        int nu = u + du, nv = v + dv;
                        if (nu >= 0 && nu < countU && nv >= 0 && nv < countV && ptsCopy[nu, nv].HasValue)
                        {
                            Point3d? ptFound = null;
                            int nnu = nu + du, nnv = nv + dv;

                            if (bLineExts && nnu >= 0 && nnu < countU && nnv >= 0 && nnv < countV && ptsCopy[nnu, nnv].HasValue)
                            {
                                Line lineThru = new Line(ptsCopy[nnu, nnv].Value, ptsCopy[nu, nv].Value);
                                lineThru.Length *= 2.0;
                                
                                if (Intersection.LineLine(zLine, lineThru, out double a, out double b, 0.0, false))
                                {
                                    ptFound = zLine.PointAt(a);
                                }
                            }
                            else
                            {
                                ptFound = zLine.ClosestPoint(ptsCopy[nu, nv].Value, false);
                            }

                            if (ptFound.HasValue) closestPts.Add(ptFound.Value);
                        }
                    }

                    if (closestPts.Count >= 1)
                    {
                        Point3d sum = Point3d.Origin;
                        foreach (var p in closestPts) sum += p;
                        
                        pts[u, v] = sum / closestPts.Count;
                        modified = true;
                    }
                }
            }

            return modified;
        }

        private static NurbsSurface FitIterTranslHighToLow9Pts(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance, bool bDebug, bool skirtBorders)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = ptsTarget.GetLength(0);
            int countV = ptsTarget.GetLength(1);
            double elevTol = 0.1 * Math.Min(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, fTolerance);

            var allTargets = new List<(int u, int v, double z)>();
            for (int u = 0; u < countU; u++)
                for (int v = 0; v < countV; v++)
                    if (ptsTarget[u, v].HasValue)
                        allTargets.Add((u, v, ptsTarget[u, v].Value.Z));

            allTargets = allTargets.OrderByDescending(t => t.z).ToList();

            var uvsInElevGroups = new List<List<(int u, int v)>>();
            var zsHighestPerElevGroup = new List<double>();
            
            double lastTolStart = double.PositiveInfinity;
            foreach (var target in allTargets)
            {
                if (Math.Abs(lastTolStart - target.z) > elevTol || uvsInElevGroups.Count == 0)
                {
                    uvsInElevGroups.Add(new List<(int u, int v)>());
                    zsHighestPerElevGroup.Add(target.z);
                    lastTolStart = target.z;
                }
                uvsInElevGroups.Last().Add((target.u, target.v));
            }

            var zsTargets = new double[countU, countV];
            var zsMinAdjustedPerNeighbors = new double[countU, countV];

            for (int u = 0; u < countU; u++)
                for (int v = 0; v < countV; v++)
                    if (ptsTarget[u, v].HasValue)
                        zsTargets[u, v] = zsMinAdjustedPerNeighbors[u, v] = ptsTarget[u, v].Value.Z;

            for (int iGroup = 0; iGroup < uvsInElevGroups.Count; iGroup++)
            {
                foreach (var (u, v) in uvsInElevGroups[iGroup])
                {
                    zsTargets[u, v] = zsHighestPerElevGroup[iGroup];
                    zsMinAdjustedPerNeighbors[u, v] = zsHighestPerElevGroup[iGroup];
                }
            }

            var uvsNeighborsPerElevGroup = new List<List<(int u, int v)>>();
            var uvsNeighborsFlat = new HashSet<(int u, int v)>();
            var dirs = new[] { (-1,-1), (-1,0), (-1,1), (0,-1), (0,1), (1,-1), (1,0), (1,1) };

            int borderLow = skirtBorders ? 2 : -1;
            int uHigh = skirtBorders ? countU - 3 : countU;
            int vHigh = skirtBorders ? countV - 3 : countV;

            for (int iGroup = 0; iGroup < uvsInElevGroups.Count; iGroup++)
            {
                CheckEscape();
                var currentGroupSet = new HashSet<(int u, int v)>(uvsInElevGroups[iGroup]);
                var neighbors = new List<(int u, int v)>();

                foreach (var (uT, vT) in uvsInElevGroups[iGroup])
                {
                    foreach (var (du, dv) in dirs)
                    {
                        int uN = uT + du, vN = vT + dv;
                        if (currentGroupSet.Contains((uN, vN))) continue;
                        if (uvsNeighborsFlat.Contains((uN, vN))) continue;
                        
                        if (uN > borderLow && uN < uHigh && vN > borderLow && vN < vHigh)
                        {
                            neighbors.Add((uN, vN));
                            uvsNeighborsFlat.Add((uN, vN));

                            if (zsMinAdjustedPerNeighbors[uN, vN] < zsTargets[uT, vT])
                                zsMinAdjustedPerNeighbors[uN, vN] = zsTargets[uT, vT];
                        }
                    }
                }
                uvsNeighborsPerElevGroup.Add(neighbors);
            }

            double zMax = HighestElevation(ptsTarget);

            if (uvsInElevGroups.Count > 0)
            {
                foreach (var (u, v) in uvsInElevGroups[0])
                {
                    ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                    cp.Z = zMax;
                    nsOut.Points.SetControlPoint(u, v, cp);
                }
                foreach (var (u, v) in uvsNeighborsPerElevGroup[0])
                {
                    ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                    cp.Z = zMax;
                    nsOut.Points.SetControlPoint(u, v, cp);
                }
            }

            var uvsNeighborsCumPrev = new HashSet<(int u, int v)>();

            for (int iGroup = 1; iGroup < uvsInElevGroups.Count; iGroup++)
            {
                var targetGroup = uvsInElevGroups[iGroup];
                var neighborsOfGroup = uvsNeighborsPerElevGroup[iGroup];
                
                foreach (var prevN in uvsNeighborsPerElevGroup[iGroup - 1]) 
                    uvsNeighborsCumPrev.Add(prevN);

                foreach (var (uT, vT) in targetGroup)
                {
                    if (uvsNeighborsCumPrev.Contains((uT, vT))) continue;
                    ControlPoint cp = nsOut.Points.GetControlPoint(uT, vT);
                    cp.Z = ptsTarget[uT, vT].Value.Z;
                    nsOut.Points.SetControlPoint(uT, vT, cp);
                }

                foreach (var (uN, vN) in neighborsOfGroup)
                {
                    if (uvsNeighborsPerElevGroup[0].Contains((uN, vN))) continue;
                    ControlPoint cp = nsOut.Points.GetControlPoint(uN, vN);
                    cp.Z = zsMinAdjustedPerNeighbors[uN, vN];
                    nsOut.Points.SetControlPoint(uN, vN, cp);
                }

                bool needSearch = false;
                foreach (var (uT, vT) in targetGroup)
                {
                    Point2d uv = nsOut.Points.GetGrevillePoint(uT, vT);
                    Point3d ptGreville = nsOut.PointAt(uv.X, uv.Y);
                    
                    if (ptGreville.Z + 0.001 * fTolerance < ptsTarget[uT, vT].Value.Z)
                    {
                        needSearch = true;
                        break;
                    }
                }

                if (needSearch)
                {
                    double fractionL = 0.0, fractionH = 1.0;
                    while (true)
                    {
                        CheckEscape();
                        double fractionM = 0.5 * fractionL + 0.5 * fractionH;

                        foreach (var (uN, vN) in neighborsOfGroup)
                        {
                            ControlPoint cp = nsOut.Points.GetControlPoint(uN, vN);
                            double zLowest = zsMinAdjustedPerNeighbors[uN, vN];

                            double zHighest = double.NegativeInfinity;
                            foreach (var (du, dv) in dirs)
                            {
                                int uNN = uN + du, vNN = vN + dv;
                                if (uNN >= 0 && uNN < countU && vNN >= 0 && vNN < countV)
                                    if (zsTargets[uNN, vNN] > zHighest) zHighest = zsTargets[uNN, vNN];
                            }

                            cp.Z = zLowest + (zHighest - zLowest) * fractionM;
                            nsOut.Points.SetControlPoint(uN, vN, cp);
                        }

                        bool allOnOrAbove = true;
                        foreach (var (uT, vT) in targetGroup)
                        {
                            Point2d uv = nsOut.Points.GetGrevillePoint(uT, vT);
                            if (nsOut.PointAt(uv.X, uv.Y).Z < ptsTarget[uT, vT].Value.Z)
                            {
                                allOnOrAbove = false;
                                break;
                            }
                        }

                        if (allOnOrAbove) fractionH = fractionM;
                        else fractionL = fractionM;

                        if (Math.Abs(fractionH - fractionL) <= 0.001) break;
                    }

                    foreach (var (uN, vN) in neighborsOfGroup)
                    {
                        zsMinAdjustedPerNeighbors[uN, vN] = nsOut.Points.GetControlPoint(uN, vN).Location.Z;
                    }
                }
            }

            var uvsDoneFlat = new HashSet<(int, int)>(uvsInElevGroups[0]);
            foreach (var group in uvsNeighborsPerElevGroup)
                foreach (var pt in group)
                    uvsDoneFlat.Add(pt);

            int startLimit = skirtBorders ? 3 : 0;
            int endLimitU = skirtBorders ? countU - 3 : countU;
            int endLimitV = skirtBorders ? countV - 3 : countV;

            for (int u = startLimit; u < endLimitU; u++)
            {
                for (int v = startLimit; v < endLimitV; v++)
                {
                    if (!uvsDoneFlat.Contains((u, v)) && ptsTarget[u, v].HasValue)
                    {
                        ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                        cp.Z = ptsTarget[u, v].Value.Z;
                        nsOut.Points.SetControlPoint(u, v, cp);
                    }
                }
            }

            return nsOut;
        }
    }
}