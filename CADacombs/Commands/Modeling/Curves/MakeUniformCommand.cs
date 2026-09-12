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
    public class MakeUniformCommand : Command
    {
        public MakeUniformCommand() { Instance = this; }
        public static MakeUniformCommand Instance { get; private set; }
        
        public override string EnglishName => "ccMakeUniformCrv";

        // Sticky Options
        private bool _limitDev = true;
        private double _devTol = -1.0; 
        private bool _preserveEndTangents = true;
        private bool _preserveEndCurvatures = true;
        
        // Include Options (Inverted from original Exclude logic)
        private bool _includeLines = false; 
        private bool _includeArcs = false; 
        private bool _includePolyCurves = false;

        private bool _deleteInput = true;
        private bool _debug = false;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (_devTol < 0) _devTol = 0.1 * doc.ModelAbsoluteTolerance;

            var go = new GetObject();
            go.SetCommandPrompt("Select curves to make uniform");
            go.GeometryFilter = ObjectType.Curve;
            go.GroupSelect = true;
            go.SubObjectSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            OptionToggle optLimitDev = new OptionToggle(_limitDev, "No", "Yes");
            OptionDouble optDevTol = new OptionDouble(_devTol);
            OptionToggle optPreserveTan = new OptionToggle(_preserveEndTangents, "No", "Yes");
            OptionToggle optPreserveCurv = new OptionToggle(_preserveEndCurvatures, "No", "Yes");
            OptionToggle optDelete = new OptionToggle(_deleteInput, "No", "Yes");
            OptionToggle optDebug = new OptionToggle(_debug, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                
                int idxLimit = go.AddOptionToggle("LimitDev", ref optLimitDev);
                
                int idxTol = -1;
                if (_limitDev)
                {
                    idxTol = go.AddOptionDouble("DevTol", ref optDevTol);
                }
                
                int idxPreserveTan = go.AddOptionToggle("PreserveEndTangents", ref optPreserveTan);
                
                int idxPreserveCurv = -1;
                if (_preserveEndTangents)
                {
                    idxPreserveCurv = go.AddOptionToggle("PreserveEndCurvatures", ref optPreserveCurv);
                }

                int idxInclude = go.AddOption("ObjectFilter"); 
                int idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

                var res = go.GetMultiple(1, 0);

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxLimit) _limitDev = optLimitDev.CurrentValue;
                    else if (opt.Index == idxTol) 
                    {
                        if (optDevTol.CurrentValue < 0.0)
                        {
                            _devTol = 0.1 * doc.ModelAbsoluteTolerance;
                            optDevTol.CurrentValue = _devTol; // Update UI fallback
                        }
                        else
                        {
                            _devTol = optDevTol.CurrentValue;
                        }
                    }
                    else if (opt.Index == idxPreserveTan)
                    {
                        _preserveEndTangents = optPreserveTan.CurrentValue;
                        if (!_preserveEndTangents) 
                        {
                            _preserveEndCurvatures = false;
                            optPreserveCurv.CurrentValue = false;
                        }
                    }
                    else if (opt.Index == idxPreserveCurv) _preserveEndCurvatures = optPreserveCurv.CurrentValue;
                    else if (opt.Index == idxDelete) _deleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxDebug) _debug = optDebug.CurrentValue;
                    else if (opt.Index == idxInclude) 
                    {
                        RunIncludeSettingsMenu();
                    }
                }
            }

            int processedCount = 0;
            List<Guid> newObjectIds = new List<Guid>();

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                // 1. Inclusions (Strict Type Checking)
                if (!_includeLines && crv is LineCurve)
                {
                    if (_debug) RhinoApp.WriteLine($"Skipped curve {objRef.ObjectId}: IncludeLines is No.");
                    continue;
                }
                if (!_includeArcs && crv is ArcCurve)
                {
                    if (_debug) RhinoApp.WriteLine($"Skipped curve {objRef.ObjectId}: IncludeArcs is No.");
                    continue;
                }
                if (!_includePolyCurves && crv is PolyCurve)
                {
                    if (_debug) RhinoApp.WriteLine($"Skipped curve {objRef.ObjectId}: IncludePolyCurves is No.");
                    continue;
                }

                // 2. Core Solver
                var result = MakeUniformLogic.TryMakeUniform(crv, _limitDev, _devTol, _preserveEndTangents, _preserveEndCurvatures);

                if (_debug && !string.IsNullOrEmpty(result.Log))
                {
                    RhinoApp.WriteLine($"--- Debug Log for Curve {objRef.ObjectId} ---");
                    RhinoApp.WriteLine(result.Log);
                }

                // 3. Document Action
                if (result.UniformCurve != null)
                {
                    if (_deleteInput)
                    {
                        if (doc.Objects.Replace(objRef.ObjectId, result.UniformCurve))
                            processedCount++;
                    }
                    else
                    {
                        Guid id = doc.Objects.AddCurve(result.UniformCurve, objRef.Object().Attributes);
                        if (id != Guid.Empty)
                        {
                            newObjectIds.Add(id);
                            processedCount++;
                        }
                    }
                }
            }

            RhinoApp.WriteLine(processedCount > 0 
                ? $"Successfully made {processedCount} curve(s) uniform." 
                : "No curves could be made uniform under the current constraints.");

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
            go.SetCommandPrompt("Include these objects as input");
            go.AcceptNothing(true); 

            OptionToggle optLines = new OptionToggle(_includeLines, "No", "Yes");
            OptionToggle optArcs = new OptionToggle(_includeArcs, "No", "Yes");
            OptionToggle optPoly = new OptionToggle(_includePolyCurves, "No", "Yes");

            while (true)
            {
                go.ClearCommandOptions();
                int idxLines = go.AddOptionToggle("Lines", ref optLines);
                int idxArcs = go.AddOptionToggle("Arcs", ref optArcs);
                int idxPoly = go.AddOptionToggle("PolyCurves", ref optPoly);

                var res = go.Get();
                if (res == Rhino.Input.GetResult.Cancel || res == Rhino.Input.GetResult.Nothing) break; 

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxLines) _includeLines = optLines.CurrentValue;
                    else if (opt.Index == idxArcs) _includeArcs = optArcs.CurrentValue;
                    else if (opt.Index == idxPoly) _includePolyCurves = optPoly.CurrentValue;
                }
            }
        }
    }
}