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

            var goTargets = new GetObject();
            goTargets.SetCommandPrompt("Select target surfaces, polysurfaces, SubDs and meshes");
            goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
            goTargets.AcceptNumber(false, false);
            goTargets.DisablePreSelect(); 
            goTargets.GetMultiple(1, 0);

            if (goTargets.CommandResult() != Result.Success)
                return goTargets.CommandResult();

            var targetRefs = goTargets.Objects().ToList();

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