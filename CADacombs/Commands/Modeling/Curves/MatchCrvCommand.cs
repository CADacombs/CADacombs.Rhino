using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling.Curves
{
    [System.Runtime.InteropServices.Guid("1a2b3c4d-5e6f-7a8b-9c0d-e1f2a3b4c5d6")] // TODO: Replace with unique GUID
    public class MatchCrvCommand : Command
    {
        public override string EnglishName => "ccMatchCrv";

        // Sticky settings
        private int _iContinuity = 1; 
        private double _fAngleTolDeg = -1.0; 
        private bool _bSkipIfAlreadyAtContinuity = true;
        private int _iPreserveOtherEnd = 2; 
        private bool _bLimitCrvDev = true;
        private double _fDevTol = -1.0;
        private bool _bMaximizeMinRadius = false;
        private bool _bModOnly1stPicked = true;
        private bool _bDeformLines = false;
        private bool _bRebuildRationals = false;
        private bool _bReplace = true;
        private bool _bEcho = true;
        private bool _bDebug = false;

        private readonly string[] _continuityNames = { "Position", "Tangency", "Curvature" };
        private readonly string[] _preserveNames = { "None", "Position", "Tangency", "Curvature", "Periodic" };

        public MatchCrvCommand()
        {
            Instance = this;
        }

        public static MatchCrvCommand Instance { get; private set; }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            LoadSettings(doc);

            var go = new GetObject();
            go.SetCommandPrompt("Select 2 curves near their ends to match");
            go.GeometryFilter = ObjectType.Curve;
            go.OneByOnePostSelect = true;
            go.DisablePreSelect();
            go.AcceptNumber(true, true);

            while (true)
            {
                go.ClearCommandOptions();

                var optAngleTol = new OptionDouble(_fAngleTolDeg);
                var optSkip = new OptionToggle(_bSkipIfAlreadyAtContinuity, "Process", "Skip");
                var optLimitDev = new OptionToggle(_bLimitCrvDev, "No", "Yes");
                var optDevTol = new OptionDouble(_fDevTol);
                var optMaxRadius = new OptionToggle(_bMaximizeMinRadius, "No", "Yes");
                var optModOnly1st = new OptionToggle(_bModOnly1stPicked, "EitherOrBoth", "Only1stPicked");
                var optDeformLines = new OptionToggle(_bDeformLines, "No", "Yes");
                var optRebuild = new OptionToggle(_bRebuildRationals, "No", "Yes");
                var optReplace = new OptionToggle(_bReplace, "Add", "Replace");
                var optEcho = new OptionToggle(_bEcho, "No", "Yes");
                var optDebug = new OptionToggle(_bDebug, "No", "Yes");

                // Correct native OptionList handling
                int idxContinuity = go.AddOptionList("Continuity", _continuityNames, _iContinuity);
                go.AddOptionDouble("AngleTol", ref optAngleTol);
                go.AddOptionToggle("IfAlreadyAtContinuity", ref optSkip);
                int idxPreserve = go.AddOptionList("PreserveOtherEnd", _preserveNames, _iPreserveOtherEnd);
                
                go.AddOptionToggle("LimitCrvDev", ref optLimitDev);
                if (_bLimitCrvDev) go.AddOptionDouble("DevTol", ref optDevTol);
                if (_iContinuity == 0 || _iContinuity == 1) go.AddOptionToggle("MaximizeMinRadius", ref optMaxRadius);
                
                go.AddOptionToggle("ModifyCrv", ref optModOnly1st);
                go.AddOptionToggle("DeformLines", ref optDeformLines);
                go.AddOptionToggle("RebuildRationals", ref optRebuild);
                go.AddOptionToggle("Action", ref optReplace);
                go.AddOptionToggle("Echo", ref optEcho);
                go.AddOptionToggle("Debug", ref optDebug);

                GetResult res = go.GetMultiple(2, 2);

                if (res == GetResult.Number)
                {
                    double num = go.Number();
                    if (num == 0 || num == 1 || num == 2)
                        _iContinuity = (int)num;
                    else
                        _fDevTol = num;
                    
                    SaveSettings();
                    continue;
                }
                else if (res == GetResult.Option)
                {
                    var option = go.Option();
                    if (option.Index == idxContinuity) _iContinuity = option.CurrentListOptionIndex;
                    else if (option.Index == idxPreserve) _iPreserveOtherEnd = option.CurrentListOptionIndex;

                    _fAngleTolDeg = optAngleTol.CurrentValue;
                    if (_fAngleTolDeg <= 0) _fAngleTolDeg = RhinoMath.ToDegrees(RhinoMath.ZeroTolerance);
                    
                    _bSkipIfAlreadyAtContinuity = optSkip.CurrentValue;
                    _bLimitCrvDev = optLimitDev.CurrentValue;
                    
                    _fDevTol = optDevTol.CurrentValue;
                    if (_fDevTol <= 0) _fDevTol = RhinoMath.ZeroTolerance;

                    _bMaximizeMinRadius = optMaxRadius.CurrentValue;
                    _bModOnly1stPicked = optModOnly1st.CurrentValue;
                    _bDeformLines = optDeformLines.CurrentValue;
                    _bRebuildRationals = optRebuild.CurrentValue;
                    _bReplace = optReplace.CurrentValue;
                    _bEcho = optEcho.CurrentValue;
                    _bDebug = optDebug.CurrentValue;

                    SaveSettings();
                    continue;
                }
                else if (res != GetResult.Object)
                {
                    return Result.Cancel;
                }
                break;
            }

            ObjRef[] objRefs = go.Objects();
            if (objRefs == null || objRefs.Length != 2) return Result.Cancel;

            Curve cA = objRefs[0].Curve();
            Curve cB = objRefs[1].Curve();
            if (cA == null || cB == null) return Result.Failure;

            bool bT1WorkEndA = IsSelectionCloserToT1(cA, objRefs[0].SelectionPoint());
            bool bT1WorkEndB = IsSelectionCloserToT1(cB, objRefs[1].SelectionPoint());

            if (_bSkipIfAlreadyAtContinuity)
            {
                if (AreCurvesAtContinuity(cA, bT1WorkEndA, cB, bT1WorkEndB, _iContinuity))
                {
                    if (_bEcho) RhinoApp.WriteLine($"Curves already meet at G{_iContinuity} continuity. Skipping.");
                    return Result.Success;
                }
            }

            if (!_bDebug) doc.Views.RedrawEnabled = false;

            double currentDevTol = _bLimitCrvDev ? _fDevTol : double.MaxValue;

            NurbsCurve resultA = null;
            NurbsCurve resultB = null;

            if (_bModOnly1stPicked)
            {
                if (_bDebug) RhinoApp.WriteLine("Modifying A and not B...");
                resultA = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, _iContinuity, _iPreserveOtherEnd, _bDeformLines, _bMaximizeMinRadius, currentDevTol);
            }
            else
            {
                if (_bDebug) RhinoApp.WriteLine("Evaluating A->B, B->A, and Both for least deviation...");
                
                NurbsCurve candA = CurveMatchMath.MatchOneCurve(cA, cB, bT1WorkEndA, bT1WorkEndB, _iContinuity, _iPreserveOtherEnd, _bDeformLines, _bMaximizeMinRadius, currentDevTol);
                NurbsCurve candB = CurveMatchMath.MatchOneCurve(cB, cA, bT1WorkEndB, bT1WorkEndA, _iContinuity, _iPreserveOtherEnd, _bDeformLines, _bMaximizeMinRadius, currentDevTol);
                var bothResult = CurveMatchMath.MatchBothCurves(cA, cB, bT1WorkEndA, bT1WorkEndB, _iContinuity, _iPreserveOtherEnd, _bDeformLines, _bMaximizeMinRadius, currentDevTol);

                double devA = candA != null ? CurveMatchMath.GetMaximumDeviation(cA, candA) : double.MaxValue;
                double devB = candB != null ? CurveMatchMath.GetMaximumDeviation(cB, candB) : double.MaxValue;
                
                double devBothA = bothResult.ResultA != null ? CurveMatchMath.GetMaximumDeviation(cA, bothResult.ResultA) : double.MaxValue;
                double devBothB = bothResult.ResultB != null ? CurveMatchMath.GetMaximumDeviation(cB, bothResult.ResultB) : double.MaxValue;
                double maxDevBoth = Math.Max(devBothA, devBothB);

                double minDev = Math.Min(devA, Math.Min(devB, maxDevBoth));

                if (minDev == double.MaxValue)
                {
                    resultA = null; resultB = null;
                }
                else if (minDev == devA)
                {
                    resultA = candA;
                    candB?.Dispose();
                    bothResult.ResultA?.Dispose();
                    bothResult.ResultB?.Dispose();
                }
                else if (minDev == devB)
                {
                    resultB = candB;
                    candA?.Dispose();
                    bothResult.ResultA?.Dispose();
                    bothResult.ResultB?.Dispose();
                }
                else
                {
                    resultA = bothResult.ResultA;
                    resultB = bothResult.ResultB;
                    candA?.Dispose();
                    candB?.Dispose();
                }
            }

            if (resultA == null && resultB == null)
            {
                RhinoApp.WriteLine("Curve(s) within tolerance could not be created, so they were not modified.");
                return Result.Failure;
            }

            if (resultA != null) ProcessResult(doc, objRefs[0], cA, resultA);
            if (resultB != null) ProcessResult(doc, objRefs[1], cB, resultB);

            doc.Views.RedrawEnabled = true;
            return Result.Success;
        }

        private void ProcessResult(RhinoDoc doc, ObjRef objRef, Curve originalCrv, NurbsCurve newCrv)
        {
            if (_bReplace)
            {
                if (doc.Objects.Replace(objRef.ObjectId, newCrv))
                {
                    if (_bEcho) RhinoApp.WriteLine("Curve was replaced.");
                    doc.Objects.Select(objRef.ObjectId);
                }
            }
            else
            {
                Guid g1 = doc.Objects.AddCurve(newCrv);
                if (g1 != Guid.Empty)
                {
                    if (_bEcho) RhinoApp.WriteLine("Curve was added.");
                    doc.Objects.Select(g1);
                }
            }

            if (_bEcho || _bDebug)
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

        private bool AreCurvesAtContinuity(Curve cA, bool bT1WorkEndA, Curve cB, bool bT1WorkEndB, int targetContinuity)
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

            int? level = ContinuityUtils.GetContinuityLevel(vB, vA, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, _fAngleTolDeg, 2.0, 5.0);
            
            return level.HasValue && level.Value >= targetContinuity;
        }

        private void LoadSettings(RhinoDoc doc)
        {
            _iContinuity = Settings.GetInteger("iContinuity", _iContinuity);
            _fAngleTolDeg = Settings.GetDouble("fAngleTolDeg", _fAngleTolDeg);
            if (_fAngleTolDeg <= 0) _fAngleTolDeg = 0.1 * RhinoMath.ToDegrees(doc.ModelAngleToleranceRadians);

            _bSkipIfAlreadyAtContinuity = Settings.GetBool("bSkipIfAlreadyAtContinuity", _bSkipIfAlreadyAtContinuity);
            _iPreserveOtherEnd = Settings.GetInteger("iPreserveOtherEnd", _iPreserveOtherEnd);
            _bLimitCrvDev = Settings.GetBool("bLimitCrvDev", _bLimitCrvDev);
            _fDevTol = Settings.GetDouble("fDevTol", _fDevTol);
            if (_fDevTol <= 0) _fDevTol = 0.1 * doc.ModelAbsoluteTolerance;

            _bMaximizeMinRadius = Settings.GetBool("bMaximizeMinRadius", _bMaximizeMinRadius);
            _bModOnly1stPicked = Settings.GetBool("bModOnly1stPicked", _bModOnly1stPicked);
            _bDeformLines = Settings.GetBool("bDeformLines", _bDeformLines);
            _bRebuildRationals = Settings.GetBool("bRebuildRationals", _bRebuildRationals);
            _bReplace = Settings.GetBool("bReplace", _bReplace);
            _bEcho = Settings.GetBool("bEcho", _bEcho);
            _bDebug = Settings.GetBool("bDebug", _bDebug);
        }

        private void SaveSettings()
        {
            Settings.SetInteger("iContinuity", _iContinuity);
            Settings.SetDouble("fAngleTolDeg", _fAngleTolDeg);
            Settings.SetBool("bSkipIfAlreadyAtContinuity", _bSkipIfAlreadyAtContinuity);
            Settings.SetInteger("iPreserveOtherEnd", _iPreserveOtherEnd);
            Settings.SetBool("bLimitCrvDev", _bLimitCrvDev);
            Settings.SetDouble("fDevTol", _fDevTol);
            Settings.SetBool("bMaximizeMinRadius", _bMaximizeMinRadius);
            Settings.SetBool("bModOnly1stPicked", _bModOnly1stPicked);
            Settings.SetBool("bDeformLines", _bDeformLines);
            Settings.SetBool("bRebuildRationals", _bRebuildRationals);
            Settings.SetBool("bReplace", _bReplace);
            Settings.SetBool("bEcho", _bEcho);
            Settings.SetBool("bDebug", _bDebug);
        }
    }
}