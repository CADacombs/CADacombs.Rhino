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
        private bool _preserveTangents = true;
        private bool _limitDev = true;
        private double _devTol = -1.0; 
        private string _targetDegreesString = "235";
        
        // Exclude Options
        private bool _excludeBeziers = true;
        private bool _excludeLines = true;
        private bool _excludeArcs = true;
        private bool _excludeOtherConical = true;

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

            OptionToggle optPreserve = new OptionToggle(_preserveTangents, "No", "Yes");
            OptionToggle optLimitDev = new OptionToggle(_limitDev, "No", "Yes");
            OptionDouble optDevTol = new OptionDouble(_devTol);
            OptionToggle optDelete = new OptionToggle(_deleteInput, "No", "Yes");
            OptionToggle optDebug = new OptionToggle(_debug, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                
                int idxPreserve = go.AddOptionToggle("PreserveTans", ref optPreserve);
                int idxLimit = go.AddOptionToggle("LimitDev", ref optLimitDev);
                
                int idxTol = -1;
                if (_limitDev) idxTol = go.AddOptionDouble("DevTol", ref optDevTol);
                
                int idxTarget = go.AddOption("TargetDegrees", _targetDegreesString); 
                int idxExclude = go.AddOption("ExcludeSettings"); // Fixed: Removed punctuation
                int idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

                var res = go.GetMultiple(1, 0);

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxPreserve) _preserveTangents = optPreserve.CurrentValue;
                    else if (opt.Index == idxLimit) _limitDev = optLimitDev.CurrentValue;
                    else if (opt.Index == idxTol) _devTol = optDevTol.CurrentValue;
                    else if (opt.Index == idxDelete) _deleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxDebug) _debug = optDebug.CurrentValue;
                    else if (opt.Index == idxExclude) 
                    {
                        RunExcludeSettingsMenu();
                    }
                    else if (opt.Index == idxTarget)
                    {
                        string inputStr = _targetDegreesString;
                        var strRes = Rhino.Input.RhinoGet.GetString("Enter target degrees (e.g., 235)", true, ref inputStr);
                        if (strRes == Result.Success && !string.IsNullOrWhiteSpace(inputStr))
                        {
                            var cleanedList = ConvertToBezierLogic.ParseDegreesString(inputStr);
                            _targetDegreesString = string.Join("", cleanedList);
                        }
                    }
                }
            }

            int processedCount = 0;
            List<Guid> newObjectIds = new List<Guid>();
            List<int> targets = ConvertToBezierLogic.ParseDegreesString(_targetDegreesString);

            if (targets.Count == 0) return Result.Cancel;

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                var result = ConvertToBezierLogic.TryConvert(
                    crv, targets, _limitDev, _devTol, _preserveTangents, 
                    _excludeBeziers, _excludeLines, _excludeArcs, _excludeOtherConical, _debug);

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

        private void RunExcludeSettingsMenu()
        {
            var go = new GetOption();
            go.SetCommandPrompt("Object exclusion settings");
            go.AcceptNothing(true); 

            OptionToggle optBez = new OptionToggle(_excludeBeziers, "No", "Yes");
            OptionToggle optLines = new OptionToggle(_excludeLines, "No", "Yes");
            OptionToggle optArcs = new OptionToggle(_excludeArcs, "No", "Yes");
            OptionToggle optConic = new OptionToggle(_excludeOtherConical, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                int idxBez = go.AddOptionToggle("ExcludeBeziers", ref optBez);
                int idxLines = go.AddOptionToggle("Lines", ref optLines);
                int idxArcs = go.AddOptionToggle("Arcs", ref optArcs);
                int idxConic = go.AddOptionToggle("OtherConical", ref optConic);

                var res = go.Get();
                if (res == Rhino.Input.GetResult.Cancel || res == Rhino.Input.GetResult.Nothing) break; 

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxBez) _excludeBeziers = optBez.CurrentValue;
                    else if (opt.Index == idxLines) _excludeLines = optLines.CurrentValue;
                    else if (opt.Index == idxArcs) _excludeArcs = optArcs.CurrentValue;
                    else if (opt.Index == idxConic) _excludeOtherConical = optConic.CurrentValue;
                }
            }
        }
    }
}