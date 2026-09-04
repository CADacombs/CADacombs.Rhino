using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class ContinuityUtils
    {
        /// <summary>
        /// Extracts the mathematical vectors required for up to G3 continuity evaluation.
        /// Now accepts generic Curve (not just NurbsCurve) to support raw segment evaluation.
        /// </summary>
        public static (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) GetContinuityVectorsAt(
            Curve crv, double t, CurveEvaluationSide side)
        {
            Vector3d[] derivs = crv.DerivativeAt(t, 3, side);

            // If 1st derivative is tiny (e.g. stacked CVs), return zeroes
            if (derivs[1].IsTiny()) 
                return (new Point3d(derivs[0]), Vector3d.Zero, Vector3d.Zero, Vector3d.Zero);

            Vector3d d1 = derivs[1];
            Vector3d d2 = derivs[2];
            Vector3d d3 = derivs[3];

            Vector3d tangent = d1 / d1.Length;

            Vector3d cross1 = Vector3d.CrossProduct(d1, d2);
            Vector3d curvature = Vector3d.CrossProduct(cross1, d1) / Math.Pow(d1.Length, 4);

            // Torsion / G3 Condition
            Vector3d torsionPart1 = (-3.0 * (d1 * d2) * cross1) / Math.Pow(d1 * d1, 3.0);
            Vector3d torsionPart2 = Vector3d.CrossProduct(d1, d3) / Math.Pow(d1 * d1, 2.0);
            Vector3d torsion = torsionPart1 + torsionPart2;

            return (new Point3d(derivs[0]), tangent, curvature, torsion);
        }

        /// <summary>
        /// Evaluates Parametric (C) continuity by comparing derivative vectors directly.
        /// </summary>
        public static int GetParametricContinuity(
            NurbsCurve ncA, double tA, CurveEvaluationSide sideA,
            NurbsCurve ncB, double tB, CurveEvaluationSide sideB, 
            double lengthTol = 1.49e-8)
        {
            int maxDegree = Math.Max(ncA.Degree, ncB.Degree);
            Vector3d[] derivsA = ncA.DerivativeAt(tA, maxDegree, sideA);
            Vector3d[] derivsB = ncB.DerivativeAt(tB, maxDegree, sideB);

            if (derivsA[1].IsTiny() || derivsB[1].IsTiny()) return 0;

            for (int i = 0; i <= maxDegree; i++)
            {
                Vector3d vA = derivsA[i];
                Vector3d vB = derivsB[i];

                if (vA.IsTiny() && vB.IsTiny())
                {
                    if (i == maxDegree) return int.MaxValue;
                    continue;
                }
                if (vA.IsTiny() || vB.IsTiny()) return i - 1;
                if ((vA - vB).Length > lengthTol) return i - 1;
            }
            return int.MaxValue;
        }

        /// <summary>
        /// Evaluates continuity at a specific parameter internally using SPB custom tolerance logic.
        /// </summary>
        public static string GetSpbContinuity(Curve crv, double t)
        {
            double g1AngleTolRad = RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
            double g2AngleTolRad = RhinoMath.ToRadians(2.0);
            double crvDeltaTolerance = 0.05;

            var (_, tanB, crvB, _) = GetContinuityVectorsAt(crv, t, CurveEvaluationSide.Below);
            var (_, tanA, crvA, _) = GetContinuityVectorsAt(crv, t, CurveEvaluationSide.Above);

            if (Vector3d.VectorAngle(tanB, tanA) > g1AngleTolRad) return "G0";

            bool belowIsLinear = crvB.IsTiny();
            bool aboveIsLinear = crvA.IsTiny();

            if (belowIsLinear && aboveIsLinear) return "G2";
            if (belowIsLinear || aboveIsLinear) return "G1";
            if (Vector3d.VectorAngle(crvB, crvA) > g2AngleTolRad) return "G1";

            double kBelow = crvB.Length;
            double kAbove = crvA.Length;
            if (Math.Abs(kBelow - kAbove) / Math.Max(kBelow, kAbove) > crvDeltaTolerance) return "G1";

            return "G2";
        }

        /// <summary>
        /// Evaluates continuity across two distinct segments (used by PolyCurve seams).
        /// </summary>
        public static string GetSpbContinuityBetweenSegments(Curve segBelow, Curve segAbove)
        {
            double g1AngleTolRad = RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
            double g2AngleTolRad = RhinoMath.ToRadians(2.0);
            double crvDeltaTolerance = 0.05;

            var (_, tanB, crvB, _) = GetContinuityVectorsAt(segBelow, segBelow.Domain.T1, CurveEvaluationSide.Below);
            var (_, tanA, crvA, _) = GetContinuityVectorsAt(segAbove, segAbove.Domain.T0, CurveEvaluationSide.Above);

            if (Vector3d.VectorAngle(tanB, tanA) > g1AngleTolRad) return "G0";

            bool belowIsLinear = crvB.Length <= 1e-9;
            bool aboveIsLinear = crvA.Length <= 1e-9;

            if (belowIsLinear && aboveIsLinear) return "G2";
            if (belowIsLinear || aboveIsLinear) return "G1";
            if (Vector3d.VectorAngle(crvB, crvA) > g2AngleTolRad) return "G1";

            double kBelow = crvB.Length;
            double kAbove = crvA.Length;
            if (Math.Abs(kBelow - kAbove) / Math.Max(kBelow, kAbove) > crvDeltaTolerance) return "G1";

            return "G2";
        }
    }
}