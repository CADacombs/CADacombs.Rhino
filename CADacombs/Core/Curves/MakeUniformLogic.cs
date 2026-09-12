using System;
using Rhino;
using Rhino.Geometry;

namespace CADacombs.Core.Curves
{
    public static class MakeUniformLogic
    {
        public static (NurbsCurve UniformCurve, double Deviation, string Log) TryMakeUniform(
            Curve crvIn, 
            bool limitDev, 
            double devTol, 
            bool preserveEndTangents,
            bool preserveEndCurvatures)
        {
            if (crvIn == null) return (null, 0.0, "Input is null.");

            var ncIn = crvIn.ToNurbsCurve();
            if (ncIn == null) return (null, 0.0, "Failed to convert input to NurbsCurve.");

            // 1. Check if already uniform
            if (UniformityChecker.IsUniform(ncIn))
            {
                return (null, 0.0, "Curve is already uniform.");
            }

            NurbsCurve ncOut = ncIn.DuplicateCurve() as NurbsCurve;
            if (ncOut == null) return (null, 0.0, "Failed to duplicate curve.");

            // 2. Apply uniform knot spacing based on periodicity
            if (ncIn.IsPeriodic)
            {
                ncOut.Knots.CreatePeriodicKnots(1.0);
            }
            else
            {
                ncOut.Knots.CreateUniformKnots(1.0);
            }

            // 3. Preserve end conditions
            if (preserveEndTangents || preserveEndCurvatures)
            {
                var conditionType = preserveEndCurvatures 
                    ? NurbsCurve.NurbsCurveEndConditionType.Curvature 
                    : NurbsCurve.NurbsCurveEndConditionType.Tangency;

                if (!MatchEndConditions(ncOut, ncIn, conditionType))
                {
                    ncOut.Dispose();
                    return (null, 0.0, $"Failed to preserve end conditions ({conditionType}).");
                }
            }

            // 4. Deviation Check
            double maxDev = 0.0;
            if (Curve.GetDistancesBetweenCurves(ncIn, ncOut, 0.1 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, out maxDev, out _, out _, out _, out _, out _))
            {
                if (limitDev && maxDev > devTol)
                {
                    ncOut.Dispose();
                    return (null, maxDev, $"Result exceeded allowed deviation (Dev: {maxDev:E3}).");
                }
            }
            else
            {
                ncOut.Dispose();
                return (null, 0.0, "Failed to compute distance between curves.");
            }

            return (ncOut, maxDev, $"Successfully made uniform (Dev: {maxDev:E3}).");
        }

        private static bool MatchEndConditions(NurbsCurve toMod, NurbsCurve refCrv, NurbsCurve.NurbsCurveEndConditionType type)
        {
            // Using dummy vectors for curvature if only requesting tangency.
            // Rhino internally ignores the curvature parameter if type == Tangency.
            Vector3d startCurvature = type == NurbsCurve.NurbsCurveEndConditionType.Curvature ? refCrv.CurvatureAt(refCrv.Domain.Min) : Vector3d.Zero;
            Vector3d endCurvature = type == NurbsCurve.NurbsCurveEndConditionType.Curvature ? refCrv.CurvatureAt(refCrv.Domain.Max) : Vector3d.Zero;

            bool startSuccess = toMod.SetEndCondition(
                false,
                type,
                toMod.PointAtStart,
                refCrv.TangentAtStart,
                startCurvature
            );

            bool endSuccess = toMod.SetEndCondition(
                true,
                type,
                toMod.PointAtEnd,
                refCrv.TangentAtEnd,
                endCurvature
            );

            return startSuccess && endSuccess;
        }
    }
}