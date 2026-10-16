using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using CADacombs.Core.Reporting;

namespace CADacombs.Core
{
    public class ProjectionResult
    {
        public NurbsSurface Surface { get; set; }
        public int Iterations { get; set; }
        public double ElapsedSeconds { get; set; }
        public double MaxDeviation { get; set; }
        public bool Converged { get; set; }
        public string Message { get; set; }
    }

    public static class ProjectionMath
    {
        public static ProjectionResult FitDirectGreville(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 0;
            double maxDev = 0.0;
            bool converged = false;

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

            while (sw.Elapsed.TotalSeconds < timeoutSecs)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();

                iterations++;
                bool bTransPts = false;
                maxDev = 0.0;

                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        if (sw.Elapsed.TotalSeconds > timeoutSecs) break;

                        if (!ptsTarget[u, v].HasValue) continue;

                        Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                        Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                        Vector3d vect = ptsTarget[u, v].Value - ptGr;
                        double dist = vect.Length;

                        if (dist > maxDev) maxDev = dist;

                        if (dist > fTolerance)
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
                    converged = true;
                    break;
                }
            }
            sw.Stop();

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string maxDevStr = FormatUtils.FormatDistance(maxDev, prec);

            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max. Greville point deviation from target(s): {maxDevStr}";
            if (!converged && maxDev > fTolerance)
            {
                msg += " <- Out of tolerance";
            }

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = converged,
                Message = msg
            };
        }

        public static ProjectionResult FitDirectControlPoints(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (ptsTarget[u, v].HasValue)
                    {
                        ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                        cp.Location = ptsTarget[u, v].Value;
                        nsOut.Points.SetControlPoint(u, v, cp);
                    }
                }
            }
            sw.Stop();

            double maxDev = 0.0;
            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (ptsTarget[u, v].HasValue)
                    {
                        Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                        Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                        double dist = ptGr.DistanceTo(ptsTarget[u, v].Value);
                        if (dist > maxDev) maxDev = dist;
                    }
                }
            }

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string maxDevStr = FormatUtils.FormatDistance(maxDev, prec);

            string msg = $"1 iterations, {sw.Elapsed.TotalSeconds:F3}s, Max. Greville point deviation from target(s): {maxDevStr}";
            if (maxDev > fTolerance)
            {
                msg += " <- Out of tolerance";
            }

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = 1,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = msg 
            };
        }

        public static ProjectionResult FitWeightedRelaxed(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int iterations = 0;
            double maxDev = double.MaxValue;
            
            double domU = nsOut.Domain(0).Length;
            double domV = nsOut.Domain(1).Length;
            double minDom = Math.Min(domU, domV);
            
            int sampleCountU = nsOut.Points.CountU * 3;
            int sampleCountV = nsOut.Points.CountV * 3;
            double minSpacing = Math.Max(domU / sampleCountU, domV / sampleCountV) * 1.5;

            double R = minDom * (FitSrfToTargetOptions.PercentOfDomain / 100.0);
            if (R < minSpacing) R = minSpacing; 
            
            double L = 0.01 * R; 
            double twoSigmaSq = 2.0 * R * R; 
            
            double damping = 0.5;
            int weightCurve = FitSrfToTargetOptions.WeightCurve;
            bool isZAxis = FitSrfToTargetOptions.MappingDirection == 1;
            Vector3d rayDir = FitSrfToTargetOptions.FlipDirection ? Vector3d.ZAxis : -Vector3d.ZAxis; 

            // Calculate overall bounding box to set an absolute ceiling/floor for pure raycasting
            BoundingBox targetBox = BoundingBox.Empty;
            if (isZAxis)
            {
                foreach (var m in meshes) targetBox.Union(m.GetBoundingBox(true));
                foreach (var b in breps) targetBox.Union(b.GetBoundingBox(true));
            }

            while (sw.Elapsed.TotalSeconds < timeoutSecs && maxDev > fTolerance)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                iterations++;
                maxDev = 0.0;

                var sampleUVs = new List<Point2d>();
                var sampleVectors = new List<Vector3d>();

                for (int i = 0; i < sampleCountU; i++)
                {
                    if (sw.Elapsed.TotalSeconds > timeoutSecs) break;
                    
                    for (int j = 0; j < sampleCountV; j++)
                    {
                        double u = nsOut.Domain(0).ParameterAt((double)i / (sampleCountU - 1));
                        double v = nsOut.Domain(1).ParameterAt((double)j / (sampleCountV - 1));
                        Point3d sPt = nsOut.PointAt(u, v);
                        Point3d? closestPt = null;

                        if (isZAxis)
                        {
                            // Guarantee origin is safely outside the targets to prevent passing through geometries
                            double safeZ = (rayDir.Z < 0) ? Math.Max(sPt.Z + 1.0, targetBox.Max.Z + 10.0) : Math.Min(sPt.Z - 1.0, targetBox.Min.Z - 10.0);
                            Point3d safePt = new Point3d(sPt.X, sPt.Y, safeZ);

                            var hits = new List<Point3d>();
                            if (breps.Count > 0)
                            {
                                var bHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBreps(breps, new[] { safePt }, rayDir, 0.01);
                                if (bHits != null) hits.AddRange(bHits.Where(h => (h.Z - safePt.Z) * rayDir.Z >= -1e-5));
                            }
                            if (meshes.Count > 0)
                            {
                                var mHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToMeshes(meshes, new[] { safePt }, rayDir, 0.01);
                                if (mHits != null) hits.AddRange(mHits.Where(h => (h.Z - safePt.Z) * rayDir.Z >= -1e-5));
                            }
                            if (hits.Count > 0)
                            {
                                closestPt = hits.OrderBy(h => safePt.DistanceTo(h)).First();
                            }
                        }
                        else 
                        {
                            double minDist = double.MaxValue;
                            foreach (var mesh in meshes)
                            {
                                var mPt = mesh.ClosestPoint(sPt);
                                double dist = sPt.DistanceTo(mPt);
                                if (dist < minDist) { minDist = dist; closestPt = mPt; }
                            }
                            foreach (var brep in breps)
                            {
                                var bPt = brep.ClosestPoint(sPt);
                                double dist = sPt.DistanceTo(bPt);
                                if (dist < minDist) { minDist = dist; closestPt = bPt; }
                            }
                        }

                        if (closestPt.HasValue)
                        {
                            Vector3d vec = closestPt.Value - sPt;
                            if (vec.Length > maxDev) maxDev = vec.Length;
                            
                            sampleUVs.Add(new Point2d(u, v));
                            sampleVectors.Add(vec);
                        }
                    }
                }

                if (sampleVectors.Count == 0)
                {
                    if (iterations == 1) iterations = 0; // Flag as total miss
                    break;
                }

                var newCpLocations = new Point3d[nsOut.Points.CountU, nsOut.Points.CountV];

                for (int cu = 0; cu < nsOut.Points.CountU; cu++)
                {
                    for (int cv = 0; cv < nsOut.Points.CountV; cv++)
                    {
                        Point2d cpUV = nsOut.Points.GetGrevillePoint(cu, cv);
                        ControlPoint cp = nsOut.Points.GetControlPoint(cu, cv);
                        
                        Vector3d sumWVec = Vector3d.Zero;
                        double sumW = 0.0;

                        for (int k = 0; k < sampleUVs.Count; k++)
                        {
                            double distSq = Math.Pow(sampleUVs[k].X - cpUV.X, 2) + Math.Pow(sampleUVs[k].Y - cpUV.Y, 2);
                            double d = Math.Sqrt(distSq);
                            double weight = 0.0;

                            if (weightCurve == 2) 
                            {
                                weight = Math.Exp(-distSq / twoSigmaSq);
                            }
                            else if (d <= L) weight = 1.0;
                            else if (d < R)
                            {
                                if (weightCurve == 0) 
                                    weight = 1.0 - (d - L) / (R - L);
                                else if (weightCurve == 1) 
                                    weight = 1.0 - (1.0 - L / d) / (1.0 - L / R);
                            }

                            if (weight > 0)
                            {
                                sumWVec += sampleVectors[k] * weight;
                                sumW += weight;
                            }
                        }

                        if (sumW > 1e-6)
                        {
                            Vector3d avgVec = sumWVec / sumW;
                            newCpLocations[cu, cv] = cp.Location + (avgVec * damping);
                        }
                        else
                        {
                            newCpLocations[cu, cv] = cp.Location;
                        }
                    }
                }

                for (int cu = 0; cu < nsOut.Points.CountU; cu++)
                {
                    for (int cv = 0; cv < nsOut.Points.CountV; cv++)
                    {
                        ControlPoint cp = nsOut.Points.GetControlPoint(cu, cv);
                        cp.Location = newCpLocations[cu, cv];
                        nsOut.Points.SetControlPoint(cu, cv, cp);
                    }
                }
            }

            sw.Stop();
            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max. deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";
            if (maxDev > fTolerance && iterations > 0) msg += " <- Out of tolerance";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = iterations == 0 ? null : msg
            };
        }

        public static ProjectionResult FitPythonLegacy(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsWIP = nsIn.Duplicate() as NurbsSurface;
            
            nsWIP.SetDomain(0, new Interval(0.0, 1.0));
            nsWIP.SetDomain(1, new Interval(0.0, 1.0));

            int countU = nsWIP.Points.CountU;
            int countV = nsWIP.Points.CountV;
            int totalCps = countU * countV;

            double sLen = FitSrfToTargetOptions.SamplingLength;
            var ptsTarget = new List<Point3d>();
            foreach (var mesh in meshes) 
            {
                ptsTarget.AddRange(mesh.Vertices.ToPoint3dArray());
            }
            foreach (var brep in breps)
            {
                var mp = new MeshingParameters(sLen, sLen) { SimplePlanes = false }; 
                var bMeshes = Mesh.CreateFromBrep(brep, mp);
                if (bMeshes != null)
                {
                    foreach (var bm in bMeshes)
                    {
                        bm.Vertices.CombineIdentical(true, true);
                        ptsTarget.AddRange(bm.Vertices.ToPoint3dArray());
                    }
                }
            }

            // Rapid point decimation using native Rhino functions to prevent hangs
            ptsTarget = Point3d.CullDuplicates(ptsTarget, 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance).ToList();
            if (ptsTarget.Count > 10000)
            {
                double cullTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance * 10;
                while (ptsTarget.Count > 10000 && cullTol < 1000.0)
                {
                    ptsTarget = Point3d.CullDuplicates(ptsTarget, cullTol).ToList();
                    cullTol *= 2.0;
                }
            }

            if (ptsTarget.Count == 0)
            {
                return new ProjectionResult { Surface = nsWIP, Converged = false, Iterations = 0 };
            }

            int samplingMode = FitSrfToTargetOptions.SamplingMode;
            int weightCurve = FitSrfToTargetOptions.WeightCurve;
            bool isNormalTrans = FitSrfToTargetOptions.MappingDirection == 0; 
            
            // Reversing the ray direction for Python Legacy because it projects FROM the target UP to the surface
            Vector3d rayDir = FitSrfToTargetOptions.FlipDirection ? Vector3d.ZAxis : -Vector3d.ZAxis;
            Vector3d reverseRayDir = -rayDir;
            
            double domLenU = nsWIP.Domain(0).Length;
            double domLenV = nsWIP.Domain(1).Length;
            double pct = FitSrfToTargetOptions.PercentOfDomain / 100.0;
            double uvDistTol_Upr = Math.Min(domLenU * pct, domLenV * pct);
            double uvDistTol_Lwr = 0.01 * uvDistTol_Upr;
            
            bool[] bFreezeCps = new bool[totalCps];
            int iterations = 0;
            double maxDev = double.MaxValue;
            double previousMaxDev = double.MaxValue;

            Brep wipBrep = null;

            while (sw.Elapsed.TotalSeconds < timeoutSecs)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                iterations++;

                var uvsTargetProjd = new List<Point2d>();
                var ptsProjdHit = new List<Point3d>();
                var dists = new List<double>();
                var validTargets = new List<Point3d>();

                if (!isNormalTrans) wipBrep = nsWIP.ToBrep();

                foreach (var pt0 in ptsTarget)
                {
                    if (sw.Elapsed.TotalSeconds > timeoutSecs) break;

                    if (isNormalTrans) 
                    {
                        if (nsWIP.ClosestPoint(pt0, out double u, out double v))
                        {
                            Point3d hit = nsWIP.PointAt(u, v);
                            Vector3d normal = nsWIP.NormalAt(u, v);
                            Vector3d diff = pt0 - hit;
                            
                            double dist = diff.Length;
                            if (Vector3d.VectorAngle(normal, diff) > Math.PI / 4.0) dist = -dist; 

                            uvsTargetProjd.Add(new Point2d(u, v));
                            ptsProjdHit.Add(hit);
                            dists.Add(dist);
                            validTargets.Add(pt0);
                        }
                    }
                    else 
                    {
                        var rayHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBreps(new[] { wipBrep }, new[] { pt0 }, reverseRayDir, 0.01);
                        var validHits = rayHits?.Where(h => (h.Z - pt0.Z) * reverseRayDir.Z >= -1e-5).ToList();
                        
                        if (validHits != null && validHits.Count > 0)
                        {
                            var hit = validHits.OrderBy(h => pt0.DistanceTo(h)).First();
                            nsWIP.ClosestPoint(hit, out double u, out double v);
                            double dist = hit.DistanceTo(pt0);
                            Vector3d diff = pt0 - hit;
                            if (Vector3d.VectorAngle(nsWIP.NormalAt(u, v), diff) > Math.PI / 4.0) dist = -dist;

                            uvsTargetProjd.Add(new Point2d(u, v));
                            ptsProjdHit.Add(hit);
                            dists.Add(dist);
                            validTargets.Add(pt0);
                        }
                    }
                }

                if (validTargets.Count == 0)
                {
                    if (iterations == 1) iterations = 0; // Flag as total miss
                    break;
                }
                
                maxDev = 0.0;
                foreach (var d in dists) { if (Math.Abs(d) > maxDev) maxDev = Math.Abs(d); }
                
                if (maxDev <= fTolerance || (iterations > 1 && Math.Abs(previousMaxDev - maxDev) < 0.1 * fTolerance)) break;

                var uvsGreville = new List<Point2d>();
                var vectsAtGrev = new List<Vector3d>();
                var ptsCpNs0 = new List<Point3d>();

                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        uvsGreville.Add(nsWIP.Points.GetGrevillePoint(u, v));
                        ptsCpNs0.Add(nsWIP.Points.GetControlPoint(u, v).Location);
                        
                        Vector3d normal = nsWIP.NormalAt(uvsGreville.Last().X, uvsGreville.Last().Y);
                        if (!isNormalTrans)
                        {
                            normal = Vector3d.VectorAngle(normal, rayDir) < Math.PI / 2.0 ? rayDir : -rayDir;
                        }
                        vectsAtGrev.Add(normal);
                    }
                }

                var magnitudes = new double[totalCps];

                for (int iG = 0; iG < totalCps; iG++)
                {
                    if (bFreezeCps[iG]) continue;

                    var weightsTsPerG = new List<double>();
                    var distsWeightedTsPerG = new List<double>();
                    double nearestDist = double.MaxValue;

                    for (int iT = 0; iT < validTargets.Count; iT++)
                    {
                        double distUV = uvsGreville[iG].DistanceTo(uvsTargetProjd[iT]);
                        if (distUV < nearestDist) nearestDist = distUV;

                        double w = 0.0;
                        if (distUV <= uvDistTol_Lwr) w = 1.0;
                        else if (distUV < uvDistTol_Upr)
                        {
                            if (weightCurve == 0) 
                                w = 1.0 - (distUV - uvDistTol_Lwr) / (uvDistTol_Upr - uvDistTol_Lwr);
                            else if (weightCurve == 1)
                                w = 1.0 - (1.0 - uvDistTol_Lwr / distUV) / (1.0 - uvDistTol_Lwr / uvDistTol_Upr);
                            else if (weightCurve == 2)
                                w = Math.Exp(-(distUV * distUV) / (2.0 * uvDistTol_Upr * uvDistTol_Upr));
                        }

                        weightsTsPerG.Add(w);
                        distsWeightedTsPerG.Add(w * dists[iT]);
                    }

                    double distWeightedNormalized = 0.0;

                    if (samplingMode == 0) 
                    {
                        int countPerf = 0;
                        int idxPerf = -1;
                        for (int k = 0; k < weightsTsPerG.Count; k++)
                        {
                            if (Math.Abs(weightsTsPerG[k] - 1.0) < 1e-6)
                            {
                                countPerf++;
                                idxPerf = k;
                            }
                        }

                        if (countPerf == 1)
                        {
                            distWeightedNormalized = distsWeightedTsPerG[idxPerf];
                        }
                        else
                        {
                            double sumW = 0.0;
                            double sumD = 0.0;
                            foreach (var w in weightsTsPerG) sumW += w;
                            foreach (var d in distsWeightedTsPerG) sumD += d;
                            if (sumW > 0) distWeightedNormalized = sumD / sumW;
                        }
                    }
                    else 
                    {
                        double wNearest = 0.0;
                        if (nearestDist <= uvDistTol_Lwr) wNearest = 1.0;
                        else if (nearestDist < uvDistTol_Upr)
                        {
                            if (weightCurve == 0) wNearest = 1.0 - (nearestDist - uvDistTol_Lwr) / (uvDistTol_Upr - uvDistTol_Lwr);
                            else if (weightCurve == 1) wNearest = 1.0 - (1.0 - uvDistTol_Lwr / nearestDist) / (1.0 - uvDistTol_Lwr / uvDistTol_Upr);
                            else if (weightCurve == 2) wNearest = Math.Exp(-(nearestDist * nearestDist) / (2.0 * uvDistTol_Upr * uvDistTol_Upr));
                        }

                        if (wNearest > 0)
                        {
                            var minDists = new List<double>();
                            for (int iT = 0; iT < validTargets.Count; iT++)
                            {
                                if (Math.Abs(uvsGreville[iG].DistanceTo(uvsTargetProjd[iT]) - nearestDist) < 1e-6)
                                {
                                    minDists.Add(wNearest * dists[iT]);
                                }
                            }
                            if (minDists.Count > 0)
                            {
                                double sD = 0;
                                foreach (var md in minDists) sD += md;
                                distWeightedNormalized = sD / minDists.Count;
                            }
                        }
                    }

                    magnitudes[iG] = distWeightedNormalized; 
                }

                for (int iG = 0; iG < totalCps; iG++)
                {
                    for (int iT = 0; iT < validTargets.Count; iT++)
                    {
                        if (Math.Abs(uvsGreville[iG].X - uvsTargetProjd[iT].X) < 1e-6 &&
                            Math.Abs(uvsGreville[iG].Y - uvsTargetProjd[iT].Y) < 1e-6 &&
                            Math.Abs(dists[iT]) < 1e-6)
                        {
                            bFreezeCps[iG] = true;
                            break;
                        }
                    }

                    if (!bFreezeCps[iG])
                    {
                        ptsCpNs0[iG] += vectsAtGrev[iG] * magnitudes[iG];
                    }
                }

                for (int u = 0; u < countU; u++)
                {
                    for (int v = 0; v < countV; v++)
                    {
                        var cp = nsWIP.Points.GetControlPoint(u, v);
                        cp.Location = ptsCpNs0[u * countV + v];
                        nsWIP.Points.SetControlPoint(u, v, cp);
                    }
                }

                previousMaxDev = maxDev;
            }

            sw.Stop();
            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max. deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";
            if (maxDev > fTolerance && iterations > 0) msg += " <- Out of tolerance";

            return new ProjectionResult
            {
                Surface = nsWIP,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = iterations == 0 ? null : msg
            };
        }
    }
}