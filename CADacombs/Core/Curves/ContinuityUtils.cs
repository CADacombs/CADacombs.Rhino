using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class ContinuityUtils
    {
        public static (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) GetContinuityVectorsAt(
            Curve crv, double t, CurveEvaluationSide side)
        {
            Vector3d[] derivs = crv.DerivativeAt(t, 3, side);

            if (derivs[1].IsTiny()) 
                return (new Point3d(derivs[0]), Vector3d.Zero, Vector3d.Zero, Vector3d.Zero);

            Vector3d d1 = derivs[1];
            Vector3d d2 = derivs[2];
            Vector3d d3 = derivs[3];

            Vector3d tangent = d1 / d1.Length;

            Vector3d cross1 = Vector3d.CrossProduct(d1, d2);
            Vector3d curvature = Vector3d.CrossProduct(cross1, d1) / Math.Pow(d1.Length, 4);

            Vector3d torsionPart1 = (-3.0 * (d1 * d2) * cross1) / Math.Pow(d1 * d1, 3.0);
            Vector3d torsionPart2 = Vector3d.CrossProduct(d1, d3) / Math.Pow(d1 * d1, 2.0);
            Vector3d torsion = torsionPart1 + torsionPart2;

            return (new Point3d(derivs[0]), tangent, curvature, torsion);
        }

        public static int? GetContinuityLevel(
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vB, 
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vA,
            double distTol, double g1AngleTolDeg, double g2PlusAngleTolDeg, double vectMagTolPct)
        {
            if (vB.Pt.DistanceTo(vA.Pt) > distTol) return null; // Not G0
            if (vB.Tangent.IsTiny() || vA.Tangent.IsTiny()) return null; // Stacked points

            double angleTan = RhinoMath.ToDegrees(Vector3d.VectorAngle(vB.Tangent, vA.Tangent));
            if (angleTan > g1AngleTolDeg) return 0; 

            if (vB.Curvature.IsTiny() && vA.Curvature.IsTiny())
            {
                if (vB.Torsion.IsTiny() && vA.Torsion.IsTiny()) return 3;
                return 2;
            }

            if (vB.Curvature.IsTiny() || vA.Curvature.IsTiny()) return 1; 

            double angleCrv = RhinoMath.ToDegrees(Vector3d.VectorAngle(vB.Curvature, vA.Curvature));
            if (angleCrv > g2PlusAngleTolDeg) return 1; 

            double kB = vB.Curvature.Length;
            double kA = vA.Curvature.Length;
            if (Math.Abs(kB - kA) / Math.Max(kB, kA) > (vectMagTolPct / 100.0)) return 1; 

            if (vB.Torsion.IsTiny() && vA.Torsion.IsTiny()) return 3;
            if (vB.Torsion.IsTiny() || vA.Torsion.IsTiny()) return 2; 

            double angleTors = RhinoMath.ToDegrees(Vector3d.VectorAngle(vB.Torsion, vA.Torsion));
            if (angleTors > g2PlusAngleTolDeg) return 2; 

            double tB = vB.Torsion.Length;
            double tA = vA.Torsion.Length;
            if (Math.Abs(tB - tA) / Math.Max(tB, tA) > (vectMagTolPct / 100.0)) return 2; 

            return 3; 
        }

        public static string FormatContinuityString(int? gLevel)
        {
            if (!gLevel.HasValue) return "Gap";
            if (gLevel.Value == 3) return "G3+";
            return $"G{gLevel.Value}";
        }

        public static string GetSpbContinuity(Curve crv, double t)
        {
            var vB = GetContinuityVectorsAt(crv, t, CurveEvaluationSide.Below);
            var vA = GetContinuityVectorsAt(crv, t, CurveEvaluationSide.Above);
            
            int? level = GetContinuityLevel(
                vB, vA, 
                RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
                RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees, 
                2.0, 
                5.0);

            return FormatContinuityString(level);
        }

        public static string GetSpbContinuityBetweenSegments(Curve segBelow, Curve segAbove)
        {
            var vB = GetContinuityVectorsAt(segBelow, segBelow.Domain.T1, CurveEvaluationSide.Below);
            var vA = GetContinuityVectorsAt(segAbove, segAbove.Domain.T0, CurveEvaluationSide.Above);

            int? level = GetContinuityLevel(
                vB, vA, 
                RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
                RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees, 
                2.0, 
                5.0);

            return FormatContinuityString(level);
        }

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
        /// Mathematically verifies true geometric G-infinity by normalizing the parametric domain
        /// of Curve B to match the exact evaluation speed of Curve A before testing C-infinity.
        /// Automatically upgrades geometrically identical Degree 1 and 2 spans to G-infinity.
        /// </summary>
        public static bool IsGInfinity(
            NurbsCurve ncA, double tA, CurveEvaluationSide sideA,
            NurbsCurve ncB, double tB, CurveEvaluationSide sideB, 
            double g1AngleTolDeg = 1.0, double lengthTol = 1.49e-8)
        {
            int maxDegree = Math.Max(ncA.Degree, ncB.Degree);
            Vector3d[] derivsA = ncA.DerivativeAt(tA, maxDegree, sideA);
            Vector3d[] derivsB = ncB.DerivativeAt(tB, maxDegree, sideB);

            if (derivsA[1].IsTiny() || derivsB[1].IsTiny()) return false;

            double angleTolRad = RhinoMath.ToRadians(g1AngleTolDeg);
            
            // 1. Ensure they flow in the exact same direction (G1 alignment)
            if (derivsA[1].IsParallelTo(derivsB[1], angleTolRad) != 1) return false;

            // 2. Degree 1 & 2 Shortcut (Lines and Conic Arcs)
            // A line or conic cannot mathematically diverge if it matches exactly at G3.
            if (maxDegree <= 2)
            {
                var vA = GetContinuityVectorsAt(ncA, tA, sideA);
                var vB = GetContinuityVectorsAt(ncB, tB, sideB);
                
                // Use strict internal tolerances to verify exact geometric continuation
                int? gLevel = GetContinuityLevel(
                    vB, vA, 
                    RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
                    g1AngleTolDeg, 
                    0.5, // Strict angle tolerance (degrees)
                    1.0  // Strict magnitude delta (%)
                );
                    
                if (gLevel == 3) return true;
            }

            // 3. Calculate the parametric speed ratio for higher degree curves
            double scale = derivsB[1].Length / derivsA[1].Length;

            // 4. If domains are already identically paced, a standard C-check is sufficient
            if (Math.Abs(scale - 1.0) <= 1e-6)
            {
                return GetParametricContinuity(ncA, tA, sideA, ncB, tB, sideB, lengthTol) == int.MaxValue;
            }

            // 5. Duplicate and scale Curve B's domain to match Curve A's parametric speed
            NurbsCurve ncB_scaled = (NurbsCurve)ncB.Duplicate();
            Interval oldDom = ncB.Domain;
            double tB_norm = oldDom.NormalizedParameterAt(tB);
            
            ncB_scaled.Domain = new Interval(oldDom.T0 * scale, oldDom.T1 * scale);
            double tB_scaled = ncB_scaled.Domain.ParameterAt(tB_norm);

            // 6. Test if the geometry is identically polynomial across all derivative magnitudes
            return GetParametricContinuity(ncA, tA, sideA, ncB_scaled, tB_scaled, sideB, lengthTol) == int.MaxValue;
        }
    }
}