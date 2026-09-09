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
    public class ConvertToBezierCommand : Command
    {
        public ConvertToBezierCommand() { Instance = this; }
        public static ConvertToBezierCommand Instance { get; private set; }
        
        public override string EnglishName => "ccConvertCrvToBezier";

        // Sticky Options
        private bool _deleteInput = true;
        private bool _keepOriginalDegree = false;
        private bool _preserveTangents = true;
        private string _targetDegreesString = "352";
        private double _devTol = -1.0; 

        // Skip Options
        private bool _skipLines = true;
        private bool _skipArcs = true;
        private bool _skipBeziers = true;
        private bool _skipOtherConical = true;

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

            OptionToggle optDelete = new OptionToggle(_deleteInput, "No", "Yes");
            OptionToggle optKeepDeg = new OptionToggle(_keepOriginalDegree, "No", "Yes");
            OptionToggle optPreserve = new OptionToggle(_preserveTangents, "No", "Yes");
            OptionDouble optDevTol = new OptionDouble(_devTol);

            while (true)
            {
                go.ClearCommandOptions();
                
                int idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                int idxKeep = go.AddOptionToggle("KeepOriginalDegree", ref optKeepDeg);
                
                int idxTarget = -1;
                if (!_keepOriginalDegree)
                {
                    // Displays the current string next to the option
                    idxTarget = go.AddOption("TargetDegrees", _targetDegreesString); 
                }
                
                int idxTol = go.AddOptionDouble("DevTol", ref optDevTol);
                int idxPreserve = go.AddOptionToggle("PreserveTangents", ref optPreserve);
                int idxSkip = go.AddOption("SkipSettings..."); // Acts as a button to open the sub-menu

                var res = go.GetMultiple(1, 0);

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxDelete) _deleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxKeep) _keepOriginalDegree = optKeepDeg.CurrentValue;
                    else if (opt.Index == idxTol) _devTol = optDevTol.CurrentValue;
                    else if (opt.Index == idxPreserve) _preserveTangents = optPreserve.CurrentValue;
                    else if (opt.Index == idxSkip) 
                    {
                        RunSkipSettingsMenu();
                    }
                    else if (opt.Index == idxTarget)
                    {
                        string inputStr = _targetDegreesString;
                        var strRes = RhinoGet.GetString("Enter target degrees as a single string (e.g., 2357)", true, ref inputStr);
                        if (strRes == Result.Success && !string.IsNullOrWhiteSpace(inputStr))
                        {
                            // Clean the string immediately using our Core engine parser
                            var cleanedList = ConvertToBezierLogic.ParseDegreesString(inputStr);
                            _targetDegreesString = string.Join("", cleanedList);
                        }
                    }
                }
            }

            int processedCount = 0;
            List<Guid> newObjectIds = new List<Guid>();

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                // Handle SkipOtherConical directly in the command wrapper
                if (_skipOtherConical && crv.ToNurbsCurve() != null)
                {
                    var nc = crv.ToNurbsCurve();
                    if (nc.IsRational && nc.Degree <= 2) continue;
                }

                // Determine target degrees
                List<int> targets = _keepOriginalDegree 
                    ? new List<int> { crv.Degree } 
                    : ConvertToBezierLogic.ParseDegreesString(_targetDegreesString);

                if (targets.Count == 0)
                {
                    RhinoApp.WriteLine("No valid target degrees provided.");
                    break;
                }

                // Send to Core Engine
                var result = ConvertToBezierLogic.TryConvert(crv, targets, _devTol, _preserveTangents, _skipLines, _skipArcs);

                if (result.Bezier != null)
                {
                    if (_deleteInput)
                    {
                        if (doc.Objects.Replace(objRef.ObjectId, result.Bezier))
                            processedCount++;
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

            if (processedCount > 0)
            {
                RhinoApp.WriteLine($"Successfully converted {processedCount} curve(s) to Bezier.");
                
                if (!_deleteInput && newObjectIds.Count > 0)
                {
                    doc.Objects.UnselectAll();
                    foreach (Guid id in newObjectIds) doc.Objects.Select(id);
                }
            }
            else
            {
                RhinoApp.WriteLine("No curves could be converted to Bezier within the specified tolerance.");
            }

            doc.Views.Redraw();
            return Result.Success;
        }

        private void RunSkipSettingsMenu()
        {
            var go = new GetOption();
            go.SetCommandPrompt("Configure skip settings");
            go.AcceptNothing(true); // Pressing enter returns to main menu

            OptionToggle optLines = new OptionToggle(_skipLines, "No", "Yes");
            OptionToggle optArcs = new OptionToggle(_skipArcs, "No", "Yes");
            OptionToggle optBez = new OptionToggle(_skipBeziers, "No", "Yes");
            OptionToggle optConic = new OptionToggle(_skipOtherConical, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                int idxLines = go.AddOptionToggle("SkipLines", ref optLines);
                int idxArcs = go.AddOptionToggle("SkipArcs", ref optArcs);
                int idxBez = go.AddOptionToggle("SkipBeziers", ref optBez);
                int idxConic = go.AddOptionToggle("SkipOtherConical", ref optConic);

                var res = go.Get();

                if (res == Rhino.Input.GetResult.Cancel || res == Rhino.Input.GetResult.Nothing) 
                    break; // Return to main prompt

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxLines) _skipLines = optLines.CurrentValue;
                    else if (opt.Index == idxArcs) _skipArcs = optArcs.CurrentValue;
                    else if (opt.Index == idxBez) _skipBeziers = optBez.CurrentValue;
                    else if (opt.Index == idxConic) _skipOtherConical = optConic.CurrentValue;
                }
            }
        }
    }
}