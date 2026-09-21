using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core
{
    /// <summary>
    /// The core shared mathematical solvers and point-allocation engine.
    /// Utilizes exact 4D geometric reparameterization and quotient rules.
    /// </summary>
    public static class EndBulgeMath
    {
        /// <summary>
        /// Evaluates if a NURBS curve can mathematically maintain G3 continuity.
        /// </summary>
        /// <summary>
        /// Evaluates if a NURBS curve can mathematically maintain G3 continuity.
        /// With the 4D quotient rule implementation, this is universally true for Degree >= 3.
        /// </summary>
        public static bool CanMaintainG3(NurbsCurve nc, bool evalT1End)
        {
            if (nc == null) return false;
            
            // G3 requires a minimum mathematical flexibility of Degree 3
            if (nc.Degree < 3) return false;
            
            // G3 physically requires 4 control points (P0, P1, P2, P3) to manipulate
            if (nc.Points.Count < 4) return false;

            return true;
        }

        /// <summary>
        /// The master curve generation and point allocation algorithm.
        /// Returns a Tuple containing the resulting Curve, an Error string (if any), and mathematical Info.
        /// </summary>
        public static (NurbsCurve Result, string Error, (int MaxModT0, int MaxModT1, bool Overlap)? Info) 
            CreateCurve(
                NurbsCurve nc_In,
                double fScale_T0 = 1.0, double fSlideG2_T0 = 0.0, double fSlideG3_T0 = 0.0,
                double fScale_T1 = 1.0, double fSlideG2_T1 = 0.0, double fSlideG3_T1 = 0.0,
                int iG_T0 = 3, int iG_T1 = 3, int iPickedEnd = 0, bool bDebug = false)
        {
            if (iG_T0 < 0 && iG_T1 < 0) return (null, "Both continuity inputs are None.", null);
            if (nc_In.IsPeriodic) return (null, "Input curve is periodic.", null);
            
            // Baseline verification
            if (Math.Abs(fScale_T0 - 1.0) > RhinoMath.ZeroTolerance || Math.Abs(fScale_T1 - 1.0) > RhinoMath.ZeroTolerance) { }
            else if (Math.Abs(fSlideG2_T0) > RhinoMath.ZeroTolerance || Math.Abs(fSlideG3_T0) > RhinoMath.ZeroTolerance ||
                     Math.Abs(fSlideG2_T1) > RhinoMath.ZeroTolerance || Math.Abs(fSlideG3_T1) > RhinoMath.ZeroTolerance) { }
            else return (null, "All scale and slide values result in no change to the geometry.", null);

            // --- POINT ALLOCATION ENGINE ---
            int N = nc_In.Points.Count;
            int req_0 = Math.Max(0, iG_T0 + 1);
            int req_1 = Math.Max(0, iG_T1 + 1);

            bool bOverlap = false;
            int alloc_0, alloc_1;

            if (req_0 + req_1 > N)
            {
                bOverlap = true;
                if (req_0 > req_1)
                {
                    alloc_0 = Math.Min(req_0, N);
                    alloc_1 = N - alloc_0;
                }
                else if (req_1 > req_0)
                {
                    alloc_1 = Math.Min(req_1, N);
                    alloc_0 = N - alloc_1;
                }
                else
                {
                    if (iPickedEnd == 0)
                    {
                        alloc_0 = Math.Min(req_0, N);
                        alloc_1 = N - alloc_0;
                    }
                    else
                    {
                        alloc_1 = Math.Min(req_1, N);
                        alloc_0 = N - alloc_1;
                    }
                }
            }
            else
            {
                alloc_0 = req_0;
                alloc_1 = req_1;
            }

            int max_mod_T0 = alloc_0 - 1;
            int max_mod_T1 = alloc_1 - 1;

            int scale_limit_T0 = max_mod_T0 + 1;
            int scale_limit_T1 = max_mod_T1 + 1;

            if (bDebug && bOverlap)
            {
                RhinoApp.WriteLine($"Overlap detected. T0 continuity capped at G{max_mod_T0}, T1 capped at G{max_mod_T1}.");
            }

            // --- BASELINE CHECK LOCAL FUNCTION ---
            bool IsBaseline(double s, double g2, double g3, int scaleLimit)
            {
                if (scaleLimit < 2) return true;
                bool b = Math.Abs(s - 1.0) <= RhinoMath.ZeroTolerance;
                if (scaleLimit > 2) b = b && (Math.Abs(g2) <= RhinoMath.ZeroTolerance);
                if (scaleLimit > 3) b = b && (Math.Abs(g3) <= RhinoMath.ZeroTolerance);
                return b;
            }

            bool base_T0 = IsBaseline(fScale_T0, fSlideG2_T0, fSlideG3_T0, scale_limit_T0);
            bool base_T1 = IsBaseline(fScale_T1, fSlideG2_T1, fSlideG3_T1, scale_limit_T1);

            if (base_T0 && base_T1)
                return (null, "Input parameters do not lead to modification of the geometry.", (max_mod_T0, max_mod_T1, bOverlap));

            Point3d[] pts_Prime = new Point3d[N];
            for (int i = 0; i < N; i++)
            {
                pts_Prime[i] = nc_In.Points[i].Location;
            }

            double unit_scale = RhinoMath.UnitScale(UnitSystem.Centimeters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            double min_dist = 1e-6 * unit_scale;

            var knots = nc_In.Knots;
            int deg = nc_In.Degree;

            // ----------------------------------------------------
            // SCALE T0 END (4D Quotient Rule)
            // ----------------------------------------------------
            if (!base_T0)
            {
                int limit = scale_limit_T0;
                int[] idx = new int[4];
                double[] w = new double[4];
                
                for (int i = 0; i < 4; i++) 
                {
                    idx[i] = i < N ? i : N - 1;
                    w[i] = nc_In.Points.GetWeight(idx[i]);
                }
                
                double t0 = knots[deg - 1];
                double[] deltas = new double[3];
                deltas[0] = (knots.Count > deg) ? knots[deg] - t0 : 1.0;
                deltas[1] = (knots.Count > deg + 1) ? knots[deg + 1] - t0 : deltas[0];
                deltas[2] = (knots.Count > deg + 2) ? knots[deg + 2] - t0 : deltas[1];

                ApplyQuotientRuleEndBulge(ref pts_Prime, idx, w, deltas, deg, limit, fScale_T0, fSlideG2_T0, fSlideG3_T0);
            }

            // ----------------------------------------------------
            // SCALE T1 END (4D Quotient Rule Backwards)
            // ----------------------------------------------------
            if (!base_T1)
            {
                int limit = scale_limit_T1;
                int last = N - 1;
                int[] idx = new int[4];
                double[] w = new double[4];
                
                for (int i = 0; i < 4; i++) 
                {
                    idx[i] = last - i >= 0 ? last - i : 0;
                    w[i] = nc_In.Points.GetWeight(idx[i]);
                }
                
                double t1 = knots[N - 1]; // Max domain parameter
                double[] deltas = new double[3];
                deltas[0] = (N - 2 >= 0) ? t1 - knots[N - 2] : 1.0;
                deltas[1] = (N - 3 >= 0) ? t1 - knots[N - 3] : deltas[0];
                deltas[2] = (N - 4 >= 0) ? t1 - knots[N - 4] : deltas[1];

                ApplyQuotientRuleEndBulge(ref pts_Prime, idx, w, deltas, deg, limit, fScale_T1, fSlideG2_T1, fSlideG3_T1);
            }

            // Enforce minimum distance (ignoring inherently stacked singularity points)
            for (int i = 0; i < N - 1; i++)
            {
                if (pts_Prime[i].DistanceTo(pts_Prime[i + 1]) < min_dist)
                {
                    double orig_dist = nc_In.Points[i].Location.DistanceTo(nc_In.Points[i + 1].Location);
                    if (orig_dist >= min_dist)
                    {
                        string sReport = "Minimum control point distance (1e-6 cm) violated. Is Scale too small?";
                        if (bDebug) RhinoApp.WriteLine(sReport);
                        return (null, sReport, null);
                    }
                }
            }

            // Reconstruct final output curve
            NurbsCurve nc_Out = (NurbsCurve)nc_In.Duplicate();
            for (int i = 0; i < N; i++)
            {
                nc_Out.Points.SetPoint(i, pts_Prime[i], nc_In.Points.GetWeight(i));
            }

            return (nc_Out, null, (max_mod_T0, max_mod_T1, bOverlap));
        }

        /// <summary>
        /// Internal solver applying exact geometric reparameterization algebraically to 4D homogeneous vectors.
        /// </summary>
        private static void ApplyQuotientRuleEndBulge(
            ref Point3d[] pts_Prime, int[] indices, double[] w, double[] delta, 
            int degree, int limit, double scale, double slide2, double slide3)
        {
            if (limit <= 1) return;

            double p_f = degree;
            double d1 = delta[0] < 1e-12 ? 1.0 : delta[0];
            double d2 = delta[1] < 1e-12 ? d1 : delta[1];
            double d3 = delta[2] < 1e-12 ? d2 : delta[2];

            // Extract Generic B-Spline Derivative Coefficients
            double a1 = p_f / d1;
            double a0 = -a1;

            double b2 = (p_f * (p_f - 1.0)) / (d1 * d2);
            double b0 = (p_f * (p_f - 1.0)) / (d1 * d1);
            double b1 = -b2 - b0;

            double c3 = 0, c2 = 0, c1 = 0, c0 = 0;
            if (degree >= 3)
            {
                c3 = (p_f * (p_f - 1.0) * (p_f - 2.0)) / (d1 * d2 * d3);
                c0 = -(p_f * (p_f - 1.0) * (p_f - 2.0)) / (d1 * d1 * d1);
                double c_term1 = (p_f * (p_f - 1.0) * (p_f - 2.0)) / (d1 * d2 * d2);
                double c_term2 = (p_f * (p_f - 1.0) * (p_f - 2.0)) / (d1 * d1 * d2);
                c2 = -c3 - c_term1 - c_term2;
                c1 = -c3 - c2 - c0;
            }

            Vector3d p0 = (Vector3d)pts_Prime[indices[0]];
            double w0 = w[0];

            Vector3d p1 = (Vector3d)pts_Prime[indices[1]];
            double w1 = w[1];

            // Original 1st Derivative (Quotient Rule)
            Vector3d A1 = p1 * (a1 * w1) + p0 * (a0 * w0);
            double W1 = w1 * a1 + w0 * a0;
            Vector3d C1 = (A1 - p0 * W1) / w0;

            // Target 1st Derivative (Pure Scale)
            double m = scale;
            Vector3d C1_tgt = C1 * m;

            // Back-solve to 3D point
            Vector3d A1_tgt = C1_tgt * w0 + p0 * W1;
            Vector3d p1_tgt = (A1_tgt - p0 * (a0 * w0)) / (a1 * w1);
            
            pts_Prime[indices[1]] = (Point3d)p1_tgt;

            if (limit > 2 && degree >= 2)
            {
                Vector3d p2 = (Vector3d)pts_Prime[indices[2]];
                double w2 = w[2];

                // Original 2nd Derivative (Quotient Rule)
                Vector3d A2 = p2 * (b2 * w2) + p1 * (b1 * w1) + p0 * (b0 * w0);
                double W2 = w2 * b2 + w1 * b1 + w0 * b0;
                Vector3d C2 = (A2 - C1 * 2.0 * W1 - p0 * W2) / w0;

                // Parametric Slide Parameter (Translates 3D slide to parametric acceleration)
                double k2 = m * slide2 * ((b2 * w2) / (a1 * w1));

                // Target 2nd Derivative (Geometrically locked to tangent path)
                Vector3d C2_tgt = C2 * (m * m) + C1 * k2;

                // Back-solve to 3D point
                Vector3d A2_tgt = C2_tgt * w0 + C1_tgt * 2.0 * W1 + p0 * W2;
                Vector3d p2_tgt = (A2_tgt - p1_tgt * (b1 * w1) - p0 * (b0 * w0)) / (b2 * w2);

                pts_Prime[indices[2]] = (Point3d)p2_tgt;

                if (limit > 3 && degree >= 3)
                {
                    Vector3d p3 = (Vector3d)pts_Prime[indices[3]];
                    double w3 = w[3];

                    // Original 3rd Derivative (Quotient Rule)
                    Vector3d A3 = p3 * (c3 * w3) + p2 * (c2 * w2) + p1 * (c1 * w1) + p0 * (c0 * w0);
                    double W3 = w3 * c3 + w2 * c2 + w1 * c1 + w0 * c0;
                    Vector3d C3 = (A3 - C2 * 3.0 * W1 - C1 * 3.0 * W2 - p0 * W3) / w0;

                    // Parametric Slide Parameter
                    double k3 = m * slide3 * ((c3 * w3) / (a1 * w1));

                    // Target 3rd Derivative (Geometrically locked reparameterization cross-term)
                    Vector3d C3_tgt = C3 * (m * m * m) + C2 * (3.0 * m * k2) + C1 * k3;

                    // Back-solve to 3D point
                    Vector3d A3_tgt = C3_tgt * w0 + C2_tgt * 3.0 * W1 + C1_tgt * 3.0 * W2 + p0 * W3;
                    Vector3d p3_tgt = (A3_tgt - p2_tgt * (c2 * w2) - p1_tgt * (c1 * w1) - p0 * (c0 * w0)) / (c3 * w3);

                    pts_Prime[indices[3]] = (Point3d)p3_tgt;
                }
            }
        }
    }
}