using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class ConvertToBezierCommand : Command
    {
        public ConvertToBezierCommand() { Instance = this; }
        public static ConvertToBezierCommand Instance { get; private set; }
        
        public override string EnglishName => "ccConvertCrvToBezier";

        // Sticky Options
        private KnotRemovalUtils.PreserveEndType _preserveEnds = KnotRemovalUtils.PreserveEndType.Tangency;
        private bool _limitDev = true;
        private double _devTol = -1.0; 
        private string _targetDegreesString = "235";
        private bool _convertToNonrational = true;
        
        // Include Options (ordered to match filter evaluation)
        private bool _includeLines = false;
        private bool _includeArcs = false;
        private bool _includeOtherConical = false;
        private bool _includeRational = false;
        private bool _includeBeziers = false;

        private bool _deleteInput = true;
        private bool _debug = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (_devTol < 0) _devTol = 0.1 * doc.ModelAbsoluteTolerance;

            var go = new GetObject();
            go.SetCommandPrompt("Select curves to convert to Bezier");
            go.GeometryFilter = ObjectType.Curve;
            go.GroupSelect = true;
            go.SubObjectSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            string[] preserveEndNames = new[] { "Position", "Tangency", "Curvature" };
            OptionToggle optLimitDev = new OptionToggle(_limitDev, "No", "Yes");
            OptionDouble optDevTol = new OptionDouble(_devTol);
            OptionToggle optNonrational = new OptionToggle(_convertToNonrational, "No", "Yes");
            OptionToggle optDelete = new OptionToggle(_deleteInput, "No", "Yes");
            OptionToggle optDebug = new OptionToggle(_debug, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                
                int idxPreserve = go.AddOptionList("PreserveEnds", preserveEndNames, (int)_preserveEnds);
                int idxLimit = go.AddOptionToggle("LimitDev", ref optLimitDev);
                
                int idxTol = -1;
                if (_limitDev) idxTol = go.AddOptionDouble("DevTol", ref optDevTol);
                
                int idxTarget = go.AddOption("TargetDegrees", _targetDegreesString); 
                int idxNonrational = go.AddOptionToggle("ConvertToNonrational", ref optNonrational);
                int idxInclude = go.AddOption("IncludeSettings");
                int idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

                var res = go.GetMultiple(1, 0);

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxPreserve)
                    {
                        _preserveEnds = (KnotRemovalUtils.PreserveEndType)opt.CurrentListOptionIndex;
                        if (_preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature)
                        {
                            var filtered = ConvertToBezierLogic.ParseDegreesString(_targetDegreesString, 5);
                            _targetDegreesString = filtered.Count > 0 ? string.Join("", filtered) : "5";
                        }
                    }
                    else if (opt.Index == idxLimit) _limitDev = optLimitDev.CurrentValue;
                    else if (opt.Index == idxTol) _devTol = optDevTol.CurrentValue;
                    else if (opt.Index == idxNonrational) _convertToNonrational = optNonrational.CurrentValue;
                    else if (opt.Index == idxDelete) _deleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxDebug) _debug = optDebug.CurrentValue;
                    else if (opt.Index == idxInclude) 
                    {
                        RunIncludeSettingsMenu();
                    }
                    else if (opt.Index == idxTarget)
                    {
                        int minDeg = _preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature ? 5 : 2;
                        string prompt = minDeg == 4
                            ? "Enter target degrees >= 5 (e.g., 57)"
                            : "Enter target degrees (e.g., 235)";

                        string inputStr = _targetDegreesString;
                        var strRes = Rhino.Input.RhinoGet.GetString(prompt, true, ref inputStr);
                        if (strRes == Result.Success && !string.IsNullOrWhiteSpace(inputStr))
                        {
                            var cleanedList = ConvertToBezierLogic.ParseDegreesString(inputStr, minDeg);
                            if (cleanedList.Count > 0)
                                _targetDegreesString = string.Join("", cleanedList);
                            else if (minDeg == 5)
                                RhinoApp.WriteLine("Degrees below 5 are not allowed when PreserveEnds is set to Curvature.");
                        }
                    }
                }
            }

            int processedCount = 0;
            List<Guid> newObjectIds = new List<Guid>();
            int activeMinDeg = _preserveEnds == KnotRemovalUtils.PreserveEndType.Curvature ? 5 : 2;
            List<int> targets = ConvertToBezierLogic.ParseDegreesString(_targetDegreesString, activeMinDeg);

            if (targets.Count == 0) return Result.Cancel;

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                var result = ConvertToBezierLogic.TryConvert(
                    crv, targets, _limitDev, _devTol, _preserveEnds, _convertToNonrational,
                    _includeLines, _includeArcs, _includeOtherConical, _includeRational, _includeBeziers, _debug);

                if (_debug && !string.IsNullOrEmpty(result.Log))
                {
                    RhinoApp.WriteLine($"--- Debug Log for Curve {objRef.ObjectId} ---");
                    RhinoApp.WriteLine(result.Log);
                }

                if (result.Bezier != null)
                {
                    if (_deleteInput)
                    {
                        if (doc.Objects.Replace(objRef.ObjectId, result.Bezier)) processedCount++;
                    }
                    else
                    {
                        Guid id = doc.Objects.AddCurve(result.Bezier, objRef.Object().Attributes);
                        if (id != Guid.Empty)
                        {
                            newObjectIds.Add(id);
                            processedCount++;
                        }
                    }
                }
            }

            RhinoApp.WriteLine(processedCount > 0 
                ? $"Successfully converted {processedCount} curve(s) to Bezier." 
                : "No curves could be converted to Bezier under the current constraints.");

            if (!_deleteInput && newObjectIds.Count > 0)
            {
                doc.Objects.UnselectAll();
                foreach (Guid id in newObjectIds) doc.Objects.Select(id);
            }

            doc.Views.Redraw();
            return Result.Success;
        }

        private void RunIncludeSettingsMenu()
        {
            var go = new GetOption();
            go.SetCommandPrompt("Included objects to convert");
            go.AcceptNothing(true); 

            OptionToggle optLines = new OptionToggle(_includeLines, "No", "Yes");
            OptionToggle optArcs = new OptionToggle(_includeArcs, "No", "Yes");
            OptionToggle optConic = new OptionToggle(_includeOtherConical, "No", "Yes");
            OptionToggle optRational = new OptionToggle(_includeRational, "No", "Yes");
            OptionToggle optBez = new OptionToggle(_includeBeziers, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                int idxLines = go.AddOptionToggle("Lines", ref optLines);
                int idxArcs = go.AddOptionToggle("Arcs", ref optArcs);
                int idxConic = go.AddOptionToggle("OtherConical", ref optConic);
                int idxRational = go.AddOptionToggle("Rational", ref optRational);
                int idxBez = go.AddOptionToggle("Beziers", ref optBez);

                var res = go.Get();
                if (res == Rhino.Input.GetResult.Cancel || res == Rhino.Input.GetResult.Nothing) break; 

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxLines) _includeLines = optLines.CurrentValue;
                    else if (opt.Index == idxArcs) _includeArcs = optArcs.CurrentValue;
                    else if (opt.Index == idxConic) _includeOtherConical = optConic.CurrentValue;
                    else if (opt.Index == idxRational) _includeRational = optRational.CurrentValue;
                    else if (opt.Index == idxBez) _includeBeziers = optBez.CurrentValue;
                }
            }
        }
    }
}