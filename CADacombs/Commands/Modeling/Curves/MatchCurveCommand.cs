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
        private readonly string[] _lineModeNames = { "Lock", "KeepAsLine", "ConvertToNURBS" };
        private readonly string[] _arcModeNames = { "Lock", "KeepAsArc", "ConvertToNURBS" };
        private readonly string[] _nurbsModeNames = { "Lock", "ExtendTrim", "Deform" };
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
                go.SetCommandPrompt("Select 2 open curves near their ends to match");
                go.GeometryFilter = ObjectType.Curve;
                go.GeometryAttributeFilter = GeometryAttributeFilter.OpenCurve;
                go.OneByOnePostSelect = true;
                go.DisablePreSelect();

                if (go.GetMultiple(2, 2) != GetResult.Object) return Result.Cancel;
                objRefs = go.Objects();

                Curve cA_int = objRefs[0].Curve();
                Curve cB_int = objRefs[1].Curve();
                if (cA_int == null || cB_int == null) return Result.Failure;
                if (cA_int.IsClosed || cB_int.IsClosed)
                {
                    RhinoApp.WriteLine("Closed curves are not supported.");
                    return Result.Cancel;
                }

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

                if (dialog.ResultCurveA != null) ProcessResult(doc, objRefs[0], cA_int, dialog.ResultCurveA, bT1_A);
                if (dialog.ResultCurveB != null) ProcessResult(doc, objRefs[1], cB_int, dialog.ResultCurveB, bT1_B);

                doc.Views.Redraw();
                return Result.Success;
            }

            var goCli = new GetObject();
            goCli.SetCommandPrompt("Select 2 open curves near their ends to match");
            goCli.GeometryFilter = ObjectType.Curve;
            goCli.GeometryAttributeFilter = GeometryAttributeFilter.OpenCurve;
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

                var optPrevInflections = new OptionToggle(MatchCrvOptions.PreventExtraInflections, "Allow", "Reject");
                var optPreserveIntCont = new OptionToggle(MatchCrvOptions.PreserveInternalContinuity, "AllowDegrade", "Preserve");

                var optDelete = new OptionToggle(MatchCrvOptions.DeleteInput, "No", "Yes");
                var optDebug = new OptionToggle(MatchCrvOptions.Debug, "No", "Yes");

                int idxContinuity = goCli.AddOptionList("Continuity", _continuityNames, MatchCrvOptions.Continuity);
                int idxPreserve = goCli.AddOptionList("PreserveOtherEnd", _preserveNames, MatchCrvOptions.PreserveOtherEnd);
                int idxModify = goCli.AddOptionList("ModifyCurve", _modifyNames, MatchCrvOptions.ModifyMode);
                int idxLineMode = goCli.AddOptionList("Lines", _lineModeNames, MatchCrvOptions.LineMode);
                int idxArcMode = goCli.AddOptionList("Arcs", _arcModeNames, MatchCrvOptions.ArcMode);
                int idxNurbsMode = goCli.AddOptionList("NURBS", _nurbsModeNames, MatchCrvOptions.NurbsMode);
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

                goCli.AddOptionToggle("ExtraInflections", ref optPrevInflections);
                goCli.AddOptionToggle("InternalCont", ref optPreserveIntCont);

                int idxMaxInt = -1;
                if (MatchCrvOptions.PreserveInternalContinuity)
                {
                    idxMaxInt = goCli.AddOptionList("MaxInternalCont", _maxIntNames, MatchCrvOptions.MaxInternalContinuity == 3 ? 1 : 0);
                }

                goCli.AddOptionToggle("DeleteInput", ref optDelete);
                goCli.AddOptionToggle("Debug", ref optDebug);

                GetResult res = goCli.GetMultiple(2, 2);

                if (res == GetResult.Number)
                {
                    double num = goCli.Number();
                    MatchCrvOptions.DevTol = num > 0.0 ? num : MatchCrvOptions.GetDefaultDevTol();
                    MatchCrvOptions.Save(this.Settings);
                    continue;
                }
                else if (res == GetResult.Option)
                {
                    var option = goCli.Option();
                    if (option.Index == idxContinuity) MatchCrvOptions.Continuity = option.CurrentListOptionIndex;
                    else if (option.Index == idxPreserve) MatchCrvOptions.PreserveOtherEnd = option.CurrentListOptionIndex;
                    else if (option.Index == idxModify) MatchCrvOptions.ModifyMode = option.CurrentListOptionIndex;
                    else if (option.Index == idxLineMode) MatchCrvOptions.LineMode = option.CurrentListOptionIndex;
                    else if (option.Index == idxArcMode) MatchCrvOptions.ArcMode = option.CurrentListOptionIndex;
                    else if (option.Index == idxNurbsMode) MatchCrvOptions.NurbsMode = option.CurrentListOptionIndex;
                    else if (option.Index == idxIncr) MatchCrvOptions.IncrAsNeeded = option.CurrentListOptionIndex;
                    else if (idxMaxInt != -1 && option.Index == idxMaxInt) MatchCrvOptions.MaxInternalContinuity = option.CurrentListOptionIndex == 1 ? 3 : 2;

                    MatchCrvOptions.LimitCrvDev = optLimitDev.CurrentValue;
                    MatchCrvOptions.DevTol = optDevTol.CurrentValue > 0.0 ? optDevTol.CurrentValue : MatchCrvOptions.GetDefaultDevTol();

                    MatchCrvOptions.SkipIfAlreadyAtContinuity = optSkip.CurrentValue;
                    MatchCrvOptions.PosTol = optPosTol.CurrentValue > 0.0 ? optPosTol.CurrentValue : MatchCrvOptions.GetDefaultPosTol();
                    MatchCrvOptions.AngleTolDeg = optAngleTol.CurrentValue > 0.0 ? optAngleTol.CurrentValue : MatchCrvOptions.GetDefaultAngleTolDeg();
                    MatchCrvOptions.CurvatureMagTolPct = optCurvTol.CurrentValue > 0.0 ? optCurvTol.CurrentValue : MatchCrvOptions.GetDefaultCurvMagTolPct();

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
            if (cA.IsClosed || cB.IsClosed)
            {
                RhinoApp.WriteLine("Closed curves are not supported.");
                return Result.Cancel;
            }

            bool bT1WorkEndA = IsSelectionCloserToT1(cA, objRefs[0].SelectionPoint());
            bool bT1WorkEndB = IsSelectionCloserToT1(cB, objRefs[1].SelectionPoint());

            if (MatchCrvOptions.SkipIfAlreadyAtContinuity)
            {
                if (CurveMatchMath.AreCurvesAtContinuity(cA, bT1WorkEndA, cB, bT1WorkEndB, MatchCrvOptions.Continuity, MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct))
                {
                    if (MatchCrvOptions.Echo) RhinoApp.WriteLine($"Curves already meet at G{MatchCrvOptions.Continuity} continuity. Skipping.");
                    return Result.Success;
                }
            }

            if (!MatchCrvOptions.Debug) doc.Views.RedrawEnabled = false;
            double currentDevTol = MatchCrvOptions.LimitCrvDev ? MatchCrvOptions.DevTol : double.MaxValue;

            Curve resultA = null;
            Curve resultB = null;
            string lastErrorMsg = null;

            if (MatchCrvOptions.ModifyMode == 0) // First Curve
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying First Curve Only...");
                var res = CurveMatchMath.MatchOneCurve(
                    cA, cB, bT1WorkEndA, bT1WorkEndB,
                    MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd,
                    MatchCrvOptions.LineMode, MatchCrvOptions.ArcMode, MatchCrvOptions.NurbsMode,
                    MatchCrvOptions.IncrAsNeeded, currentDevTol,
                    MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity,
                    MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                resultA = res.Result;
                lastErrorMsg = res.ErrorMsg;
            }
            else if (MatchCrvOptions.ModifyMode == 1) // Average
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Modifying Both Curves (Average)...");
                var bothResult = CurveMatchMath.MatchBothCurves(
                    cA, cB, bT1WorkEndA, bT1WorkEndB,
                    MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd,
                    MatchCrvOptions.LineMode, MatchCrvOptions.ArcMode, MatchCrvOptions.NurbsMode,
                    MatchCrvOptions.IncrAsNeeded, currentDevTol,
                    MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity,
                    MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                resultA = bothResult.ResultA;
                resultB = bothResult.ResultB;
                lastErrorMsg = bothResult.ErrorMsg;
            }
            else if (MatchCrvOptions.ModifyMode == 2) // Minimize Deviation
            {
                if (MatchCrvOptions.Debug) RhinoApp.WriteLine("Evaluating First, Second, and Both for least deviation...");

                var resFirst = CurveMatchMath.MatchOneCurve(
                    cA, cB, bT1WorkEndA, bT1WorkEndB,
                    MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd,
                    MatchCrvOptions.LineMode, MatchCrvOptions.ArcMode, MatchCrvOptions.NurbsMode,
                    MatchCrvOptions.IncrAsNeeded, currentDevTol,
                    MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity,
                    MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                var resSecond = CurveMatchMath.MatchOneCurve(
                    cB, cA, bT1WorkEndB, bT1WorkEndA,
                    MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd,
                    MatchCrvOptions.LineMode, MatchCrvOptions.ArcMode, MatchCrvOptions.NurbsMode,
                    MatchCrvOptions.IncrAsNeeded, currentDevTol,
                    MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity,
                    MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);
                var bothResult = CurveMatchMath.MatchBothCurves(
                    cA, cB, bT1WorkEndA, bT1WorkEndB,
                    MatchCrvOptions.Continuity, MatchCrvOptions.PreserveOtherEnd,
                    MatchCrvOptions.LineMode, MatchCrvOptions.ArcMode, MatchCrvOptions.NurbsMode,
                    MatchCrvOptions.IncrAsNeeded, currentDevTol,
                    MatchCrvOptions.PreventExtraInflections, MatchCrvOptions.PreserveInternalContinuity, MatchCrvOptions.MaxInternalContinuity,
                    MatchCrvOptions.PosTol, MatchCrvOptions.AngleTolDeg, MatchCrvOptions.CurvatureMagTolPct, MatchCrvOptions.Debug);

                Curve candA = resFirst.Result;
                Curve candB = resSecond.Result;

                double devA = candA != null ? CurveMatchMath.GetEndSegmentAverageDeviation(cA, candA, bT1WorkEndA) : double.MaxValue;
                double devB = candB != null ? CurveMatchMath.GetEndSegmentAverageDeviation(cB, candB, bT1WorkEndB) : double.MaxValue;
                double devBoth = double.MaxValue;
                if (bothResult.ResultA != null && bothResult.ResultB != null)
                    devBoth = (CurveMatchMath.GetEndSegmentAverageDeviation(cA, bothResult.ResultA, bT1WorkEndA) +
                               CurveMatchMath.GetEndSegmentAverageDeviation(cB, bothResult.ResultB, bT1WorkEndB)) / 2.0;
                else if (bothResult.ResultA != null)
                    devBoth = CurveMatchMath.GetEndSegmentAverageDeviation(cA, bothResult.ResultA, bT1WorkEndA);
                else if (bothResult.ResultB != null)
                    devBoth = CurveMatchMath.GetEndSegmentAverageDeviation(cB, bothResult.ResultB, bT1WorkEndB);

                double minDev = Math.Min(devA, Math.Min(devB, devBoth));

                if (minDev == double.MaxValue)
                {
                    candA?.Dispose(); candB?.Dispose(); bothResult.ResultA?.Dispose(); bothResult.ResultB?.Dispose();
                    resultA = null;
                    resultB = null;
                    lastErrorMsg = CurveMatchMath.PickBestErrorMessage(resFirst.ErrorMsg, resSecond.ErrorMsg, bothResult.ErrorMsg);
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

            if (resultA != null) ProcessResult(doc, objRefs[0], cA, resultA, bT1WorkEndA);
            if (resultB != null) ProcessResult(doc, objRefs[1], cB, resultB, bT1WorkEndB);

            doc.Views.RedrawEnabled = true;
            return Result.Success;
        }

        private void ProcessResult(RhinoDoc doc, ObjRef objRef, Curve originalCrv, Curve newCrv, bool bT1WorkEnd)
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
                double avgDev = CurveMatchMath.GetEndSegmentAverageDeviation(originalCrv, newCrv, bT1WorkEnd);

                string sRadPre = radPre.HasValue ? FormatUtils.FormatDistance(radPre.Value, prec) : "(None)";
                string sRadPost = radPost.HasValue ? FormatUtils.FormatDistance(radPost.Value, prec) : "(None)";

                RhinoApp.WriteLine($"Min Radius: {sRadPre} -> {sRadPost}  |  Max Dev: {FormatUtils.FormatDistance(maxDev, prec)}  |  Avg Dev: {FormatUtils.FormatDistance(avgDev, prec)}");
            }
        }

        private bool IsSelectionCloserToT1(Curve c, Point3d pt)
        {
            double t;
            if (!c.ClosestPoint(pt, out t))
                return pt.DistanceTo(c.PointAtEnd) < pt.DistanceTo(c.PointAtStart);

            double totalLen = c.GetLength();
            if (totalLen > RhinoMath.ZeroTolerance)
            {
                double lenFromStart = c.GetLength(new Interval(c.Domain.T0, t));
                return lenFromStart > 0.5 * totalLen;
            }

            return t > c.Domain.Mid;
        }
    }
}
