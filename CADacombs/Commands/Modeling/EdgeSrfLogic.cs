using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public static class EdgeSrfLogic
    {
        public static Result Execute(RhinoDoc doc, List<ObjRef> rhCrvs_In, List<int> iContinuity_PerCrv)
        {
            double tol = doc.ModelAbsoluteTolerance;

            if (rhCrvs_In.Count < 2 || rhCrvs_In.Count > 4)
            {
                RhinoApp.WriteLine($"{rhCrvs_In.Count} curves provided. Need 2, 3, or 4.");
                return Result.Failure;
            }

            // 1. EXTRACT GEOMETRY & ORIGINAL ISOSTATUS
            // We use a Tuple to remember the original side of the reference surface!
            List<(GeometryBase Geom, IsoStatus Side)> geoms_Nurbs = new List<(GeometryBase, IsoStatus)>();
            List<Curve> cs_R = new List<Curve>();

            for (int i = 0; i < rhCrvs_In.Count; i++)
            {
                var geomNurbs = NurbsMatchMath.GetNurbsGeomFromObjRef(rhCrvs_In[i], iContinuity_PerCrv[i], EdgeSrfOptions.Echo, EdgeSrfOptions.Debug, out IsoStatus side);
                if (geomNurbs == null)
                {
                    RhinoApp.WriteLine("NURBS geometry could not be obtained from input.");
                    return Result.Failure;
                }
                
                geoms_Nurbs.Add((geomNurbs, side));

                if (geomNurbs is NurbsSurface nsR)
                    cs_R.Add(NurbsMatchMath.GetIsoCurveOfSide(side, nsR).ToNurbsCurve());
                else
                    cs_R.Add(((Curve)geomNurbs).ToNurbsCurve());
            }

            if (NurbsMatchMath.DoAnyCrvsCompletelyOverlap(cs_R, tol))
            {
                RhinoApp.WriteLine("Some edges/trims completely overlap.");
                return Result.Failure;
            }

            // 2. NETWORK PREPARATION AND BASE SURFACE GENERATION
            NurbsSurface ns_M_Start = null;
            
            if (rhCrvs_In.Count == 4)
            {
                if (!NurbsMatchMath.DoCrvsFormAClosedLoop(cs_R, tol))
                {
                    cs_R = NurbsMatchMath.TrimCurvesAtIntersections(cs_R, tol);
                    if (!NurbsMatchMath.DoCrvsFormAClosedLoop(cs_R, tol))
                    {
                        RhinoApp.WriteLine("Curves do not form a closed loop, and attempt to split trims failed.");
                        return Result.Failure;
                    }
                }
            }
            else if (rhCrvs_In.Count == 3)
            {
                var pCrvs = Curve.JoinCurves(cs_R, tol);
                if (pCrvs.Length > 0)
                {
                    if (pCrvs[0].IsClosed)
                    {
                        RhinoApp.WriteLine("3-curve input joins into a closed loop. This is not (yet) supported.");
                        return Result.Failure;
                    }
                    Curve polyCrv = pCrvs[0];
                    Curve nc_ToAdd = new LineCurve(polyCrv.PointAtStart, polyCrv.PointAtEnd).ToNurbsCurve();
                    cs_R.Add(nc_ToAdd);
                    geoms_Nurbs.Add((nc_ToAdd, IsoStatus.None));
                    iContinuity_PerCrv.Add(-1);
                }
            }
            else if (rhCrvs_In.Count == 2)
            {
                bool adjacent = false;
                Point3d pt = Point3d.Unset;
                double adjTol = tol * 10.0; 

                if (cs_R[0].PointAtEnd.DistanceTo(cs_R[1].PointAtStart) <= adjTol ||
                    cs_R[0].PointAtEnd.DistanceTo(cs_R[1].PointAtEnd) <= adjTol)
                {
                    adjacent = true; pt = cs_R[0].PointAtEnd;
                }
                else if (cs_R[0].PointAtStart.DistanceTo(cs_R[1].PointAtStart) <= adjTol ||
                         cs_R[0].PointAtStart.DistanceTo(cs_R[1].PointAtEnd) <= adjTol)
                {
                    adjacent = true; pt = cs_R[0].PointAtStart;
                }

                if (adjacent) // Adjacent curves
                {
                    if (cs_R[0].PointAtEnd.DistanceTo(pt) <= adjTol) cs_R[0].Reverse();
                    if (cs_R[1].PointAtEnd.DistanceTo(pt) <= adjTol) cs_R[1].Reverse();

                    SumSurface sumsrf = SumSurface.Create(cs_R[0], cs_R[1]);
                    if (sumsrf != null) 
                    {
                        ns_M_Start = sumsrf.ToNurbsSurface();
                        
                        // FIX: Extract the 2 missing edges from the SumSurface just like Python!
                        Curve[] temp_C = new Curve[4];
                        IsoStatus[] temp_sides = { IsoStatus.West, IsoStatus.South, IsoStatus.East, IsoStatus.North };
                        for (int i = 0; i < 4; i++) temp_C[i] = NurbsMatchMath.GetIsoCurveOfSide(temp_sides[i], ns_M_Start);
                        
                        int?[] temp_map = NurbsMatchMath.FindMatchingCurvesByEndPoints(temp_C, cs_R.ToArray(), adjTol);
                        for (int i = 0; i < 4; i++)
                        {
                            if (!temp_map[i].HasValue)
                            {
                                var nc_ToAdd = temp_C[i].ToNurbsCurve();
                                cs_R.Add(nc_ToAdd);
                                geoms_Nurbs.Add((nc_ToAdd, IsoStatus.None));
                                iContinuity_PerCrv.Add(-1);
                            }
                        }
                    }
                }
                else // Opposite curves
                {
                    Point3d[] pts0 = new Point3d[2];
                    Point3d[] pts1 = new Point3d[2];

                    if (Curve.DoDirectionsMatch(cs_R[0], cs_R[1]))
                    {
                        pts0[0] = cs_R[0].PointAtStart; pts1[0] = cs_R[1].PointAtStart;
                        pts0[1] = cs_R[0].PointAtEnd;   pts1[1] = cs_R[1].PointAtEnd;
                    }
                    else
                    {
                        pts0[0] = cs_R[0].PointAtStart; pts1[0] = cs_R[1].PointAtEnd;
                        pts0[1] = cs_R[0].PointAtEnd;   pts1[1] = cs_R[1].PointAtStart;
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        var nc_ToAdd = new LineCurve(pts0[i], pts1[i]).ToNurbsCurve();
                        nc_ToAdd.IncreaseDegree((iContinuity_PerCrv[i] + 1) * 2 - 1);
                        cs_R.Add(nc_ToAdd);
                        geoms_Nurbs.Add((nc_ToAdd, IsoStatus.None));
                        iContinuity_PerCrv.Add(-1);
                    }
                }
            }

            if (ns_M_Start == null)
            {
                Brep bRes = Brep.CreateEdgeSurface(cs_R);
                if (bRes == null || bRes.Surfaces.Count != 1)
                {
                    RhinoApp.WriteLine("CreateEdgeSurface failed or created polyface brep.");
                    return Result.Failure;
                }
                ns_M_Start = bRes.Surfaces[0].ToNurbsSurface();
            }

            if (ns_M_Start.IsRational)
            {
                NurbsMatchMath.MakeNonRational(ns_M_Start);
            }

            // ==========================================
            // PHASE 3B PART 2: MATCHING & CORNER AVERAGING
            // ==========================================
            
            IsoStatus[] sides = { IsoStatus.West, IsoStatus.South, IsoStatus.East, IsoStatus.North };
            Curve[] cs_C = new Curve[4];
            for (int i = 0; i < 4; i++) cs_C[i] = NurbsMatchMath.GetIsoCurveOfSide(sides[i], ns_M_Start);

            double matchTol = tol * 10.0;
            int?[] idx_R_per_M = NurbsMatchMath.FindMatchingCurvesByEndPoints(cs_C, cs_R.ToArray(), matchTol);

            for (int i = 0; i < 4; i++)
            {
                if (!idx_R_per_M[i].HasValue)
                {
                    RhinoApp.WriteLine($"Could not map reference curve to {sides[i]} side of base surface.");
                    return Result.Failure;
                }
            }

            Dictionary<IsoStatus, GeometryBase> geoms_Nurbs_AR = new Dictionary<IsoStatus, GeometryBase>();
            Dictionary<IsoStatus, int> iConts_Per_Side = new Dictionary<IsoStatus, int>();

            for (int i = 0; i < 4; i++)
            {
                IsoStatus side = sides[i];
                int rIdx = idx_R_per_M[i].Value;
                
                // PASS IN THE ORIGINAL SIDE (sideR) FROM THE TUPLE
                IsoStatus sideR = geoms_Nurbs[rIdx].Side;
                GeometryBase refGeom = geoms_Nurbs[rIdx].Geom;
                
                var aligned = NurbsMatchMath.CreateAlignedRefGeom_PerStart(refGeom, sideR, ns_M_Start, side, matchTol);
                if (aligned == null) return Result.Failure;
                
                geoms_Nurbs_AR[side] = aligned;
                int cont = iContinuity_PerCrv[rIdx];

                if (aligned is NurbsCurve && cont > 0)
                {
                    if (EdgeSrfOptions.Echo) RhinoApp.WriteLine($"Reduced {side} target from {cont} to 0 (Input is a curve).");
                    cont = 0;
                }
                else if (aligned is NurbsSurface nsRef && cont >= 2)
                {
                    if (!nsRef.IsPlanar(1e-8))
                    {
                        if (EdgeSrfOptions.Echo) RhinoApp.WriteLine($"Reduced {side} target from {cont} to 1 (Surface not planar).");
                        cont = 1;
                    }
                }
                iConts_Per_Side[side] = cont;
            }

            // Increase Coons Degree if needed
            if (ns_M_Start.Points.CountU < (iConts_Per_Side[IsoStatus.West] + iConts_Per_Side[IsoStatus.East] + 2))
                ns_M_Start.IncreaseDegreeU(ns_M_Start.Degree(0) + (iConts_Per_Side[IsoStatus.West] + iConts_Per_Side[IsoStatus.East] + 2) - ns_M_Start.Points.CountU);
            
            if (ns_M_Start.Points.CountV < (iConts_Per_Side[IsoStatus.South] + iConts_Per_Side[IsoStatus.North] + 2))
                ns_M_Start.IncreaseDegreeV(ns_M_Start.Degree(1) + (iConts_Per_Side[IsoStatus.South] + iConts_Per_Side[IsoStatus.North] + 2) - ns_M_Start.Points.CountV);

            // Match Degrees, Domains, and Knots
            foreach (var side in sides)
            {
                NurbsMatchMath.TransferHigherDegree(ns_M_Start, geoms_Nurbs_AR[side], side, side);
                NurbsMatchMath.TransferDomain(ns_M_Start, geoms_Nurbs_AR[side], side, side);
                NurbsMatchMath.TransferUniqueKnotVector(geoms_Nurbs_AR[side], ns_M_Start, side, side);
                NurbsMatchMath.TransferUniqueKnotVector(ns_M_Start, geoms_Nurbs_AR[side], side, side);
            }

            // Iterative G1/G2 Matching & Corner Tracking
            NurbsSurface ns_WIP = (NurbsSurface)ns_M_Start.Duplicate();
            Dictionary<int, NurbsMatchMath.CornerAdjustment> g1Corners = new Dictionary<int, NurbsMatchMath.CornerAdjustment>();
            Dictionary<int, NurbsMatchMath.CornerAdjustment> g2Corners = new Dictionary<int, NurbsMatchMath.CornerAdjustment>();

            foreach (var side in sides)
            {
                int cont = iConts_Per_Side[side];
                if (cont < 1 || geoms_Nurbs_AR[side] is NurbsCurve) continue;

                NurbsSurface ns_R = (NurbsSurface)geoms_Nurbs_AR[side];
                
                bool modT0 = (side == IsoStatus.West || side == IsoStatus.East) ? iConts_Per_Side[IsoStatus.South] == -1 : iConts_Per_Side[IsoStatus.West] == -1;
                bool modT1 = (side == IsoStatus.West || side == IsoStatus.East) ? iConts_Per_Side[IsoStatus.North] == -1 : iConts_Per_Side[IsoStatus.East] == -1;

                var ns_G1 = NurbsMatchMath.SetContinuity_G1(ns_M_Start, ns_WIP, side, ns_R, side, modT0, modT1);
                if (ns_G1 != null)
                {
                    ns_WIP.Dispose();
                    ns_WIP = ns_G1;

                    int[] idxM1 = NurbsMatchMath.GetPtRowIndicesPerG(ns_M_Start, side, 1);
                    if (idxM1.Length >= 4)
                    {
                        ns_R.TryGetPlane(out Plane planeR, 1e-9);
                        foreach (int i in new[] { 1, idxM1.Length - 2 })
                        {
                            int m1 = idxM1[i];
                            var (u, v) = NurbsMatchMath.GetUvIdx(ns_WIP, m1);
                            Point3d pt = ns_WIP.Points.GetControlPoint(u, v).Location;

                            if (g1Corners.ContainsKey(m1)) g1Corners[m1].Add(pt, planeR, null);
                            else g1Corners[m1] = new NurbsMatchMath.CornerAdjustment(pt, planeR, null);
                        }
                    }
                }

                if (cont == 2)
                {
                    var ns_G2 = NurbsMatchMath.SetContinuity_G2(ns_M_Start, ns_WIP, side, ns_R, side, modT0, modT1, EdgeSrfOptions.Debug, false);
                    if (ns_G2 != null)
                    {
                        ns_WIP.Dispose();
                        ns_WIP = ns_G2;

                        int[] idxM2 = NurbsMatchMath.GetPtRowIndicesPerG(ns_M_Start, side, 2);
                        if (idxM2.Length >= 6)
                        {
                            foreach (int i in new[] { 2, idxM2.Length - 3 })
                            {
                                int m2 = idxM2[i];
                                var (u, v) = NurbsMatchMath.GetUvIdx(ns_WIP, m2);
                                Point3d pt = ns_WIP.Points.GetControlPoint(u, v).Location;

                                if (g2Corners.ContainsKey(m2)) g2Corners[m2].Add(pt, null, null);
                                else g2Corners[m2] = new NurbsMatchMath.CornerAdjustment(pt, null, null);
                            }
                        }
                    }
                }
            }

            // Corner Averaging
            foreach (var kvp in g1Corners)
            {
                var (u, v) = NurbsMatchMath.GetUvIdx(ns_WIP, kvp.Key);
                Point3d avgPt = kvp.Value.Points[0];
                
                if (kvp.Value.Points.Count == 2)
                {
                    if (!kvp.Value.Planes[0].HasValue && kvp.Value.Planes[1].HasValue)
                    {
                        Point3d proj = kvp.Value.Points[0];
                        proj.Transform(Transform.PlanarProjection(kvp.Value.Planes[1].Value));
                        avgPt = (proj + kvp.Value.Points[1]) / 2.0;
                    }
                    else if (kvp.Value.Planes[0].HasValue && !kvp.Value.Planes[1].HasValue)
                    {
                        Point3d proj = kvp.Value.Points[1];
                        proj.Transform(Transform.PlanarProjection(kvp.Value.Planes[0].Value));
                        avgPt = (kvp.Value.Points[0] + proj) / 2.0;
                    }
                    else
                    {
                        avgPt = (kvp.Value.Points[0] + kvp.Value.Points[1]) / 2.0;
                    }
                }
                ns_WIP.Points.SetPoint(u, v, avgPt);
            }

            foreach (var kvp in g2Corners)
            {
                var (u, v) = NurbsMatchMath.GetUvIdx(ns_WIP, kvp.Key);
                Point3d avgPt = kvp.Value.Points[0];
                if (kvp.Value.Points.Count == 2) avgPt = (kvp.Value.Points[0] + kvp.Value.Points[1]) / 2.0;
                ns_WIP.Points.SetPoint(u, v, avgPt);
            }

            // Output Final Geometry
            doc.Objects.AddSurface(ns_WIP);
            doc.Views.Redraw();

            if (EdgeSrfOptions.Echo)
                RhinoApp.WriteLine("EdgeSrf Complete: Matched continuity applied and corner points averaged.");

            return Result.Success;
        }
    }
}