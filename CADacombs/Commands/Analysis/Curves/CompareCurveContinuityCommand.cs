using System;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Analysis.Curves
{
    public class CompareCurveContinuityCommand : Command
    {
        public CompareCurveContinuityCommand() { Instance = this; }
        public static CompareCurveContinuityCommand Instance { get; private set; }
        public override string EnglishName => "spb_GCon";

        // Sticky Options
        private double _distTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
        private double _angleTolDeg = RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
        private bool _alignDirs = true;
        private bool _addDot = false;
        private bool _echo = true;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select first curve near end");
            go.GeometryFilter = ObjectType.Curve;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();
            
            ObjRef refA = go.Object(0);
            
            go.SetCommandPrompt("Select second curve near end");
            go.DisablePreSelect();

            OptionDouble optValDist = new OptionDouble(_distTol);
            OptionDouble optValAngle = new OptionDouble(_angleTolDeg);
            OptionToggle optValAlign = new OptionToggle(_alignDirs, "No", "Yes");
            OptionToggle optValDot = new OptionToggle(_addDot, "No", "Yes");
            OptionToggle optValEcho = new OptionToggle(_echo, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                int optDoc = go.AddOption("DocTols");
                int optTight = go.AddOption("TightTols");
                
                // Passed cleanly by reference
                int optDist = go.AddOptionDouble("DistTol", ref optValDist);
                int optAngle = go.AddOptionDouble("VectAngleTol", ref optValAngle);
                int optAlign = go.AddOptionToggle("AlignCrvDirs", ref optValAlign);
                int optDot = go.AddOptionToggle("AddDot", ref optValDot);
                int optEcho = go.AddOptionToggle("Echo", ref optValEcho);

                var res = go.Get();

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optDoc)
                    {
                        optValDist.CurrentValue = doc.ModelAbsoluteTolerance;
                        optValAngle.CurrentValue = doc.ModelAngleToleranceDegrees;
                    }
                    else if (opt.Index == optTight)
                    {
                        optValDist.CurrentValue = 1e-12;
                        optValAngle.CurrentValue = 1e-6;
                    }

                    _distTol = optValDist.CurrentValue;
                    _angleTolDeg = optValAngle.CurrentValue;
                    _alignDirs = optValAlign.CurrentValue;
                    _addDot = optValDot.CurrentValue;
                    _echo = optValEcho.CurrentValue;
                }
            }

            ObjRef refB = go.Object(0);
            Curve crvA = refA.Curve();
            Curve crvB = refB.Curve();

            crvA.ClosestPoint(refA.SelectionPoint(), out double tA);
            crvB.ClosestPoint(refB.SelectionPoint(), out double tB);
            
            bool evalEndA = tA >= crvA.Domain.Mid;
            bool evalEndB = tB >= crvB.Domain.Mid;

            if (_alignDirs && evalEndA == evalEndB)
            {
                crvB = crvB.DuplicateCurve();
                crvB.Reverse();
                evalEndB = !evalEndB;
            }

            NurbsCurve ncA = crvA.ToNurbsCurve();
            NurbsCurve ncB = crvB.ToNurbsCurve();

            double evalTA = evalEndA ? ncA.Domain.T1 : ncA.Domain.T0;
            var sideA = evalEndA ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            double evalTB = evalEndB ? ncB.Domain.T1 : ncB.Domain.T0;
            var sideB = evalEndB ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            var vecsA = ContinuityUtils.GetContinuityVectorsAt(ncA, evalTA, sideA);
            var vecsB = ContinuityUtils.GetContinuityVectorsAt(ncB, evalTB, sideB);

            int cCont = ContinuityUtils.GetParametricContinuity(ncA, evalTA, sideA, ncB, evalTB, sideB);

            if (_echo) PrintReport(vecsA, vecsB, cCont, doc);

            if (_addDot)
            {
                string cStr = cCont == int.MaxValue ? "inf" : cCont.ToString();
                doc.Objects.AddTextDot($"C{cStr}", vecsA.Pt);
            }

            return Result.Success;
        }

        private void PrintReport(
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vecsA, 
            (Point3d Pt, Vector3d Tangent, Vector3d Curvature, Vector3d Torsion) vecsB,
            int cCont, RhinoDoc doc)
        {
            StringBuilder sb = new StringBuilder();
            double zeroTol = 1e-9;
            string units = doc.ModelUnitSystem.ToString().ToLower();
            int prec = doc.ModelDistanceDisplayPrecision;

            sb.AppendLine("Torsion difference:");
            if (vecsA.Torsion.Length <= zeroTol && vecsB.Torsion.Length <= zeroTol)
                sb.AppendLine("(Both curves have no change in curvature at evaluated ends.)");
            else
            {
                double angleTors = RhinoMath.ToDegrees(Vector3d.VectorAngle(vecsA.Torsion, vecsB.Torsion));
                sb.AppendLine($"{Math.Round(angleTors, prec)} degrees, {Math.Round(Math.Abs(vecsA.Torsion.Length - vecsB.Torsion.Length), prec)} magnitude");
            }

            sb.AppendLine("\nCurvature difference:");
            if (vecsA.Curvature.Length <= zeroTol && vecsB.Curvature.Length <= zeroTol)
                sb.AppendLine("(Both curves are linear at evaluated ends.)");
            else
            {
                double angleCrv = RhinoMath.ToDegrees(Vector3d.VectorAngle(vecsA.Curvature, vecsB.Curvature));
                double rA = 1.0 / vecsA.Curvature.Length;
                double rB = 1.0 / vecsB.Curvature.Length;
                sb.AppendLine($"{Math.Round(angleCrv, prec)} degrees, R{Math.Round(Math.Abs(rA - rB), prec)} {units}");
            }

            double angleTan = RhinoMath.ToDegrees(Vector3d.VectorAngle(vecsA.Tangent, vecsB.Tangent));
            sb.AppendLine($"\nTangent difference: {Math.Round(angleTan, prec)} degrees");

            double dist = vecsA.Pt.DistanceTo(vecsB.Pt);
            sb.AppendLine($"\nCurve end difference: {Math.Round(dist, prec)} {units}");

            string cStr = cCont == int.MaxValue ? "(inf)" : cCont.ToString();
            sb.AppendLine($"\nParametric Continuity: C{cStr}");

            RhinoApp.WriteLine(sb.ToString());
        }
    }
}