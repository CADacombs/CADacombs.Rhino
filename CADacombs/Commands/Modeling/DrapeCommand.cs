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
    public class DrapeCommand : Command
    {
        public DrapeCommand() { Instance = this; }
        public static DrapeCommand Instance { get; private set; }
        public override string EnglishName => "ccDrape";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (doc.ModelUnitSystem != UnitSystem.Inches && DrapeOptions.SpanSpacing == 1.0)
                DrapeOptions.SpanSpacing = 25.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);

            List<ObjRef> targetRefs = new List<ObjRef>();
            ObjRef startingSrfRef = null;

            // 1. INITIAL TARGET SELECTION
            var goTargets = new GetObject();
            goTargets.SetCommandPrompt("Select target breps or meshes");
            goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
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
                return DrapeLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRef);
            }
            else
            {
                goTargets.GetMultiple(1, 0);
                if (goTargets.CommandResult() != Result.Success) return goTargets.CommandResult();
                targetRefs.AddRange(goTargets.Objects());
            }

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            var conduit = new DrapeConduit { Enabled = true };
            Result commandResult = Result.Cancel;

            // 2. INTERACTIVE DIALOG LOOP
            while (true)
            {
                RhinoApp.SetCommandPrompt("Continue in dialog");
                
                var dialog = new DrapeDialog(targetRefs.ToArray(), startingSrfRef, conduit);
                var parent = RhinoEtoApp.MainWindowForDocument(doc);
                dialog.ShowSemiModal(doc, parent);

                if (dialog.Action == DrapeDialogAction.Ok)
                {
                    if (dialog.ResultSurface != null)
                    {
                        doc.Objects.AddSurface(dialog.ResultSurface);
                        
                        if (startingSrfRef != null && DrapeOptions.DeleteStartingSrf && DrapeOptions.UserProvidesStartingSrf)
                            doc.Objects.Delete(startingSrfRef.ObjectId, true);
                            
                        commandResult = Result.Success;
                    }
                    break;
                }
                else if (dialog.Action == DrapeDialogAction.ReselectTargets)
                {
                    var oldTargets = new List<ObjRef>(targetRefs);
                    var goEdit = new GetObject();
                    goEdit.SetCommandPrompt("Reselect target breps or meshes");
                    goEdit.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
                    goEdit.GetMultiple(1, 0);
                    
                    if (goEdit.CommandResult() == Result.Success)
                    {
                        targetRefs.Clear();
                        targetRefs.AddRange(goEdit.Objects());
                    }
                    else
                    {
                        targetRefs = oldTargets; // Safely revert on cancel
                    }
                    doc.Objects.UnselectAll();
                }
                else if (dialog.Action == DrapeDialogAction.AddRemoveTargets)
                {
                    var oldTargets = new List<ObjRef>(targetRefs);
                    var goAddRem = new GetObject();
                    goAddRem.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;

                    // STEP 1: Auto-Load the fresh GetObject instance with the current targets
                    if (targetRefs.Count > 0)
                    {
                        doc.Objects.UnselectAll();
                        foreach (var t in targetRefs) doc.Objects.Select(t.ObjectId);
                        doc.Views.Redraw();
                        
                        goAddRem.EnablePreSelect(true, true);
                        goAddRem.GetMultiple(1, 0); // This instantly returns, loading the objects into memory
                    }
                    
                    // STEP 2: Let the user natively interact with the loaded instance
                    goAddRem.SetCommandPrompt("Select targets to add, or Ctrl+Click to remove (press Enter when done)");
                    goAddRem.EnablePreSelect(false, true); // Do not auto-accept this time
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
                            targetRefs.AddRange(currentList);
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
                    var picked = PickCustomSurface(targetRefs);
                    if (picked != null)
                    {
                        startingSrfRef = picked;
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

        private ObjRef PickCustomSurface(List<ObjRef> currentTargets)
        {
            while (true)
            {
                var goSrf = new GetObject();
                goSrf.SetCommandPrompt("Select custom starting surface");
                goSrf.GeometryFilter = ObjectType.Surface;
                goSrf.DisablePreSelect();
                goSrf.SubObjectSelect = true; 

                var resSrf = goSrf.Get();
                if (resSrf == GetResult.Cancel) return null;

                if (resSrf == GetResult.Object)
                {
                    ObjRef srfRef = goSrf.Object(0);
                    RhinoDoc.ActiveDoc.Objects.UnselectAll();

                    bool isTarget = false;
                    if (currentTargets != null)
                    {
                        foreach (var t in currentTargets)
                        {
                            if (srfRef.ObjectId == t.ObjectId) { isTarget = true; break; }
                        }
                    }

                    if (isTarget)
                    {
                        RhinoApp.WriteLine("Starting surface cannot be one of the target objects.");
                        continue;
                    }

                    Surface srf = srfRef.Surface();
                    if (srf == null && srfRef.Brep()?.Faces.Count == 1)
                        srf = srfRef.Brep().Faces[0].UnderlyingSurface();

                    if (srf == null || !IsStartingSrfSupported(srf.ToNurbsSurface()))
                    {
                        RhinoApp.WriteLine("Starting surface must be an open, degree-3 NURBS with only multiplicity-of-1 interior knots.");
                        continue;
                    }
                    return srfRef;
                }
            }
        }

        private bool SetupAndProcessOptions(GetObject go, out GetResult res) { res = go.Get(); return true; }
        
        private bool IsStartingSrfSupported(NurbsSurface ns)
        {
            if (ns == null) return false;
            if (ns.Degree(0) != 3 || ns.Degree(1) != 3) return false;
            if (ns.IsClosed(0) || ns.IsClosed(1)) return false;

            for (int iDir = 0; iDir < 2; iDir++)
            {
                var knots = iDir == 1 ? ns.KnotsV : ns.KnotsU;
                int degree = ns.Degree(iDir);
                int iK = ns.IsPeriodic(iDir) ? 0 : degree;
                int count = ns.IsPeriodic(iDir) ? knots.Count : knots.Count - degree;
                
                while (iK < count)
                {
                    DrapeLogic.CheckEscape();
                    if (knots.KnotMultiplicity(iK) > 1) return false;
                    iK++;
                }
            }
            return true;
        }
    }
}