using System;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Core.Curves
{
    public static class UniformityChecker
    {
        public static bool IsUniform(NurbsCurve nc)
        {
            if (nc == null) return false;

            // Single span is inherently uniform
            if (nc.Points.Count == nc.Degree + 1)
                return true;

            int start = nc.IsPeriodic ? 0 : nc.Degree - 1;
            int end = nc.Knots.Count - (nc.IsPeriodic ? 0 : nc.Degree - 1) - 1;

            if (start >= end) return false;

            double span0 = nc.Knots[start + 1] - nc.Knots[start];

            for (int i = start + 1; i < end; i++)
            {
                if (nc.Knots.KnotMultiplicity(i) > 1)
                    return false;

                double currentSpan = nc.Knots[i + 1] - nc.Knots[i];
                
                // Using 1e-9 relative tolerance as noted in the Python script
                if (!MathUtils.AreEpsilonEqual(span0, currentSpan, 1e-9))
                    return false;
            }

            return true;
        }
    }
}