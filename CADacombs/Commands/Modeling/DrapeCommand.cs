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
            if (doc.ModelUnitSystem != UnitSystem.Inches && DrapeOptions.SpanSpacing == 1.0)
                DrapeOptions.SpanSpacing = 25.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);

            List<ObjRef> targetRefs = new List<ObjRef>();
            List<ObjRef> startingSrfRefs = new List<ObjRef>();

            if (cmdMode == DrapeCommandMode.Project)
            {
                startingSrfRefs = PickCustomSurfaces(null, "Select the surfaces to project", true);
                if (startingSrfRefs == null || startingSrfRefs.Count == 0) return Result.Cancel;

                var goTargets = new GetObject();
                goTargets.SetCommandPrompt("Select surfaces, polysurfaces, SubDs and meshes to project onto");
                goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
                goTargets.EnablePreSelect(false, true); 
                goTargets.DeselectAllBeforePostSelect = false;

                if (mode == RunMode.Scripted)
                {
                    while (true)
                    {
                        if (!SetupAndProcessOptions(goTargets, out GetResult resTargets)) continue;
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
                    if (targetRefs.Count == 0) return Result.Cancel;
                    return DrapeLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
                }
                else
                {
                    goTargets.GetMultiple(1, 0);
                    if (goTargets.CommandResult() != Result.Success) return goTargets.CommandResult();
                    
                    foreach (var obj in goTargets.Objects())
                    {
                        bool isStartingSrf = false;
                        foreach (var startSrf in startingSrfRefs)
                        {
                            if (obj.ObjectId == startSrf.ObjectId) { isStartingSrf = true; break; }
                        }
                        if (!isStartingSrf) targetRefs.Add(obj);
                    }

                    if (targetRefs.Count == 0)
                    {
                        RhinoApp.WriteLine("No valid targets selected.");
                        return Result.Cancel;
                    }
                }
            }
            else
            {
                var goTargets = new GetObject();
                goTargets.SetCommandPrompt("Select target surfaces, polysurfaces, SubDs and meshes");
                goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
                goTargets.AcceptNumber(true, true);

                if (mode == RunMode.Scripted)
                {
                    while (true)
                    {
                        if (!SetupAndProcessOptions(goTargets, out GetResult resTargets)) continue;
                        if (resTargets == GetResult.Cancel) return Result.Cancel;
                        if (resTargets == GetResult.Object)
                        {
                            targetRefs.AddRange(goTargets.Objects());
                            break;
                        }
                    }
                    return DrapeLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
                }
                else
                {
                    goTargets.GetMultiple(1, 0);
                    if (goTargets.CommandResult() != Result.Success) return goTargets.CommandResult();
                    targetRefs.AddRange(goTargets.Objects());
                }
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
                        foreach (var ns in dialog.ResultSurfaces)
                        {
                            doc.Objects.AddSurface(ns);
                        }
                        
                        if (startingSrfRefs != null && DrapeOptions.DeleteStartingSrf && DrapeOptions.UserProvidesStartingSrf)
                        {
                            foreach (var srf in startingSrfRefs)
                            {
                                doc.Objects.Delete(srf.ObjectId, true);
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
                else if (dialog.Action == DrapeDialogAction.PickCustomSurface)
                {
                    var picked = PickCustomSurfaces(targetRefs, "Select custom starting surface", false);
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

        public static List<ObjRef> PickCustomSurfaces(List<ObjRef> currentTargets, string prompt = "Select custom starting surface(s)", bool allowPreSelect = false)
        {
            while (true)
            {
                var goSrf = new GetObject();
                goSrf.SetCommandPrompt(prompt);
                goSrf.GeometryFilter = ObjectType.Surface;
                if (!allowPreSelect) goSrf.DisablePreSelect();
                goSrf.SubObjectSelect = true; 

                var resSrf = goSrf.GetMultiple(1, 0);
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

        private static bool SetupAndProcessOptions(GetObject go, out GetResult res) { res = go.Get(); return true; }
    }
}