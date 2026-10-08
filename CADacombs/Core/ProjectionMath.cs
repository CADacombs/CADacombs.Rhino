using System;
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

            // Initial explicit snap
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

            // Iterative relaxation loop bounded by timeout and cancel token
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
            string tolStr = FormatUtils.FormatDistance(fTolerance, prec);

            string msg = $"{iterations} iterations, {sw.Elapsed.TotalSeconds:F3}s, Max deviation: {maxDevStr}.";
            if (!converged && maxDev > fTolerance)
            {
                msg += $" All Greville points do not lie on target(s) within {tolStr}.";
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

        public static NurbsSurface FitDirectControlPoints(Point3d?[,] ptsTarget, NurbsSurface nsIn)
        {
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
            return nsOut;
        }
    }
}