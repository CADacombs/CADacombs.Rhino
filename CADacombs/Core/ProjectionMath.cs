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
            
            // Derive sigma from PercentOfDomain
            double domU = nsOut.Domain(0).Length;
            double domV = nsOut.Domain(1).Length;
            double minDom = Math.Min(domU, domV);
            double sigma = minDom * (FitSrfToTargetOptions.PercentOfDomain / 100.0);
            if (sigma < 1e-6) sigma = 1e-6; // Prevent division by zero
            double twoSigmaSq = 2.0 * sigma * sigma;
            double damping = 0.5; // Lambda to prevent oscillation

            // Dense sampling grid: 3x control points
            int sampleCountU = nsOut.Points.CountU * 3;
            int sampleCountV = nsOut.Points.CountV * 3;

            while (sw.Elapsed.TotalSeconds < timeoutSecs && maxDev > fTolerance)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                iterations++;
                maxDev = 0.0;

                // 1. Gather Sample Vectors (V_k)
                var sampleUVs = new List<Point2d>();
                var sampleVectors = new List<Vector3d>();

                for (int i = 0; i < sampleCountU; i++)
                {
                    for (int j = 0; j < sampleCountV; j++)
                    {
                        double u = nsOut.Domain(0).ParameterAt((double)i / (sampleCountU - 1));
                        double v = nsOut.Domain(1).ParameterAt((double)j / (sampleCountV - 1));
                        
                        Point3d sPt = nsOut.PointAt(u, v);
                        Point3d? closestPt = null;
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

                        if (closestPt.HasValue)
                        {
                            Vector3d vec = closestPt.Value - sPt;
                            if (vec.Length > maxDev) maxDev = vec.Length;
                            
                            sampleUVs.Add(new Point2d(u, v));
                            sampleVectors.Add(vec);
                        }
                    }
                }

                if (sampleVectors.Count == 0) break; // Missed all targets

                // 2. Distribute Vectors to Control Points via Gaussian Kernel
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
                            double weight = Math.Exp(-distSq / twoSigmaSq);
                            
                            sumWVec += sampleVectors[k] * weight;
                            sumW += weight;
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

                // 3. Apply Updates
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
            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max. 3D deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";
            if (maxDev > fTolerance) msg += " <- Out of tolerance";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = msg
            };
        }

        public static ProjectionResult FitPythonLegacy(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsWIP = nsIn.Duplicate() as NurbsSurface;
            
            // Normalize domains to [0, 1] as in the Python script
            nsWIP.SetDomain(0, new Interval(0.0, 1.0));
            nsWIP.SetDomain(1, new Interval(0.0, 1.0));

            int countU = nsWIP.Points.CountU;
            int countV = nsWIP.Points.CountV;
            int totalCps = countU * countV;

            // 1. Generate Static Target Points (mimicking xPoint3d_createFromObjects.py)
            var ptsTarget = new List<Point3d>();
            foreach (var mesh in meshes) 
            {
                var verts = mesh.Vertices.ToPoint3dArray();
                ptsTarget.AddRange(verts);
            }
            foreach (var brep in breps)
            {
                var mp = new MeshingParameters(0.1); 
                var bMeshes = Mesh.CreateFromBrep(brep, mp);
                if (bMeshes != null)
                {
                    foreach (var bm in bMeshes) ptsTarget.AddRange(bm.Vertices.ToPoint3dArray());
                }
            }
            ptsTarget = Point3d.CullDuplicates(ptsTarget, 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance).ToList();

            if (ptsTarget.Count == 0)
            {
                return new ProjectionResult { Surface = nsWIP, Converged = false, Message = "No target points generated." };
            }

            // Options
            int samplingMode = FitSrfToTargetOptions.SamplingMode;
            int weightCurve = FitSrfToTargetOptions.WeightCurve;
            bool isNormalTrans = FitSrfToTargetOptions.MappingDirection == 0; // 0 = Closest Point (Normals in legacy), 1 = Z-Axis
            
            double domLenU = nsWIP.Domain(0).Length;
            double domLenV = nsWIP.Domain(1).Length;
            double pct = FitSrfToTargetOptions.PercentOfDomain / 100.0;
            double uvDistTol_Upr = Math.Min(domLenU * pct, domLenV * pct);
            double uvDistTol_Lwr = 0.01 * uvDistTol_Upr;

            bool[] bFreezeCps = new bool[totalCps];
            int iterations = 0;
            double maxDev = double.MaxValue;
            double previousMaxDev = double.MaxValue;

            while (sw.Elapsed.TotalSeconds < timeoutSecs)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                iterations++;

                // 2. Pull targets to WIP surface
                var uvsTargetProjd = new List<Point2d>();
                var ptsProjdHit = new List<Point3d>();
                var dists = new List<double>();
                var validTargets = new List<Point3d>();

                foreach (var pt0 in ptsTarget)
                {
                    if (isNormalTrans) // Closest Point
                    {
                        if (nsWIP.ClosestPoint(pt0, out double u, out double v))
                        {
                            Point3d hit = nsWIP.PointAt(u, v);
                            Vector3d normal = nsWIP.NormalAt(u, v);
                            Vector3d diff = pt0 - hit;
                            
                            double dist = diff.Length;
                            if (Vector3d.VectorAngle(normal, diff) > Math.PI / 4.0) dist = -dist; // Signed distance

                            uvsTargetProjd.Add(new Point2d(u, v));
                            ptsProjdHit.Add(hit);
                            dists.Add(dist);
                            validTargets.Add(pt0);
                        }
                    }
                    else // Z-Axis Projection
                    {
                        var rayHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBreps(new[] { nsWIP.ToBrep() }, new[] { pt0 }, Vector3d.ZAxis, tolerance: 0.01);
                        if (rayHits != null && rayHits.Length > 0)
                        {
                            nsWIP.ClosestPoint(rayHits[0], out double u, out double v);
                            double dist = rayHits[0].DistanceTo(pt0);
                            Vector3d diff = pt0 - rayHits[0];
                            if (Vector3d.VectorAngle(nsWIP.NormalAt(u, v), diff) > Math.PI / 4.0) dist = -dist;

                            uvsTargetProjd.Add(new Point2d(u, v));
                            ptsProjdHit.Add(rayHits[0]);
                            dists.Add(dist);
                            validTargets.Add(pt0);
                        }
                    }
                }

                if (validTargets.Count == 0) break;
                maxDev = dists.Max(d => Math.Abs(d));
                if (maxDev <= fTolerance || (iterations > 1 && Math.Abs(previousMaxDev - maxDev) < 0.1 * fTolerance)) break;

                // 3. Evaluate Grevilles & Distributed Weights
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
                            normal = Vector3d.VectorAngle(normal, Vector3d.ZAxis) < Math.PI / 2.0 ? Vector3d.ZAxis : -Vector3d.ZAxis;
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
                            if (weightCurve == 0) // Linear
                                w = 1.0 - (distUV - uvDistTol_Lwr) / (uvDistTol_Upr - uvDistTol_Lwr);
                            else // Hyperbolic
                                w = 1.0 - (1.0 - uvDistTol_Lwr / distUV) / (1.0 - uvDistTol_Lwr / uvDistTol_Upr);
                        }

                        weightsTsPerG.Add(w);
                        distsWeightedTsPerG.Add(w * dists[iT]);
                    }

                    double distWeightedNormalized = 0.0;

                    if (samplingMode == 0) // Distributed
                    {
                        if (weightsTsPerG.Count(w => Math.Abs(w - 1.0) < 1e-6) == 1)
                        {
                            distWeightedNormalized = distsWeightedTsPerG[weightsTsPerG.IndexOf(1.0)];
                        }
                        else
                        {
                            double sumW = weightsTsPerG.Sum();
                            if (sumW > 0) distWeightedNormalized = distsWeightedTsPerG.Sum() / sumW;
                        }
                    }
                    else // NearestPoint
                    {
                        double wNearest = 0.0;
                        if (nearestDist <= uvDistTol_Lwr) wNearest = 1.0;
                        else if (nearestDist < uvDistTol_Upr)
                        {
                            if (weightCurve == 0) wNearest = 1.0 - (nearestDist - uvDistTol_Lwr) / (uvDistTol_Upr - uvDistTol_Lwr);
                            else wNearest = 1.0 - (1.0 - uvDistTol_Lwr / nearestDist) / (1.0 - uvDistTol_Lwr / uvDistTol_Upr);
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
                            if (minDists.Count > 0) distWeightedNormalized = minDists.Average();
                        }
                    }

                    magnitudes[iG] = distWeightedNormalized; // Omitted corner-check multiplier logic for simplicity, per typical relaxations
                }

                // 4. Freeze Checks & Apply Translations
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
            if (maxDev > fTolerance) msg += " <- Out of tolerance";

            return new ProjectionResult
            {
                Surface = nsWIP,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = msg
            };
        }
    }
}