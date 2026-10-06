using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core
{
    public static class ProjectionMath
    {
        public static NurbsSurface FitDirectGreville(Point3d?[,] ptsTarget, NurbsSurface nsIn, double fTolerance)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            // Initially move control points whose Grevilles are not within tolerance
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

            // Iterative relaxation
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

        public static NurbsSurface FitDirectControlPoints(Point3d?[,] ptsTarget, NurbsSurface nsIn)
        {
            var nsOut = nsIn.Duplicate() as NurbsSurface;
            int countU = nsIn.Points.CountU;
            int countV = nsIn.Points.CountV;

            // Direct 1:1 Control Point Translation (No iteration needed)
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