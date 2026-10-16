using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace CADacombs.Core
{
    public static class IntersectionMath
    {
        // ---------------------------------------------------------
        // ORIGINAL DRAPE METHOD - FULLY RESTORED
        // ---------------------------------------------------------
        public static Point3d?[,] ProjectPtsToObjs(Point3d[,] ptsIn, List<Brep> breps, List<Mesh> meshes, double docTol, bool flatten, Func<bool> checkCancel = null)
        {
            int countU = ptsIn.GetLength(0);
            int countV = ptsIn.GetLength(1);
            var ptsOut = new Point3d?[countU, countV];
            double rayTol = 0.1 * docTol;

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                    
                    var rawPts = new List<Point3d>();
                    Point3d pt = ptsIn[u, v];

                    if (meshes.Count > 0)
                    {
                        var meshHits = Intersection.ProjectPointsToMeshes(meshes, new[] { pt }, Vector3d.ZAxis, rayTol);
                        if (meshHits != null) rawPts.AddRange(meshHits);
                    }

                    if (breps.Count > 0)
                    {
                        var brepHits = Intersection.ProjectPointsToBreps(breps, new[] { pt }, Vector3d.ZAxis, rayTol);
                        if (brepHits != null) rawPts.AddRange(brepHits);
                    }

                    var projectedPts = new List<Point3d>();
                    foreach (var hit in rawPts)
                    {
                        if (!flatten && hit.Z > pt.Z + rayTol) continue;
                        projectedPts.Add(hit);
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

        // ---------------------------------------------------------
        // NEW METHOD: EXCLUSIVELY FOR FITSRFTOTARGET (RAYCAST)
        // ---------------------------------------------------------
        public static Point3d?[,] RaycastPtsToObjs(Point3d[,] ptsIn, List<Brep> breps, List<Mesh> meshes, Vector3d rayDir, Func<bool> checkCancel = null)
        {
            int countU = ptsIn.GetLength(0);
            int countV = ptsIn.GetLength(1);
            var ptsOut = new Point3d?[countU, countV];

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                    
                    var rawPts = new List<Point3d>();
                    Point3d pt = ptsIn[u, v];

                    if (meshes.Count > 0)
                    {
                        var meshHits = Intersection.ProjectPointsToMeshes(meshes, new[] { pt }, rayDir, 0.01);
                        if (meshHits != null) rawPts.AddRange(meshHits);
                    }

                    if (breps.Count > 0)
                    {
                        var brepHits = Intersection.ProjectPointsToBreps(breps, new[] { pt }, rayDir, 0.01);
                        if (brepHits != null) rawPts.AddRange(brepHits);
                    }

                    var validHits = new List<Point3d>();
                    foreach (var h in rawPts)
                    {
                        if ((h.Z - pt.Z) * rayDir.Z >= -1e-5)
                        {
                            validHits.Add(h);
                        }
                    }

                    if (validHits.Count == 0)
                    {
                        ptsOut[u, v] = null;
                    }
                    else
                    {
                        ptsOut[u, v] = validHits.OrderBy(h => pt.DistanceTo(h)).First();
                    }
                }
            }
            return ptsOut;
        }

        // ---------------------------------------------------------
        // NEW METHOD: EXCLUSIVELY FOR FITSRFTOTARGET (PULL)
        // ---------------------------------------------------------
        public static Point3d?[,] PullPtsToObjs(Point3d[,] ptsIn, List<Brep> breps, List<Mesh> meshes, Func<bool> checkCancel = null)
        {
            int countU = ptsIn.GetLength(0);
            int countV = ptsIn.GetLength(1);
            var ptsOut = new Point3d?[countU, countV];

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                    
                    Point3d pt = ptsIn[u, v];
                    Point3d? closestPt = null;
                    double minDist = double.MaxValue;

                    foreach (var mesh in meshes)
                    {
                        var mPt = mesh.ClosestPoint(pt);
                        double dist = pt.DistanceTo(mPt);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestPt = mPt;
                        }
                    }

                    foreach (var brep in breps)
                    {
                        var bPt = brep.ClosestPoint(pt);
                        double dist = pt.DistanceTo(bPt);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestPt = bPt;
                        }
                    }

                    ptsOut[u, v] = closestPt;
                }
            }
            return ptsOut;
        }
    }
}