using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public enum DrapeCommandMode { Drape, Project }
    public enum DrapeDialogAction { None, Ok, Cancel, AddRemoveTargets, ReselectTargets, PickCustomSurface, AddRemoveStartingSurfaces }

    public class DrapeCommand : Command
    {
        public DrapeCommand() { Instance = this; }
        public static DrapeCommand Instance { get; private set; }
        public override string EnglishName => "ccDrape";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!DrapeOptions.HasRunBefore)
            {
                DrapeOptions.FitMethod = 0; 
                DrapeOptions.HasRunBefore = true;
            }

            return RunSharedCommand(doc, mode, DrapeCommandMode.Drape);
        }

        public static Result RunSharedCommand(RhinoDoc doc, RunMode mode, DrapeCommandMode cmdMode)
        {
            if (doc.RuntimeSerialNumber != DrapeOptions.LastDocSerialNumber)
            {
                DrapeOptions.Tolerance = 10.0 * doc.ModelAbsoluteTolerance;
                DrapeOptions.LastDocSerialNumber = doc.RuntimeSerialNumber;
            }

            if (doc.ModelUnitSystem != UnitSystem.Inches && DrapeOptions.SpanSpacing == 1.0)
                DrapeOptions.SpanSpacing = 25.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);

            List<ObjRef> targetRefs = new List<ObjRef>();
            List<ObjRef> startingSrfRefs = new List<ObjRef>();

            // Instantiate option trackers so values persist inside the command loops
            string[] missList = { "FixToStart", "LowestNeighbor", "Extrapolate" };
            string[] outList = { "Input", "Current", "TargetObject" };
            string[] projList = { "Greville", "ControlPoints" };
            string[] drapeList = { "Skirted", "Hugging" };

            var opDir = new OptionToggle(DrapeOptions.FlipCPlane, "CPlaneNegativeZ", "CPlanePositiveZ");
            var opStart = new OptionToggle(DrapeOptions.UserProvidesStartingSrf, "Create", "Select");
            var opSpan = new OptionDouble(DrapeOptions.SpanSpacing);
            var opBeyond = new OptionInteger(DrapeOptions.SpansBeyondEachSide);
            var opTol = new OptionDouble(DrapeOptions.Tolerance);

            if (cmdMode == DrapeCommandMode.Project)
            {
                startingSrfRefs = PickCustomSurfaces(null, "Select the surfaces to project", true, true, mode);
                if (startingSrfRefs == null || startingSrfRefs.Count == 0) return Result.Cancel;

                var goTargets = new GetObject();
                goTargets.SetCommandPrompt("Select surfaces, polysurfaces, SubDs and meshes to project onto");
                goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
                goTargets.EnablePreSelect(false, true); 
                goTargets.DeselectAllBeforePostSelect = false;

                while (true)
                {
                    SetupCommandOptions(goTargets, cmdMode, mode);

                    var resTargets = goTargets.GetMultiple(1, 0);

                    if (resTargets == GetResult.Option)
                    {
                        var opt = goTargets.Option();
                        string name = opt.EnglishName;

                        if (name == "ProjectMethod") DrapeOptions.FitMethod = opt.CurrentListOptionIndex + 2;
                        else if (name == "Tolerance") DrapeOptions.Tolerance = opTol.CurrentValue;
                        else if (name == "Direction") DrapeOptions.FlipCPlane = opDir.CurrentValue;
                        else if (name == "MissAction") DrapeOptions.TargetMisses = opt.CurrentListOptionIndex;
                        else if (name == "OutputLayer") DrapeOptions.OutputLayer = opt.CurrentListOptionIndex;
                        
                        continue;
                    }
                    if (resTargets == GetResult.Cancel) return Result.Cancel;
                    if (resTargets == GetResult.Object)
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

                if (mode == RunMode.Scripted)
                    return DrapeLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
            }
            else // DRAPE MODE
            {
                var goTargets = new GetObject();
                goTargets.SetCommandPrompt("Select target surfaces, polysurfaces, SubDs and meshes");
                goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
                goTargets.AcceptNumber(true, true);

                while (true)
                {
                    SetupCommandOptions(goTargets, cmdMode, mode);

                    var resTargets = goTargets.GetMultiple(1, 0);

                    if (resTargets == GetResult.Option)
                    {
                        var opt = goTargets.Option();
                        string name = opt.EnglishName;

                        if (name == "StartingSurface") DrapeOptions.UserProvidesStartingSrf = opStart.CurrentValue;
                        else if (name == "SpanSpacing") DrapeOptions.SpanSpacing = opSpan.CurrentValue;
                        else if (name == "SpansBeyond") DrapeOptions.SpansBeyondEachSide = opBeyond.CurrentValue;
                        else if (name == "DrapeMethod") DrapeOptions.FitMethod = opt.CurrentListOptionIndex;
                        else if (name == "Tolerance") DrapeOptions.Tolerance = opTol.CurrentValue;
                        else if (name == "Direction") DrapeOptions.FlipCPlane = opDir.CurrentValue;
                        else if (name == "MissAction") DrapeOptions.TargetMisses = opt.CurrentListOptionIndex;
                        else if (name == "OutputLayer") DrapeOptions.OutputLayer = opt.CurrentListOptionIndex;

                        continue;
                    }
                    else if (resTargets == GetResult.Number)
                    {
                        if (!DrapeOptions.UserProvidesStartingSrf)
                        {
                            DrapeOptions.SpanSpacing = goTargets.Number();
                            opSpan.CurrentValue = DrapeOptions.SpanSpacing;
                        }
                        else
                        {
                            RhinoApp.WriteLine("Numeric input ignored.");
                        }
                        continue;
                    }
                    if (resTargets == GetResult.Cancel) return Result.Cancel;
                    if (resTargets == GetResult.Object)
                    {
                        targetRefs.AddRange(goTargets.Objects());
                        break;
                    }
                }

                if (targetRefs.Count == 0)
                {
                    RhinoApp.WriteLine("No valid targets selected.");
                    return Result.Cancel;
                }

                if (DrapeOptions.UserProvidesStartingSrf)
                {
                    startingSrfRefs = PickCustomSurfaces(targetRefs, "Select custom starting surface(s)", false, false, mode);
                    if (startingSrfRefs == null || startingSrfRefs.Count == 0) return Result.Cancel;
                }

                if (mode == RunMode.Scripted)
                    return DrapeLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
            }

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            var conduit = new DrapeConduit { Enabled = true };
            Result commandResult = Result.Cancel;

            while (true)
            {
                RhinoApp.SetCommandPrompt("Continue in dialog");
                
                var dialog = new DrapeDialog(targetRefs.ToArray(), startingSrfRefs, conduit);
                var parent = RhinoEtoApp.MainWindowForDocument(doc);
                dialog.ShowSemiModal(doc, parent);

                if (dialog.Action == DrapeDialogAction.Ok)
                {
                    if (dialog.ResultSurfaces != null && dialog.ResultSurfaces.Count > 0)
                    {
                        for (int i = 0; i < dialog.ResultSurfaces.Count; i++)
                        {
                            var ns = dialog.ResultSurfaces[i];
                            if (ns == null) continue;

                            bool isSelect = DrapeOptions.UserProvidesStartingSrf && startingSrfRefs != null && i < startingSrfRefs.Count;
                            bool doReplace = isSelect && DrapeOptions.DeleteStartingSrf;

                            if (doReplace)
                            {
                                Guid idToReplace = startingSrfRefs[i].ObjectId;
                                doc.Objects.Replace(idToReplace, ns);
                                
                                if (DrapeOptions.OutputLayer != 0) 
                                {
                                    var rhObj = doc.Objects.FindId(idToReplace);
                                    if (rhObj != null)
                                    {
                                        var modAttr = rhObj.Attributes.Duplicate();
                                        if (DrapeOptions.OutputLayer == 1)
                                        {
                                            modAttr.LayerIndex = doc.Layers.CurrentLayerIndex;
                                            modAttr.ColorSource = ObjectColorSource.ColorFromLayer;
                                        }
                                        else if (DrapeOptions.OutputLayer == 2 && targetRefs != null && targetRefs.Count > 0 && targetRefs[0].Object() != null)
                                        {
                                            var tgtAttr = targetRefs[0].Object().Attributes;
                                            modAttr.LayerIndex = tgtAttr.LayerIndex;
                                            modAttr.ColorSource = tgtAttr.ColorSource;
                                            modAttr.ObjectColor = tgtAttr.ObjectColor;
                                        }
                                        doc.Objects.ModifyAttributes(rhObj, modAttr, true);
                                    }
                                }
                            }
                            else
                            {
                                var attr = new ObjectAttributes();
                                if (DrapeOptions.OutputLayer == 0 && isSelect && startingSrfRefs != null && i < startingSrfRefs.Count && startingSrfRefs[i].Object() != null)
                                {
                                    var srcAttr = startingSrfRefs[i].Object().Attributes;
                                    attr.LayerIndex = srcAttr.LayerIndex;
                                    attr.ColorSource = srcAttr.ColorSource;
                                    attr.ObjectColor = srcAttr.ObjectColor;
                                }
                                else if (DrapeOptions.OutputLayer == 2 && targetRefs != null && targetRefs.Count > 0 && targetRefs[0].Object() != null)
                                {
                                    var tgtAttr = targetRefs[0].Object().Attributes;
                                    attr.LayerIndex = tgtAttr.LayerIndex;
                                    attr.ColorSource = tgtAttr.ColorSource;
                                    attr.ObjectColor = tgtAttr.ObjectColor;
                                }
                                else
                                {
                                    attr.LayerIndex = doc.Layers.CurrentLayerIndex;
                                    attr.ColorSource = ObjectColorSource.ColorFromLayer;
                                }

                                doc.Objects.AddSurface(ns, attr);
                            }
                        }
                        commandResult = Result.Success;
                    }
                    break;
                }
                else if (dialog.Action == DrapeDialogAction.ReselectTargets)
                {
                    var oldTargets = new List<ObjRef>(targetRefs);
                    var goEdit = new GetObject();
                    
                    goEdit.SetCommandPrompt(cmdMode == DrapeCommandMode.Project ? "Reselect surfaces, polysurfaces, SubDs and meshes to project onto" : "Reselect target surfaces, polysurfaces, SubDs and meshes");
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
                else if (dialog.Action == DrapeDialogAction.AddRemoveStartingSurfaces)
                {
                    var oldSrfs = startingSrfRefs == null ? new List<ObjRef>() : new List<ObjRef>(startingSrfRefs);
                    var goAddRem = new GetObject();
                    goAddRem.GeometryFilter = ObjectType.Surface;
                    goAddRem.SubObjectSelect = true;

                    if (startingSrfRefs != null && startingSrfRefs.Count > 0)
                    {
                        doc.Objects.UnselectAll();
                        foreach (var s in startingSrfRefs) doc.Objects.Select(s.ObjectId);
                        doc.Views.Redraw();
                        
                        goAddRem.EnablePreSelect(true, true);
                        goAddRem.GetMultiple(1, 0); 
                    }
                    
                    goAddRem.SetCommandPrompt("Select custom starting surface(s) to add, or Ctrl+Click to remove (press Enter when done)");
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
                                else RhinoApp.WriteLine("Starting surfaces cannot be one of the target objects.");
                            }
                            if (startingSrfRefs.Count == 0) startingSrfRefs = oldSrfs;
                        }
                    }
                    else
                    {
                        startingSrfRefs = oldSrfs;
                    }
                    doc.Objects.UnselectAll();
                    DrapeOptions.UserProvidesStartingSrf = true;
                }
                else if (dialog.Action == DrapeDialogAction.PickCustomSurface)
                {
                    var picked = PickCustomSurfaces(targetRefs, "Select custom starting surface(s)", false, false, RunMode.Interactive);
                    if (picked != null && picked.Count > 0)
                    {
                        startingSrfRefs = picked;
                        DrapeOptions.UserProvidesStartingSrf = true; 
                    }
                }
                else
                {
                    commandResult = Result.Cancel;
                    break;
                }
            }

            RhinoApp.SetCommandPrompt("");
            conduit.Enabled = false;
            doc.Views.Redraw();
            return commandResult;
        }

        private static void SetupCommandOptions(GetObject go, DrapeCommandMode cmdMode, RunMode mode)
        {
            go.ClearCommandOptions();
            
            if (mode != RunMode.Scripted) return;

            string[] missList = { "FixToStart", "LowestNeighbor", "Extrapolate" };
            string[] outList = { "Input", "Current", "TargetObject" };
            string[] projList = { "Greville", "ControlPoints" };
            string[] drapeList = { "Skirted", "Hugging" };

            var opDir = new OptionToggle(DrapeOptions.FlipCPlane, "CPlaneNegativeZ", "CPlanePositiveZ");
            var opStart = new OptionToggle(DrapeOptions.UserProvidesStartingSrf, "Create", "Select");
            var opSpan = new OptionDouble(DrapeOptions.SpanSpacing);
            var opBeyond = new OptionInteger(DrapeOptions.SpansBeyondEachSide);
            var opTol = new OptionDouble(DrapeOptions.Tolerance);

            if (cmdMode == DrapeCommandMode.Project)
            {
                go.AddOptionList("ProjectMethod", projList, DrapeOptions.FitMethod >= 2 ? DrapeOptions.FitMethod - 2 : 0);
            }
            else
            {
                go.AddOptionToggle("StartingSurface", ref opStart);
                if (!DrapeOptions.UserProvidesStartingSrf)
                {
                    go.AddOptionDouble("SpanSpacing", ref opSpan);
                    go.AddOptionInteger("SpansBeyond", ref opBeyond);
                }
                go.AddOptionList("DrapeMethod", drapeList, DrapeOptions.FitMethod <= 1 ? DrapeOptions.FitMethod : 0);
            }

            go.AddOptionDouble("Tolerance", ref opTol);
            go.AddOptionToggle("Direction", ref opDir);
            go.AddOptionList("MissAction", missList, DrapeOptions.TargetMisses);
            go.AddOptionList("OutputLayer", outList, DrapeOptions.OutputLayer);
        }

        public static List<ObjRef> PickCustomSurfaces(List<ObjRef> currentTargets, string prompt, bool allowPreSelect, bool isProjectMode, RunMode mode)
        {
            var goSrf = new GetObject();
            goSrf.SetCommandPrompt(prompt);
            goSrf.GeometryFilter = ObjectType.Surface;
            if (!allowPreSelect) goSrf.DisablePreSelect();
            goSrf.SubObjectSelect = true; 

            while (true)
            {
                goSrf.ClearCommandOptions();
                var opDelete = new OptionToggle(DrapeOptions.DeleteStartingSrf, "No", "Yes");
                var opFlatten = new OptionToggle(DrapeOptions.FlattenStartingSrf, "No", "Yes");

                if (mode == RunMode.Scripted)
                {
                    goSrf.AddOptionToggle("DeleteInput", ref opDelete);
                    if (!isProjectMode) goSrf.AddOptionToggle("FlattenStartingSrf", ref opFlatten);
                }

                var resSrf = goSrf.GetMultiple(1, 0);

                if (resSrf == GetResult.Option)
                {
                    var opt = goSrf.Option();
                    if (opt.EnglishName == "DeleteInput") DrapeOptions.DeleteStartingSrf = opDelete.CurrentValue;
                    else if (opt.EnglishName == "FlattenStartingSrf") DrapeOptions.FlattenStartingSrf = opFlatten.CurrentValue;
                    continue;
                }

                if (resSrf == GetResult.Cancel) return null;

                if (resSrf == GetResult.Object)
                {
                    var srfRefs = goSrf.Objects().ToList();
                    RhinoDoc.ActiveDoc.Objects.UnselectAll(); 

                    bool isTarget = false;
                    if (currentTargets != null)
                    {
                        foreach (var srfRef in srfRefs)
                        {
                            foreach (var t in currentTargets)
                            {
                                if (srfRef.ObjectId == t.ObjectId) { isTarget = true; break; }
                            }
                            if (isTarget) break;
                        }
                    }

                    if (isTarget)
                    {
                        RhinoApp.WriteLine("Starting surfaces cannot be one of the target objects.");
                        continue;
                    }

                    bool allValid = true;
                    foreach(var srfRef in srfRefs)
                    {
                        Surface srf = srfRef.Surface();
                        if (srf == null && srfRef.Brep()?.Faces.Count == 1)
                            srf = srfRef.Brep().Faces[0].UnderlyingSurface();

                        if (srf == null)
                        {
                            allValid = false;
                            break;
                        }
                    }

                    if (!allValid)
                    {
                        RhinoApp.WriteLine("Valid surface not found in selection.");
                        continue;
                    }
                    
                    return srfRefs;
                }
            }
        }
    }
}