using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace CADacombs.Core
{
    public static class NurbsMatchMath
    {
        // ==============================================================================
        // 1. MASTER ENTRY POINT
        // ==============================================================================
        public static (NurbsSurface Surface, int AchievedContinuity) CreateSurface(
            NurbsSurface nsMIn, IsoStatus sideM, ObjRef objRefRef, bool bMatchWithParamsAligned,
            int iContinuity, int iPreserveOtherEnd, bool bMaintainDegree, bool bEcho, bool bDebug, bool bAddRefs)
        {
            if (!nsMIn.IsValidWithLog(out string sLog))
            {
                RhinoApp.WriteLine(sLog);
                return (null, 0);
            }

            GeometryBase geomRNurbs = GetNurbsGeomFromObjRef(objRefRef, iContinuity, bEcho, bDebug, out IsoStatus sideR);
            if (geomRNurbs == null) return (null, 0);

            if (geomRNurbs is NurbsSurface nsRIn && nsRIn.IsRational) MakeNonRational(nsRIn);
            if (geomRNurbs is NurbsCurve ncRIn && ncRIn.IsRational) MakeNonRational(ncRIn);

            GeometryBase nurbsR = CreateAlignedRefGeom(geomRNurbs, sideR, nsMIn, sideM, bMatchWithParamsAligned);
            if (nurbsR == null || !nurbsR.IsValidWithLog(out sLog))
            {
                RhinoApp.WriteLine(sLog ?? "Invalid reference geometry.");
                return (null, 0);
            }

            IsoStatus sideBoth = sideM;
            NurbsSurface nsWIP = (NurbsSurface)nsMIn.Duplicate();

            // Match Degrees and Domains
            TransferHigherDegree(nurbsR, nsWIP, sideBoth, sideBoth);
            TransferHigherDegree(nsWIP, nurbsR, sideBoth, sideBoth);
            TransferDomain(nsWIP, nurbsR, sideBoth, sideBoth);

            // Match Knot Vectors
            TransferUniqueKnotVector(nurbsR, nsWIP, sideBoth, sideBoth);
            TransferUniqueKnotVector(nsWIP, nurbsR, sideBoth, sideBoth);

            // Add points/knots transversely if needed to support the continuity tiers
            int iDirFromMSide = (sideBoth == IsoStatus.East || sideBoth == IsoStatus.West) ? 0 : 1;
            int currentPtCt = iDirFromMSide == 1 ? nsWIP.Points.CountV : nsWIP.Points.CountU;
            int requiredPtCt = (iContinuity + 1) + (iPreserveOtherEnd == 0 ? 0 : iPreserveOtherEnd);

            if (!bMaintainDegree)
            {
                int reqDegree = requiredPtCt - 1;
                int currentDegree = nsWIP.Degree(iDirFromMSide);
                if (currentDegree < reqDegree)
                {
                    if (iDirFromMSide == 1) nsWIP.IncreaseDegreeV(reqDegree);
                    else nsWIP.IncreaseDegreeU(reqDegree);
                }
            }
            else if (requiredPtCt > currentPtCt)
            {
                int knotsToAdd = requiredPtCt - currentPtCt;
                var knots = iDirFromMSide == 1 ? nsWIP.KnotsV : nsWIP.KnotsU;
                AddKnotsUniformly(knots, knots.Count + knotsToAdd);
            }

            NurbsSurface nsMBeforeAny = (NurbsSurface)nsWIP.Duplicate();

            // Continuity Adjustments
            NurbsSurface nsG0 = SetContinuity_G0(nsMBeforeAny, sideBoth, nurbsR, sideBoth);
            if (nsG0 == null) return (null, 0);

            if (iContinuity == 0)
            {
                if (bEcho) RhinoApp.WriteLine("Modified surface toward G0.");
                if (AreInOutEpsilonEqual(nsMBeforeAny, nsG0, bEcho)) return (null, 0);
                return (nsG0, 0);
            }

            NurbsSurface nsG1 = SetContinuity_G1(nsMBeforeAny, nsG0, sideBoth, nurbsR, sideBoth, true, true);
            if (nsG1 == null)
            {
                if (AreInOutEpsilonEqual(nsMBeforeAny, nsG0, bEcho)) return (null, 0);
                return (nsG0, 0);
            }

            if (iContinuity == 1)
            {
                if (AreInOutEpsilonEqual(nsMBeforeAny, nsG1, bEcho)) return (null, 0);
                return (nsG1, 1);
            }

            NurbsSurface nsG2 = SetContinuity_G2(nsMBeforeAny, nsG1, sideBoth, nurbsR, sideBoth, true, true, bDebug, bAddRefs);
            if (nsG2 == null)
            {
                if (AreInOutEpsilonEqual(nsMBeforeAny, nsG1, bEcho)) return (null, 0);
                return (nsG1, 1);
            }

            if (AreInOutEpsilonEqual(nsMBeforeAny, nsG2, bEcho)) return (null, 0);
            return (nsG2, 2);
        }

        // ==============================================================================
        // 2. CONTINUITY SOLVERS (G0, G1, G2)
        // ==============================================================================
        public static NurbsSurface SetContinuity_G0(NurbsSurface nsMIn, IsoStatus sideM, GeometryBase nurbsR, IsoStatus sideR)
        {
            NurbsSurface nsOut = (NurbsSurface)nsMIn.Duplicate();
            int[] idxM0 = GetPtRowIndicesPerG(nsMIn, sideM, 0);
            
            Point3d[] ptsR;
            int[] idxR0;

            if (nurbsR is NurbsSurface nsRef)
            {
                ptsR = GetControlPoints(nsRef);
                idxR0 = GetPtRowIndicesPerG(nsRef, sideR, 0);
            }
            else
            {
                var ncRef = (NurbsCurve)nurbsR;
                ptsR = GetControlPoints(ncRef);
                idxR0 = new int[ncRef.Points.Count];
                for (int i = 0; i < idxR0.Length; i++) idxR0[i] = i;
            }

            for (int i = 0; i < idxM0.Length; i++)
            {
                var (uM, vM) = GetUvIdx(nsOut, idxM0[i]);
                int iR = idxR0[i];
                nsOut.Points.SetPoint(uM, vM, ptsR[iR]);
            }
            return nsOut;
        }

        public static NurbsSurface SetContinuity_G1(NurbsSurface nsMBefore, NurbsSurface nsMIn, IsoStatus sideM, 
            GeometryBase nurbsR, IsoStatus sideR, bool modRowEndT0, bool modRowEndT1)
        {
            if (!(nurbsR is NurbsSurface nsRef)) return null;

            NurbsSurface nsOut = (NurbsSurface)nsMIn.Duplicate();
            Point3d[] ptsMBefore = GetControlPoints(nsMBefore);
            Point3d[] ptsMOut = GetControlPoints(nsOut);
            Point3d[] ptsR = GetControlPoints(nsRef);

            int[] idxM0 = GetPtRowIndicesPerG(nsMIn, sideM, 0);
            int[] idxM1 = GetPtRowIndicesPerG(nsMIn, sideM, 1);
            int[] idxR1 = GetPtRowIndicesPerG(nsRef, sideR, 1);

            int start = modRowEndT0 ? 0 : 1;
            int stop = modRowEndT1 ? idxM0.Length : idxM0.Length - 1;

            if (nsRef.TryGetPlane(out Plane planeR, 1e-9))
            {
                var xform = Transform.PlanarProjection(planeR);
                for (int i = start; i < stop; i++)
                {
                    int m1 = idxM1[i];
                    Point3d ptTo = ptsMOut[m1];
                    ptTo.Transform(xform);
                    var (u, v) = GetUvIdx(nsOut, m1);
                    nsOut.Points.SetPoint(u, v, ptTo);
                }
                return nsOut;
            }

            // Pivot Step
            for (int i = start; i < stop; i++)
            {
                int m0 = idxM0[i], m1 = idxM1[i], r1 = idxR1[i];
                Point3d? rv = Pivot_M_about_C_onto_CR(ptsR[r1], ptsMOut[m0], ptsMOut[m1]);
                if (rv.HasValue)
                {
                    var (u, v) = GetUvIdx(nsOut, m1);
                    nsOut.Points.SetPoint(u, v, rv.Value);
                }
            }

            // Update local point tracker
            ptsMOut = GetControlPoints(nsOut);

            // Scale Step
            double ratioSum = 0;
            int sampleCount = 0;
            int[] samples = (modRowEndT0 == modRowEndT1) ? new[] { 0, idxM0.Length - 1 } : (modRowEndT0 ? new[] { idxM0.Length - 1 } : new[] { 0 });

            foreach (int i in samples)
            {
                int m0 = idxM0[i], m1 = idxM1[i], r1 = idxR1[i];
                double distM = ptsMBefore[m1].DistanceTo(ptsMBefore[m0]);
                double distR = ptsR[r1].DistanceTo(ptsMOut[m0]);
                if (distR > 1e-9)
                {
                    ratioSum += (distM / distR);
                    sampleCount++;
                }
            }
            
            double avgRatio = sampleCount > 0 ? (ratioSum / sampleCount) : 1.0;

            for (int i = start; i < stop; i++)
            {
                int m0 = idxM0[i], m1 = idxM1[i], r1 = idxR1[i];
                Vector3d vecR = ptsMOut[m0] - ptsR[r1];
                Point3d ptTo = ptsMOut[m0] + (vecR * avgRatio);
                var (u, v) = GetUvIdx(nsOut, m1);
                nsOut.Points.SetPoint(u, v, ptTo);
            }

            return nsOut;
        }

        public static NurbsSurface SetContinuity_G2(NurbsSurface nsMBefore, NurbsSurface nsMIn, IsoStatus sideM, 
            GeometryBase nurbsR, IsoStatus sideR, bool modRowEndT0, bool modRowEndT1, bool bDebug, bool bAddRefs)
        {
            if (!(nurbsR is NurbsSurface nsRef)) return null;

            NurbsSurface nsOut = (NurbsSurface)nsMIn.Duplicate();
            Point3d[] ptsAOut = GetControlPoints(nsOut);
            Point3d[] ptsR = GetControlPoints(nsRef);
            Point3d[] ptsMBefore = GetControlPoints(nsMBefore);

            int[] idxM0 = GetPtRowIndicesPerG(nsMIn, sideM, 0);
            int[] idxM1 = GetPtRowIndicesPerG(nsMIn, sideM, 1);
            int[] idxM2 = GetPtRowIndicesPerG(nsMIn, sideM, 2);
            
            int[] idxR0 = GetPtRowIndicesPerG(nsRef, sideR, 0);
            int[] idxR1 = GetPtRowIndicesPerG(nsRef, sideR, 1);
            int[] idxR2 = GetPtRowIndicesPerG(nsRef, sideR, 2);

            int dirR = (sideR == IsoStatus.South || sideR == IsoStatus.North) ? 0 : 1;
            
            if (nsRef.Degree(dirR) == 1)
            {
                for (int i = 0; i < idxM0.Length; i++)
                {
                    int a0 = idxM0[i], a1 = idxM1[i], a2 = idxM2[i];
                    Point3d ptTo = new Line(ptsAOut[a0], ptsAOut[a1]).ClosestPoint(ptsAOut[a2], false);
                    var (u, v) = GetUvIdx(nsOut, a2);
                    nsOut.Points.SetPoint(u, v, ptTo);
                }
                return nsOut;
            }

            double mA_Deg = MultiplierForDegree(nsOut, sideM);
            double mR_Deg = MultiplierForDegree(nsRef, sideR);
            double mA_Knots = MultiplierForAdjacentSimpleKnots(nsOut, sideM);
            double mR_Knots = MultiplierForAdjacentSimpleKnots(nsRef, sideR);

            int start = modRowEndT0 ? 0 : 2;
            int stop = modRowEndT1 ? idxM0.Length : idxM0.Length - 2;

            var a2s = new Dictionary<int, Point3d>();
            bool transA2ThruA1 = false;

            for (int i = start; i < stop; i++)
            {
                Point3d a0 = ptsR[idxR0[i]], a1 = ptsAOut[idxM1[i]];
                Point3d r0 = ptsR[idxR0[i]], r1 = ptsR[idxR1[i]], r2 = ptsR[idxR2[i]];

                double d1 = (a1 - r0).Length;
                double d2 = (r1 - r0).Length;

                // This check does not occur in the Python script.
                // Surfaces with singularites are not accepted, but stacked points are still possible.
                if (d2 < 1e-9) continue;
                
                double m = Math.Pow(d1 / d2, 2.0);
                double M = m * (mA_Knots / mR_Knots) * (mR_Deg / mA_Deg);

                Point3d a2 = 2.0 * a1 - (Vector3d)a0 + M * (-2.0 * r1 + r2 + r0);
                a2s[i] = a2;

                Plane plane = new Plane(a0, a1 - a0);
                Point3d p2 = plane.ClosestPoint(a2);

                if ((a2 - p2) * (a1 - a0) < 0.0) transA2ThruA1 = true;

                var (u, v) = GetUvIdx(nsOut, idxM2[i]);
                nsOut.Points.SetPoint(u, v, a2);
            }

            if (!transA2ThruA1) return nsOut;

            // Correct inverted G2 rows
            double m0m1_ratios_sum = 0.0;
            int[] refs = (modRowEndT0 && modRowEndT1) ? new int[idxM0.Length] : (!modRowEndT0 && !modRowEndT1 ? new[] { 0, idxM0.Length - 1 } : (modRowEndT0 ? new[] { 0 } : new[] { idxM0.Length - 1 }));
            if (refs.Length > 2) for(int k=0; k<refs.Length; k++) refs[k]=k;

            foreach (int i in refs)
            {
                Point3d a0 = ptsMBefore[idxM0[i]], a1 = ptsMBefore[idxM1[i]], a2 = ptsMBefore[idxM2[i]];
                Plane plane = new Plane(a0, a1 - a0);
                Point3d p2 = plane.ClosestPoint(a2);
                m0m1_ratios_sum += (a2 - p2).Length / (a1 - a0).Length;
            }
            double avgRatio = m0m1_ratios_sum / refs.Length;

            for (int i = start; i < stop; i++)
            {
                Point3d a0 = ptsAOut[idxM0[i]], a1 = ptsAOut[idxM1[i]];
                Plane plane = new Plane(a0, a1 - a0);
                plane.Translate(avgRatio * (a1 - a0));
                Point3d correctedA2 = plane.ClosestPoint(a2s[i]);

                var (u, v) = GetUvIdx(nsOut, idxM2[i]);
                nsOut.Points.SetPoint(u, v, correctedA2);
            }

            return nsOut;
        }

        // ==============================================================================
        // 3. PARAMETER & GEOMETRY ALIGNMENT
        // ==============================================================================
        public static bool AreParamsAlignedPerPickPts(ObjRef objA, ObjRef objB)
        {
            if (objA.SelectionPoint() == Point3d.Unset || objB.SelectionPoint() == Point3d.Unset) return true;

            bool? isCloserT0A = IsPickCloserToT0(objA, true);
            if (!isCloserT0A.HasValue) return true;

            bool? isCloserT0B = IsPickCloserToT0(objB, false);
            if (!isCloserT0B.HasValue) return true;

            return isCloserT0A.Value == isCloserT0B.Value;
        }

        private static bool? IsPickCloserToT0(ObjRef objRef, bool useFullIsoCrv)
        {
            Point3d ptSel = objRef.SelectionPoint();
            
            if (objRef.Object() is CurveObject)
            {
                Curve c = objRef.Curve();
                return c.PointAtStart.DistanceTo(ptSel) < c.PointAtEnd.DistanceTo(ptSel);
            }

            BrepTrim trim = objRef.Trim();
            if (trim == null)
            {
                BrepEdge edge = objRef.Edge();
                if (edge == null) return null;
                int[] iTs = edge.TrimIndices();
                if (iTs.Length != 1) throw new InvalidOperationException($"Edge has {iTs.Length} trims.");
                trim = edge.Brep.Trims[iTs[0]];
            }

            if (trim.IsoStatus == IsoStatus.None)
            {
                Curve c = objRef.Curve();
                return c.PointAtStart.DistanceTo(ptSel) < c.PointAtEnd.DistanceTo(ptSel);
            }

            Curve crv3D;
            if (useFullIsoCrv)
            {
                crv3D = GetIsoCurveOfSide(trim.IsoStatus, trim.Face.UnderlyingSurface().ToNurbsSurface());
            }
            else
            {
                crv3D = trim.Edge.DuplicateCurve();
                if (trim.IsReversed()) crv3D.Reverse();

                if (trim.IsoStatus == IsoStatus.South || trim.IsoStatus == IsoStatus.East)
                {
                    // pass
                }
                else if (trim.IsoStatus == IsoStatus.West || trim.IsoStatus == IsoStatus.North)
                {
                    crv3D.Reverse();
                }
                else if (trim.IsoStatus == IsoStatus.X)
                {
                    if (trim.PointAt(trim.Domain.T1).Y < trim.PointAt(trim.Domain.T0).Y) crv3D.Reverse();
                }
                else if (trim.IsoStatus == IsoStatus.Y)
                {
                    if (trim.PointAt(trim.Domain.T1).X < trim.PointAt(trim.Domain.T0).X) crv3D.Reverse();
                }
            }

            return crv3D.PointAtStart.DistanceTo(ptSel) < crv3D.PointAtEnd.DistanceTo(ptSel);
        }

        public static GeometryBase CreateAlignedRefGeom(GeometryBase geomR, IsoStatus sideR, NurbsSurface nsM, IsoStatus sideM, bool bMatchWithParamsAligned)
        {
            if (geomR is NurbsSurface nsRef)
            {
                if (sideM == sideR)
                {
                    // pass
                }
                else if ((sideM == IsoStatus.South || sideM == IsoStatus.North) && (sideR == IsoStatus.South || sideR == IsoStatus.North))
                {
                    nsRef.Reverse(1, true);
                }
                else if ((sideM == IsoStatus.West || sideM == IsoStatus.East) && (sideR == IsoStatus.West || sideR == IsoStatus.East))
                {
                    nsRef.Reverse(0, true);
                }
                else
                {
                    nsRef.Transpose(true);

                    if (sideM == IsoStatus.South && sideR == IsoStatus.East) nsRef.Reverse(1, true);
                    else if (sideM == IsoStatus.South && sideR == IsoStatus.West) { /* pass */ }
                    else if (sideM == IsoStatus.East && sideR == IsoStatus.North) { /* pass */ }
                    else if (sideM == IsoStatus.East && sideR == IsoStatus.South) nsRef.Reverse(0, true);
                    else if (sideM == IsoStatus.North && sideR == IsoStatus.West) nsRef.Reverse(1, true);
                    else if (sideM == IsoStatus.North && sideR == IsoStatus.East) { /* pass */ }
                    else if (sideM == IsoStatus.West && sideR == IsoStatus.South) { /* pass */ }
                    else if (sideM == IsoStatus.West && sideR == IsoStatus.North) nsRef.Reverse(0, true);
                    else throw new InvalidOperationException("Invalid IsoStatus pair for Transpose.");
                }

                if (!bMatchWithParamsAligned)
                {
                    nsRef.Reverse((sideM == IsoStatus.South || sideM == IsoStatus.North) ? 0 : 1, true);
                }
                return nsRef;
            }
            
            if (geomR is NurbsCurve ncRef)
            {
                if (!bMatchWithParamsAligned) ncRef.Reverse();
                return ncRef;
            }
            return null;
        }

        // ==============================================================================
        // 4. KNOT & DEGREE MATCHING
        // ==============================================================================
        public static bool TransferUniqueKnotVector(GeometryBase nurbsA, GeometryBase nurbsB, IsoStatus sideA, IsoStatus sideB, double paramTol = RhinoMath.ZeroTolerance)
        {
            int knotCtBIn = GetKnotCount(nurbsB, sideB);
            int degA = GetDegree(nurbsA, sideA);
            int knotCtA = GetKnotCount(nurbsA, sideA);

            int iK = degA;
            while (iK < (knotCtA - degA))
            {
                double tA = GetKnot(nurbsA, sideA, iK);
                int mA = GetKnotMultiplicity(nurbsA, sideA, iK);

                if (GetKnotCount(nurbsB, sideB) <= iK || Math.Abs(tA - GetKnot(nurbsB, sideB, iK)) > paramTol)
                {
                    InsertKnot(nurbsB, sideB, tA, mA);
                }
                else
                {
                    int mB = GetKnotMultiplicity(nurbsB, sideB, iK);
                    if (mB < mA)
                    {
                        double tB = GetKnot(nurbsB, sideB, iK);
                        InsertKnot(nurbsB, sideB, tB, mA);
                    }
                }
                iK += mA;
            }
            return GetKnotCount(nurbsB, sideB) > knotCtBIn;
        }

        public static bool TransferHigherDegree(GeometryBase nFrom, GeometryBase nTo, IsoStatus sideFrom, IsoStatus sideTo)
        {
            int degreeFrom = GetDegree(nFrom, sideFrom);
            
            if (nTo is NurbsSurface nsTo)
            {
                if (sideTo == IsoStatus.South || sideTo == IsoStatus.North)
                {
                    if (nsTo.Degree(0) >= degreeFrom) return false;
                    return nsTo.IncreaseDegreeU(degreeFrom);
                }
                else
                {
                    if (nsTo.Degree(1) >= degreeFrom) return false;
                    return nsTo.IncreaseDegreeV(degreeFrom);
                }
            }
            else if (nTo is NurbsCurve ncTo)
            {
                if (ncTo.Degree >= degreeFrom) return false;
                return ncTo.IncreaseDegree(degreeFrom);
            }
            return false;
        }

        public static bool TransferDomain(GeometryBase nFrom, GeometryBase nTo, IsoStatus sideFrom, IsoStatus sideTo)
        {
            Interval domainFrom = (nFrom is NurbsSurface ns) ? ns.Domain((sideFrom == IsoStatus.South || sideFrom == IsoStatus.North) ? 0 : 1) : ((NurbsCurve)nFrom).Domain;

            if (nTo is NurbsSurface nsTo)
            {
                return nsTo.SetDomain((sideTo == IsoStatus.South || sideTo == IsoStatus.North) ? 0 : 1, domainFrom);
            }
            else if (nTo is NurbsCurve ncTo)
            {
                ncTo.Domain = domainFrom;
                return true;
            }
            return false;
        }

        // ==============================================================================
        // 5. LOW-LEVEL HELPERS
        // ==============================================================================
        public static bool MakeNonRational(GeometryBase geom, double tol = 1e-9)
        {
            if (geom is NurbsSurface ns)
            {
                if (!ns.IsRational) return true;

                double maxWeight = double.MinValue;
                double minWeight = double.MaxValue;

                for (int u = 0; u < ns.Points.CountU; u++)
                {
                    for (int v = 0; v < ns.Points.CountV; v++)
                    {
                        double w = ns.Points.GetControlPoint(u, v).Weight;
                        if (w > maxWeight) maxWeight = w;
                        if (w < minWeight) minWeight = w;
                    }
                }

                if (Math.Abs(1.0 - maxWeight) <= tol && Math.Abs(1.0 - minWeight) <= tol)
                {
                    return ns.MakeNonRational();
                }
                return false;
            }
            
            if (geom is NurbsCurve nc)
            {
                if (!nc.IsRational) return true;

                double maxWeight = double.MinValue;
                for (int i = 0; i < nc.Points.Count; i++)
                {
                    double w = nc.Points[i].Weight;
                    if (w > maxWeight) maxWeight = w;
                }

                if (Math.Abs(1.0 - maxWeight) <= tol)
                {
                    for (int i = 0; i < nc.Points.Count; i++)
                    {
                        ControlPoint cp = nc.Points[i];
                        // Set the point location directly with a weight of 1.0
                        nc.Points.SetPoint(i, cp.Location, 1.0);
                    }
                    return true;
                }
                return false;
            }
            
            return false;
        }

        public static Point3d? Pivot_M_about_C_onto_CR(Point3d R, Point3d C, Point3d M)
        {
            Vector3d vCR = R - C;
            if (vCR.IsTiny()) return null;

            Vector3d vCM = M - C;
            if (vCM.IsTiny()) return null;

            vCR.Unitize();
            vCR *= vCM.Length;
            return C - vCR;
        }

        public static GeometryBase GetNurbsGeomFromObjRef(ObjRef obj, int iContinuity, bool bEcho, bool bDebug, out IsoStatus outIso)
        {
            outIso = IsoStatus.None;
            BrepTrim trim = obj.Trim();
            if (trim == null)
            {
                if (obj.Edge() != null && obj.Edge().TrimIndices().Length == 1)
                    trim = obj.Edge().Brep.Trims[obj.Edge().TrimIndices()[0]];
            }

            if (trim != null)
            {
                outIso = trim.IsoStatus;
                
                if (outIso == IsoStatus.None)
                {
                    NurbsSurface nsTan = CreateTanSrfFromEdge(trim, bDebug);
                    if (nsTan == null) return trim.Edge.DuplicateCurve().ToNurbsCurve(); 
                    
                    Curve[] ncsTanNS = new Curve[] 
                    {
                        GetIsoCurveOfSide(IsoStatus.West, nsTan),
                        GetIsoCurveOfSide(IsoStatus.South, nsTan),
                        GetIsoCurveOfSide(IsoStatus.East, nsTan),
                        GetIsoCurveOfSide(IsoStatus.North, nsTan)
                    };
                    
                    double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                    int? idxB = FindMatchingCurveByEndPoints(trim.Edge, ncsTanNS, tol);
                    
                    if (idxB.HasValue)
                    {
                        IsoStatus[] statuses = { IsoStatus.West, IsoStatus.South, IsoStatus.East, IsoStatus.North };
                        outIso = statuses[idxB.Value];
                    }
                    else return trim.Edge.DuplicateCurve().ToNurbsCurve();

                    if (bEcho)
                    {
                        Surface checkSrf = trim.Face.UnderlyingSurface();
                        if (!checkSrf.IsPlanar(1e-9))
                        {
                            string s = "Non-isocurve trim of a non-planar surface picked.";
                            if (iContinuity == 2) s += " G2 will probably not be achieved.";
                            if (iContinuity > 0) s += " G1 may only be approximately achieved. Use _EdgeContinuity to check results.";
                            RhinoApp.WriteLine(s);
                        }
                    }
                    return nsTan;
                }
                
                // RESTORED: Exact Python Domain Shrinking & X/Y IsoStatus Logic
                Interval? domU = null;
                Interval? domV = null;

                Interval[] domains = GetSrfDomainsWithinIsoCrvEdgeEnds(trim);
                if (domains != null)
                {
                    domU = domains[0];
                    domV = domains[1];
                }

                if (trim.IsoStatus == IsoStatus.X)
                {
                    bool? rc = IsFaceOnT1SideOfXYIsoCrv(trim);
                    if (rc.HasValue)
                    {
                        if (rc.Value)
                        {
                            domU = new Interval(trim.PointAt(trim.Domain.Mid).X, trim.Face.UnderlyingSurface().Domain(0).T1);
                            outIso = IsoStatus.West;
                        }
                        else
                        {
                            domU = new Interval(trim.Face.UnderlyingSurface().Domain(0).T0, trim.PointAt(trim.Domain.Mid).X);
                            outIso = IsoStatus.East;
                        }
                    }
                }
                if (trim.IsoStatus == IsoStatus.Y)
                {
                    bool? rc = IsFaceOnT1SideOfXYIsoCrv(trim);
                    if (rc.HasValue)
                    {
                        if (rc.Value)
                        {
                            domV = new Interval(trim.PointAt(trim.Domain.Mid).Y, trim.Face.UnderlyingSurface().Domain(1).T1);
                            outIso = IsoStatus.South;
                        }
                        else
                        {
                            domV = new Interval(trim.Face.UnderlyingSurface().Domain(1).T0, trim.PointAt(trim.Domain.Mid).Y);
                            outIso = IsoStatus.North;
                        }
                    }
                }

                if (domU == null && domV == null) return trim.Face.UnderlyingSurface().ToNurbsSurface();

                if (domU == null) domU = trim.Face.UnderlyingSurface().Domain(0);
                if (domV == null) domV = trim.Face.UnderlyingSurface().Domain(1);

                Surface trimmedSrf = trim.Face.UnderlyingSurface().Trim(domU.Value, domV.Value);
                if (trimmedSrf != null) return trimmedSrf.ToNurbsSurface();

                return trim.Face.UnderlyingSurface().ToNurbsSurface();
            }
            
            return obj.Curve()?.ToNurbsCurve();
        }

        public static int? FindMatchingCurveByEndPoints(Curve curveA, Curve[] curvesB, double tol)
        {
            Point3d startA = curveA.PointAtStart;
            Point3d endA = curveA.PointAtEnd;

            for (int i = 0; i < curvesB.Length; i++)
            {
                Curve cB = curvesB[i];
                Point3d startB = cB.PointAtStart;
                Point3d endB = cB.PointAtEnd;

                if ((startA.DistanceTo(startB) <= tol && endA.DistanceTo(endB) <= tol) ||
                    (startA.DistanceTo(endB) <= tol && endA.DistanceTo(startB) <= tol))
                {
                    return i;
                }
            }
            return null;
        }

        public static int?[] FindMatchingCurvesByEndPoints(Curve[] curvesA, Curve[] curvesB, double tol)
        {
            var matches = new int?[curvesA.Length];
            var usedB = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < curvesA.Length; i++)
            {
                Curve cA = curvesA[i];
                Point3d startA = cA.PointAtStart;
                Point3d endA = cA.PointAtEnd;

                for (int j = 0; j < curvesB.Length; j++)
                {
                    if (usedB.Contains(j)) continue; // Prevent double-mapping!

                    Curve cB = curvesB[j];
                    Point3d startB = cB.PointAtStart;
                    Point3d endB = cB.PointAtEnd;

                    if ((startA.DistanceTo(startB) <= tol && endA.DistanceTo(endB) <= tol) ||
                        (startA.DistanceTo(endB) <= tol && endA.DistanceTo(startB) <= tol))
                    {
                        matches[i] = j;
                        usedB.Add(j);
                        break;
                    }
                }
            }
            return matches;
        }

        public static Curve GetIsoCurveOfSide(IsoStatus iso, NurbsSurface ns)
        {
            if (iso == IsoStatus.West) return ns.IsoCurve(1, ns.Domain(0).T0);
            if (iso == IsoStatus.South) return ns.IsoCurve(0, ns.Domain(1).T0);
            if (iso == IsoStatus.East) return ns.IsoCurve(1, ns.Domain(0).T1);
            if (iso == IsoStatus.North) return ns.IsoCurve(0, ns.Domain(1).T1);
            return null;
        }

        public static int[] GetPtRowIndicesPerG(NurbsSurface ns, IsoStatus side, int iG)
        {
            int ctV = ns.Points.CountV;
            int ctU = ns.Points.CountU;
            int ctAll = ctU * ctV;
            var idxs = new List<int>();

            if (side == IsoStatus.West) for (int i = iG * ctV; i < (iG + 1) * ctV; i++) idxs.Add(i);
            else if (side == IsoStatus.East) for (int i = ctAll - (iG + 1) * ctV; i < ctAll - iG * ctV; i++) idxs.Add(i);
            else if (side == IsoStatus.South) for (int i = iG; i < ctAll; i += ctV) idxs.Add(i);
            else if (side == IsoStatus.North) for (int i = ctV - (iG + 1); i < ctAll; i += ctV) idxs.Add(i);

            return idxs.ToArray();
        }

        public static (int u, int v) GetUvIdx(NurbsSurface ns, int idxFlat)
        {
            return (idxFlat / ns.Points.CountV, idxFlat % ns.Points.CountV);
        }

        private static Point3d[] GetControlPoints(GeometryBase geom)
        {
            if (geom is NurbsSurface ns)
            {
                var pts = new Point3d[ns.Points.CountU * ns.Points.CountV];
                int i = 0;
                foreach (var pt in ns.Points) pts[i++] = pt.Location;
                return pts;
            }
            if (geom is NurbsCurve nc)
            {
                var pts = new Point3d[nc.Points.Count];
                for (int i = 0; i < nc.Points.Count; i++) pts[i] = nc.Points[i].Location;
                return pts;
            }
            return new Point3d[0];
        }

        private static double MultiplierForDegree(NurbsSurface ns, IsoStatus side)
        {
            int deg = ns.Degree((side == IsoStatus.South || side == IsoStatus.North) ? 1 : 0);
            return (double)(deg - 1) / deg;
        }

        private static double MultiplierForAdjacentSimpleKnots(NurbsSurface ns, IsoStatus side)
        {
            int dir = (side == IsoStatus.South || side == IsoStatus.North) ? 1 : 0;
            if (ns.SpanCount(dir) == 1) return 1.0;

            var knots = dir == 1 ? ns.KnotsV : ns.KnotsU;
            int deg = ns.Degree(dir);
            int idxKnot = (side == IsoStatus.East || side == IsoStatus.North) ? knots.Count - deg - 1 : deg;

            if (knots.KnotMultiplicity(idxKnot) > 1) return 1.0;

            double[] spans = ns.GetSpanVector(dir);
            double lEnd = (side == IsoStatus.East || side == IsoStatus.North) ? spans[spans.Length - 1] - spans[spans.Length - 2] : spans[1] - spans[0];
            double lAdj = (side == IsoStatus.East || side == IsoStatus.North) ? spans[spans.Length - 2] - spans[spans.Length - 3] : spans[2] - spans[1];

            return (lEnd + lAdj) / lEnd;
        }

        private static int GetKnotCount(GeometryBase geom, IsoStatus side) => (geom is NurbsSurface ns) ? ((side == IsoStatus.South || side == IsoStatus.North) ? ns.KnotsU.Count : ns.KnotsV.Count) : ((NurbsCurve)geom).Knots.Count;
        private static int GetDegree(GeometryBase geom, IsoStatus side) => (geom is NurbsSurface ns) ? ns.Degree((side == IsoStatus.South || side == IsoStatus.North) ? 0 : 1) : ((NurbsCurve)geom).Degree;
        private static double GetKnot(GeometryBase geom, IsoStatus side, int i) => (geom is NurbsSurface ns) ? ((side == IsoStatus.South || side == IsoStatus.North) ? ns.KnotsU[i] : ns.KnotsV[i]) : ((NurbsCurve)geom).Knots[i];
        private static int GetKnotMultiplicity(GeometryBase geom, IsoStatus side, int i) => (geom is NurbsSurface ns) ? ((side == IsoStatus.South || side == IsoStatus.North) ? ns.KnotsU.KnotMultiplicity(i) : ns.KnotsV.KnotMultiplicity(i)) : ((NurbsCurve)geom).Knots.KnotMultiplicity(i);
        private static void InsertKnot(GeometryBase geom, IsoStatus side, double t, int m)
        {
            if (geom is NurbsSurface ns) { if (side == IsoStatus.South || side == IsoStatus.North) ns.KnotsU.InsertKnot(t, m); else ns.KnotsV.InsertKnot(t, m); }
            else ((NurbsCurve)geom).Knots.InsertKnot(t, m);
        }
        
        private static void AddKnotsUniformly(NurbsSurfaceKnotList knots, int targetCount)
        {
            int degree = knots.KnotMultiplicity(0);
            while (knots.Count < targetCount)
            {
                var tsToAdd = new List<double>();
                for (int i = degree - 1; i < knots.Count - degree; i++) tsToAdd.Add(0.5 * knots[i] + 0.5 * knots[i + 1]);
                foreach (double t in tsToAdd)
                {
                    knots.InsertKnot(t);
                    if (knots.Count >= targetCount) return;
                }
            }
        }

        public static NurbsSurface CreateTanSrfFromEdge(BrepTrim rgT, bool bDebug)
        {
            double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            rgT.Brep.Faces.ShrinkFaces();
            BrepEdge rgE = rgT.Edge;
            NurbsSurface ns = rgT.Face.UnderlyingSurface().ToNurbsSurface();

            Curve pushup = ns.Pushup(rgT, 0.1 * tol);
            NurbsCurve ncA_Start;

            if (pushup != null && 
                pushup.PointAtStart.DistanceTo(rgE.PointAtStart) <= tol && 
                pushup.PointAtEnd.DistanceTo(rgE.PointAtEnd) <= tol)
            {
                ncA_Start = pushup.ToNurbsCurve();
            }
            else
            {
                ncA_Start = rgE.ToNurbsCurve();
            }

            if (ncA_Start.SpanCount == 1)
            {
                Curve p2 = ns.Pushup(rgT, tol);
                if (p2 != null) ncA_Start = p2.ToNurbsCurve();
            }
            else if (ncA_Start.SpanCount > 1)
            {
                NurbsCurve simplified = SimplifyCrv(ncA_Start, tol, bDebug);
                if (simplified != null) ncA_Start = simplified;
            }
            else
            {
                throw new InvalidOperationException("Invalid span count on starting curve.");
            }

            NurbsCurve ncA = (NurbsCurve)ncA_Start.Duplicate();
            bool bDegreeWasIncrFrom1To2 = false;

            if (ncA.Degree == 1)
            {
                bDegreeWasIncrFrom1To2 = ncA.IncreaseDegree(2);
            }

            double[] ts = ncA.GrevilleParameters();
            
            // Fix: Extract actual Greville Points, not Control Points
            var pts_on_ncA = ncA.GrevillePoints(); 
            
            double angle = RhinoMath.ToRadians(90.0);
            double dist = 10.0 * tol;

            Point3d? CreateEndPt(int i, double ang)
            {
                double t = ts[i];
                Point3d ptStart = pts_on_ncA[i]; // Fix: Use Greville point
                
                if (!ns.ClosestPoint(ptStart, out double u, out double v)) return null;
                Vector3d vNormal = ns.NormalAt(u, v);
                
                if (!ncA.PerpendicularFrameAt(t, out Plane frame)) return null;

                Point3d ptNormal = ptStart + vNormal * dist;
                Transform xformRotation = Transform.Rotation(ang, frame.ZAxis, frame.Origin);
                ptNormal.Transform(xformRotation);

                return ptNormal;
            }

            int midIdx = ts.Length / 2;
            Point3d? pt_Mid_Neg = CreateEndPt(midIdx, -angle);
            Point3d? pt_Mid_Pos = CreateEndPt(midIdx, angle);

            if (!pt_Mid_Neg.HasValue || !pt_Mid_Pos.HasValue)
                throw new InvalidOperationException("ClosestPoint or PerpendicularFrameAt failed. G1+ will not occur.");

            ns.ClosestPoint(pt_Mid_Neg.Value, out double uNeg, out double vNeg);
            PointFaceRelation pfrel_Neg = rgT.Face.IsPointOnFace(uNeg, vNeg, 0.1 * tol);

            ns.ClosestPoint(pt_Mid_Pos.Value, out double uPos, out double vPos);
            PointFaceRelation pfrel_Pos = rgT.Face.IsPointOnFace(uPos, vPos, 0.1 * tol);

            double chosenAngle;
            if (pfrel_Neg == PointFaceRelation.Interior && pfrel_Pos != PointFaceRelation.Interior)
                chosenAngle = -angle;
            else if (pfrel_Neg != PointFaceRelation.Interior && pfrel_Pos == PointFaceRelation.Interior)
                chosenAngle = angle;
            else
                throw new InvalidOperationException($"PointFaceRelations are {pfrel_Neg} and {pfrel_Pos}");

            // Fix: Build the list of target Greville points
            var pts_on_ncB = new List<Point3d>();
            for (int i = 0; i < ts.Length; i++)
            {
                Point3d? newPt = CreateEndPt(i, chosenAngle);
                if (newPt.HasValue) pts_on_ncB.Add(newPt.Value);
            }

            NurbsCurve ncB;
            if (bDegreeWasIncrFrom1To2)
            {
                ncA = ncA_Start;
                ncB = (NurbsCurve)ncA.Duplicate();
                ncB.SetGrevillePoints(new[] { pts_on_ncB[0], pts_on_ncB[2] });
            }
            else
            {
                ncB = (NurbsCurve)ncA.Duplicate();
                ncB.SetGrevillePoints(pts_on_ncB);
            }

            Brep[] lofted = Brep.CreateFromLoft(
                new Curve[] { ncA, ncB }, 
                Point3d.Unset, Point3d.Unset, 
                LoftType.Straight, false);

            if (lofted == null || lofted.Length != 1)
            {
                RhinoApp.WriteLine("Loft failed or resulted in multiple Breps. Continuity increase will not occur on an edge.");
                return null;
            }

            return lofted[0].Surfaces[0].ToNurbsSurface();
        }

        private static NurbsCurve SimplifyCrv(NurbsCurve ncA_Start, double tol, bool bDebug)
        {
            // Try to make Bezier
            int[] bezierDegrees = { 1, 2, 3, 5 };
            foreach (int d in bezierDegrees)
            {
                Curve nc_WIP = ncA_Start.Rebuild(d + 1, d, true);
                // Requires 6 out parameters: maxDist, maxParamA, maxParamB, minDist, minParamA, minParamB
                if (nc_WIP != null && Curve.GetDistancesBetweenCurves(nc_WIP, ncA_Start, 0.01 * tol, out double maxDist, out _, out _, out _, out _, out _))
                {
                    if (maxDist <= 0.5 * tol) return nc_WIP.ToNurbsCurve();
                }
            }

            if (ncA_Start.Knots.KnotStyle == KnotStyle.QuasiUniform) return null;

            // Try to make quasi-uniform
            for (int p = 3; p <= ncA_Start.Points.Count; p++)
            {
                foreach (int d in new[] { 3, 5 })
                {
                    if (p - d < 1) continue;
                    Curve nc_WIP = ncA_Start.Rebuild(p, d, true);
                    if (nc_WIP != null && Curve.GetDistancesBetweenCurves(nc_WIP, ncA_Start, 0.1 * tol, out double maxDist, out _, out _, out _, out _, out _))
                    {
                        if (maxDist <= 0.5 * tol) return nc_WIP.ToNurbsCurve();
                    }
                }
            }
            return null;
        }

        private static bool AreInOutEpsilonEqual(NurbsSurface nsIn, NurbsSurface nsOut, bool bEcho)
        {
            double epsilon = 1e-6;
            if (nsOut.EpsilonEquals(nsIn, epsilon))
            {
                double eps_prev = epsilon;
                while (true)
                {
                    eps_prev = epsilon;
                    epsilon /= 10.0;
                    if (!nsOut.EpsilonEquals(nsIn, epsilon))
                    {
                        break;
                    }
                }
                if (bEcho)
                {
                    RhinoApp.WriteLine($"Input and output NURBS surfaces are EpsilonEqual within {eps_prev}. No change.");
                }
                nsOut.Dispose();
                return true;
            }
            return false;
        }

        private static bool? IsFaceOnT1SideOfXYIsoCrv(BrepTrim rgTrim)
        {
            if (rgTrim.IsoStatus != IsoStatus.X && rgTrim.IsoStatus != IsoStatus.Y) return null;

            double max_edge_tol = 0.0;
            foreach (var e in rgTrim.Brep.Edges)
            {
                if (e.Tolerance > max_edge_tol) max_edge_tol = e.Tolerance;
            }
            double tol = Math.Max(max_edge_tol, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);

            rgTrim.Edge.PerpendicularFrameAt(rgTrim.Edge.Domain.Mid, out Plane frame);
            Circle circle = new Circle(frame, 10.0 * tol);
            ArcCurve arcCrv = new ArcCurve(circle);

            bool intersectSuccess = Rhino.Geometry.Intersect.Intersection.CurveBrepFace(arcCrv, rgTrim.Face, 0.1 * tol, out Curve[] crvs, out Point3d[] pts);
            
            if (!intersectSuccess || pts == null || pts.Length == 0) return null;

            if (pts.Length > 1)
            {
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    for (int j = i + 1; j < pts.Length; j++)
                    {
                        if (pts[i].DistanceTo(pts[j]) > max_edge_tol)
                        {
                            RhinoApp.WriteLine("Points non-coincident within edge tolerance found. Check results.");
                        }
                    }
                }
            }

            Point3d pt = pts[0];
            Point3d pt_Mid = rgTrim.Edge.PointAt(rgTrim.Edge.Domain.Mid);
            
            rgTrim.Face.UnderlyingSurface().ClosestPoint(pt_Mid, out double uOnCrv, out double vOnCrv);
            rgTrim.Face.UnderlyingSurface().ClosestPoint(pt, out double uOnCircle, out double vOnCircle);

            if (rgTrim.IsoStatus == IsoStatus.X) return uOnCircle > uOnCrv;
            else return vOnCircle > vOnCrv;
        }

        private static Interval[] GetSrfDomainsWithinIsoCrvEdgeEnds(BrepTrim rgTrim)
        {
            BrepEdge edge = rgTrim.Edge;
            Surface srf = rgTrim.Face.UnderlyingSurface();
            double tol0 = RhinoMath.ZeroTolerance;

            if (rgTrim.IsoStatus == IsoStatus.West || rgTrim.IsoStatus == IsoStatus.East || rgTrim.IsoStatus == IsoStatus.X)
            {
                srf.ClosestPoint(edge.PointAtStart, out double u0, out double v0);
                double t_Start = v0;
                double t_End; // Hoisted outside the if/else block
                
                if (Math.Abs(t_Start - srf.Domain(1).T0) <= tol0)
                {
                    t_Start = srf.Domain(1).T0;
                    srf.ClosestPoint(edge.PointAtEnd, out double u1, out double v1);
                    t_End = v1;
                    if (Math.Abs(t_End - srf.Domain(1).T1) <= tol0) return null; 
                }
                else
                {
                    srf.ClosestPoint(edge.PointAtEnd, out double u1, out double v1);
                    t_End = v1;
                    if (Math.Abs(t_End - srf.Domain(1).T1) <= tol0) t_End = srf.Domain(1).T1;
                }
                return new Interval[] { srf.Domain(0), new Interval(Math.Min(t_Start, t_End), Math.Max(t_Start, t_End)) };
            }
            
            if (rgTrim.IsoStatus == IsoStatus.South || rgTrim.IsoStatus == IsoStatus.North || rgTrim.IsoStatus == IsoStatus.Y)
            {
                srf.ClosestPoint(edge.PointAtStart, out double u0, out double v0);
                double t_Start = u0;
                double t_End; // Hoisted outside the if/else block
                
                if (Math.Abs(t_Start - srf.Domain(0).T0) <= tol0)
                {
                    t_Start = srf.Domain(0).T0;
                    srf.ClosestPoint(edge.PointAtEnd, out double u1, out double v1);
                    t_End = u1;
                    if (Math.Abs(t_End - srf.Domain(0).T1) <= tol0) return null;
                }
                else
                {
                    srf.ClosestPoint(edge.PointAtEnd, out double u1, out double v1);
                    t_End = u1;
                    if (Math.Abs(t_End - srf.Domain(0).T1) <= tol0) t_End = srf.Domain(0).T1;
                }
                return new Interval[] { new Interval(Math.Min(t_Start, t_End), Math.Max(t_Start, t_End)), srf.Domain(1) };
            }
            
            return null;
        }

        // --- EDGE SRF TOPOLOGY HELPERS ---

        /// <summary>
        /// Instantly calculates the opposite IsoStatus mathematically.
        /// </summary>
        public static IsoStatus GetOppSide(IsoStatus side)
        {
            if (side == IsoStatus.None || side == IsoStatus.X || side == IsoStatus.Y)
                return side;
            return (IsoStatus)(((int)side + 2) % 4);
        }

        public static bool IsPointAtEitherCrvEnd(Point3d pt, Curve cA, double tol)
        {
            if (cA.PointAtStart.DistanceTo(pt) <= tol) return true;
            if (cA.PointAtEnd.DistanceTo(pt) <= tol) return true;
            return false;
        }

        public static bool DoAnyCrvsCompletelyOverlap(List<Curve> cs, double tol)
        {
            var found = new (bool Start, bool End)[cs.Count];

            for (int iA = 0; iA < cs.Count - 1; iA++)
            {
                Curve cA = cs[iA];
                
                // Check Start
                if (!found[iA].Start)
                {
                    Point3d ptA = cA.PointAtStart;
                    for (int iB = iA + 1; iB < cs.Count; iB++)
                    {
                        Curve cB = cs[iB];
                        if (ptA.DistanceTo(cB.PointAtStart) <= tol)
                        {
                            found[iA].Start = true; found[iB].Start = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtEnd) <= tol) return true;
                            break;
                        }
                        if (ptA.DistanceTo(cB.PointAtEnd) <= tol)
                        {
                            found[iA].Start = true; found[iB].End = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtStart) <= tol) return true;
                            break;
                        }
                    }
                }

                // Check End
                if (!found[iA].End)
                {
                    Point3d ptA = cA.PointAtEnd;
                    for (int iB = iA + 1; iB < cs.Count; iB++)
                    {
                        Curve cB = cs[iB];
                        if (ptA.DistanceTo(cB.PointAtStart) <= tol)
                        {
                            found[iA].End = true; found[iB].Start = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtEnd) <= tol) return true;
                            break;
                        }
                        if (ptA.DistanceTo(cB.PointAtEnd) <= tol)
                        {
                            found[iA].End = true; found[iB].End = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtStart) <= tol) return true;
                            break;
                        }
                    }
                }
            }
            return false;
        }

        public static (bool Start, bool End)[] EndsMatchWithOtherEnds(List<Curve> cs, double tol)
        {
            var matches = new (bool Start, bool End)[cs.Count];

            for (int iA = 0; iA < cs.Count - 1; iA++)
            {
                Curve cA = cs[iA];
                if (matches[iA].Start && matches[iA].End) continue;

                if (!matches[iA].Start)
                {
                    Point3d ptA = cA.PointAtStart;
                    for (int iB = iA + 1; iB < cs.Count; iB++)
                    {
                        Curve cB = cs[iB];
                        if (ptA.DistanceTo(cB.PointAtStart) <= tol)
                        {
                            matches[iA].Start = true; matches[iB].Start = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtEnd) <= tol) throw new System.Exception("2 curves completely overlap.");
                            break;
                        }
                        if (ptA.DistanceTo(cB.PointAtEnd) <= tol)
                        {
                            matches[iA].Start = true; matches[iB].End = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtStart) <= tol) throw new System.Exception("2 curves completely overlap.");
                            break;
                        }
                    }
                }

                if (!matches[iA].End)
                {
                    Point3d ptA = cA.PointAtEnd;
                    for (int iB = iA + 1; iB < cs.Count; iB++)
                    {
                        Curve cB = cs[iB];
                        if (ptA.DistanceTo(cB.PointAtStart) <= tol)
                        {
                            matches[iA].End = true; matches[iB].Start = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtEnd) <= tol) throw new System.Exception("2 curves completely overlap.");
                            break;
                        }
                        if (ptA.DistanceTo(cB.PointAtEnd) <= tol)
                        {
                            matches[iA].End = true; matches[iB].End = true;
                            if (cA.PointAtEnd.DistanceTo(cB.PointAtStart) <= tol) throw new System.Exception("2 curves completely overlap.");
                            break;
                        }
                    }
                }
            }
            return matches;
        }

        public static bool DoCrvsFormAClosedLoop(List<Curve> cs, double tol)
        {
            var matches = EndsMatchWithOtherEnds(cs, tol);
            foreach (var match in matches)
            {
                if (!match.Start || !match.End) return false;
            }
            return true;
        }

        public static List<List<Point3d>> GetPtsAtNonEndIntersects(List<Curve> cs, double tol)
        {
            var matches = EndsMatchWithOtherEnds(cs, tol);
            var ptsXs = new List<List<Point3d>>();
            for (int i = 0; i < cs.Count; i++) ptsXs.Add(new List<Point3d>());

            for (int iA = 0; iA < cs.Count - 1; iA++)
            {
                if (matches[iA].Start && matches[iA].End) continue;
                Curve cA = cs[iA];

                for (int iB = iA + 1; iB < cs.Count; iB++)
                {
                    if (matches[iB].Start && matches[iB].End) continue;
                    Curve cB = cs[iB];

                    var events = Rhino.Geometry.Intersect.Intersection.CurveCurve(cA, cB, tol, 0.0);
                    if (events == null || events.Count == 0) continue;

                    foreach (var ev in events)
                    {
                        // Safely extract PointA from the event, avoiding overlap segments
                        Point3d pt = ev.PointA;
                        if (!IsPointAtEitherCrvEnd(pt, cA, tol)) ptsXs[iA].Add(pt);
                        if (!IsPointAtEitherCrvEnd(pt, cB, tol)) ptsXs[iB].Add(pt);
                    }
                }
            }
            return ptsXs;
        }

        /// <summary>
        /// Bundles corner point data for safe averaging when multiple sides modify the same index.
        /// </summary>
        public struct CornerAdjustment
        {
            public List<Point3d> Points;
            public List<Plane?> Planes;
            public List<Vector3d?> Vectors;

            public CornerAdjustment(Point3d pt, Plane? plane, Vector3d? vector)
            {
                Points = new List<Point3d> { pt };
                Planes = new List<Plane?> { plane };
                Vectors = new List<Vector3d?> { vector };
            }

            public void Add(Point3d pt, Plane? plane, Vector3d? vector)
            {
                Points.Add(pt);
                Planes.Add(plane);
                Vectors.Add(vector);
            }
        }

        public static List<Curve> TrimCurvesAtIntersections(List<Curve> csIn, double tol)
        {
            var matches = EndsMatchWithOtherEnds(csIn, tol);
            
            bool needsIntersect = false;
            foreach (var m in matches)
            {
                if (!m.Start || !m.End) needsIntersect = true;
            }
            if (!needsIntersect) return csIn;

            var ptsXs = GetPtsAtNonEndIntersects(csIn, tol);
            var csOut = new List<Curve>();

            for (int i = 0; i < csIn.Count; i++)
            {
                Curve c = csIn[i];
                if (ptsXs[i].Count == 0)
                {
                    csOut.Add(c);
                    continue;
                }

                List<double> ts = new List<double>();
                foreach (var pt in ptsXs[i])
                {
                    if (c.ClosestPoint(pt, out double t)) ts.Add(t);
                }

                if (matches[i].Start) ts.Add(c.Domain.T0);
                if (matches[i].End) ts.Add(c.Domain.T1);

                ts.Sort();
                if (ts.Count >= 2)
                {
                    Curve trimmed = c.Trim(ts[0], ts[ts.Count - 1]);
                    csOut.Add(trimmed ?? c);
                }
                else
                {
                    csOut.Add(c);
                }
            }
            return csOut;
        }

        public static bool AreSrfParamsAlignedAlongMatchingSides(NurbsSurface nsA, IsoStatus sideA, NurbsSurface nsB, IsoStatus sideB, double tol)
        {
            var ptsA = GetSideEndPtsInAscendingSrfParam(nsA, sideA);
            var ptsB = GetSideEndPtsInAscendingSrfParam(nsB, sideB);

            if (ptsA[0].DistanceTo(ptsB[0]) < tol && ptsA[1].DistanceTo(ptsB[1]) < tol) return true;
            if (ptsA[0].DistanceTo(ptsB[1]) < tol && ptsA[1].DistanceTo(ptsB[0]) < tol) return false;

            throw new InvalidOperationException("The 2 sides' positions do not match.");
        }

        private static Point3d[] GetSideEndPtsInAscendingSrfParam(NurbsSurface ns, IsoStatus side)
        {
            var cps = ns.Points;
            Point3d ptSW = cps.GetControlPoint(0, 0).Location;
            Point3d ptSE = cps.GetControlPoint(cps.CountU - 1, 0).Location;
            Point3d ptNW = cps.GetControlPoint(0, cps.CountV - 1).Location;
            Point3d ptNE = cps.GetControlPoint(cps.CountU - 1, cps.CountV - 1).Location;

            if (side == IsoStatus.East) return new[] { ptSE, ptNE };
            if (side == IsoStatus.West) return new[] { ptSW, ptNW };
            if (side == IsoStatus.South) return new[] { ptSW, ptSE };
            if (side == IsoStatus.North) return new[] { ptNW, ptNE };
            return new Point3d[0];
        }

        public static GeometryBase CreateAlignedRefGeom_PerStart(GeometryBase geomR, IsoStatus sideR, NurbsSurface nsM, IsoStatus sideM, double tol)
        {
            if (geomR is NurbsSurface nsRef)
            {
                if (sideM == sideR) { /* pass */ }
                else if ((sideM == IsoStatus.South || sideM == IsoStatus.North) && (sideR == IsoStatus.South || sideR == IsoStatus.North)) nsRef.Reverse(1, true);
                else if ((sideM == IsoStatus.West || sideM == IsoStatus.East) && (sideR == IsoStatus.West || sideR == IsoStatus.East)) nsRef.Reverse(0, true);
                else
                {
                    nsRef.Transpose(true);
                    if (sideM == IsoStatus.South && sideR == IsoStatus.East) nsRef.Reverse(1, true);
                    else if (sideM == IsoStatus.South && sideR == IsoStatus.West) { /* pass */ }
                    else if (sideM == IsoStatus.East && sideR == IsoStatus.North) { /* pass */ }
                    else if (sideM == IsoStatus.East && sideR == IsoStatus.South) nsRef.Reverse(0, true);
                    else if (sideM == IsoStatus.North && sideR == IsoStatus.West) nsRef.Reverse(1, true);
                    else if (sideM == IsoStatus.North && sideR == IsoStatus.East) { /* pass */ }
                    else if (sideM == IsoStatus.West && sideR == IsoStatus.South) { /* pass */ }
                    else if (sideM == IsoStatus.West && sideR == IsoStatus.North) nsRef.Reverse(0, true);
                    else throw new Exception("Invalid Transpose logic.");
                }

                if (!AreSrfParamsAlignedAlongMatchingSides(nsM, sideM, nsRef, sideM, tol))
                {
                    nsRef.Reverse((sideM == IsoStatus.South || sideM == IsoStatus.North) ? 0 : 1, true);
                }
                return nsRef;
            }

            if (geomR is NurbsCurve ncRef)
            {
                Curve cM = GetIsoCurveOfSide(sideM, nsM);
                if (!Curve.DoDirectionsMatch(cM, ncRef)) ncRef.Reverse();
                return ncRef;
            }
            return null;
        }
    }
}