using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core
{
    /// <summary>
    /// The core shared mathematical solvers and point-allocation engine.
    /// </summary>
    public static class EndBulgeMath
    {
        /// <summary>
        /// Evaluates if a NURBS curve can mathematically maintain G3 continuity.
        /// </summary>
        public static bool CanMaintainG3(NurbsCurve nc, bool evalT1End)
        {
            if (nc == null) return false;
            if (nc.Points.Count < 4) return false;
            if (nc.SpanCount == 1) return true;

            var knots = nc.Knots;
            int degree = nc.Degree;
            int iKnot = evalT1End ? (knots.Count - degree - 1) : degree;
            return knots.KnotMultiplicity(iKnot) >= 3;
        }

        /// <summary>
        /// The master curve generation and point allocation algorithm.
        /// Returns a Tuple containing the resulting Curve, an Error string (if any), and mathematical Info.
        /// (Note: For continuity inputs, -1 equals 'None', 0=G0, 1=G1, 2=G2, 3=G3)
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

            // STRICT NATIVE CONTINUITY LOCKING:
            // We no longer distribute "free" interior points to smooth the curve. 
            // Translation is strictly clamped to the points governed by the active continuity tier.
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

            // Extract points safely using native C# arrays
            Point3d[] pts_Prime = new Point3d[N];
            for (int i = 0; i < N; i++)
            {
                pts_Prime[i] = nc_In.Points[i].Location;
            }

            // Document units scale logic
            double unit_scale = RhinoMath.UnitScale(UnitSystem.Centimeters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            double min_dist = 1e-6 * unit_scale;

            // ----------------------------------------------------
            // SCALE T0 END
            // ----------------------------------------------------
            if (!base_T0)
            {
                Point3d p0 = nc_In.Points[0].Location;
                Transform xform_T0 = Transform.Scale(p0, fScale_T0);

                for (int i = 1; i < scale_limit_T0; i++)
                {
                    Point3d pt_p = nc_In.Points[i].Location;
                    pt_p.Transform(xform_T0);
                    pts_Prime[i] = pt_p;
                }

                if (scale_limit_T0 > 1)
                {
                    Point3d p1 = nc_In.Points[1].Location;
                    Point3d p1p = pts_Prime[1];
                    Vector3d slide_vec = p1p - p0;
                    double orig_len_T0 = (p1 - p0).Length;

                    if (scale_limit_T0 > 2)
                    {
                        Point3d p2 = nc_In.Points[2].Location;
                        Point3d p2p_base;

                        if (max_mod_T0 >= 2 && orig_len_T0 > min_dist)
                        {
                            double m2 = Math.Pow((p1p - p0).Length / orig_len_T0, 2.0);
                            p2p_base = (2.0 * p1p - p0) + m2 * (-2.0 * p1 + p2 + p0);
                        }
                        else
                        {
                            p2p_base = pts_Prime[2];
                        }

                        Vector3d p2_slide = slide_vec * fSlideG2_T0;
                        pts_Prime[2] = p2p_base + p2_slide;

                        if (scale_limit_T0 > 3)
                        {
                            Point3d p3 = nc_In.Points[3].Location;
                            Point3d p3p_base;
                            Vector3d p3_comp;

                            if (max_mod_T0 >= 3 && orig_len_T0 > min_dist)
                            {
                                double m3 = Math.Pow((p1p - p0).Length / orig_len_T0, 3.0);
                                p3p_base = (3.0 * p2p_base - 3.0 * p1p + p0) + m3 * (p3 - 3.0 * p2 + 3.0 * p1 - p0);
                                p3_comp = 3.0 * p2_slide;
                            }
                            else
                            {
                                p3p_base = pts_Prime[3];
                                p3_comp = Vector3d.Zero;
                            }

                            Vector3d p3_slide = slide_vec * fSlideG3_T0;
                            pts_Prime[3] = p3p_base + p3_comp + p3_slide;
                        }
                    }
                }
            }

            // ----------------------------------------------------
            // SCALE T1 END
            // ----------------------------------------------------
            if (!base_T1)
            {
                int last = N - 1;
                Point3d p0 = nc_In.Points[last].Location;
                Transform xform_T1 = Transform.Scale(p0, fScale_T1);

                for (int i = 1; i < scale_limit_T1; i++)
                {
                    int idx = last - i;
                    Point3d pt_p = nc_In.Points[idx].Location;
                    pt_p.Transform(xform_T1);
                    pts_Prime[idx] = pt_p;
                }

                if (scale_limit_T1 > 1)
                {
                    Point3d p1 = nc_In.Points[last - 1].Location;
                    Point3d p1p = pts_Prime[last - 1];
                    Vector3d slide_vec = p1p - p0;
                    double orig_len_T1 = (p1 - p0).Length;

                    if (scale_limit_T1 > 2)
                    {
                        Point3d p2 = nc_In.Points[last - 2].Location;
                        Point3d p2p_base;

                        if (max_mod_T1 >= 2 && orig_len_T1 > min_dist)
                        {
                            double m2 = Math.Pow((p1p - p0).Length / orig_len_T1, 2.0);
                            p2p_base = (2.0 * p1p - p0) + m2 * (-2.0 * p1 + p2 + p0);
                        }
                        else
                        {
                            p2p_base = pts_Prime[last - 2];
                        }

                        Vector3d p2_slide = slide_vec * fSlideG2_T1;
                        pts_Prime[last - 2] = p2p_base + p2_slide;

                        if (scale_limit_T1 > 3)
                        {
                            Point3d p3 = nc_In.Points[last - 3].Location;
                            Point3d p3p_base;
                            Vector3d p3_comp;

                            if (max_mod_T1 >= 3 && orig_len_T1 > min_dist)
                            {
                                double m3 = Math.Pow((p1p - p0).Length / orig_len_T1, 3.0);
                                p3p_base = (3.0 * p2p_base - 3.0 * p1p + p0) + m3 * (p3 - 3.0 * p2 + 3.0 * p1 - p0);
                                p3_comp = 3.0 * p2_slide;
                            }
                            else
                            {
                                p3p_base = pts_Prime[last - 3];
                                p3_comp = Vector3d.Zero;
                            }

                            Vector3d p3_slide = slide_vec * fSlideG3_T1;
                            pts_Prime[last - 3] = p3p_base + p3_comp + p3_slide;
                        }
                    }
                }
            }

            // Enforce minimum distance (but ignore inherently stacked singularity points)
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
    }
}