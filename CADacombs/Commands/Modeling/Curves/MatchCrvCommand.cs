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
        private readonly string[] _preserveNames = { "None", "Position", "Tangency", "Curvature", "Periodic" };
        private readonly string[] _modifyNames = { "First", "Average", "MinimizeDev" };

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

                var dialog = new MatchCrvDialog(this.Settings);
                var dialogResult = dialog.ShowModal(RhinoEtoApp.MainWindow);
                
                if (dialogResult != true) return Result.Cancel;
            }
            else
            {
                var go = new GetObject();
                go.SetCommandPrompt("Select 2 curves near their ends to match");
                go.GeometryFilter = ObjectType.Curve;
                go.OneByOnePostSelect = true;
                go.DisablePreSelect();
                go.AcceptNumber(true, true);

                while (true)
                {
                    go.ClearCommandOptions();

                    var optPosTol = new OptionDouble(MatchCrvOptions.PosTol);
                    var optAngleTol = new OptionDouble(MatchCrvOptions.AngleTolDeg);
                    var optCurvTol = new OptionDouble(MatchCrvOptions.CurvatureMagTolPct);
                    var optSkip = new OptionToggle(MatchCrvOptions.SkipIfAlreadyAtContinuity, "Process", "Skip");
                    
                    var optLimitDev = new OptionToggle(MatchCrvOptions.LimitCrvDev, "No", "Yes");
                    var optDevTol = new OptionDouble(MatchCrvOptions.DevTol);
                    
                    var optDeformLines = new OptionToggle(MatchCrvOptions.DeformLines, "No", "Yes");
                    var optRebuild = new OptionToggle(MatchCrvOptions.RebuildRationals, "No", "Yes");
                    var optKeepArcs = new OptionToggle(MatchCrvOptions.KeepArcs, "No", "Yes");
                    
                    var optDelete = new OptionToggle(MatchCrvOptions.DeleteInput, "Keep", "Delete");
                    var optEcho = new OptionToggle(MatchCrvOptions.Echo, "No", "Yes");
                    var optDebug = new OptionToggle(MatchCrvOptions.Debug, "No", "Yes");

                    int idxContinuity = go.AddOptionList("Continuity", _continuityNames, MatchCrvOptions.Continuity);
                    int idxPreserve = go.AddOptionList("PreserveOtherEnd", _preserveNames, MatchCrvOptions.PreserveOtherEnd);
                    int idxModify = go.AddOptionList("ModifyCurve", _modifyNames, MatchCrvOptions.ModifyMode);
                    
                    go.AddOptionToggle("LimitCrvDev", ref optLimitDev);
                    if (MatchCrvOptions.LimitCrvDev) go.AddOptionDouble("DevTol", ref optDevTol);
                    
                    go.AddOptionToggle("IfAlreadyAtContinuity", ref optSkip);
                    if (MatchCrvOptions.SkipIfAlreadyAtContinuity)
                    {
                        go.AddOptionDouble("PosTol", ref optPosTol);
                        if (MatchCrvOptions.Continuity >= 1) go.AddOptionDouble("AngleTol", ref optAngleTol);
                        if (MatchCrvOptions.Continuity == 2) go.AddOptionDouble("CurvMagTol", ref optCurvTol);
                    }

                    go.AddOptionToggle("DeformLines", ref optDeformLines);
                    go.AddOptionToggle("RebuildRationals", ref optRebuild);
                    if (MatchCrvOptions.RebuildRationals) go.AddOptionToggle("KeepArcs", ref optKeepArcs);
                    
                    go.AddOptionToggle("Action", ref optDelete);
                    go.AddOptionToggle("Echo", ref optEcho);
                    go.AddOptionToggle("Debug", ref optDebug);

                    GetResult res = go.GetMultiple(2, 2);

                    if (res == GetResult.Number)
                    {
                        MatchCrvOptions.DevTol = go.Number();
                        MatchCrvOptions.Save(this.Settings);
                        continue;
                    }
                    else if (res == GetResult.Option)
                    {
                        var option = go.Option();
                        if (option.Index == idxContinuity) MatchCrvOptions.Continuity = option.CurrentListOptionIndex;
                        else if (option.Index == idxPreserve) MatchCrvOptions.PreserveOtherEnd = option.CurrentListOptionIndex;
                        else if (option.Index == idxModify) MatchCrvOptions.ModifyMode = option.CurrentListOptionIndex;

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
                        MatchCrvOptions.RebuildRationals = optRebuild.CurrentValue;
                        MatchCrvOptions.KeepArcs = optKeepArcs.CurrentValue;

                        MatchCrvOptions.DeleteInput = optDelete.CurrentValue;
                        MatchCrvOptions.Echo = optEcho.CurrentValue;
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
                objRefs = go.Objects();
            }

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

            if (MatchCrvOptions.ModifyMode == 0) // First Curve
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying First Curve Only...");
                resultA = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, currentDevTol, MatchCrvOptions.Debug);
            }
            else if (MatchCrvOptions.ModifyMode == 1) // Average
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying Both Curves (Average)...");
                var bothResult = CurveMatchMath.MatchBothCurves(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, currentDevTol, MatchCrvOptions.Debug);
                resultA = bothResult.ResultA;
                resultB = bothResult.ResultB;
            }
            else if (MatchCrvOptions.ModifyMode == 2) // Minimize Deviation
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Evaluating First, Second, and Both for least deviation...");
                
                NurbsCurve candA = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, currentDevTol, MatchCrvOptions.Debug);
                NurbsCurve candB = CurveMatchMath.MatchOneCurve(cB, cA, bT1WorkEndB, bT1WorkEndA, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, currentDevTol, MatchCrvOptions.Debug);
                var bothResult = CurveMatchMath.MatchBothCurves(cA, cB, bT1WorkEndA, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd, MatchCrvOptions.DeformLines, currentDevTol, MatchCrvOptions.Debug);

                double devA = candA != null ? CurveMatchMath.GetAverageDeviation(cA, candA) : double.MaxValue;
                double devB = candB != null ? CurveMatchMath.GetAverageDeviation(cB, candB) : double.MaxValue;
                double devBoth = (bothResult.ResultA != null && bothResult.ResultB != null) 
                    ? CurveMatchMath.GetAverageDeviation(cA, bothResult.ResultA) + CurveMatchMath.GetAverageDeviation(cB, bothResult.ResultB) 
                    : double.MaxValue;

                if (MatchCrvOptions.Debug)
                {
                    RhinoApp.WriteLine($" Final Shootout A-Only: AvgDev = {(candA != null ? devA.ToString("F6") : "Failed Tol")}");
                    RhinoApp.WriteLine($" Final Shootout B-Only: AvgDev = {(candB != null ? devB.ToString("F6") : "Failed Tol")}");
                    RhinoApp.WriteLine($" Final Shootout Both  : AvgDev Sum = {(bothResult.ResultA != null ? devBoth.ToString("F6") : "Failed Tol")}");
                }

                double minDev = Math.Min(devA, Math.Min(devB, devBoth));

                if (minDev == double.MaxValue) { resultA = null; resultB = null; }
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
                RhinoApp.WriteLine("Curve(s) within tolerance could not be created, so they were not modified.");
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
                double? radPre = RadiusMinimaMath.GetMinimumRadius(originalCrv);
                double? radPost = RadiusMinimaMath.GetMinimumRadius(newCrv);
                double dev = CurveMatchMath.GetMaximumDeviation(originalCrv, newCrv);

                string sRadPre = radPre.HasValue ? FormatUtils.FormatDistance(radPre.Value, doc.ModelDistanceDisplayPrecision) : "(None)";
                string sRadPost = radPost.HasValue ? FormatUtils.FormatDistance(radPost.Value, doc.ModelDistanceDisplayPrecision) : "(None)";

                RhinoApp.WriteLine($"MinimumRadius: {sRadPre} -> {sRadPost}  Deviation: {FormatUtils.FormatDistance(dev, doc.ModelDistanceDisplayPrecision)}");
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

            int? level = ContinuityUtils.GetContinuityLevel(vB, vA, posTol, angleTolDeg, 2.0, curvatureMagTolPct);
            
            return level.HasValue && level.Value >= targetContinuity;
        }
    }
}