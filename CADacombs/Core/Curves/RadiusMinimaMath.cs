using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class RadiusMinimaMath
    {
        /// <summary>
        /// Returns the points and values of the minimum radii (maximum curvature) of a given curve.
        /// Leverages Rhino 8.4+ Curve.MaxCurvaturePoints(out double[] t).
        /// </summary>
        public static List<(Point3d Point, double Radius)> GetRadiusMinima(Curve curve, bool includeArcs = true, double tolerance = 1e-9)
        {
            var data = new List<(Point3d, double)>();
            if (curve == null) return data;

            // 1. Constant zero curvature (Infinite radius)
            if (curve is LineCurve || curve is PolylineCurve || curve.IsLinear(tolerance))
            {
                data.Add((curve.PointAt(curve.Domain.Mid), RhinoMath.UnsetValue));
                return data;
            }

            // 2. Global exact arc (Constant radius)
            if (curve.TryGetArc(out Arc globalArc, tolerance))
            {
                if (includeArcs) data.Add((globalArc.MidPoint, globalArc.Radius));
                return data;
            }

            // 3. Native Rhino 8+ local curvature maxima
            Point3d[] maxCurvPts = curve.MaxCurvaturePoints(out double[] tValues);
            
            if (maxCurvPts != null && tValues != null)
            {
                for (int i = 0; i < tValues.Length; i++)
                {
                    double k = curve.CurvatureAt(tValues[i]).Length;
                    if (k > RhinoMath.ZeroTolerance)
                    {
                        data.Add((maxCurvPts[i], 1.0 / k)); // R = 1 / K
                    }
                }
            }

            // 4. Fallback for constant-curvature internal spans
            if (includeArcs)
            {
                ExtractInternalArcRadii(curve, data, tolerance);
            }

            return data;
        }

        public static double? GetMinimumRadius(Curve curve, bool includeArcs = true, double tolerance = 1e-9)
        {
            var minima = GetRadiusMinima(curve, includeArcs, tolerance);
            if (minima.Count == 0) return null;

            double min = double.MaxValue;
            bool found = false;

            foreach (var item in minima)
            {
                if (item.Radius != RhinoMath.UnsetValue && item.Radius < min)
                {
                    min = item.Radius;
                    found = true;
                }
            }

            return found ? min : (double?)null;
        }

        private static void ExtractInternalArcRadii(Curve curve, List<(Point3d, double)> data, double tolerance)
        {
            if (curve is PolyCurve polyCurve)
            {
                for (int i = 0; i < polyCurve.SegmentCount; i++)
                {
                    using (Curve segment = polyCurve.SegmentCurve(i))
                    {
                        if (segment != null && segment.TryGetArc(out Arc arc, tolerance))
                        {
                            data.Add((arc.MidPoint, arc.Radius));
                        }
                    }
                }
            }
            else if (curve is NurbsCurve nurb)
            {
                for (int i = 0; i < nurb.SpanCount; i++)
                {
                    using (Curve span = nurb.Trim(nurb.SpanDomain(i)))
                    {
                        if (span != null && span.TryGetArc(out Arc arc, tolerance))
                        {
                            data.Add((arc.MidPoint, arc.Radius));
                        }
                    }
                }
            }
        }
    }
}