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
        public string StopReason { get; set; } = "";
        public List<Line> ErrorLines { get; set; } = new List<Line>();
        public Line MaxErrorLine { get; set; } = Line.Unset;
    }

    public class SolverProgress
    {
        public double ElapsedSeconds { get; set; }
        public double TimeoutSeconds { get; set; }
        public double CurrentError { get; set; }
        public double InitialError { get; set; }
        public double Tolerance { get; set; }
    }

    public static class NurbsMatrixMath
    {
        public static double[] EvaluateBasisVector(NurbsSurface ns, double u, double v)
        {
            int totalCps = ns.Points.CountU * ns.Points.CountV;
            double[] basisVals = new double[totalCps];
            
            var dummySrf = ns.Duplicate() as NurbsSurface;
            for(int i = 0; i < dummySrf.Points.CountU; i++)
                for(int j = 0; j < dummySrf.Points.CountV; j++)
                    dummySrf.Points.SetPoint(i, j, 0, 0, 0, 1.0); 

            Point3d ptZero = dummySrf.PointAt(u, v); 
            double baseZ = ptZero.Z;

            int idx = 0;
            for (int i = 0; i < ns.Points.CountU; i++)
            {
                for (int j = 0; j < ns.Points.CountV; j++)
                {
                    dummySrf.Points.SetPoint(i, j, 0, 0, 1.0, 1.0);
                    Point3d ptOne = dummySrf.PointAt(u, v);
                    basisVals[idx] = ptOne.Z - baseZ;
                    
                    dummySrf.Points.SetPoint(i, j, 0, 0, 0, 1.0);
                    idx++;
                }
            }
            return basisVals;
        }

        public static Matrix BuildThinPlateSplineStiffnessMatrix(int countU, int countV, double lambda)
        {
            int n = countU * countV;
            Matrix L = new Matrix(n, n);
            
            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    int row = u * countV + v;
                    L[row, row] = 4 * lambda;

                    if (u > 0) L[row, (u - 1) * countV + v] = -1 * lambda;
                    if (u < countU - 1) L[row, (u + 1) * countV + v] = -1 * lambda;
                    if (v > 0) L[row, u * countV + (v - 1)] = -1 * lambda;
                    if (v < countV - 1) L[row, u * countV + (v + 1)] = -1 * lambda;
                }
            }
            return L;
        }
    }

    public static class ProjectionMath
    {
        public static bool MeasureTrueDeviation(NurbsSurface ns, List<Brep> breps, List<Mesh> meshes, out double maxDev, out List<Line> errLines)
        {
            maxDev = 0.0;
            errLines = new List<Line>();
            
            if ((breps == null || breps.Count == 0) && (meshes == null || meshes.Count == 0)) return false;

            var srfMesh = Mesh.CreateFromSurface(ns, MeshingParameters.FastRenderMesh);
            if (srfMesh == null) return false;

            foreach (var pt in srfMesh.Vertices)
            {
                Point3d sPt = new Point3d(pt);
                Point3d? closestPt = null;
                double minDist = double.MaxValue;

                foreach (var m in meshes)
                {
                    var cPt = m.ClosestPoint(sPt);
                    double dist = sPt.DistanceTo(cPt);
                    if (dist < minDist) { minDist = dist; closestPt = cPt; }
                }
                foreach (var b in breps)
                {
                    var cPt = b.ClosestPoint(sPt);
                    double dist = sPt.DistanceTo(cPt);
                    if (dist < minDist) { minDist = dist; closestPt = cPt; }
                }

                if (closestPt.HasValue)
                {
                    double dist = sPt.DistanceTo(closestPt.Value);
                    if (dist > maxDev) maxDev = dist;
                    errLines.Add(new Line(sPt, closestPt.Value));
                }
            }
            errLines = errLines.OrderByDescending(l => l.Length).ToList();
            return true;
        }

        public static ProjectionResult FitDirectGreville(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null, Action<SolverProgress> progress = null)
        {
            return FitDirectGreville(ptsTarget, nsIn, null, null, fTolerance, timeoutSecs, checkCancel, progress);
        }

        public static ProjectionResult FitDirectGreville(Point3d?[,] ptsTarget, NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null, Action<SolverProgress> progress = null)
        {
            bool hasTargets = false;
            foreach (var pt in ptsTarget) { if (pt.HasValue) { hasTargets = true; break; } }
            if (!hasTargets) return new ProjectionResult { Converged = false, Iterations = 0, Message = "The starting surface completely misses the targets." };

            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 0;
            double maxDev = 0.0;
            double initialDev = -1;

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (!ptsTarget[u, v].HasValue) continue;

                    Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                    Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                    Vector3d vect = ptsTarget[u, v].Value - ptGr;

                    if (vect.Length > initialDev) initialDev = vect.Length;

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

                progress?.Invoke(new SolverProgress { ElapsedSeconds = sw.Elapsed.TotalSeconds, TimeoutSeconds = timeoutSecs, CurrentError = maxDev, InitialError = initialDev, Tolerance = fTolerance });

                if (!bTransPts) break;
            }
            sw.Stop();

            var errLines = new List<Line>();
            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    if (ptsTarget[u, v].HasValue)
                    {
                        Point2d uv = nsOut.Points.GetGrevillePoint(u, v);
                        Point3d ptGr = nsOut.PointAt(uv.X, uv.Y);
                        errLines.Add(new Line(ptGr, ptsTarget[u, v].Value));
                    }
                }
            }
            errLines = errLines.OrderByDescending(l => l.Length).ToList();

            if (MeasureTrueDeviation(nsOut, breps, meshes, out double finalMaxDev, out var finalErrLines))
            {
                maxDev = finalMaxDev;
                errLines = finalErrLines;
            }

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";

            string stopReason = "";
            if (maxDev <= fTolerance) stopReason = "Converged";
            else if (sw.Elapsed.TotalSeconds >= timeoutSecs) stopReason = "Timed out";
            else stopReason = "Geometrically constrained";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = msg,
                StopReason = stopReason,
                ErrorLines = errLines,
                MaxErrorLine = errLines.FirstOrDefault()
            };
        }

        public static ProjectionResult FitDirectControlPoints(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance, Action<SolverProgress> progress = null)
        {
            return FitDirectControlPoints(ptsTarget, nsIn, null, null, fTolerance, progress);
        }

        public static ProjectionResult FitDirectControlPoints(Point3d?[,] ptsTarget, NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, Action<SolverProgress> progress = null)
        {
            bool hasTargets = false;
            foreach (var pt in ptsTarget) { if (pt.HasValue) { hasTargets = true; break; } }
            if (!hasTargets) return new ProjectionResult { Converged = false, Iterations = 0, Message = "The starting surface completely misses the targets." };

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

            double maxDev = 0.0;
            var errLines = new List<Line>();
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
                        errLines.Add(new Line(ptGr, ptsTarget[u, v].Value));
                    }
                }
            }
            sw.Stop();
            errLines = errLines.OrderByDescending(l => l.Length).ToList();

            if (MeasureTrueDeviation(nsOut, breps, meshes, out double finalMaxDev, out var finalErrLines))
            {
                maxDev = finalMaxDev;
                errLines = finalErrLines;
            }

            progress?.Invoke(new SolverProgress { ElapsedSeconds = sw.Elapsed.TotalSeconds, TimeoutSeconds = sw.Elapsed.TotalSeconds, CurrentError = maxDev, InitialError = maxDev, Tolerance = fTolerance });

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"1 iterations, {sw.Elapsed.TotalSeconds:F3}s, Max deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";

            string stopReason = maxDev <= fTolerance ? "Converged" : "Single-step solve complete";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = 1,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = maxDev,
                Converged = maxDev <= fTolerance,
                Message = msg,
                StopReason = stopReason,
                ErrorLines = errLines,
                MaxErrorLine = errLines.FirstOrDefault()
            };
        }

        public static ProjectionResult FitWeightedRelaxed(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, bool constrain1D, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null, Action<SolverProgress> progress = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int iterations = 0;
            double maxDev = double.MaxValue;
            double initialError = -1;
            bool strictMode = false;
            bool fullyConverged = false;
            
            double domU = nsOut.Domain(0).Length;
            double domV = nsOut.Domain(1).Length;
            double minDom = Math.Min(domU, domV);
            
            int sampleCountU = nsOut.Points.CountU * 3;
            int sampleCountV = nsOut.Points.CountV * 3;
            double minSpacing = Math.Max(domU / sampleCountU, domV / sampleCountV) * 1.5;

            double R = minDom * (ConformSrfOptions.PercentOfDomain / 100.0);
            if (R < minSpacing) R = minSpacing; 
            
            double L = 0.01 * R; 
            double twoSigmaSq = 2.0 * R * R; 
            
            double damping = 0.5;
            int weightCurve = ConformSrfOptions.WeightCurve;
            bool isZAxis = ConformSrfOptions.MappingDirection == 1;
            Vector3d rayDir = ConformSrfOptions.FlipDirection ? Vector3d.ZAxis : -Vector3d.ZAxis; 
            double safeRetreatDist = Math.Max(fTolerance * 10.0, 1.0);

            int samplingMode = ConformSrfOptions.SamplingMode;
            List<Point3d> targetPts = null;

            if (samplingMode == 1)
            {
                targetPts = new List<Point3d>();
                foreach (var m in meshes) targetPts.AddRange(m.Vertices.ToPoint3dArray());
                foreach (var b in breps)
                {
                    var bm = Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh);
                    if (bm != null) foreach (var m in bm) targetPts.AddRange(m.Vertices.ToPoint3dArray());
                }
                
                if (targetPts.Count == 0) samplingMode = 0; 
                else if (targetPts.Count > 5000)
                {
                    int step = targetPts.Count / 5000;
                    var thinned = new List<Point3d>(5000);
                    for (int i = 0; i < targetPts.Count; i += step) thinned.Add(targetPts[i]);
                    targetPts = thinned;
                }
            }

            var docUnits = RhinoDoc.ActiveDoc.ModelUnitSystem;
            double mmScale = RhinoMath.UnitScale(UnitSystem.Millimeters, docUnits);
            double minProgress = 1e-5 * mmScale;
            double prevMaxDev = double.MaxValue;
            int stallCount = 0;

            while (sw.Elapsed.TotalSeconds < timeoutSecs && !fullyConverged)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                iterations++;
                maxDev = 0.0;

                var sampleUVs = new List<Point2d>();
                var sampleVectors = new List<Vector3d>();

                if (samplingMode == 0)
                {
                    for (int i = 0; i < sampleCountU; i++)
                    {
                        if (sw.Elapsed.TotalSeconds > timeoutSecs) break;
                        
                        for (int j = 0; j < sampleCountV; j++)
                        {
                            if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                            
                            double u = nsOut.Domain(0).ParameterAt((double)i / (sampleCountU - 1));
                            double v = nsOut.Domain(1).ParameterAt((double)j / (sampleCountV - 1));
                            
                            Point3d sPtWIP = nsOut.PointAt(u, v);
                            Point3d sPtOriginal = nsIn.PointAt(u, v); 
                            Point3d? closestPt = null;

                            if (isZAxis)
                            {
                                Point3d safePt = sPtOriginal - rayDir * safeRetreatDist;

                                var hits = new List<Point3d>();
                                if (breps.Count > 0)
                                {
                                    var bHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBreps(breps, new[] { safePt }, rayDir, 0.01);
                                    if (bHits != null) hits.AddRange(bHits.Where(h => (h.Z - sPtOriginal.Z) * rayDir.Z >= -1e-5));
                                }
                                if (meshes.Count > 0)
                                {
                                    var mHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToMeshes(meshes, new[] { safePt }, rayDir, 0.01);
                                    if (mHits != null) hits.AddRange(mHits.Where(h => (h.Z - sPtOriginal.Z) * rayDir.Z >= -1e-5));
                                }
                                if (hits.Count > 0) closestPt = hits.OrderBy(h => sPtOriginal.DistanceTo(h)).First();
                            }
                            else 
                            {
                                double minDist = double.MaxValue;
                                foreach (var mesh in meshes) { var mPt = mesh.ClosestPoint(sPtWIP); if (sPtWIP.DistanceTo(mPt) < minDist) { minDist = sPtWIP.DistanceTo(mPt); closestPt = mPt; } }
                                foreach (var brep in breps) { var bPt = brep.ClosestPoint(sPtWIP); if (sPtWIP.DistanceTo(bPt) < minDist) { minDist = sPtWIP.DistanceTo(bPt); closestPt = bPt; } }
                            }

                            if (closestPt.HasValue)
                            {
                                Vector3d vec = closestPt.Value - sPtWIP;
                                if (vec.Length > maxDev) maxDev = vec.Length;
                                
                                sampleUVs.Add(new Point2d(u, v));
                                sampleVectors.Add(vec);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var tPt in targetPts)
                    {
                        if (sw.Elapsed.TotalSeconds > timeoutSecs) break;
                        if (checkCancel?.Invoke() == true) throw new OperationCanceledException();

                        if (nsOut.ClosestPoint(tPt, out double u, out double v))
                        {
                            Point3d sPtWIP = nsOut.PointAt(u, v);
                            Vector3d vec = tPt - sPtWIP;
                            
                            if (isZAxis)
                            {
                                vec = new Vector3d(0, 0, tPt.Z - sPtWIP.Z);
                            }
                            
                            if (vec.Length > maxDev) maxDev = vec.Length;
                            
                            sampleUVs.Add(new Point2d(u, v));
                            sampleVectors.Add(vec);
                        }
                    }
                }

                if (initialError < 0) initialError = maxDev;

                if (maxDev <= fTolerance && !strictMode) 
                {
                    strictMode = true; 
                }

                if (strictMode)
                {
                    if (MeasureTrueDeviation(nsOut, breps, meshes, out double trueMaxDev, out var strictLines))
                    {
                        if (trueMaxDev <= fTolerance)
                        {
                            maxDev = trueMaxDev;
                            fullyConverged = true;
                            break;
                        }
                        
                        foreach (var line in strictLines.Where(l => l.Length > fTolerance).Take(sampleCountU * sampleCountV))
                        {
                            if (nsOut.ClosestPoint(line.From, out double u, out double v))
                            {
                                sampleUVs.Add(new Point2d(u, v));
                                sampleVectors.Add(line.To - line.From);
                                if (line.Length > maxDev) maxDev = line.Length;
                            }
                        }
                    }
                    else
                    {
                        if (maxDev <= fTolerance) fullyConverged = true;
                        break;
                    }
                }

                if (iterations > 1 && Math.Abs(prevMaxDev - maxDev) < minProgress)
                {
                    stallCount++;
                    if (stallCount >= 3) break; 
                }
                else
                {
                    stallCount = 0;
                }
                prevMaxDev = maxDev;

                if (sampleVectors.Count == 0) break;

                progress?.Invoke(new SolverProgress { ElapsedSeconds = sw.Elapsed.TotalSeconds, TimeoutSeconds = timeoutSecs, CurrentError = maxDev, InitialError = initialError, Tolerance = fTolerance });

                var newCpLocations = new Point3d[nsOut.Points.CountU, nsOut.Points.CountV];

                for (int cu = 0; cu < nsOut.Points.CountU; cu++)
                {
                    for (int cv = 0; cv < nsOut.Points.CountV; cv++)
                    {
                        if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                        
                        Point2d cpUV = nsOut.Points.GetGrevillePoint(cu, cv);
                        ControlPoint cp = nsOut.Points.GetControlPoint(cu, cv);
                        
                        Vector3d sumWVec = Vector3d.Zero;
                        double sumW = 0.0;

                        for (int k = 0; k < sampleUVs.Count; k++)
                        {
                            double distSq = Math.Pow(sampleUVs[k].X - cpUV.X, 2) + Math.Pow(sampleUVs[k].Y - cpUV.Y, 2);
                            double d = Math.Sqrt(distSq);
                            double weight = 0.0;

                            if (weightCurve == 2) weight = Math.Exp(-distSq / twoSigmaSq);
                            else if (d <= L) weight = 1.0;
                            else if (d < R)
                            {
                                if (weightCurve == 0) weight = 1.0 - (d - L) / (R - L);
                                else if (weightCurve == 1) weight = 1.0 - (1.0 - L / d) / (1.0 - L / R);
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
                            if (constrain1D)
                            {
                                avgVec.X = 0;
                                avgVec.Y = 0;
                            }
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
            if (iterations == 0) return new ProjectionResult { Converged = false, Iterations = 0, Message = "The starting surface completely misses the targets." };

            var errLines = new List<Line>();
            double finalMaxDev = maxDev;

            if (MeasureTrueDeviation(nsOut, breps, meshes, out double measuredMaxDev, out var measuredErrLines))
            {
                finalMaxDev = measuredMaxDev;
                errLines = measuredErrLines;
            }

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max deviation from target(s): {FormatUtils.FormatDistance(finalMaxDev, prec)}";

            string unitStr = docUnits.ToString().ToLower();
            string stopReason = "";
            if (fullyConverged || finalMaxDev <= fTolerance) stopReason = "Converged";
            else if (sw.Elapsed.TotalSeconds >= timeoutSecs) stopReason = "Timed out";
            else if (stallCount >= 3) stopReason = $"Solver made no improvement >= {minProgress:G} {unitStr}";
            else stopReason = "Geometrically constrained";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = iterations,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = finalMaxDev,
                Converged = finalMaxDev <= fTolerance,
                Message = msg,
                StopReason = stopReason,
                ErrorLines = errLines,
                MaxErrorLine = errLines.FirstOrDefault()
            };
        }

        public static ProjectionResult FitGlobalLeastSquares(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, bool constrain1D, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null, Action<SolverProgress> progress = null)
        {
            return FitMatrixSolverCore(nsIn, breps, meshes, fTolerance, timeoutSecs, false, 0.0, constrain1D, checkCancel, progress);
        }

        public static ProjectionResult FitEnergyMinimization(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double stiffness, bool constrain1D, double fTolerance, double timeoutSecs, Func<bool> checkCancel = null, Action<SolverProgress> progress = null)
        {
            return FitMatrixSolverCore(nsIn, breps, meshes, fTolerance, timeoutSecs, true, stiffness, constrain1D, checkCancel, progress);
        }

        private static ProjectionResult FitMatrixSolverCore(NurbsSurface nsIn, List<Brep> breps, List<Mesh> meshes, double fTolerance, double timeoutSecs, bool useStiffness, double stiffness, bool constrain1D, Func<bool> checkCancel, Action<SolverProgress> progress)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            
            int countU = nsOut.Points.CountU;
            int countV = nsOut.Points.CountV;
            int totalCps = countU * countV;

            int sampleCountU = countU * 3;
            int sampleCountV = countV * 3;

            bool isZAxis = ConformSrfOptions.MappingDirection == 1;
            Vector3d rayDir = ConformSrfOptions.FlipDirection ? Vector3d.ZAxis : -Vector3d.ZAxis; 
            double safeRetreatDist = Math.Max(fTolerance * 10.0, 1.0);

            var validUVs = new List<Point2d>();
            var targetPts = new List<Point3d>();

            for (int i = 0; i < sampleCountU; i++)
            {
                if (sw.Elapsed.TotalSeconds > timeoutSecs) break;

                for (int j = 0; j < sampleCountV; j++)
                {
                    if (checkCancel?.Invoke() == true) throw new OperationCanceledException();

                    double u = nsOut.Domain(0).ParameterAt((double)i / (sampleCountU - 1));
                    double v = nsOut.Domain(1).ParameterAt((double)j / (sampleCountV - 1));
                    
                    Point3d sPtWIP = nsOut.PointAt(u, v);
                    Point3d sPtOriginal = nsIn.PointAt(u, v); 
                    Point3d? closestPt = null;

                    if (isZAxis)
                    {
                        Point3d safePt = sPtOriginal - rayDir * safeRetreatDist;

                        var hits = new List<Point3d>();
                        if (breps.Count > 0)
                        {
                            var bHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBreps(breps, new[] { safePt }, rayDir, 0.01);
                            if (bHits != null) hits.AddRange(bHits.Where(h => (h.Z - sPtOriginal.Z) * rayDir.Z >= -1e-5));
                        }
                        if (meshes.Count > 0)
                        {
                            var mHits = Rhino.Geometry.Intersect.Intersection.ProjectPointsToMeshes(meshes, new[] { safePt }, rayDir, 0.01);
                            if (mHits != null) hits.AddRange(mHits.Where(h => (h.Z - sPtOriginal.Z) * rayDir.Z >= -1e-5));
                        }
                        if (hits.Count > 0) closestPt = hits.OrderBy(h => sPtOriginal.DistanceTo(h)).First();
                    }
                    else 
                    {
                        double minDist = double.MaxValue;
                        foreach (var mesh in meshes) { var mPt = mesh.ClosestPoint(sPtWIP); if (sPtWIP.DistanceTo(mPt) < minDist) { minDist = sPtWIP.DistanceTo(mPt); closestPt = mPt; } }
                        foreach (var brep in breps) { var bPt = brep.ClosestPoint(sPtWIP); if (sPtWIP.DistanceTo(bPt) < minDist) { minDist = sPtWIP.DistanceTo(bPt); closestPt = bPt; } }
                    }

                    if (closestPt.HasValue)
                    {
                        validUVs.Add(new Point2d(u, v));
                        targetPts.Add(closestPt.Value);
                    }
                }
            }

            if (validUVs.Count == 0) return new ProjectionResult { Converged = false, Iterations = 0, Message = "The starting surface completely misses the targets." };

            int m = validUVs.Count;
            int n = totalCps;
            
            progress?.Invoke(new SolverProgress { ElapsedSeconds = sw.Elapsed.TotalSeconds, TimeoutSeconds = timeoutSecs, CurrentError = fTolerance * 10, InitialError = fTolerance * 10, Tolerance = fTolerance });

            Matrix N = new Matrix(m, n);
            for (int r = 0; r < m; r++)
            {
                if (checkCancel?.Invoke() == true) throw new OperationCanceledException();
                double[] basisVals = NurbsMatrixMath.EvaluateBasisVector(nsOut, validUVs[r].X, validUVs[r].Y);
                for (int c = 0; c < n; c++)
                {
                    N[r, c] = basisVals[c];
                }
            }

            Matrix Qx = new Matrix(m, 1);
            Matrix Qy = new Matrix(m, 1);
            Matrix Qz = new Matrix(m, 1);
            for (int r = 0; r < m; r++)
            {
                Qx[r, 0] = targetPts[r].X;
                Qy[r, 0] = targetPts[r].Y;
                Qz[r, 0] = targetPts[r].Z;
            }

            Matrix NT = new Matrix(n, m);
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    NT[c, r] = N[r, c];
                }
            }
            Matrix NT_N = NT * N;

            if (useStiffness)
            {
                Matrix L = NurbsMatrixMath.BuildThinPlateSplineStiffnessMatrix(countU, countV, stiffness);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        NT_N[i, j] += L[i, j];
            }

            if (!NT_N.Invert(1e-12))
            {
                return new ProjectionResult { Converged = false, Iterations = 1, Message = "Matrix math failed: System is singular or under-constrained." };
            }

            Matrix NT_N_inv_NT = NT_N * NT;
            Matrix Pz = NT_N_inv_NT * Qz;
            Matrix Px = null;
            Matrix Py = null;

            if (!constrain1D)
            {
                Px = NT_N_inv_NT * Qx;
                Py = NT_N_inv_NT * Qy;
            }

            for (int u = 0; u < countU; u++)
            {
                for (int v = 0; v < countV; v++)
                {
                    int row = u * countV + v;
                    ControlPoint cp = nsOut.Points.GetControlPoint(u, v);
                    
                    if (constrain1D) cp.Location = new Point3d(cp.Location.X, cp.Location.Y, Pz[row, 0]);
                    else cp.Location = new Point3d(Px[row, 0], Py[row, 0], Pz[row, 0]);
                    
                    nsOut.Points.SetControlPoint(u, v, cp);
                }
            }
            sw.Stop();

            double finalMaxDev = 0.0;
            var finalErrLines = new List<Line>();

            if (MeasureTrueDeviation(nsOut, breps, meshes, out double measuredMaxDev, out var measuredErrLines))
            {
                finalMaxDev = measuredMaxDev;
                finalErrLines = measuredErrLines;
            }

            progress?.Invoke(new SolverProgress { ElapsedSeconds = sw.Elapsed.TotalSeconds, TimeoutSeconds = sw.Elapsed.TotalSeconds, CurrentError = finalMaxDev, InitialError = finalMaxDev, Tolerance = fTolerance });

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            string msg = $"1 iterations, {sw.Elapsed.TotalSeconds:F3}s, Max deviation from target(s): {FormatUtils.FormatDistance(finalMaxDev, prec)}";
            
            string stopReason = finalMaxDev <= fTolerance ? "Converged" : "Single-step solve complete";

            return new ProjectionResult
            {
                Surface = nsOut,
                Iterations = 1,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MaxDeviation = finalMaxDev,
                Converged = finalMaxDev <= fTolerance,
                Message = msg,
                StopReason = stopReason,
                ErrorLines = finalErrLines,
                MaxErrorLine = finalErrLines.FirstOrDefault()
            };
        }
    }
}