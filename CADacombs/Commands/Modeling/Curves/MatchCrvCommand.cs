using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling.Curves
{
    [System.Runtime.InteropServices.Guid("1a2b3c4d-5e6f-7a8b-9c0d-e1f2a3b4c5d6")] 
    public class MatchCrvCommand : Command
    {
        public override string EnglishName => "ccMatchCrv";

        private readonly string[] _continuityNames = { "Position", "Tangency", "Curvature" };
        private readonly string[] _preserveNames = { "None", "Position", "Tangency", "Curvature" };
        private readonly string[] _modifyNames = { "First", "Average", "MinimizeDev" };
        private readonly string[] _incrNames = { "Deg", "SpanCt", "None" };
        private readonly string[] _maxIntNames = { "G2", "G3" };

        public MatchCrvCommand()
        {
            Instance = this;
        }

        public static MatchCrvCommand Instance { get; private set; }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            MatchCrvOptions.Load(this.Settings);
            
            ObjRef[] objRefs = null;

            if (mode == RunMode.Interactive)
            {
                var go = new GetObject();
                go.SetCommandPrompt("Select 2 curves near their ends to match");
                go.GeometryFilter = ObjectType.Curve;
                go.OneByOnePostSelect = true;
                go.DisablePreSelect();

                if (go.GetMultiple(2, 2) != GetResult.Object) return Result.Cancel;
                objRefs = go.Objects();

                Curve cA_int = objRefs[0].Curve();
                Curve cB_int = objRefs[1].Curve();
                if (cA_int == null || cB_int == null) return Result.Failure;

                bool bT1_A = IsSelectionCloserToT1(cA_int, objRefs[0].SelectionPoint());
                bool bT1_B = IsSelectionCloserToT1(cB_int, objRefs[1].SelectionPoint());

                RhinoApp.SetCommandPrompt("Continue in dialog...");
                var dialog = new MatchCrvDialog(this.Settings, cA_int, bT1_A, cB_int, bT1_B);
                var dialogResult = dialog.ShowSemiModal(doc, RhinoEtoApp.MainWindow);
                RhinoApp.SetCommandPrompt("Command complete.");
                
                if (dialogResult != true) return Result.Cancel;

                if (dialog.ResultCurveA == null && dialog.ResultCurveB == null)
                {
                    RhinoApp.WriteLine(dialog.LastErrorMessage ?? "Result cannot be created with the current constraints.");
                    return Result.Failure;
                }

                if (dialog.ResultCurveA != null) ProcessResult(doc, objRefs[0], cA_int, dialog.ResultCurveA);
                if (dialog.ResultCurveB != null) ProcessResult(doc, objRefs[1], cB_int, dialog.ResultCurveB);

                doc.Views.Redraw();
                return Result.Success;
            }
            
            var goCli = new GetObject();
            goCli.SetCommandPrompt("Select 2 curves near their ends to match");
            goCli.GeometryFilter = ObjectType.Curve;
            goCli.OneByOnePostSelect = true;
            goCli.DisablePreSelect();
            goCli.AcceptNumber(true, true);

            while (true)
            {
                goCli.ClearCommandOptions();

                var optPosTol = new OptionDouble(MatchCrvOptions.PosTol);
                var optAngleTol = new OptionDouble(MatchCrvOptions.AngleTolDeg);
                var optCurvTol = new OptionDouble(MatchCrvOptions.CurvatureMagTolPct);
                var optSkip = new OptionToggle(MatchCrvOptions.SkipIfAlreadyAtContinuity, "Process", "Skip");
                
                var optLimitDev = new OptionToggle(MatchCrvOptions.LimitCrvDev, "No", "Yes");
                var optDevTol = new OptionDouble(MatchCrvOptions.DevTol);
                
                var optDeformLines = new OptionToggle(MatchCrvOptions.DeformLines, "No", "Yes");
                var optDeformArcs = new OptionToggle(MatchCrvOptions.DeformArcs, "No", "Yes");
                var optPrevInflections = new OptionToggle(MatchCrvOptions.PreventExtraInflections, "Allow", "Prevent");
                var optPreserveIntCont = new OptionToggle(MatchCrvOptions.PreserveInternalContinuity, "AllowDegrade", "Preserve");
                
                var optDelete = new OptionToggle(MatchCrvOptions.DeleteInput, "Keep", "Delete");
                var optDebug = new OptionToggle(MatchCrvOptions.Debug, "No", "Yes");

                int idxContinuity = goCli.AddOptionList("Continuity", _continuityNames, MatchCrvOptions.Continuity);
                int idxPreserve = goCli.AddOptionList("PreserveOtherEnd", _preserveNames, MatchCrvOptions.PreserveOtherEnd);
                int idxModify = goCli.AddOptionList("ModifyCurve", _modifyNames, MatchCrvOptions.ModifyMode);
                int idxIncr = goCli.AddOptionList("IncrAsNeeded", _incrNames, MatchCrvOptions.IncrAsNeeded);
                
                goCli.AddOptionToggle("CrvDevLimit", ref optLimitDev);
                if (MatchCrvOptions.LimitCrvDev) goCli.AddOptionDouble("DevTol", ref optDevTol);
                
                goCli.AddOptionToggle("IfAlreadyAtContinuity", ref optSkip);
                if (MatchCrvOptions.SkipIfAlreadyAtContinuity)
                {
                    goCli.AddOptionDouble("PosTol", ref optPosTol);
                    if (MatchCrvOptions.Continuity >= 1) goCli.AddOptionDouble("AngleTol", ref optAngleTol);
                    if (MatchCrvOptions.Continuity == 2) goCli.AddOptionDouble("CurvMagTol", ref optCurvTol);
                }

                goCli.AddOptionToggle("DeformLines", ref optDeformLines);
                goCli.AddOptionToggle("DeformArcs", ref optDeformArcs);
                goCli.AddOptionToggle("ExtraInflections", ref optPrevInflections);
                goCli.AddOptionToggle("InternalCont", ref optPreserveIntCont);
                
                int idxMaxInt = -1;
                if (MatchCrvOptions.PreserveInternalContinuity)
                {
                    idxMaxInt = goCli.AddOptionList("MaxInternalCont", _maxIntNames, MatchCrvOptions.MaxInternalContinuity == 3 ? 1 : 0);
                }
                
                goCli.AddOptionToggle("Action", ref optDelete);
                goCli.AddOptionToggle("Debug", ref optDebug);

                GetResult res = goCli.GetMultiple(2, 2);

                if (res == GetResult.Number)
                {
                    MatchCrvOptions.DevTol = goCli.Number();
                    MatchCrvOptions.Save(this.Settings);
                    continue;
                }
                else if (res == GetResult.Option)
                {
                    var option = goCli.Option();
                    if (option.Index == idxContinuity) MatchCrvOptions.Continuity = option.CurrentListOptionIndex;
                    else if (option.Index == idxPreserve) MatchCrvOptions.PreserveOtherEnd = option.CurrentListOptionIndex;
                    else if (option.Index == idxModify) MatchCrvOptions.ModifyMode = option.CurrentListOptionIndex;
                    else if (option.Index == idxIncr) MatchCrvOptions.IncrAsNeeded = option.CurrentListOptionIndex;
                    else if (idxMaxInt != -1 && option.Index == idxMaxInt) MatchCrvOptions.MaxInternalContinuity = option.CurrentListOptionIndex == 1 ? 3 : 2;

                    MatchCrvOptions.LimitCrvDev = optLimitDev.CurrentValue;
                    MatchCrvOptions.DevTol = optDevTol.CurrentValue;
                    if (MatchCrvOptions.DevTol <= 0) MatchCrvOptions.DevTol = RhinoMath.ZeroTolerance;

                    MatchCrvOptions.SkipIfAlreadyAtContinuity = optSkip.CurrentValue;
                    MatchCrvOptions.PosTol = optPosTol.CurrentValue;
                    if (MatchCrvOptions.PosTol <= 0) MatchCrvOptions.PosTol = RhinoMath.ZeroTolerance;
                    
                    MatchCrvOptions.AngleTolDeg = optAngleTol.CurrentValue;
                    if (MatchCrvOptions.AngleTolDeg <= 0) MatchCrvOptions.AngleTolDeg = RhinoMath.ToDegrees(RhinoMath.ZeroTolerance);
                    
                    MatchCrvOptions.CurvatureMagTolPct = optCurvTol.CurrentValue;

                    MatchCrvOptions.DeformLines = optDeformLines.CurrentValue;
                    MatchCrvOptions.DeformArcs = optDeformArcs.CurrentValue;
                    MatchCrvOptions.PreventExtraInflections = optPrevInflections.CurrentValue;
                    MatchCrvOptions.PreserveInternalContinuity = optPreserveIntCont.CurrentValue;

                    MatchCrvOptions.DeleteInput = optDelete.CurrentValue;
                    MatchCrvOptions.Debug = optDebug.CurrentValue;

                    MatchCrvOptions.Save(this.Settings);
                    continue;
                }
                else if (res == GetResult.Object)
                {
                    break;
                }
                return Result.Cancel;
            }
            
            objRefs = goCli.Objects();
            if (objRefs == null || objRefs.Length != 2) return Result.Cancel;

            Curve cA = objRefs[0].Curve();
            Curve cB = objRefs[1].Curve();
            if (cA == null || cB == null) return Result.Failure;

            bool bT1WorkEndA = IsSelectionCloserToT1(cA, objRefs[0].SelectionPoint());
            bool bT1WorkEndB = IsSelectionCloserToT1(cB, objRefs[1].SelectionPoint());

            if (MatchCrvOptions.SkipIfAlreadyAtContinuity)
            {
                if (AreCurvesAtContinuity(cA, bT1WorkEndA, cB, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct))
                {
                    if (MatchCrvOptions.Echo) RhinoApp.WriteLine($"Curves already meet at G{MatchCrvOptions.Continuity} continuity. Skipping.");
                    return Result.Success;
                }
            }

            if (!MatchCrvOptions.Debug) doc.Views.RedrawEnabled = false;
            double currentDevTol = MatchCrvOptions.LimitCrvDev ? MatchCrvOptions.DevTol : double.MaxValue;

            NurbsCurve resultA = null;
            NurbsCurve resultB = null;
            string lastErrorMsg = null;

            if (MatchCrvOptions.ModifyMode == 0) // First Curve
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying First Curve Only...");
                var res = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, MatchCrvOptions.DeformArcs, MatchCrvOptions.IncrAsNeeded, currentDevTol, MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                resultA = res.Result;
                lastErrorMsg = res.ErrorMsg;
            }
            else if (MatchCrvOptions.ModifyMode == 1) // Average
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying Both Curves (Average)...");
                var bothResult = CurveMatchMath.MatchBothCurves(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, MatchCrvOptions.DeformArcs, MatchCrvOptions.IncrAsNeeded, currentDevTol, MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                resultA = bothResult.ResultA;
                resultB = bothResult.ResultB;
                lastErrorMsg = bothResult.ErrorMsg;
            }
            else if (MatchCrvOptions.ModifyMode == 2) // Minimize Deviation
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Evaluating First, Second, and Both for least deviation...");
                
                var resFirst = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, MatchCrvOptions.DeformArcs, MatchCrvOptions.IncrAsNeeded, currentDevTol, MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                var resSecond = CurveMatchMath.MatchOneCurve(cB, cA, bT1WorkEndB, bT1WorkEndA, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, MatchCrvOptions.DeformArcs, MatchCrvOptions.IncrAsNeeded, currentDevTol, MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                var bothResult = CurveMatchMath.MatchBothCurves(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, MatchCrvOptions.DeformArcs, MatchCrvOptions.IncrAsNeeded, currentDevTol, MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);

                NurbsCurve candA = resFirst.Result;
                NurbsCurve candB = resSecond.Result;

                double devA = candA != null ? CurveMatchMath.GetAverageDeviation(cA, candA) : double.MaxValue;
                double devB = candB != null ? CurveMatchMath.GetAverageDeviation(cB, candB) : double.MaxValue;
                double devBoth = (bothResult.ResultA != null && bothResult.ResultB != null) 
                    ? (CurveMatchMath.GetAverageDeviation(cA, bothResult.ResultA) + CurveMatchMath.GetAverageDeviation(cB, bothResult.ResultB)) / 2.0 
                    : double.MaxValue;

                double minDev = Math.Min(devA, Math.Min(devB, devBoth));

                if (minDev == double.MaxValue) 
                { 
                    resultA = null; 
                    resultB = null; 
                    lastErrorMsg = bothResult.ErrorMsg ?? resFirst.ErrorMsg ?? resSecond.ErrorMsg ?? "Result cannot be created with the current constraints.";
                }
                else if (minDev == devA)
                {
                    resultA = candA;
                    candB?.Dispose(); bothResult.ResultA?.Dispose(); bothResult.ResultB?.Dispose();
                }
                else if (minDev == devB)
                {
                    resultB = candB;
                    candA?.Dispose(); bothResult.ResultA?.Dispose(); bothResult.ResultB?.Dispose();
                }
                else
                {
                    resultA = bothResult.ResultA; resultB = bothResult.ResultB;
                    candA?.Dispose(); candB?.Dispose();
                }
            }

            if (resultA == null && resultB == null)
            {
                RhinoApp.WriteLine(lastErrorMsg ?? "Result cannot be created with the current constraints.");
                doc.Views.RedrawEnabled = true;
                return Result.Failure;
            }

            if (resultA != null) ProcessResult(doc, objRefs[0], cA, resultA);
            if (resultB != null) ProcessResult(doc, objRefs[1], cB, resultB);

            doc.Views.RedrawEnabled = true;
            return Result.Success;
        }

        private void ProcessResult(RhinoDoc doc, ObjRef objRef, Curve originalCrv, NurbsCurve newCrv)
        {
            if (MatchCrvOptions.DeleteInput)
            {
                if (doc.Objects.Replace(objRef.ObjectId, newCrv))
                {
                    if (MatchCrvOptions.Echo && !MatchCrvOptions.Debug) RhinoApp.WriteLine("Curve was replaced.");
                    doc.Objects.Select(objRef.ObjectId);
                }
            }
            else
            {
                Guid g1 = doc.Objects.AddCurve(newCrv);
                if (g1 != Guid.Empty)
                {
                    if (MatchCrvOptions.Echo && !MatchCrvOptions.Debug) RhinoApp.WriteLine("Curve was added.");
                    doc.Objects.Select(g1);
                }
            }

            if (MatchCrvOptions.Echo || MatchCrvOptions.Debug)
            {
                int prec = doc.ModelDistanceDisplayPrecision;
                
                double? radPre = RadiusMinimaMath.GetMinimumRadius(originalCrv);
                double? radPost = RadiusMinimaMath.GetMinimumRadius(newCrv);
                double maxDev = CurveMatchMath.GetMaximumDeviation(originalCrv, newCrv);
                double avgDev = CurveMatchMath.GetAverageDeviation(originalCrv, newCrv);

                string sRadPre = radPre.HasValue ? FormatUtils.FormatDistance(radPre.Value, prec) : "(None)";
                string sRadPost = radPost.HasValue ? FormatUtils.FormatDistance(radPost.Value, prec) : "(None)";

                RhinoApp.WriteLine($"Min Radius: {sRadPre} -> {sRadPost}  |  Max Dev: {FormatUtils.FormatDistance(maxDev, prec)}  |  Avg Dev: {FormatUtils.FormatDistance(avgDev, prec)}");
            }
        }

        private bool IsSelectionCloserToT1(Curve c, Point3d pt)
        {
            c.ClosestPoint(pt, out double t);
            return t > c.Domain.Mid;
        }

        private bool AreCurvesAtContinuity(Curve cA, bool bT1WorkEndA, Curve cB, bool bT1WorkEndB, int targetContinuity, double posTol, double angleTolDeg, double curvatureMagTolPct)
        {
            double tA = bT1WorkEndA ? cA.Domain.T1 : cA.Domain.T0;
            double tB = bT1WorkEndB ? cB.Domain.T1 : cB.Domain.T0;
            
            CurveEvaluationSide sideA = bT1WorkEndA ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;
            CurveEvaluationSide sideB = bT1WorkEndB ? CurveEvaluationSide.Below : CurveEvaluationSide.Above;

            var vA = ContinuityUtils.GetContinuityVectorsAt(cA, tA, sideA);
            var vB = ContinuityUtils.GetContinuityVectorsAt(cB, tB, sideB);

            if (bT1WorkEndA == bT1WorkEndB) 
            {
                vB.Tangent = -vB.Tangent;
            }

            int? level = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, angleTolDeg, curvatureMagTolPct);
            
            return level.HasValue && level.Value >= targetContinuity;
        }
    }
}