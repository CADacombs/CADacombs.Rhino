using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static partial class CurveMatchMath
    {
        private static Dictionary<double, int> GetInternalContinuities(NurbsCurve nc, double posTol, double angleTolDeg, double curvTolPct)
        {
            var dict = new Dictionary<double, int>();
            var uniqueKnots = new HashSet<double>();

            for (int i = nc.Degree; i < nc.Knots.Count - nc.Degree; i++)
            {
                uniqueKnots.Add(nc.Knots[i]);
            }

            foreach (double t in uniqueKnots)
            {
                var vBelow = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Below);
                var vAbove = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Above);

                int level = ContinuityUtils.GetContinuityLevel(vBelow, vAbove, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;
                dict[t] = level;
            }
            return dict;
        }

        private static bool TryGetConvertibleArc(Curve origCrv, out Arc arc)
        {
            arc = Arc.Unset;
            if (origCrv == null || origCrv.IsClosed) return false;

            if (origCrv is ArcCurve ac)
            {
                arc = ac.Arc;
                return arc.IsValid && !arc.IsCircle;
            }

            if ((!(origCrv is NurbsCurve nc) || nc.IsRational) &&
                origCrv.TryGetArc(out arc, RhinoMath.ZeroTolerance))
            {
                return arc.IsValid && !arc.IsCircle;
            }

            return false;
        }

        /// <summary>
        /// Converts an open circular arc into a non-rational polynomial Degree-5 NurbsCurve
        /// (with simple C^4 interior knots if split) that preserves exact G2 (position, tangent,
        /// and 1/R curvature) at both endpoints and stays within 0.1 * ModelAbsoluteTolerance.
        /// </summary>
        private static NurbsCurve CreatePolynomialArcNurbs(Curve origCrv, Arc arc, double devTol)
        {
            Point3d center = arc.Center;
            double radius = arc.Radius;
            double totalSweep = arc.Angle;
            if (radius <= RhinoMath.ZeroTolerance || totalSweep <= RhinoMath.ZeroTolerance)
                return null;

            Point3d ptStart = origCrv.PointAtStart;
            Point3d ptEnd = origCrv.PointAtEnd;
            Vector3d tanStart = origCrv.TangentAtStart;
            Vector3d tanEnd = origCrv.TangentAtEnd;

            Vector3d xAxis = ptStart - center;
            if (!xAxis.Unitize()) return null;

            Vector3d yAxis = tanStart - (tanStart * xAxis) * xAxis;
            if (!yAxis.Unitize()) return null;

            Vector3d startOut = tanStart;
            if (!startOut.Unitize()) return null;
            Vector3d startNorm = center - ptStart;
            if (!startNorm.Unitize()) return null;

            Vector3d endInto = -tanEnd;
            if (!endInto.Unitize()) return null;
            Vector3d endNorm = center - ptEnd;
            if (!endNorm.Unitize()) return null;

            double docTol = RhinoDoc.ActiveDoc != null ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 1e-3;
            double arcTol = 0.1 * docTol;
            if (arcTol <= RhinoMath.ZeroTolerance)
                arcTol = RhinoMath.SqrtEpsilon;
            if (devTol < double.MaxValue && devTol > 0.0)
                arcTol = Math.Max(RhinoMath.ZeroTolerance, Math.Min(arcTol, 0.25 * devTol));

            const int deg = 5;
            int minSpans = Math.Max(1, (int)Math.Ceiling(totalSweep / (5.0 * Math.PI / 6.0)));

            for (int nSpans = minSpans; nSpans <= 32; nSpans++)
            {
                int nCp = nSpans + deg;
                double subSweep = totalSweep / nSpans;
                double subHalf = 0.5 * subSweep;
                double cs = Math.Cos(subHalf);
                double sn = Math.Sin(subHalf);
                double sinQuarter = Math.Sin(0.5 * subHalf);
                double oneMinusC = 2.0 * sinQuarter * sinQuarter;
                double disc = Math.Max(0.0, 2500.0 * sn * sn - 3200.0 * cs * oneMinusC);
                double denom = 50.0 * sn + Math.Sqrt(disc);
                double uSub = denom > RhinoMath.ZeroTolerance ? (64.0 * oneMinusC) / denom : (subSweep / 5.0);
                double a1 = uSub * radius;

                var cps = new Point3d[nCp];

                if (nSpans == 1)
                {
                    double b1 = 1.25 * a1 * a1 / radius;
                    cps[0] = ptStart;
                    cps[1] = ptStart + a1 * startOut;
                    cps[2] = ptStart + (2.0 * a1) * startOut + b1 * startNorm;
                    cps[5] = ptEnd;
                    cps[4] = ptEnd + a1 * endInto;
                    cps[3] = ptEnd + (2.0 * a1) * endInto + b1 * endNorm;
                }
                else
                {
                    double b2 = 2.5 * a1 * a1 / radius;
                    cps[0] = ptStart;
                    cps[1] = ptStart + a1 * startOut;
                    cps[2] = ptStart + (3.0 * a1) * startOut + b2 * startNorm;
                    cps[nCp - 1] = ptEnd;
                    cps[nCp - 2] = ptEnd + a1 * endInto;
                    cps[nCp - 3] = ptEnd + (3.0 * a1) * endInto + b2 * endNorm;

                    int nFree = nSpans - 1;
                    var fullKnots = new double[nCp + deg + 1];
                    for (int k = 1; k < nSpans; k++)
                        fullKnots[deg + k] = (double)k / nSpans;
                    for (int k = nCp; k < fullKnots.Length; k++)
                        fullKnots[k] = 1.0;

                    var ata = new double[nFree, nFree];
                    var atbX = new double[nFree];
                    var atbY = new double[nFree];
                    var atbZ = new double[nFree];
                    int[] fixedIdx = { 0, 1, 2, nCp - 3, nCp - 2, nCp - 1 };

                    int nSamp = 16 * nSpans;
                    for (int sIdx = 1; sIdx <= nSamp; sIdx++)
                    {
                        double t = sIdx / (nSamp + 1.0);
                        double ang = t * totalSweep;
                        Point3d target = center + radius * (Math.Cos(ang) * xAxis + Math.Sin(ang) * yAxis);
                        double[] basis = EvalClampedBSplineBasis(deg, fullKnots, nCp, t);

                        double rx = target.X;
                        double ry = target.Y;
                        double rz = target.Z;
                        for (int f = 0; f < fixedIdx.Length; f++)
                        {
                            int fi = fixedIdx[f];
                            double bf = basis[fi];
                            rx -= bf * cps[fi].X;
                            ry -= bf * cps[fi].Y;
                            rz -= bf * cps[fi].Z;
                        }

                        for (int r = 0; r < nFree; r++)
                        {
                            double br = basis[3 + r];
                            if (br == 0.0) continue;
                            atbX[r] += br * rx;
                            atbY[r] += br * ry;
                            atbZ[r] += br * rz;
                            for (int c = r; c < nFree; c++)
                                ata[r, c] += br * basis[3 + c];
                        }
                    }

                    for (int r = 0; r < nFree; r++)
                        for (int c = 0; c < r; c++)
                            ata[r, c] = ata[c, r];

                    Point3d[] solvedFree = SolveLinearSystem3D(ata, atbX, atbY, atbZ, nFree);
                    if (solvedFree == null) continue;
                    for (int r = 0; r < nFree; r++)
                        cps[3 + r] = solvedFree[r];
                }

                var cand = new NurbsCurve(3, false, deg + 1, nCp);
                cand.Knots.CreateUniformKnots(1.0 / nSpans);
                for (int i = 0; i < nCp; i++)
                    cand.Points.SetPoint(i, cps[i]);

                if (!cand.IsValid)
                {
                    cand.Dispose();
                    continue;
                }

                double maxErr = 0.0;
                int nCheck = 32 * nSpans;
                Interval dom = cand.Domain;
                for (int k = 1; k < nCheck; k++)
                {
                    Point3d pt = cand.PointAt(dom.ParameterAt((double)k / nCheck));
                    Vector3d rel = pt - center;
                    Vector3d inPlane = (rel * xAxis) * xAxis + (rel * yAxis) * yAxis;
                    double inPlaneLen = inPlane.Length;
                    if (inPlaneLen <= RhinoMath.ZeroTolerance)
                    {
                        maxErr = double.MaxValue;
                        break;
                    }
                    Point3d closestOnCircle = center + (radius / inPlaneLen) * inPlane;
                    double err = pt.DistanceTo(closestOnCircle);
                    if (err > maxErr) maxErr = err;
                }

                if (maxErr <= arcTol || nSpans == 32)
                {
                    cand.Domain = origCrv.Domain;
                    return cand;
                }

                cand.Dispose();
            }

            return null;
        }

        private static double[] EvalClampedBSplineBasis(int deg, double[] fullKnots, int nCp, double t)
        {
            var basis = new double[nCp];
            if (t <= fullKnots[deg]) { basis[0] = 1.0; return basis; }
            if (t >= fullKnots[nCp]) { basis[nCp - 1] = 1.0; return basis; }

            int span = deg;
            while (span + 1 < nCp && t >= fullKnots[span + 1])
                span++;

            var n = new double[deg + 1];
            var left = new double[deg + 1];
            var right = new double[deg + 1];
            n[0] = 1.0;

            for (int j = 1; j <= deg; j++)
            {
                left[j] = t - fullKnots[span + 1 - j];
                right[j] = fullKnots[span + j] - t;
                double saved = 0.0;
                for (int r = 0; r < j; r++)
                {
                    double denom = right[r + 1] + left[j - r];
                    double temp = denom > 0.0 ? n[r] / denom : 0.0;
                    n[r] = saved + right[r + 1] * temp;
                    saved = left[j - r] * temp;
                }
                n[j] = saved;
            }

            for (int j = 0; j <= deg; j++)
                basis[span - deg + j] = n[j];

            return basis;
        }

        private static Point3d[] SolveLinearSystem3D(double[,] a, double[] bx, double[] by, double[] bz, int m)
        {
            var mat = (double[,])a.Clone();
            var rx = (double[])bx.Clone();
            var ry = (double[])by.Clone();
            var rz = (double[])bz.Clone();

            for (int col = 0; col < m; col++)
            {
                int piv = col;
                double maxAbs = Math.Abs(mat[col, col]);
                for (int row = col + 1; row < m; row++)
                {
                    double v = Math.Abs(mat[row, col]);
                    if (v > maxAbs) { maxAbs = v; piv = row; }
                }
                if (maxAbs <= RhinoMath.ZeroTolerance)
                    return null;

                if (piv != col)
                {
                    for (int k = col; k < m; k++)
                    {
                        double tmp = mat[col, k]; mat[col, k] = mat[piv, k]; mat[piv, k] = tmp;
                    }
                    double tx = rx[col]; rx[col] = rx[piv]; rx[piv] = tx;
                    double ty = ry[col]; ry[col] = ry[piv]; ry[piv] = ty;
                    double tz = rz[col]; rz[col] = rz[piv]; rz[piv] = tz;
                }

                double invDiag = 1.0 / mat[col, col];
                for (int row = col + 1; row < m; row++)
                {
                    double factor = mat[row, col] * invDiag;
                    if (factor == 0.0) continue;
                    for (int k = col; k < m; k++)
                        mat[row, k] -= factor * mat[col, k];
                    rx[row] -= factor * rx[col];
                    ry[row] -= factor * ry[col];
                    rz[row] -= factor * rz[col];
                }
            }

            var res = new Point3d[m];
            for (int row = m - 1; row >= 0; row--)
            {
                double sx = rx[row], sy = ry[row], sz = rz[row];
                for (int k = row + 1; k < m; k++)
                {
                    double c = mat[row, k];
                    sx -= c * res[k].X;
                    sy -= c * res[k].Y;
                    sz -= c * res[k].Z;
                }
                double inv = 1.0 / mat[row, row];
                res[row] = new Point3d(sx * inv, sy * inv, sz * inv);
            }
            return res;
        }

        /// <summary>
        /// Prepares a NURBS curve to have enough control points (and minimum degree when curving a degree-1 line)
        /// for the requested continuity + opposite-end preservation across any input degree or span count.
        /// </summary>
        private static bool PrepareNurbsForContinuity(
            Curve origCrv, ref NurbsCurve ncWip,
            int continuity, int minCpNeeded, int lineMode, int arcMode, int incrAsNeeded,
            double devTol, bool isPairMode, out string errorMsg)
        {
            errorMsg = null;
            string noun = isPairMode ? "a curve" : "the curve";
            string lineNoun = isPairMode ? "a line" : "the line";

            if (TryGetConvertibleArc(origCrv, out Arc convArc))
            {
                if (arcMode != 2)
                {
                    errorMsg = $"Result cannot be created because {noun} is an arc and Convert to NURBS is disabled.";
                    return false;
                }

                NurbsCurve polyArc = CreatePolynomialArcNurbs(origCrv, convArc, devTol);
                if (polyArc == null)
                {
                    errorMsg = "Failed to convert arc to NURBS.";
                    return false;
                }

                ncWip.Dispose();
                ncWip = polyArc;
            }
            else if (origCrv.IsArc(RhinoMath.ZeroTolerance) && arcMode != 2)
            {
                errorMsg = $"Result cannot be created because {noun} is an arc and Convert to NURBS is disabled.";
                return false;
            }
            else if (origCrv.IsLinear(RhinoMath.ZeroTolerance) && lineMode != 2 && ncWip.Points.Count < minCpNeeded)
            {
                errorMsg = $"Result cannot be created because {noun} is a line and Convert to NURBS is disabled.";
                return false;
            }

            // A degree-1 curve with SpanCt knot insertion alone remains piecewise linear and cannot achieve G1/G2 curvature
            bool needsDegreeForSmoothness = ncWip.Degree == 1 && continuity >= 1;

            if (ncWip.Points.Count < minCpNeeded || needsDegreeForSmoothness)
            {
                if (incrAsNeeded == 2)
                {
                    bool isLine = origCrv.IsLinear(RhinoMath.ZeroTolerance);
                    errorMsg = $"Result cannot be created because {(isLine ? lineNoun : noun)} requires more control points and Incr as needed is disabled.";
                    return false;
                }

                if (incrAsNeeded == 0) // Increase Degree
                {
                    int targetDeg = Math.Max(ncWip.Degree + Math.Max(0, minCpNeeded - ncWip.Points.Count),
                                             needsDegreeForSmoothness ? Math.Max(2, continuity) : ncWip.Degree);
                    if (targetDeg > ncWip.Degree)
                        ncWip.IncreaseDegree(targetDeg);
                }
                else if (incrAsNeeded == 1) // Increase Span Count
                {
                    if (needsDegreeForSmoothness)
                    {
                        int smoothDeg = Math.Min(3, Math.Max(2, minCpNeeded - 1));
                        if (smoothDeg > ncWip.Degree)
                            ncWip.IncreaseDegree(smoothDeg);
                    }

                    int ptsNeeded = minCpNeeded - ncWip.Points.Count;
                    if (ptsNeeded > 0)
                    {
                        InsertInteriorKnotsSafely(ncWip, ptsNeeded);
                    }
                }
            }

            if (ncWip.Points.Count < minCpNeeded)
            {
                errorMsg = $"Result cannot be created because {noun} does not have enough control points for the requested continuity.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Inserts ptsNeeded interior knots strictly inside open knot spans so existing knot multiplicities
        /// and internal continuities are never degraded.
        /// </summary>
        private static void InsertInteriorKnotsSafely(NurbsCurve nc, int ptsNeeded)
        {
            if (ptsNeeded <= 0) return;

            if (nc.SpanCount == 1)
            {
                Interval dom = nc.SpanDomain(0);
                for (int k = 1; k <= ptsNeeded; k++)
                {
                    double t = dom.ParameterAt((double)k / (ptsNeeded + 1));
                    nc.Knots.InsertKnot(t, 1);
                }
                return;
            }

            for (int k = 0; k < ptsNeeded; k++)
            {
                int bestSpan = 0;
                double bestLen = -1.0;
                int spanCount = nc.SpanCount;

                for (int s = 0; s < spanCount; s++)
                {
                    Interval spDom = nc.SpanDomain(s);
                    double chord = nc.PointAt(spDom.T0).DistanceTo(nc.PointAt(spDom.T1));
                    if (chord > bestLen)
                    {
                        bestLen = chord;
                        bestSpan = s;
                    }
                }

                double tMid = nc.SpanDomain(bestSpan).Mid;
                nc.Knots.InsertKnot(tMid, 1);
            }
        }

        private static (Curve Result, string ErrorMsg) SolveNurbsOneCurve(
            Curve crvToMod,
            Curve crvRef,
            bool modT1,
            bool refT1,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug)
        {
            NurbsCurve ncMod = crvToMod.ToNurbsCurve();
            NurbsCurve ncRef = crvRef.ToNurbsCurve();

            if (ncMod == null || ncRef == null)
            {
                ncMod?.Dispose();
                ncRef?.Dispose();
                return (null, "Failed to convert curves to NURBS.");
            }

            int minCpNeeded = preserveOtherEnd + continuity + 1;
            if (!PrepareNurbsForContinuity(crvToMod, ref ncMod, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, devTol, false, out string prepErr))
            {
                ncMod?.Dispose();
                ncRef.Dispose();
                return (null, prepErr);
            }

            double tRef = refT1 ? ncRef.Domain.T1 : ncRef.Domain.T0;
            Point3d ptTarget = refT1 ? ncRef.PointAtEnd : ncRef.PointAtStart;
            Vector3d tanTarget = ncRef.TangentAt(tRef);
            if (modT1 == refT1) tanTarget.Reverse();
            tanTarget.Unitize();

            var candidates = new List<(NurbsCurve Curve, string Desc)>();

            if (continuity == 0)
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;

                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                cand1.Points.SetPoint(idxCpPosOne, ptTarget, cand1.Points.GetWeight(idxCpPosOne));
                candidates.Add((cand1, "Position: Move P0 Only"));

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                bool ok = modT1 ? cand2.SetEndPoint(ptTarget) : cand2.SetStartPoint(ptTarget);
                if (ok) candidates.Add((cand2, "Position: SetEndCondition"));
                else cand2.Dispose();
            }
            else if (continuity == 1)
            {
                int idxCpPosOne = modT1 ? ncMod.Points.Count - 1 : 0;
                int idxCpTanOne = modT1 ? ncMod.Points.Count - 2 : 1;

                // Project P1 strictly onto the forward inward ray so obtuse approaches never create a 180-deg cusp
                Vector3d inwardDir = modT1 ? -tanTarget : tanTarget;
                if (inwardDir.Unitize())
                {
                    double proj = (ncMod.Points[idxCpTanOne].Location - ptTarget) * inwardDir;
                    if (proj > 1e-6)
                    {
                        NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                        cand1.Points.SetPoint(idxCpPosOne, ptTarget, cand1.Points.GetWeight(idxCpPosOne));
                        cand1.Points.SetPoint(idxCpTanOne, ptTarget + proj * inwardDir, cand1.Points.GetWeight(idxCpTanOne));
                        candidates.Add((cand1, "Tangency: ClosestPoint (Slide P1)"));
                    }
                }

                NurbsCurve cand2 = (NurbsCurve)ncMod.Duplicate();
                if (cand2.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptTarget, tanTarget))
                    candidates.Add((cand2, "Tangency: SetEndCondition"));
                else
                    cand2.Dispose();
            }
            else if (continuity == 2)
            {
                NurbsCurve cand1 = (NurbsCurve)ncMod.Duplicate();
                if (cand1.SetEndCondition(modT1, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptTarget, tanTarget, ncRef.CurvatureAt(tRef)))
                    candidates.Add((cand1, "Curvature: SetEndCondition"));
                else
                    cand1.Dispose();
            }

            NurbsCurve winner = null;
            double bestAvgDev = double.MaxValue;
            double bestMaxDev = double.MaxValue;

            int origInf = crvToMod.InflectionPoints()?.Length ?? 0;
            var origInternalCont = preserveInternalContinuity ? GetInternalContinuities(ncMod, posTol, angleTolDeg, curvTolPct) : null;

            bool rejectedByInflection = false;
            bool rejectedByDev = false;
            bool rejectedByInternalCont = false;

            if (debug) RhinoApp.WriteLine($"--- MatchOneCurve NURBS Candidates Evaluated ({candidates.Count}) ---");

            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                double maxDev = GetMaximumDeviation(crvToMod, cand.Curve);
                double avgDev = GetAverageDeviation(crvToMod, cand.Curve);
                double? minRad = RadiusMinimaMath.GetMinimumRadius(cand.Curve);

                int idxPos = modT1 ? cand.Curve.Points.Count - 1 : 0;
                int idxTan = modT1 ? cand.Curve.Points.Count - 2 : 1;
                double p0p1Dist = continuity > 0 ? cand.Curve.Points[idxPos].Location.DistanceTo(cand.Curve.Points[idxTan].Location) : 0;

                if (debug)
                    RhinoApp.WriteLine($" {cand.Desc}\n    AvgDev: {avgDev:g} | MaxDev: {maxDev:g} | MinRad: {(minRad.HasValue ? minRad.Value.ToString("g") : "N/A")} | P0-P1 Dist: {p0p1Dist:g}");

                if (preserveInternalContinuity && origInternalCont != null)
                {
                    bool degraded = false;
                    foreach (var kvp in origInternalCont)
                    {
                        var vB = ContinuityUtils.GetContinuityVectorsAt(cand.Curve, kvp.Key, CurveEvaluationSide.Below);
                        var vA = ContinuityUtils.GetContinuityVectorsAt(cand.Curve, kvp.Key, CurveEvaluationSide.Above);
                        int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                        int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                        if (newCont < targetCont)
                        {
                            if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                            degraded = true;
                            break;
                        }
                    }
                    if (degraded)
                    {
                        cand.Curve.Dispose();
                        rejectedByInternalCont = true;
                        continue;
                    }
                }

                if (preventExtraInflections)
                {
                    int candInf = cand.Curve.InflectionPoints()?.Length ?? 0;
                    if (candInf > origInf)
                    {
                        if (debug) RhinoApp.WriteLine($"    [Rejected] Added extra inflection points (Original: {origInf}, New: {candInf})");
                        cand.Curve.Dispose();
                        rejectedByInflection = true;
                        continue;
                    }
                }

                if (maxDev > devTol)
                {
                    cand.Curve.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                if (avgDev < bestAvgDev)
                {
                    bestAvgDev = avgDev;
                    bestMaxDev = maxDev;
                    winner?.Dispose();
                    winner = cand.Curve;
                }
                else
                {
                    cand.Curve.Dispose();
                }
            }

            if (winner != null && !IsMeaningfullyChanged(crvToMod, winner, bestMaxDev))
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding result: Maximum deviation ({bestMaxDev:g}) is below ZeroTolerance.");
                winner.Dispose();
                winner = null;
            }

            ncMod.Dispose();
            ncRef.Dispose();

            string errorMsg = null;
            if (winner == null)
            {
                if (rejectedByDev) errorMsg = "Result cannot be created because it exceeds the Crv dev limit.";
                else if (rejectedByInternalCont) errorMsg = "Result cannot be created because it would degrade internal continuity.";
                else if (rejectedByInflection) errorMsg = "Result cannot be created because it would add extra inflection points.";
                else errorMsg = "Result cannot be created with the current constraints.";
            }

            return (winner, errorMsg);
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveNurbsBothCurves(
            Curve crvA,
            Curve crvB,
            bool modT1_A,
            bool modT1_B,
            int continuity,
            int preserveOtherEnd,
            int lineMode,
            int arcMode,
            int incrAsNeeded,
            double devTol,
            bool preventExtraInflections,
            bool preserveInternalContinuity,
            int maxInternalContinuity,
            double posTol,
            double angleTolDeg,
            double curvTolPct,
            bool debug)
        {
            NurbsCurve ncWipA = crvA.ToNurbsCurve();
            NurbsCurve ncWipB = crvB.ToNurbsCurve();

            if (ncWipA == null || ncWipB == null)
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, "Failed to convert curves to NURBS.");
            }

            int minCpNeeded = preserveOtherEnd + continuity + 1;

            if (!PrepareNurbsForContinuity(crvA, ref ncWipA, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, devTol, true, out string errA))
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, errA);
            }

            if (!PrepareNurbsForContinuity(crvB, ref ncWipB, continuity, minCpNeeded, lineMode, arcMode, incrAsNeeded, devTol, true, out string errB))
            {
                ncWipA?.Dispose();
                ncWipB?.Dispose();
                return (null, null, errB);
            }

            double tA = modT1_A ? ncWipA.Domain.T1 : ncWipA.Domain.T0;
            double tB = modT1_B ? ncWipB.Domain.T1 : ncWipB.Domain.T0;

            int idxCpPosA = modT1_A ? ncWipA.Points.Count - 1 : 0;
            int idxCpPosB = modT1_B ? ncWipB.Points.Count - 1 : 0;
            int idxCpTanA = modT1_A ? ncWipA.Points.Count - 2 : 1;
            int idxCpTanB = modT1_B ? ncWipB.Points.Count - 2 : 1;

            double trueOrigDistA = ncWipA.Points.Count > 1 ? ncWipA.Points[idxCpPosA].Location.DistanceTo(ncWipA.Points[idxCpTanA].Location) : 0;
            double trueOrigDistB = ncWipB.Points.Count > 1 ? ncWipB.Points[idxCpPosB].Location.DistanceTo(ncWipB.Points[idxCpTanB].Location) : 0;

            Point3d ptA = modT1_A ? ncWipA.PointAtEnd : ncWipA.PointAtStart;
            Point3d ptB = modT1_B ? ncWipB.PointAtEnd : ncWipB.PointAtStart;
            Point3d ptAvg = 0.5 * (ptA + ptB);

            Vector3d curvA = ncWipA.CurvatureAt(tA);
            Vector3d curvB = ncWipB.CurvatureAt(tB);

            Vector3d tanTargetA = Vector3d.Unset;
            Vector3d tanTargetB = Vector3d.Unset;

            if (continuity >= 1)
            {
                GetOrientedEnds(ncWipA, modT1_A, out Point3d pFixA, out _, out _, out Vector3d tWorkOutA);
                GetOrientedEnds(ncWipB, modT1_B, out Point3d pFixB, out _, out _, out Vector3d tWorkOutB);
                Vector3d sharedFlow = ComputeSharedFlowDirection(tWorkOutA, tWorkOutB, pFixA, pFixB, Vector3d.ZAxis);

                tanTargetA = modT1_A ? sharedFlow : -sharedFlow;
                tanTargetB = modT1_B ? -sharedFlow : sharedFlow;

                if (debug)
                {
                    RhinoApp.WriteLine($"[Debug] Shared Tangent Target A: {tanTargetA}");
                    RhinoApp.WriteLine($"[Debug] Shared Tangent Target B: {tanTargetB}");
                }
            }

            var candidates = new List<(NurbsCurve CandA, NurbsCurve CandB, string Desc)>();

            if (continuity == 0)
            {
                NurbsCurve candA = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve candB = (NurbsCurve)ncWipB.Duplicate();
                candA.Points.SetPoint(idxCpPosA, ptAvg, candA.Points.GetWeight(idxCpPosA));
                candB.Points.SetPoint(idxCpPosB, ptAvg, candB.Points.GetWeight(idxCpPosB));
                candidates.Add((candA, candB, "Both Position: Move P0"));
            }
            else if (continuity == 1)
            {
                // Project P1 strictly along each curve's forward inward ray to guarantee smooth G1 (no 180-deg cusps)
                Vector3d inwardDirA = modT1_A ? -tanTargetA : tanTargetA;
                Vector3d inwardDirB = modT1_B ? -tanTargetB : tanTargetB;

                if (inwardDirA.Unitize() && inwardDirB.Unitize())
                {
                    double projA = (ncWipA.Points[idxCpTanA].Location - ptAvg) * inwardDirA;
                    double projB = (ncWipB.Points[idxCpTanB].Location - ptAvg) * inwardDirB;

                    if (projA > 1e-6 && projB > 1e-6)
                    {
                        NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                        NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();
                        cand1A.Points.SetPoint(idxCpPosA, ptAvg, cand1A.Points.GetWeight(idxCpPosA));
                        cand1B.Points.SetPoint(idxCpPosB, ptAvg, cand1B.Points.GetWeight(idxCpPosB));
                        cand1A.Points.SetPoint(idxCpTanA, ptAvg + projA * inwardDirA, cand1A.Points.GetWeight(idxCpTanA));
                        cand1B.Points.SetPoint(idxCpTanB, ptAvg + projB * inwardDirB, cand1B.Points.GetWeight(idxCpTanB));
                        candidates.Add((cand1A, cand1B, "Both Tangency: ClosestPoint (Slide P1)"));
                    }
                }

                NurbsCurve cand2A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand2B = (NurbsCurve)ncWipB.Duplicate();
                bool ok2A = cand2A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetA);
                bool ok2B = cand2B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Tangency, ptAvg, tanTargetB);

                if (ok2A && ok2B)
                {
                    candidates.Add((cand2A, cand2B, "Both Tangency: SetEndCondition"));

                    if (trueOrigDistA > RhinoMath.ZeroTolerance && trueOrigDistB > RhinoMath.ZeroTolerance)
                    {
                        NurbsCurve cand3A = (NurbsCurve)cand2A.Duplicate();
                        NurbsCurve cand3B = (NurbsCurve)cand2B.Duplicate();

                        Vector3d newDirA = cand3A.Points[idxCpTanA].Location - cand3A.Points[idxCpPosA].Location;
                        Vector3d newDirB = cand3B.Points[idxCpTanB].Location - cand3B.Points[idxCpPosB].Location;
                        if (newDirA.Unitize() && newDirB.Unitize())
                        {
                            cand3A.Points.SetPoint(idxCpTanA, ptAvg + (newDirA * trueOrigDistA), cand3A.Points.GetWeight(idxCpTanA));
                            cand3B.Points.SetPoint(idxCpTanB, ptAvg + (newDirB * trueOrigDistB), cand3B.Points.GetWeight(idxCpTanB));
                            candidates.Add((cand3A, cand3B, "Both Tangency: Preserve P0-P1 (_Match)"));
                        }
                        else
                        {
                            cand3A.Dispose();
                            cand3B.Dispose();
                        }
                    }
                }
                else
                {
                    cand2A.Dispose();
                    cand2B.Dispose();
                }
            }
            else if (continuity == 2)
            {
                Vector3d curvTarget = (0.5 * curvA) + (0.5 * curvB);
                curvTarget -= (curvTarget * tanTargetB) * tanTargetB;

                if (debug) RhinoApp.WriteLine($"[Debug] Shared Curvature Target: {curvTarget}");

                NurbsCurve cand1A = (NurbsCurve)ncWipA.Duplicate();
                NurbsCurve cand1B = (NurbsCurve)ncWipB.Duplicate();

                bool okA = cand1A.SetEndCondition(modT1_A, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetA, curvTarget);
                bool okB = cand1B.SetEndCondition(modT1_B, NurbsCurve.NurbsCurveEndConditionType.Curvature, ptAvg, tanTargetB, curvTarget);

                if (okA && okB)
                {
                    candidates.Add((cand1A, cand1B, "Both Curvature: SetEndCondition"));
                }
                else
                {
                    cand1A.Dispose();
                    cand1B.Dispose();
                }
            }

            (NurbsCurve ResultA, NurbsCurve ResultB) winner = (null, null);
            double bestAvgDevSum = double.MaxValue;
            double bestMaxDevA = double.MaxValue;
            double bestMaxDevB = double.MaxValue;

            int origInfA = crvA.InflectionPoints()?.Length ?? 0;
            int origInfB = crvB.InflectionPoints()?.Length ?? 0;

            var origInternalContA = preserveInternalContinuity ? GetInternalContinuities(ncWipA, posTol, angleTolDeg, curvTolPct) : null;
            var origInternalContB = preserveInternalContinuity ? GetInternalContinuities(ncWipB, posTol, angleTolDeg, curvTolPct) : null;

            bool rejectedByInflection = false;
            bool rejectedByDev = false;
            bool rejectedByInternalCont = false;

            if (debug) RhinoApp.WriteLine($"--- MatchBothCurves NURBS Candidates Evaluated ({candidates.Count}) ---");

            for (int i = 0; i < candidates.Count; i++)
            {
                var candA = candidates[i].CandA;
                var candB = candidates[i].CandB;
                string desc = candidates[i].Desc;

                double maxDevA = GetMaximumDeviation(crvA, candA);
                double maxDevB = GetMaximumDeviation(crvB, candB);
                double maxDev = Math.Max(maxDevA, maxDevB);

                double avgDevA = GetAverageDeviation(crvA, candA);
                double avgDevB = GetAverageDeviation(crvB, candB);
                double avgDevSum = avgDevA + avgDevB;

                double radA = RadiusMinimaMath.GetMinimumRadius(candA) ?? double.MinValue;
                double radB = RadiusMinimaMath.GetMinimumRadius(candB) ?? double.MinValue;
                double minRad = Math.Min(radA, radB);

                double p0p1A = continuity > 0 ? candA.Points[idxCpPosA].Location.DistanceTo(candA.Points[idxCpTanA].Location) : 0;
                double p0p1B = continuity > 0 ? candB.Points[idxCpPosB].Location.DistanceTo(candB.Points[idxCpTanB].Location) : 0;

                if (debug)
                    RhinoApp.WriteLine($" {desc}\n    AvgDev Sum: {avgDevSum:g} (A:{avgDevA:g}, B:{avgDevB:g}) | MaxDev: {maxDev:g} | MinRad: {(minRad != double.MinValue ? minRad.ToString("g") : "N/A")} | P0-P1 Dist: (A:{p0p1A:g}, B:{p0p1B:g})");

                if (preserveInternalContinuity && (origInternalContA != null || origInternalContB != null))
                {
                    bool degraded = false;
                    if (origInternalContA != null)
                    {
                        foreach (var kvp in origInternalContA)
                        {
                            var vB = ContinuityUtils.GetContinuityVectorsAt(candA, kvp.Key, CurveEvaluationSide.Below);
                            var vA = ContinuityUtils.GetContinuityVectorsAt(candA, kvp.Key, CurveEvaluationSide.Above);
                            int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                            int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                            if (newCont < targetCont)
                            {
                                if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded Curve A internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                                degraded = true;
                                break;
                            }
                        }
                    }
                    if (!degraded && origInternalContB != null)
                    {
                        foreach (var kvp in origInternalContB)
                        {
                            var vB = ContinuityUtils.GetContinuityVectorsAt(candB, kvp.Key, CurveEvaluationSide.Below);
                            var vA = ContinuityUtils.GetContinuityVectorsAt(candB, kvp.Key, CurveEvaluationSide.Above);
                            int newCont = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvTolPct) ?? 0;

                            int targetCont = Math.Min(kvp.Value, maxInternalContinuity);
                            if (newCont < targetCont)
                            {
                                if (debug) RhinoApp.WriteLine($"    [Rejected] Degraded Curve B internal continuity at t={kvp.Key:F3} (Orig: G{kvp.Value}, New: G{newCont}, Target: G{targetCont})");
                                degraded = true;
                                break;
                            }
                        }
                    }

                    if (degraded)
                    {
                        candA.Dispose();
                        candB.Dispose();
                        rejectedByInternalCont = true;
                        continue;
                    }
                }

                if (preventExtraInflections)
                {
                    int candInfA = candA.InflectionPoints()?.Length ?? 0;
                    int candInfB = candB.InflectionPoints()?.Length ?? 0;
                    if (candInfA > origInfA || candInfB > origInfB)
                    {
                        if (debug) RhinoApp.WriteLine($"    [Rejected] Added extra inflection points (OrigA: {origInfA}, OrigB: {origInfB} | CandA: {candInfA}, CandB: {candInfB})");
                        candA.Dispose();
                        candB.Dispose();
                        rejectedByInflection = true;
                        continue;
                    }
                }

                if (maxDev > devTol)
                {
                    if (candA != winner.ResultA) candA.Dispose();
                    if (candB != winner.ResultB) candB.Dispose();
                    rejectedByDev = true;
                    continue;
                }

                if (avgDevSum < bestAvgDevSum)
                {
                    bestAvgDevSum = avgDevSum;
                    bestMaxDevA = maxDevA;
                    bestMaxDevB = maxDevB;
                    if (winner.ResultA != null && winner.ResultA != candA) winner.ResultA.Dispose();
                    if (winner.ResultB != null && winner.ResultB != candB) winner.ResultB.Dispose();
                    winner = (candA, candB);
                }
                else
                {
                    candA.Dispose();
                    candB.Dispose();
                }
            }

            ncWipA.Dispose();
            ncWipB.Dispose();

            if (winner.ResultA != null && !IsMeaningfullyChanged(crvA, winner.ResultA, bestMaxDevA))
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding Result A: Max deviation ({bestMaxDevA:g}) is below ZeroTolerance.");
                winner.ResultA.Dispose();
                winner.ResultA = null;
            }
            if (winner.ResultB != null && !IsMeaningfullyChanged(crvB, winner.ResultB, bestMaxDevB))
            {
                if (debug) RhinoApp.WriteLine($"[Debug] Discarding Result B: Max deviation ({bestMaxDevB:g}) is below ZeroTolerance.");
                winner.ResultB.Dispose();
                winner.ResultB = null;
            }

            string errorMsg = null;
            if (winner.ResultA == null && winner.ResultB == null)
            {
                if (rejectedByDev) errorMsg = "Result cannot be created because it exceeds the Crv dev limit.";
                else if (rejectedByInternalCont) errorMsg = "Result cannot be created because it would degrade internal continuity.";
                else if (rejectedByInflection) errorMsg = "Result cannot be created because it would add extra inflection points.";
                else errorMsg = "Result cannot be created with the current constraints (curves may already be optimized).";
            }

            return (winner.ResultA, winner.ResultB, errorMsg);
        }
        // ==============================================================================
        // NURBS EXTEND / TRIM SOLVERS (ZERO KNOT ADDITIONS, EXACT KNOT MULTIPLICITIES)
        // ==============================================================================

        private static bool HasSameKnotMultiplicities(NurbsCurve orig, NurbsCurve cand)
        {
            if (orig == null || cand == null) return false;
            if (orig.Degree != cand.Degree ||
                orig.Points.Count != cand.Points.Count ||
                orig.SpanCount != cand.SpanCount ||
                orig.Knots.Count != cand.Knots.Count)
            {
                return false;
            }

            int i = 0;
            while (i < orig.Knots.Count)
            {
                int mOrig = orig.Knots.KnotMultiplicity(i);
                int mCand = cand.Knots.KnotMultiplicity(i);
                if (mOrig != mCand) return false;
                i += Math.Max(1, mOrig);
            }
            return true;
        }

        private static bool HasValidTrimmedNurbsStructure(NurbsCurve orig, NurbsCurve trimmed)
        {
            if (orig == null || trimmed == null || !trimmed.IsValid) return false;
            if (trimmed.Degree != orig.Degree) return false;
            if (trimmed.SpanCount > orig.SpanCount || trimmed.Points.Count > orig.Points.Count) return false;
            return true;
        }

        private static NurbsCurve BuildMaxValidSmoothExtension(
            NurbsCurve origNc, bool modT1, double desiredLength, BoundingBox refBounds)
        {
            if (origNc == null || !origNc.IsValid || desiredLength <= RhinoMath.ZeroTolerance)
                return null;

            CurveEnd side = modT1 ? CurveEnd.End : CurveEnd.Start;
            double tryLen = desiredLength;

            for (int step = 0; step < 8; step++)
            {
                if (tryLen <= RhinoMath.ZeroTolerance) break;

                Curve ext = origNc.Extend(side, tryLen, CurveExtensionStyle.Smooth);
                if (ext is NurbsCurve extNc &&
                    extNc.IsValid &&
                    HasSameKnotMultiplicities(origNc, extNc) &&
                    IsWithinWorkingScale(refBounds, extNc))
                {
                    return extNc;
                }

                ext?.Dispose();
                tryLen *= 0.5;
            }

            return null;
        }

        /// <summary>
        /// Cuts along probeExt (which equals origNc on origNc.Domain and smoothly extends beyond modT1)
        /// at parameter tTarget, using origNc.Trim directly when trimming and probeExt.Trim when extending
        /// so extended curves strictly retain identical knot multiplicities and control-point counts.
        /// </summary>
        private static NurbsCurve BuildTrimmedOrExtendedAtParameter(
            NurbsCurve origNc, NurbsCurve probeExt, bool modT1, double tTarget, BoundingBox refBounds)
        {
            Interval origDom = origNc.Domain;
            Interval probeDom = probeExt.Domain;

            if (modT1)
            {
                if (tTarget <= origDom.T0 + RhinoMath.ZeroTolerance || tTarget > probeDom.T1 + RhinoMath.ZeroTolerance)
                    return null;

                if (Math.Abs(tTarget - origDom.T1) <= RhinoMath.ZeroTolerance)
                    return (NurbsCurve)origNc.Duplicate();

                if (tTarget < origDom.T1)
                {
                    Curve tr = origNc.Trim(origDom.T0, tTarget);
                    NurbsCurve trNc = tr as NurbsCurve ?? tr?.ToNurbsCurve();
                    if (!ReferenceEquals(tr, trNc)) tr?.Dispose();
                    if (trNc != null && HasValidTrimmedNurbsStructure(origNc, trNc) && IsWithinWorkingScale(refBounds, trNc))
                        return trNc;
                    trNc?.Dispose();
                    return null;
                }

                double clampedT1 = Math.Min(tTarget, probeDom.T1);
                if (Math.Abs(clampedT1 - probeDom.T1) <= RhinoMath.ZeroTolerance)
                    return (NurbsCurve)probeExt.Duplicate();

                Curve extTr = probeExt.Trim(origDom.T0, clampedT1);
                NurbsCurve extNc = extTr as NurbsCurve;
                if (extNc != null && HasSameKnotMultiplicities(origNc, extNc) && IsWithinWorkingScale(refBounds, extNc))
                    return extNc;
                extTr?.Dispose();
                return null;
            }
            else
            {
                if (tTarget >= origDom.T1 - RhinoMath.ZeroTolerance || tTarget < probeDom.T0 - RhinoMath.ZeroTolerance)
                    return null;

                if (Math.Abs(tTarget - origDom.T0) <= RhinoMath.ZeroTolerance)
                    return (NurbsCurve)origNc.Duplicate();

                if (tTarget > origDom.T0)
                {
                    Curve tr = origNc.Trim(tTarget, origDom.T1);
                    NurbsCurve trNc = tr as NurbsCurve ?? tr?.ToNurbsCurve();
                    if (!ReferenceEquals(tr, trNc)) tr?.Dispose();
                    if (trNc != null && HasValidTrimmedNurbsStructure(origNc, trNc) && IsWithinWorkingScale(refBounds, trNc))
                        return trNc;
                    trNc?.Dispose();
                    return null;
                }

                double clampedT0 = Math.Max(tTarget, probeDom.T0);
                if (Math.Abs(clampedT0 - probeDom.T0) <= RhinoMath.ZeroTolerance)
                    return (NurbsCurve)probeExt.Duplicate();

                Curve extTr = probeExt.Trim(clampedT0, origDom.T1);
                NurbsCurve extNc = extTr as NurbsCurve;
                if (extNc != null && HasSameKnotMultiplicities(origNc, extNc) && IsWithinWorkingScale(refBounds, extNc))
                    return extNc;
                extTr?.Dispose();
                return null;
            }
        }

        private static (Curve Result, string ErrorMsg) SolveExtendTrimNurbsToFixedTarget(
            Curve origCrv, bool modT1,
            Curve crvRef, bool refT1,
            int continuity, double devTol,
            double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            NurbsCurve origNc = origCrv as NurbsCurve ?? origCrv.ToNurbsCurve();
            bool ownsOrigNc = !ReferenceEquals(origNc, origCrv);
            if (origNc == null)
                return (null, "Failed to evaluate NURBS curve for Extend/Trim.");

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            Point3d pRef = refT1 ? crvRef.PointAtEnd : crvRef.PointAtStart;
            Point3d pWork = modT1 ? origNc.PointAtEnd : origNc.PointAtStart;
            double chordDist = pWork.DistanceTo(pRef);
            double origLen = origNc.GetLength();
            BoundingBox refBounds = GetReferenceBounds(origCrv, crvRef);

            double probeLen = Math.Max(2.5 * chordDist, 0.75 * origLen);
            NurbsCurve probeExt = BuildMaxValidSmoothExtension(origNc, modT1, probeLen, refBounds) ?? (NurbsCurve)origNc.Duplicate();

            var candidates = new List<(Curve Crv, string Desc)>();
            try
            {
                var paramSeeds = new List<double>();
                double tOnOrig;
                if (origNc.ClosestPoint(pRef, out tOnOrig) && origNc.PointAt(tOnOrig).DistanceTo(pRef) <= effPosTol)
                    paramSeeds.Add(tOnOrig);

                double tOnProbe;
                if (probeExt.ClosestPoint(pRef, out tOnProbe) && probeExt.PointAt(tOnProbe).DistanceTo(pRef) <= effPosTol)
                    paramSeeds.Add(tOnProbe);

                foreach (double tHit in paramSeeds)
                {
                    NurbsCurve cand = BuildTrimmedOrExtendedAtParameter(origNc, probeExt, modT1, tHit, refBounds);
                    if (cand == null) continue;

                    if (AreCurvesAtContinuity(cand, modT1, crvRef, refT1, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                    {
                        candidates.Add((cand, "NURBS Extend/Trim: Meet Fixed Target"));
                    }
                    else
                    {
                        cand.Dispose();
                    }
                }

                if (candidates.Count == 0)
                    return (null, "Result cannot be created because the NURBS curve cannot reach the target continuity by Extend/Trim alone.");

                return EvaluateAnalyticalSingleCandidates(origCrv, candidates, devTol, debug);
            }
            finally
            {
                probeExt.Dispose();
                if (ownsOrigNc) origNc.Dispose();
            }
        }

        private static (Curve ResultA, Curve ResultB, string ErrorMsg) SolveBothExtendTrimNurbs(
            Curve crvA, bool modT1_A,
            Curve crvB, bool modT1_B,
            int continuity, double devTol,
            double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            NurbsCurve origA = crvA as NurbsCurve ?? crvA.ToNurbsCurve();
            NurbsCurve origB = crvB as NurbsCurve ?? crvB.ToNurbsCurve();
            bool ownsA = !ReferenceEquals(origA, crvA);
            bool ownsB = !ReferenceEquals(origB, crvB);

            if (origA == null || origB == null)
            {
                if (ownsA) origA?.Dispose();
                if (ownsB) origB?.Dispose();
                return (null, null, "Failed to evaluate NURBS curves for Extend/Trim.");
            }

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            Point3d pWorkA = modT1_A ? origA.PointAtEnd : origA.PointAtStart;
            Point3d pWorkB = modT1_B ? origB.PointAtEnd : origB.PointAtStart;
            double gap = pWorkA.DistanceTo(pWorkB);
            BoundingBox refBounds = GetReferenceBounds(crvA, crvB);
            double diag = Math.Max(refBounds.Diagonal.Length, 1.0);

            double probeLenA = Math.Max(3.0 * gap, Math.Max(origA.GetLength(), diag));
            double probeLenB = Math.Max(3.0 * gap, Math.Max(origB.GetLength(), diag));

            NurbsCurve probeA = BuildMaxValidSmoothExtension(origA, modT1_A, probeLenA, refBounds) ?? (NurbsCurve)origA.Duplicate();
            NurbsCurve probeB = BuildMaxValidSmoothExtension(origB, modT1_B, probeLenB, refBounds) ?? (NurbsCurve)origB.Duplicate();

            var candidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            try
            {
                var paramPairs = new List<(double tA, double tB)>();
                double ixTol = Math.Max(effPosTol, 1e-7);

                var ccx = Rhino.Geometry.Intersect.Intersection.CurveCurve(probeA, probeB, ixTol, ixTol);
                if (ccx != null)
                {
                    foreach (var ev in ccx)
                    {
                        paramPairs.Add((ev.ParameterA, ev.ParameterB));
                        if (ev.IsOverlap)
                        {
                            paramPairs.Add((ev.OverlapA.Mid, ev.OverlapB.Mid));
                        }
                    }
                }

                Point3d nearA, nearB;
                if (probeA.ClosestPoints(probeB, out nearA, out nearB) && nearA.DistanceTo(nearB) <= ixTol)
                {
                    double tNearA, tNearB;
                    if (probeA.ClosestPoint(nearA, out tNearA) && probeB.ClosestPoint(nearB, out tNearB))
                        paramPairs.Add((tNearA, tNearB));
                }

                foreach (var pair in paramPairs)
                {
                    NurbsCurve candA = BuildTrimmedOrExtendedAtParameter(origA, probeA, modT1_A, pair.tA, refBounds);
                    NurbsCurve candB = BuildTrimmedOrExtendedAtParameter(origB, probeB, modT1_B, pair.tB, refBounds);
                    if (candA == null || candB == null)
                    {
                        candA?.Dispose();
                        candB?.Dispose();
                        continue;
                    }

                    if (AreCurvesAtContinuity(candA, modT1_A, candB, modT1_B, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                    {
                        candidates.Add((candA, candB, "Both NURBS Extend/Trim: Intersection Meet"));
                    }
                    else
                    {
                        candA.Dispose();
                        candB.Dispose();
                    }
                }

                if (candidates.Count == 0)
                    return (null, null, "Result cannot be created because the two NURBS curves do not meet at the requested continuity by Extend/Trim alone.");

                return EvaluateAnalyticalPairCandidates(crvA, crvB, candidates, devTol, debug);
            }
            finally
            {
                probeA.Dispose();
                probeB.Dispose();
                if (ownsA) origA.Dispose();
                if (ownsB) origB.Dispose();
            }
        }

        private static (Curve ResultExtNurbs, Curve ResultPrim, string ErrorMsg) SolveExtendTrimNurbsAndPrimitive(
            Curve nurbsCrv, bool modT1_Nurbs,
            Curve primCrv, bool isArc, Arc origArc, Line origLine, bool modT1_Prim,
            int continuity, int preserveOtherEnd, double devTol,
            double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            NurbsCurve origNc = nurbsCrv as NurbsCurve ?? nurbsCrv.ToNurbsCurve();
            bool ownsNc = !ReferenceEquals(origNc, nurbsCrv);
            if (origNc == null)
                return (null, null, "Failed to evaluate NURBS curve for Extend/Trim.");

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            Point3d pFixNurbs, pWorkNurbs, pFixPrim, pWorkPrim;
            Vector3d tFixNurbsInto, tWorkNurbsOut, tFixPrimInto, tWorkPrimOut;
            GetOrientedEnds(origNc, modT1_Nurbs, out pFixNurbs, out pWorkNurbs, out tFixNurbsInto, out tWorkNurbsOut);
            GetOrientedEnds(primCrv, modT1_Prim, out pFixPrim, out pWorkPrim, out tFixPrimInto, out tWorkPrimOut);

            Point3d ptAvg = 0.5 * (pWorkNurbs + pWorkPrim);
            double gap = pWorkNurbs.DistanceTo(pWorkPrim);
            BoundingBox refBounds = GetReferenceBounds(nurbsCrv, primCrv);
            double diag = Math.Max(refBounds.Diagonal.Length, 1.0);

            double probeLen = Math.Max(2.5 * gap, Math.Max(origNc.GetLength(), diag));
            NurbsCurve probeExt = BuildMaxValidSmoothExtension(origNc, modT1_Nurbs, probeLen, refBounds) ?? (NurbsCurve)origNc.Duplicate();

            var pairCandidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            string lastErr = null;

            try
            {
                Interval origDom = origNc.Domain;
                Interval probeDom = probeExt.Domain;
                double tOrigWork = modT1_Nurbs ? origDom.T1 : origDom.T0;

                // Orientation-invariant parameter mapping u in [0.04, 1.0] from fixed end toward probe tip
                double ParamFromFixedFraction(double u)
                {
                    return modT1_Nurbs
                        ? probeDom.ParameterAt(u)
                        : probeDom.ParameterAt(1.0 - u);
                }

                var tSeeds = new List<double> { tOrigWork };

                Point3d[] projTargets =
                {
                    ptAvg,
                    pWorkPrim,
                    pWorkNurbs + 0.25 * (pWorkPrim - pWorkNurbs),
                    pWorkNurbs + 0.75 * (pWorkPrim - pWorkNurbs)
                };
                foreach (Point3d pt in projTargets)
                {
                    double tProj;
                    if (probeExt.ClosestPoint(pt, out tProj))
                        tSeeds.Add(tProj);
                }

                double ixTol = Math.Max(effPosTol, 1e-7);
                if (!isArc)
                {
                    Line infLine = new Line(pFixPrim, pFixPrim + tFixPrimInto);
                    var clx = Rhino.Geometry.Intersect.Intersection.CurveLine(probeExt, infLine, ixTol, ixTol);
                    if (clx != null)
                    {
                        foreach (var ev in clx)
                            tSeeds.Add(ev.ParameterA);
                    }
                }
                else
                {
                    using (var fullCircleCrv = new ArcCurve(new Circle(origArc.Plane, origArc.Radius)))
                    {
                        var ccx = Rhino.Geometry.Intersect.Intersection.CurveCurve(probeExt, fullCircleCrv, ixTol, ixTol);
                        if (ccx != null)
                        {
                            foreach (var ev in ccx)
                                tSeeds.Add(ev.ParameterA);
                        }
                    }
                }

                // Symmetric uniform samples across probeExt + 1D root-finding for exact G1/G2 kiss points
                const int nGrid = 48;
                var gridParams = new double[nGrid];
                for (int k = 0; k < nGrid; k++)
                {
                    double u = 0.08 + 0.92 * ((double)k / (nGrid - 1));
                    double tg = ParamFromFixedFraction(u);
                    gridParams[k] = tg;
                    if (k % 3 == 0) tSeeds.Add(tg);
                }

                // Outward flow tangent of candNurbs at parameter t (pointing from NURBS into Primitive)
                Vector3d FlowOutAt(double t)
                {
                    Vector3d tan = probeExt.TangentAt(t);
                    if (!modT1_Nurbs) tan.Reverse();
                    tan.Unitize();
                    return tan;
                }

                // 1D Root A: Line G1 tangency (line from pFixPrim to P(t) collinear with -FlowOutAt(t))
                if (!isArc)
                {
                    Vector3d refN = Vector3d.CrossProduct(tFixPrimInto, pWorkNurbs - pFixPrim);
                    if (!refN.Unitize()) refN = Vector3d.ZAxis;

                    double LineTanResidual(double t)
                    {
                        Point3d p = probeExt.PointAt(t);
                        Vector3d toFix = pFixPrim - p;
                        if (!toFix.Unitize()) return 0.0;
                        Vector3d flow = FlowOutAt(t);
                        return Vector3d.CrossProduct(flow, toFix) * refN;
                    }

                    AddSignChangeRoots(gridParams, LineTanResidual, tSeeds);
                }
                else
                {
                    // 1D Root B: Arc Preserve-Tangency G1 kiss (Arc(pFixPrim, tFixPrimInto, P(t)) tangent matches FlowOutAt(t))
                    Vector3d refN = origArc.Plane.ZAxis;
                    double ArcKissResidual(double t)
                    {
                        Point3d p = probeExt.PointAt(t);
                        Arc a = new Arc(pFixPrim, tFixPrimInto, p);
                        if (!a.IsValid) return double.NaN;
                        Vector3d arcIntoFromP;
                        using (var ac = new ArcCurve(a))
                        {
                            arcIntoFromP = -ac.TangentAtEnd;
                        }
                        arcIntoFromP.Unitize();
                        Vector3d flow = FlowOutAt(t);
                        return Vector3d.CrossProduct(flow, arcIntoFromP) * refN;
                    }

                    AddSignChangeRoots(gridParams, ArcKissResidual, tSeeds);

                    // 1D Root C: Arc Preserve-Position G2 osculating circle passing through pFixPrim
                    double OscCircleResidual(double t)
                    {
                        Vector3d kVec = probeExt.CurvatureAt(t);
                        double kLen = kVec.Length;
                        if (kLen <= 1e-9) return double.NaN;
                        double rOsc = 1.0 / kLen;
                        Point3d cOsc = probeExt.PointAt(t) + kVec / (kLen * kLen);
                        return cOsc.DistanceTo(pFixPrim) - rOsc;
                    }

                    AddSignChangeRoots(gridParams, OscCircleResidual, tSeeds);
                }

                double bestScore = double.MaxValue;
                double bestT = double.NaN;

                void TryEvaluateAtParameter(double tVal, string label)
                {
                    NurbsCurve candNc = BuildTrimmedOrExtendedAtParameter(origNc, probeExt, modT1_Nurbs, tVal, refBounds);
                    if (candNc == null) return;

                    if (GetMaximumDeviation(nurbsCrv, candNc) > devTol)
                    {
                        candNc.Dispose();
                        lastErr = "Result cannot be created because it exceeds the Crv dev limit.";
                        return;
                    }

                    double effAngDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
                    double effCrvPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);
                    if (AreCurvesAtContinuity(candNc, modT1_Nurbs, primCrv, modT1_Prim, continuity, effPosTol, effAngDeg, effCrvPct))
                    {
                        pairCandidates.Add((candNc.DuplicateCurve(), primCrv.DuplicateCurve(), label + " (Exact Meet)"));
                    }

                    var primRes = isArc
                        ? SolveAnalyticalArcToFixedTarget(primCrv, origArc, modT1_Prim, candNc, modT1_Nurbs, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, false)
                        : SolveAnalyticalLineToFixedTarget(primCrv, origLine, modT1_Prim, candNc, modT1_Nurbs, continuity, preserveOtherEnd, devTol, posTol, angleTolDeg, curvTolPct, false);

                    if (primRes.Result != null)
                    {
                        double sc = GetAverageDeviation(nurbsCrv, candNc, 20) + GetAverageDeviation(primCrv, primRes.Result, 20);
                        if (sc < bestScore)
                        {
                            bestScore = sc;
                            bestT = tVal;
                        }
                        pairCandidates.Add((candNc, primRes.Result, label));
                    }
                    else
                    {
                        candNc.Dispose();
                        if (!string.IsNullOrEmpty(primRes.ErrorMsg)) lastErr = primRes.ErrorMsg;
                    }
                }

                foreach (double tSeed in DeduplicateParameters(tSeeds, 1e-7 * probeDom.Length))
                {
                    TryEvaluateAtParameter(tSeed, "Hybrid NURBS(Extend/Trim) + Primitive");
                }

                // 1D coordinate polish around the best parameter so Preserve=Position / G0 achieves minimum deviation
                if (!double.IsNaN(bestT))
                {
                    double step = 0.04 * probeDom.Length;
                    double curT = bestT;
                    for (int iter = 0; iter < 12; iter++)
                    {
                        double prevBest = bestScore;
                        TryEvaluateAtParameter(curT + step, "Hybrid NURBS(Extend/Trim) + Primitive (Refined)");
                        if (bestScore < prevBest - RhinoMath.ZeroTolerance) { curT = bestT; continue; }
                        TryEvaluateAtParameter(curT - step, "Hybrid NURBS(Extend/Trim) + Primitive (Refined)");
                        if (bestScore < prevBest - RhinoMath.ZeroTolerance) { curT = bestT; continue; }
                        step *= 0.5;
                    }
                }

                if (pairCandidates.Count == 0)
                    return (null, null, lastErr ?? "Result cannot be created with NURBS set to Extend/Trim and the current constraints.");

                return EvaluateAnalyticalPairCandidates(nurbsCrv, primCrv, pairCandidates, devTol, debug);
            }
            finally
            {
                probeExt.Dispose();
                if (ownsNc) origNc.Dispose();
            }
        }

        private static (Curve ResultExtNurbs, Curve ResultDeformNurbs, string ErrorMsg) SolveExtendTrimAndDeformNurbs(
            Curve extNurbsCrv, bool modT1_Ext,
            Curve deformCrv, bool modT1_Def,
            int continuity, int preserveOtherEnd, int lineMode, int arcMode, int incrAsNeeded, double devTol,
            bool preventExtraInflections, bool preserveInternalContinuity, int maxInternalContinuity,
            double posTol, double angleTolDeg, double curvTolPct, bool debug)
        {
            NurbsCurve origExtNc = extNurbsCrv as NurbsCurve ?? extNurbsCrv.ToNurbsCurve();
            bool ownsExt = !ReferenceEquals(origExtNc, extNurbsCrv);
            if (origExtNc == null)
                return (null, null, "Failed to evaluate NURBS curve for Extend/Trim.");

            double effPosTol = Math.Max(posTol, RhinoMath.ZeroTolerance);
            double effAngleTolDeg = RhinoMath.ToDegrees(Math.Max(RhinoMath.ToRadians(angleTolDeg), RhinoMath.SqrtEpsilon));
            double effCurvTolPct = Math.Max(curvTolPct, RhinoMath.SqrtEpsilon);

            Point3d pWorkExt = modT1_Ext ? origExtNc.PointAtEnd : origExtNc.PointAtStart;
            Point3d pWorkDef = modT1_Def ? deformCrv.PointAtEnd : deformCrv.PointAtStart;
            Point3d ptAvg = 0.5 * (pWorkExt + pWorkDef);
            double gap = pWorkExt.DistanceTo(pWorkDef);
            BoundingBox refBounds = GetReferenceBounds(extNurbsCrv, deformCrv);

            double probeLen = Math.Max(2.5 * gap, origExtNc.GetLength());
            NurbsCurve probeExt = BuildMaxValidSmoothExtension(origExtNc, modT1_Ext, probeLen, refBounds) ?? (NurbsCurve)origExtNc.Duplicate();

            var pairCandidates = new List<(Curve CandA, Curve CandB, string Desc)>();
            string lastErr = null;

            try
            {
                double tOrigWork = modT1_Ext ? origExtNc.Domain.T1 : origExtNc.Domain.T0;
                var tSeeds = new List<double> { tOrigWork };

                Point3d[] sampleTargets =
                {
                    ptAvg,
                    pWorkDef,
                    pWorkExt + 0.25 * (pWorkDef - pWorkExt),
                    pWorkExt + 0.75 * (pWorkDef - pWorkExt)
                };
                foreach (Point3d st in sampleTargets)
                {
                    double tProj;
                    if (probeExt.ClosestPoint(st, out tProj))
                        tSeeds.Add(tProj);
                }

                foreach (double tVal in DeduplicateParameters(tSeeds, 1e-6 * probeExt.Domain.Length))
                {
                    NurbsCurve candExt = BuildTrimmedOrExtendedAtParameter(origExtNc, probeExt, modT1_Ext, tVal, refBounds);
                    if (candExt == null) continue;

                    if (GetMaximumDeviation(extNurbsCrv, candExt) > devTol)
                    {
                        candExt.Dispose();
                        lastErr = "Result cannot be created because it exceeds the Crv dev limit.";
                        continue;
                    }

                    if (AreCurvesAtContinuity(candExt, modT1_Ext, deformCrv, modT1_Def, continuity, effPosTol, effAngleTolDeg, effCurvTolPct))
                    {
                        pairCandidates.Add((candExt.DuplicateCurve(), deformCrv.DuplicateCurve(), "Hybrid Extend/Trim + Deform NURBS (Exact Meet)"));
                    }

                    var defRes = SolveNurbsOneCurve(
                        deformCrv, candExt, modT1_Def, modT1_Ext,
                        continuity, preserveOtherEnd, lineMode, arcMode, incrAsNeeded, devTol,
                        preventExtraInflections, preserveInternalContinuity, maxInternalContinuity,
                        posTol, angleTolDeg, curvTolPct, debug);

                    if (defRes.Result != null)
                    {
                        pairCandidates.Add((candExt, defRes.Result, "Hybrid Extend/Trim + Deform NURBS"));
                    }
                    else
                    {
                        candExt.Dispose();
                        if (!string.IsNullOrEmpty(defRes.ErrorMsg)) lastErr = defRes.ErrorMsg;
                    }
                }

                if (pairCandidates.Count == 0)
                    return (null, null, lastErr ?? "Result cannot be created with the current constraints.");

                return EvaluateAnalyticalPairCandidates(extNurbsCrv, deformCrv, pairCandidates, devTol, debug);
            }
            finally
            {
                probeExt.Dispose();
                if (ownsExt) origExtNc.Dispose();
            }
        }

        private static void AddSignChangeRoots(double[] grid, Func<double, double> f, List<double> rootsOut)
        {
            double prevT = grid[0];
            double prevV = f(prevT);

            for (int i = 1; i < grid.Length; i++)
            {
                double curT = grid[i];
                double curV = f(curT);
                if (!double.IsNaN(prevV) && !double.IsNaN(curV))
                {
                    if (Math.Abs(curV) <= 1e-12)
                    {
                        rootsOut.Add(curT);
                    }
                    else if ((prevV < 0.0 && curV > 0.0) || (prevV > 0.0 && curV < 0.0))
                    {
                        double lo = prevT, hi = curT, vLo = prevV;
                        for (int b = 0; b < 42; b++)
                        {
                            double mid = 0.5 * (lo + hi);
                            double vMid = f(mid);
                            if (double.IsNaN(vMid) || Math.Abs(vMid) <= 1e-13)
                            {
                                lo = hi = mid;
                                break;
                            }
                            if ((vLo < 0.0 && vMid > 0.0) || (vLo > 0.0 && vMid < 0.0))
                                hi = mid;
                            else
                            {
                                lo = mid;
                                vLo = vMid;
                            }
                        }
                        rootsOut.Add(0.5 * (lo + hi));
                    }
                }
                prevT = curT;
                prevV = curV;
            }
        }

        private static List<double> DeduplicateParameters(List<double> raw, double minSep)
        {
            var unique = new List<double>(raw.Count);
            foreach (double v in raw)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                bool exists = false;
                for (int i = 0; i < unique.Count; i++)
                {
                    if (Math.Abs(unique[i] - v) <= minSep) { exists = true; break; }
                }
                if (!exists) unique.Add(v);
            }
            return unique;
        }

    }
}
