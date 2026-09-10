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

        // Helper method to mimic sc.escape_test()
        public static void CheckEscape()
        {
            if (_escapeTracker != null && _escapeTracker.IsCanceled)
            {
                throw new OperationCanceledException("User canceled command.");
            }
        }

        public static Result Execute(RhinoDoc doc, ObjRef[] targetRefs, ObjRef startingSrfRef)
        {
            using (_escapeTracker = new EscapeTracker())
            {
                try
                {
                
                    // 1. Extract Target Geometries
                    var targetBreps = new List<Brep>();
                    var targetMeshes = new List<Mesh>();

                    foreach (var r in targetRefs)
                    {
                        var geom = r.Geometry();
                        if (geom is Brep b) targetBreps.Add(b);
                        else if (geom is Mesh m) targetMeshes.Add(m);
                    }

                    // 2. Handle CPlane and Transformations
                    var view = doc.Views.ActiveView;
                    if (view == null) return Result.Failure;
                    
                    Plane cPlane = view.ActiveViewport.ConstructionPlane();
                    if (DrapeOptions.FlipCPlane)
                    {
                        cPlane.Flip();
                    }

                    Transform xformToW = Transform.Identity;
                    Transform xformFromW = Transform.Identity;

                    // If not WorldXY, we map everything to WorldXY for the raycasting calculations
                    if (!cPlane.Equals(Plane.WorldXY))
                    {
                        xformToW = Transform.PlaneToPlane(cPlane, Plane.WorldXY);
                        xformFromW = Transform.PlaneToPlane(Plane.WorldXY, cPlane);

                        foreach (var b in targetBreps) b.Transform(xformToW);
                        foreach (var m in targetMeshes) m.Transform(xformToW);
                    }

                    // 3. Obtain Starting Surface
                    NurbsSurface nsWIP;
                    if (startingSrfRef == null)
                    {
                        // Combine geometries for bounding box calculation
                        var allGeom = new List<GeometryBase>();
                        allGeom.AddRange(targetBreps);
                        allGeom.AddRange(targetMeshes);

                        nsWIP = CreateStartingSurface(allGeom, DrapeOptions.SpanSpacing, DrapeOptions.SpansBeyondEachSide);
                    }
                    else
                    {
                        Surface srf = startingSrfRef.Surface();
                        if (srf == null && startingSrfRef.Brep()?.Faces.Count == 1)
                            srf = startingSrfRef.Brep().Faces[0].UnderlyingSurface();
                        
                        nsWIP = srf.ToNurbsSurface();
                        if (!xformToW.IsIdentity) nsWIP.Transform(xformToW);
                    }

                    // 4. Extract Greville Points & Project to Targets
                    Point3d[,] grevillePts = GetGrevillePoints(nsWIP);
                    Point3d?[,] targetPts = ProjectPtsToObjs(grevillePts, targetBreps, targetMeshes, doc.ModelAbsoluteTolerance);

                    if (targetPts == null)
                    {
                        RhinoApp.WriteLine("Projected points were not obtained.");
                        return Result.Failure;
                    }

                    // 5. Flatten WIP Surface to Highest Target Elevation
                    double zMax = HighestElevation(targetPts);
                    for (int u = 0; u < nsWIP.Points.CountU; u++)
                    {
                        for (int v = 0; v < nsWIP.Points.CountV; v++)
                        {
                            ControlPoint cp = nsWIP.Points.GetControlPoint(u, v);
                            nsWIP.Points.SetPoint(u, v, cp.Location.X, cp.Location.Y, zMax);
                        }
                    }

                    // 6. Handle Missing Points (Raycast Misses)
                    if (HasMissingPoints(targetPts))
                    {
                        // TODO: Delegate to missing points solver (implemented in Part 2)
                        targetPts = ResolveMissingPoints(targetPts, grevillePts, nsWIP, DrapeOptions.TargetMisses);
                    }

                    // 7. Iterative Fitting Routine
                    NurbsSurface nsOut;
                    if (targetBreps.Count == 1 && targetMeshes.Count == 0 && targetBreps[0].Faces.Count == 1)
                    {
                        // TODO: Single surface simplified fit (implemented in Part 2)
                        nsOut = FitIterTranslIndivPts(targetPts, nsWIP, DrapeOptions.Tolerance);
                    }
                    else
                    {
                        // TODO: Full high-to-low 9-pt fit (implemented in Part 2)
                        nsOut = FitIterTranslHighToLow9Pts(targetPts, nsWIP, DrapeOptions.Tolerance, DrapeOptions.Debug);
                    }

                    // 8. Output and Cleanup
                    if (!xformFromW.IsIdentity) nsOut.Transform(xformFromW);

                    if (DrapeOptions.UserProvidesStartingSrf && DrapeOptions.DeleteStartingSrf)
                    {
                        doc.Objects.Delete(startingSrfRef.ObjectId, true);
                    }

                    Guid gOut = doc.Objects.AddSurface(nsOut);
                    nsOut.Dispose();

                    if (gOut == Guid.Empty) return Result.Failure;

                    doc.Views.Redraw();
                    return Result.Success;
                }
                catch (OperationCanceledException ex)
                {
                    // This catches the escape gracefully from ANY nested method
                    RhinoApp.WriteLine(ex.Message);
                    return Result.Cancel;
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
                        // Multiple hits: find the one with the highest Z elevation
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

        // -------------------------------------------------------------------------
        // MISSING POINTS RESOLUTION
        // -------------------------------------------------------------------------
        private static Point3d?[,] ResolveMissingPoints(Point3d?[,] targetPts, Point3d[,] grevillePts, NurbsSurface nsWIP, int iTargetMisses)
        {
            int countU = targetPts.GetLength(0);
            int countV = targetPts.GetLength(1);
            var ptsOut = (Point3d?[,])targetPts.Clone();

            if (iTargetMisses == 0) // FixToStartingSrf
            {
                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        if (!ptsOut[u, v].HasValue)
                        {
                            Point2d uv = nsWIP.Points.GetGrevillePoint(u, v);
                            ptsOut[u, v] = nsWIP.PointAt(uv.X, uv.Y);
                        }
                    }
                }
            }
            else if (iTargetMisses == 1) // UseLowestNeighborHits
            {
                bool pointsAdded;
                do
                {
                    CheckEscape();
                    pointsAdded = AddMissingPointsLowestNeighborsBorder(ptsOut, grevillePts);
                } while (pointsAdded);
            }
            else if (iTargetMisses == 2) // LinearlyExtrapolateFromHits
            {
                // The Python script hardcoded bLineExts1=True, bLineExts2=False. 
                // Using the exact configuration from the Python tuple loop:
                bool bLineExts2 = false; 

                while (HasMissingPoints(ptsOut))
                {
                    CheckEscape();
                    bool modified = AddMissingPointsAlongBorder(ptsOut, grevillePts, false, bLineExts2);
                    if (!modified) break; // Break if no more changes can be made to avoid infinite loops
                }

                // Fallback for any remaining missing points
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

        private static bool AddMissingPointsLowestNeighborsBorder(Point3d?[,] pts, Point3d[,] grevillePts)
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
                        Point3d newPt = grevillePts[u, v];
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

        private static bool AddMissingPointsAlongBorder(Point3d?[,] pts, Point3d[,] grevillePts, bool bDiag, bool bLineExts)
        {
            int countU = pts.GetLength(0);
            int countV = pts.GetLength(1);
            var ptsCopy = (Point3d?[,])pts.Clone();
            bool modified = false;

            // Direction vectors: West, East, South, North, SW, SE, NW, NE (and whether they are diagonal)
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

                    Line zLine = new Line(grevillePts[u, v], Vector3d.ZAxis);
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

                    if (closestPts.Count >= 1) // Minimum neighbor count = 1 based on script
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

        // -------------------------------------------------------------------------
        // SURFACE FITTING: ITERATIVE HIGH TO LOW 
        // -------------------------------------------------------------------------
        private static NurbsSurface FitIterTranslHighToLow9Pts(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance, bool bDebug)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = ptsTarget.GetLength(0);
            int countV = ptsTarget.GetLength(1);
            double elevTol = 0.1 * Math.Min(RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, fTolerance);

            // 1. Sort and group targets by elevation
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

            // 2. Adjust target Zs array (highest in group) and setup neighbor limits
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

            // 3. Find neighbors per group
            var uvsNeighborsPerElevGroup = new List<List<(int u, int v)>>();
            var uvsNeighborsFlat = new HashSet<(int u, int v)>();
            var dirs = new[] { (-1,-1), (-1,0), (-1,1), (0,-1), (0,1), (1,-1), (1,0), (1,1) };

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
                        
                        // Enforce border restriction (2 < uN < CountU - 3)
                        if (uN > 2 && uN < countU - 3 && vN > 2 && vN < countV - 3)
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

            // 4. Initial Height Set
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

            // 5. Iterate fitting levels
            var uvsNeighborsCumPrev = new HashSet<(int u, int v)>();

            for (int iGroup = 1; iGroup < uvsInElevGroups.Count; iGroup++)
            {
                var targetGroup = uvsInElevGroups[iGroup];
                var neighborsOfGroup = uvsNeighborsPerElevGroup[iGroup];
                
                foreach (var prevN in uvsNeighborsPerElevGroup[iGroup - 1]) 
                    uvsNeighborsCumPrev.Add(prevN);

                // Set targets CP locations
                foreach (var (uT, vT) in targetGroup)
                {
                    if (uvsNeighborsCumPrev.Contains((uT, vT))) continue;
                    ControlPoint cp = nsOut.Points.GetControlPoint(uT, vT);
                    cp.Z = ptsTarget[uT, vT].Value.Z;
                    nsOut.Points.SetControlPoint(uT, vT, cp);
                }

                // Translate neighbors as low as possible
                foreach (var (uN, vN) in neighborsOfGroup)
                {
                    if (uvsNeighborsPerElevGroup[0].Contains((uN, vN))) continue;
                    ControlPoint cp = nsOut.Points.GetControlPoint(uN, vN);
                    cp.Z = zsMinAdjustedPerNeighbors[uN, vN];
                    nsOut.Points.SetControlPoint(uN, vN, cp);
                }

                // Binary search height adjustment for this level
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

                            // Highest elevation of neighbors
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

                    // Update zs_Min_Adjusted
                    foreach (var (uN, vN) in neighborsOfGroup)
                    {
                        zsMinAdjustedPerNeighbors[uN, vN] = nsOut.Points.GetControlPoint(uN, vN).Location.Z;
                    }
                }
            }

            // 6. Position points not within 3 from border nor translated
            var uvsDoneFlat = new HashSet<(int, int)>(uvsInElevGroups[0]);
            foreach (var group in uvsNeighborsPerElevGroup)
                foreach (var pt in group)
                    uvsDoneFlat.Add(pt);

            for (int u = 3; u < countU - 3; u++)
            {
                for (int v = 3; v < countV - 3; v++)
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

        // -------------------------------------------------------------------------
        // SURFACE FITTING: INDIVIDUAL POINTS (SINGLE SURFACE FALLBACK)
        // -------------------------------------------------------------------------
        private static NurbsSurface FitIterTranslIndivPts(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (!ptsTarget[u, v].HasValue) continue;

                    Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                    Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                    Vector3d vect = ptsTarget[u, v].Value - ptGr;

                    if (vect.Length > fTolerance)
                    {
                        ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                        cp.Location = ptsTarget[u, v].Value;
                        nsOut.Points.SetControlPoint(u, v, cp);
                    }
                }
            }

            for (int i = 0; i < 200; i++)
            {
                bool bTransPts = false;
                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        if (!ptsTarget[u, v].HasValue) continue;

                        Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                        Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                        Vector3d vect = ptsTarget[u, v].Value - ptGr;

                        if (vect.Length > fTolerance)
                        {
                            ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                            cp.Location += vect;
                            nsOut.Points.SetControlPoint(u, v, cp);
                            bTransPts = true;
                        }
                    }
                }

                if (!bTransPts)
                {
                    RhinoApp.WriteLine($"{i + 1} iterations for Grevilles to lie on target(s) within {fTolerance}.");
                    return nsOut;
                }
            }

            RhinoApp.WriteLine($"After 200 iterations, Grevilles still do not lie on target(s) within {fTolerance}.");
            return nsOut;
        }
    }
}