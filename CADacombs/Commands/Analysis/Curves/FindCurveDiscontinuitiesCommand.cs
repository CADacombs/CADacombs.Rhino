using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Analysis.Curves
{
    public class FindCurveDiscontinuitiesCommand : Command
    {
        public FindCurveDiscontinuitiesCommand() { Instance = this; }
        public static FindCurveDiscontinuitiesCommand Instance { get; private set; }
        public override string EnglishName => "spb_CrvDiscontinuities";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curves to find discontinuities");
            go.GeometryFilter = ObjectType.Curve;
            go.GetMultiple(1, 0);

            if (go.CommandResult() != Result.Success) return go.CommandResult();

            double angleTolG1 = doc.ModelAngleToleranceRadians;
            double angleTolG2 = RhinoMath.ToRadians(2.0);
            double crvDeltaPercent = 0.05; 

            List<Curve> newSplitCurves = new List<Curve>();
            int totalDiscontinuities = 0;

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                NurbsCurve nc = crv.ToNurbsCurve();
                List<double> splitParams = new List<double>();

                // Simplified evaluation loop across interior knots
                for (int i = nc.Degree; i < nc.Knots.Count - nc.Degree; i++)
                {
                    double t = nc.Knots[i];
                    var vecsB = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Below);
                    var vecsA = ContinuityUtils.GetContinuityVectorsAt(nc, t, CurveEvaluationSide.Above);

                    // Check G1
                    if (Vector3d.VectorAngle(vecsB.Tangent, vecsA.Tangent) > angleTolG1)
                    {
                        splitParams.Add(t);
                        continue;
                    }

                    // Check G2 (if not linear)
                    if (!vecsB.Curvature.IsTiny() && !vecsA.Curvature.IsTiny())
                    {
                        if (Vector3d.VectorAngle(vecsB.Curvature, vecsA.Curvature) > angleTolG2)
                        {
                            splitParams.Add(t);
                            continue;
                        }

                        double kBelow = vecsB.Curvature.Length;
                        double kAbove = vecsA.Curvature.Length;
                        if (Math.Abs(kBelow - kAbove) / Math.Max(kBelow, kAbove) > crvDeltaPercent)
                        {
                            splitParams.Add(t);
                        }
                    }
                }

                totalDiscontinuities += splitParams.Count;

                foreach(double t in splitParams) doc.Objects.AddPoint(nc.PointAt(t));

                if (splitParams.Count > 0)
                {
                    Curve[] split = nc.Split(splitParams);
                    if (split != null) newSplitCurves.AddRange(split);
                }
            }

            RhinoApp.WriteLine($"Found {totalDiscontinuities} discontinuities.");
            doc.Views.Redraw();
            return Result.Success;
        }
    }
}