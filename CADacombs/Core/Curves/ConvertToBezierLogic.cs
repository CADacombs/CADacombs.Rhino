using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class ConvertToBezierLogic
    {
        /// <summary>
        /// Parses a string like "235794" into an ordered List<int> { 2, 3, 5, 7, 9, 4 }, stripping invalid characters.
        /// </summary>
        public static List<int> ParseDegreesString(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<int>();
            
            return input.Where(c => char.IsDigit(c) && c != '0')
                        .Select(c => int.Parse(c.ToString()))
                        .Distinct()
                        .ToList();
        }

        /// <summary>
        /// Attempts to convert a curve into a single-span Bezier matching one of the target degrees.
        /// </summary>
        public static (NurbsCurve Bezier, double Deviation, string Log) TryConvert(
            Curve crvIn,
            List<int> targetDegrees,
            double devTol,
            bool preserveTangents,
            bool skipLines,
            bool skipArcs)
        {
            if (crvIn == null) return (null, 0.0, "Input is null.");
            
            double docTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            if (skipLines && crvIn.IsLinear(docTol)) return (null, 0.0, "Skipped linear curve.");
            if (skipArcs && crvIn.IsArc(docTol)) return (null, 0.0, "Skipped arc curve.");

            var ncIn = crvIn.ToNurbsCurve();
            
            // Check if it's already a valid Bezier of a requested degree
            if (ncIn != null && ncIn.SpanCount == 1 && !ncIn.IsRational)
            {
                if (targetDegrees.Contains(ncIn.Degree))
                    return (null, 0.0, $"Already a degree {ncIn.Degree} Bezier.");
            }

            foreach (int deg in targetDegrees)
            {
                // 1. Try Specialized Cubic Solver if target degree is 3
                if (deg == 3)
                {
                    var cubic = TryCubicBulgeFit(crvIn, devTol, out double cubicDev);
                    if (cubic != null && cubicDev <= devTol)
                    {
                        return (cubic, cubicDev, $"Cubic Bulge-Fitting success (Dev: {cubicDev:E3}).");
                    }
                }

                // 2. Try Native Rebuild Fallback for all degrees
                var rebuilt = ncIn?.Rebuild(deg + 1, deg, preserveTangents);
                if (rebuilt != null && rebuilt.IsValid)
                {
                    if (Curve.GetDistancesBetweenCurves(crvIn, rebuilt, 0.1 * docTol, out double dev, out _, out _, out _, out _, out _))
                    {
                        if (dev <= devTol)
                        {
                            // Rebuild sometimes fails to preserve tangents on Degree 2, so verify it manually.
                            if (preserveTangents)
                            {
                                double angTol = RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
                                if (Vector3d.VectorAngle(crvIn.TangentAtStart, rebuilt.TangentAtStart) > angTol ||
                                    Vector3d.VectorAngle(crvIn.TangentAtEnd, rebuilt.TangentAtEnd) > angTol)
                                {
                                    continue; 
                                }
                            }
                            return (rebuilt, dev, $"Rebuild degree {deg} success (Dev: {dev:E3}).");
                        }
                    }
                }
            }

            return (null, 0.0, "Failed to find Bezier within deviation tolerance.");
        }

        /// <summary>
        /// A high-performance brute-force search replacing the Python narrowing loops.
        /// Physically manipulates inner control points along the tangent vectors to find the optimal shape.
        /// </summary>
        private static NurbsCurve TryCubicBulgeFit(Curve crvIn, double devTol, out double minDev)
        {
            minDev = double.MaxValue;
            NurbsCurve bestCurve = null;

            Point3d p0 = crvIn.PointAtStart;
            Point3d p3 = crvIn.PointAtEnd;
            Vector3d t0 = crvIn.TangentAtStart;
            Vector3d t1 = crvIn.TangentAtEnd;
            
            double bulgeDist = p0.DistanceTo(p3);
            if (bulgeDist < 1e-12) return null;

            double docTol = 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            // Pass 1: Symmetrical search (mA == mB)
            for (double m = 0.01; m <= 1.5; m += 0.01)
            {
                var nc = BuildCubic(p0, p3, t0, t1, m, m, bulgeDist);
                if (Curve.GetDistancesBetweenCurves(crvIn, nc, docTol, out double dev, out _, out _, out _, out _, out _))
                {
                    if (dev < minDev) { minDev = dev; bestCurve = nc; }
                }
            }

            if (minDev <= devTol) return bestCurve;

            // Pass 2: Non-symmetrical tight grid search
            for (double mA = 0.02; mA <= 1.2; mA += 0.02)
            {
                for (double mB = 0.02; mB <= 1.2; mB += 0.02)
                {
                    var nc = BuildCubic(p0, p3, t0, t1, mA, mB, bulgeDist);
                    if (Curve.GetDistancesBetweenCurves(crvIn, nc, docTol, out double dev, out _, out _, out _, out _, out _))
                    {
                        if (dev < minDev) { minDev = dev; bestCurve = nc; }
                    }
                }
            }

            return bestCurve;
        }

        private static NurbsCurve BuildCubic(Point3d p0, Point3d p3, Vector3d t0, Vector3d t1, double mA, double mB, double dist)
        {
            return NurbsCurve.CreateControlPointCurve(new[] { 
                p0, 
                p0 + (mA * dist * t0), 
                p3 - (mB * dist * t1), 
                p3 
            }, 3) as NurbsCurve;
        }
    }
}