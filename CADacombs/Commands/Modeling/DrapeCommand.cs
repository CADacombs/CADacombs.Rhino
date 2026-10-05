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
                        
                        if (startingSrfRef != null && DrapeOptions.DeleteStartingSrf)
                            doc.Objects.Delete(startingSrfRef.ObjectId, true);
                            
                        commandResult = Result.Success;
                    }
                    break;
                }
                else if (dialog.Action == DrapeDialogAction.ReselectTargets)
                {
                    var goEdit = new GetObject();
                    goEdit.SetCommandPrompt("Reselect target breps or meshes");
                    goEdit.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
                    goEdit.GetMultiple(1, 0);
                    if (goEdit.CommandResult() == Result.Success)
                    {
                        targetRefs.Clear();
                        targetRefs.AddRange(goEdit.Objects());
                    }
                    doc.Objects.UnselectAll();
                }
                else if (dialog.Action == DrapeDialogAction.AddRemoveTargets)
                {
                    var goAddRem = new GetObject();
                    goAddRem.SetCommandPrompt("Select targets to add, or Ctrl+Click to remove (press Enter when done)");
                    goAddRem.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
                    
                    // The magic combo to allow active viewport modification without auto-accepting
                    goAddRem.EnablePreSelect(false, true);
                    goAddRem.EnableClearObjectsOnEntry(false);
                    goAddRem.DeselectAllBeforePostSelect = false;
                    goAddRem.AlreadySelectedObjectSelect = true;
                    
                    foreach (var t in targetRefs) doc.Objects.Select(t.ObjectId);
                    
                    goAddRem.GetMultiple(1, 0);
                    if (goAddRem.CommandResult() == Result.Success)
                    {
                        targetRefs.Clear();
                        targetRefs.AddRange(goAddRem.Objects());
                    }
                    doc.Objects.UnselectAll();
                    
                    if (targetRefs.Count == 0)
                    {
                        RhinoApp.WriteLine("All targets removed. Drape requires at least one target.");
                        var goResel = new GetObject();
                        goResel.SetCommandPrompt("Reselect target breps or meshes");
                        goResel.GeometryFilter = ObjectType.Brep | ObjectType.Mesh;
                        goResel.GetMultiple(1, 0);
                        if (goResel.CommandResult() == Result.Success)
                        {
                            targetRefs.Clear();
                            targetRefs.AddRange(goResel.Objects());
                        }
                    }
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
                else if (dialog.Action == DrapeDialogAction.ClearCustomSurface)
                {
                    startingSrfRef = null;
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