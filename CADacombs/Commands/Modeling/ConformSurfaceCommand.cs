using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    [CommandStyle(Style.Hidden)]
    public class ConformSrfCommand : Command
    {
        public ConformSrfCommand() { Instance = this; }
        public static ConformSrfCommand Instance { get; private set; }
        public override string EnglishName => "ccConformSrf";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return RunSharedCommand(doc, mode);
        }

        public static Result RunSharedCommand(RhinoDoc doc, RunMode mode)
        {
            if (!ConformSrfOptions.HasRunBefore)
            {
                ConformSrfOptions.HasRunBefore = true;
            }

            if (doc.RuntimeSerialNumber != ConformSrfOptions.LastDocSerialNumber)
            {
                ConformSrfOptions.Tolerance = 10.0 * doc.ModelAbsoluteTolerance;
                ConformSrfOptions.LastDocSerialNumber = doc.RuntimeSerialNumber;
            }

            var goSrf = new GetObject();
            goSrf.SetCommandPrompt("Select surfaces to deform (source surfaces)");
            goSrf.GeometryFilter = ObjectType.Surface;
            goSrf.SubObjectSelect = true;
            goSrf.GetMultiple(1, 0);

            if (goSrf.CommandResult() != Result.Success)
                return goSrf.CommandResult();

            var startingSrfRefs = goSrf.Objects().ToList();

            var targetRefs = new List<ObjRef>();
            var goTargets = new GetObject();
            goTargets.SetCommandPrompt("Select target surfaces, polysurfaces, SubDs and meshes");
            goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
            goTargets.EnablePreSelect(false, true); 
            goTargets.DeselectAllBeforePostSelect = false;

            string[] listDirection = { "Pull", "ProjectDown", "ProjectUp" };
            string[] listSolver = { "DirectCP", "DirectGreville", "Relaxed", "LeastSquares" };
            string[] listSampling = { "Distributed", "NearestPoint" };
            string[] listWeight = { "Linear", "Hyperbolic", "Gaussian" };
            string[] listOutput = { "Input", "Current", "Target" };

            int currentDir = ConformSrfOptions.MappingDirection == 0 ? 0 : (ConformSrfOptions.FlipDirection ? 2 : 1);

            var opDomain = new OptionDouble(ConformSrfOptions.PercentOfDomain);
            var opUseStiff = new OptionToggle(ConformSrfOptions.UseStiffness, "No", "Yes");
            var opStiffExp = new OptionInteger(ConformSrfOptions.EnergyStiffnessExponent);
            var opConstrain = new OptionToggle(ConformSrfOptions.Constrain1D, "No", "Yes");
            var opTol = new OptionDouble(ConformSrfOptions.Tolerance);
            var opTime = new OptionDouble(ConformSrfOptions.SolverTimeout);
            var opDel = new OptionToggle(ConformSrfOptions.DeleteStartingSrf, "No", "Yes");
            var opErr = new OptionToggle(ConformSrfOptions.AddErrorLines, "No", "Yes");

            while (true)
            {
                goTargets.ClearCommandOptions();
                if (mode == RunMode.Scripted)
                {
                    goTargets.AddOptionList("Direction", listDirection, currentDir);
                    goTargets.AddOptionList("SolverType", listSolver, ConformSrfOptions.SolverType);

                    bool isWeighted = ConformSrfOptions.SolverType == 2;
                    bool isMatrix = ConformSrfOptions.SolverType == 3;
                    bool isZAxis = currentDir > 0;

                    if (isWeighted)
                    {
                        goTargets.AddOptionList("Sampling", listSampling, ConformSrfOptions.SamplingMode);
                        goTargets.AddOptionList("WeightCurve", listWeight, ConformSrfOptions.WeightCurve);
                        goTargets.AddOptionDouble("DomainPercent", ref opDomain);
                    }

                    if (isMatrix)
                    {
                        goTargets.AddOptionToggle("UseStiffness", ref opUseStiff);
                        if (ConformSrfOptions.UseStiffness)
                        {
                            goTargets.AddOptionInteger("StiffnessExponent", ref opStiffExp);
                        }
                    }

                    if (isMatrix && isZAxis)
                    {
                        goTargets.AddOptionToggle("Constrain1D", ref opConstrain);
                    }

                    goTargets.AddOptionDouble("Tolerance", ref opTol);
                    goTargets.AddOptionDouble("TimeoutSecs", ref opTime);
                    goTargets.AddOptionList("OutputLayer", listOutput, ConformSrfOptions.OutputLayer);
                    goTargets.AddOptionToggle("DeleteSource", ref opDel);
                    goTargets.AddOptionToggle("AddErrorLines", ref opErr);
                }

                var resTargets = goTargets.GetMultiple(1, 0);

                if (resTargets == GetResult.Option)
                {
                    var opt = goTargets.Option();
                    string name = opt.EnglishName;

                    if (name == "Direction")
                    {
                        currentDir = opt.CurrentListOptionIndex;
                        ConformSrfOptions.MappingDirection = currentDir == 0 ? 0 : 1;
                        ConformSrfOptions.FlipDirection = currentDir == 2;
                    }
                    else if (name == "SolverType") ConformSrfOptions.SolverType = opt.CurrentListOptionIndex;
                    else if (name == "Sampling") ConformSrfOptions.SamplingMode = opt.CurrentListOptionIndex;
                    else if (name == "WeightCurve") ConformSrfOptions.WeightCurve = opt.CurrentListOptionIndex;
                    else if (name == "DomainPercent")
                    {
                        double val = opDomain.CurrentValue;
                        if (val < 0.01) val = 0.01;
                        if (val > 50.0) val = 50.0;
                        ConformSrfOptions.PercentOfDomain = val;
                        opDomain.CurrentValue = val;
                    }
                    else if (name == "UseStiffness") ConformSrfOptions.UseStiffness = opUseStiff.CurrentValue;
                    else if (name == "StiffnessExponent")
                    {
                        int val = opStiffExp.CurrentValue;
                        if (val < -10) val = -10;
                        if (val > 10) val = 10;
                        ConformSrfOptions.EnergyStiffnessExponent = val;
                        opStiffExp.CurrentValue = val;
                    }
                    else if (name == "Constrain1D") ConformSrfOptions.Constrain1D = opConstrain.CurrentValue;
                    else if (name == "Tolerance")
                    {
                        double val = opTol.CurrentValue;
                        double minTol = 1e-6 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);
                        if (val < 0) val = 10.0 * doc.ModelAbsoluteTolerance;
                        if (val < minTol) val = minTol;
                        ConformSrfOptions.Tolerance = val;
                        opTol.CurrentValue = val;
                    }
                    else if (name == "TimeoutSecs")
                    {
                        double val = opTime.CurrentValue;
                        if (val < 0.1) val = 0.1;
                        if (val > 60.0) val = 60.0;
                        ConformSrfOptions.SolverTimeout = val;
                        opTime.CurrentValue = val;
                    }
                    else if (name == "OutputLayer") ConformSrfOptions.OutputLayer = opt.CurrentListOptionIndex;
                    else if (name == "DeleteSource") ConformSrfOptions.DeleteStartingSrf = opDel.CurrentValue;
                    else if (name == "AddErrorLines") ConformSrfOptions.AddErrorLines = opErr.CurrentValue;

                    continue;
                }
                else if (resTargets == GetResult.Cancel)
                {
                    return Result.Cancel;
                }
                else if (resTargets == GetResult.Object)
                {
                    foreach (var obj in goTargets.Objects())
                    {
                        bool isStartingSrf = false;
                        foreach (var startSrf in startingSrfRefs)
                        {
                            if (obj.ObjectId == startSrf.ObjectId) { isStartingSrf = true; break; }
                        }
                        if (!isStartingSrf) targetRefs.Add(obj);
                    }
                    break;
                }
            }

            if (targetRefs.Count == 0)
            {
                RhinoApp.WriteLine("No valid targets selected.");
                return Result.Cancel;
            }

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            if (mode == RunMode.Scripted)
            {
                return ConformSrfLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
            }

            var conduit = new DrapeConduit { Enabled = true };

            while (true)
            {
                RhinoApp.SetCommandPrompt("Continue in dialog");
                
                var dialog = new ConformSrfDialog(targetRefs.ToArray(), startingSrfRefs, conduit);
                var parent = Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc);
                dialog.ShowSemiModal(doc, parent);

                if (dialog.Action == DrapeDialogAction.Ok)
                {
                    if (dialog.Results != null && dialog.Results.Count > 0)
                    {
                        ConformSrfLogic.BakeResults(doc, dialog.Results, startingSrfRefs, targetRefs.ToArray());
                    }
                    break;
                }
                else if (dialog.Action == DrapeDialogAction.ReselectTargets)
                {
                    var oldTargets = new List<ObjRef>(targetRefs);
                    var goEdit = new GetObject();
                    
                    goEdit.SetCommandPrompt("Reselect target surfaces, polysurfaces, SubDs and meshes");
                    goEdit.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
                    goEdit.GetMultiple(1, 0);
                    
                    if (goEdit.CommandResult() == Result.Success)
                    {
                        targetRefs.Clear();
                        foreach (var obj in goEdit.Objects())
                        {
                            bool isStartingSrf = false;
                            if (startingSrfRefs != null)
                            {
                                foreach (var startSrf in startingSrfRefs)
                                {
                                    if (obj.ObjectId == startSrf.ObjectId) { isStartingSrf = true; break; }
                                }
                            }
                            if (!isStartingSrf) targetRefs.Add(obj);
                        }
                        if (targetRefs.Count == 0) targetRefs = oldTargets;
                    }
                    else
                    {
                        targetRefs = oldTargets; 
                    }
                    doc.Objects.UnselectAll();
                }
                else if (dialog.Action == DrapeDialogAction.AddRemoveTargets)
                {
                    var oldTargets = new List<ObjRef>(targetRefs);
                    var goAddRem = new GetObject();
                    goAddRem.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;

                    if (targetRefs.Count > 0)
                    {
                        doc.Objects.UnselectAll();
                        foreach (var t in targetRefs) doc.Objects.Select(t.ObjectId);
                        doc.Views.Redraw();
                        
                        goAddRem.EnablePreSelect(true, true);
                        goAddRem.GetMultiple(1, 0); 
                    }
                    
                    goAddRem.SetCommandPrompt("Select targets to add, or Ctrl+Click to remove (press Enter when done)");
                    goAddRem.EnablePreSelect(false, true); 
                    goAddRem.EnableClearObjectsOnEntry(false);
                    goAddRem.DeselectAllBeforePostSelect = false;
                    goAddRem.AcceptNothing(true);
                    
                    goAddRem.GetMultiple(1, 0);
                    
                    if (goAddRem.CommandResult() == Result.Success || goAddRem.CommandResult() == Result.Nothing)
                    {
                        var currentList = goAddRem.Objects().ToList();
                        
                        if (currentList.Count == 0)
                        {
                            RhinoApp.WriteLine("No targets selected; reverting to previous selection.");
                            targetRefs = oldTargets;
                        }
                        else
                        {
                            targetRefs.Clear();
                            foreach (var obj in currentList)
                            {
                                bool isStartingSrf = false;
                                if (startingSrfRefs != null)
                                {
                                    foreach (var startSrf in startingSrfRefs)
                                    {
                                        if (obj.ObjectId == startSrf.ObjectId) { isStartingSrf = true; break; }
                                    }
                                }
                                if (!isStartingSrf) targetRefs.Add(obj);
                            }
                            if (targetRefs.Count == 0) targetRefs = oldTargets;
                        }
                    }
                    else
                    {
                        targetRefs = oldTargets;
                    }
                    
                    doc.Objects.UnselectAll();
                }
                else if (dialog.Action == DrapeDialogAction.PickCustomSurface)
                {
                    var oldSrfs = new List<ObjRef>(startingSrfRefs);
                    var goSrfEdit = new GetObject();
                    
                    goSrfEdit.SetCommandPrompt("Reselect source surfaces");
                    goSrfEdit.GeometryFilter = ObjectType.Surface;
                    goSrfEdit.SubObjectSelect = true;
                    goSrfEdit.GetMultiple(1, 0);
                    
                    if (goSrfEdit.CommandResult() == Result.Success)
                    {
                        startingSrfRefs.Clear();
                        foreach (var obj in goSrfEdit.Objects())
                        {
                            bool isTarget = false;
                            if (targetRefs != null)
                            {
                                foreach (var target in targetRefs)
                                {
                                    if (obj.ObjectId == target.ObjectId) { isTarget = true; break; }
                                }
                            }
                            if (!isTarget) startingSrfRefs.Add(obj);
                            else RhinoApp.WriteLine("Source surfaces cannot be one of the target objects.");
                        }
                        if (startingSrfRefs.Count == 0) startingSrfRefs = oldSrfs;
                    }
                    else
                    {
                        startingSrfRefs = oldSrfs; 
                    }
                    doc.Objects.UnselectAll();
                }
                else if (dialog.Action == DrapeDialogAction.AddRemoveStartingSurfaces)
                {
                    var oldSrfs = new List<ObjRef>(startingSrfRefs);
                    var goAddRem = new GetObject();
                    goAddRem.GeometryFilter = ObjectType.Surface;
                    goAddRem.SubObjectSelect = true;

                    if (startingSrfRefs.Count > 0)
                    {
                        doc.Objects.UnselectAll();
                        foreach (var s in startingSrfRefs) doc.Objects.Select(s.ObjectId);
                        doc.Views.Redraw();
                        
                        goAddRem.EnablePreSelect(true, true);
                        goAddRem.GetMultiple(1, 0); 
                    }
                    
                    goAddRem.SetCommandPrompt("Select source surfaces to add, or Ctrl+Click to remove (press Enter when done)");
                    goAddRem.EnablePreSelect(false, true); 
                    goAddRem.EnableClearObjectsOnEntry(false);
                    goAddRem.DeselectAllBeforePostSelect = false;
                    goAddRem.AcceptNothing(true);
                    
                    goAddRem.GetMultiple(1, 0);
                    
                    if (goAddRem.CommandResult() == Result.Success || goAddRem.CommandResult() == Result.Nothing)
                    {
                        var currentList = goAddRem.Objects().ToList();
                        if (currentList.Count == 0)
                        {
                            RhinoApp.WriteLine("No surfaces selected; reverting to previous selection.");
                            startingSrfRefs = oldSrfs;
                        }
                        else
                        {
                            startingSrfRefs.Clear();
                            foreach (var obj in currentList)
                            {
                                bool isTarget = false;
                                if (targetRefs != null)
                                {
                                    foreach (var target in targetRefs)
                                    {
                                        if (obj.ObjectId == target.ObjectId) { isTarget = true; break; }
                                    }
                                }
                                if (!isTarget) startingSrfRefs.Add(obj);
                                else RhinoApp.WriteLine("Source surfaces cannot be one of the target objects.");
                            }
                            if (startingSrfRefs.Count == 0) startingSrfRefs = oldSrfs;
                        }
                    }
                    else
                    {
                        startingSrfRefs = oldSrfs;
                    }
                    doc.Objects.UnselectAll();
                }
                else
                {
                    break;
                }
            }

            RhinoApp.SetCommandPrompt("");
            conduit.Enabled = false;
            doc.Views.Redraw();
            return Result.Success;
        }
    }
}