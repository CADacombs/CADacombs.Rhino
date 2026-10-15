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
    public class FitSrfToTargetCommand : Command
    {
        public FitSrfToTargetCommand() { Instance = this; }
        public static FitSrfToTargetCommand Instance { get; private set; }
        public override string EnglishName => "ccFitSrfToTarget";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!FitSrfToTargetOptions.HasRunBefore)
            {
                FitSrfToTargetOptions.HasRunBefore = true;
            }

            if (doc.RuntimeSerialNumber != FitSrfToTargetOptions.LastDocSerialNumber)
            {
                FitSrfToTargetOptions.Tolerance = 10.0 * doc.ModelAbsoluteTolerance;
                FitSrfToTargetOptions.LastDocSerialNumber = doc.RuntimeSerialNumber;
            }

            // 1. Select Targets
            var goTargets = new GetObject();
            goTargets.SetCommandPrompt("Select target surfaces, polysurfaces, SubDs and meshes");
            goTargets.GeometryFilter = ObjectType.Brep | ObjectType.Mesh | ObjectType.SubD;
            goTargets.AcceptNumber(false, false);
            goTargets.GetMultiple(1, 0);

            if (goTargets.CommandResult() != Result.Success)
                return goTargets.CommandResult();

            var targetRefs = goTargets.Objects().ToList();

            // 2. Select Starting Surfaces
            var goSrf = new GetObject();
            goSrf.SetCommandPrompt("Select custom starting surface(s) to fit to targets");
            goSrf.GeometryFilter = ObjectType.Surface;
            goSrf.SubObjectSelect = true;
            goSrf.DisablePreSelect();
            goSrf.GetMultiple(1, 0);

            if (goSrf.CommandResult() != Result.Success)
                return goSrf.CommandResult();

            var startingSrfRefs = goSrf.Objects().ToList();

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            // 3. Launch Dialog or Scripted Execution
            if (mode == RunMode.Scripted)
            {
                return FitSrfToTargetLogic.ExecuteBake(doc, targetRefs.ToArray(), startingSrfRefs);
            }

            var conduit = new DrapeConduit { Enabled = true };

            while (true)
            {
                RhinoApp.SetCommandPrompt("Continue in dialog");
                
                var dialog = new FitSrfToTargetDialog(targetRefs.ToArray(), startingSrfRefs, conduit);
                var parent = Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc);
                dialog.ShowSemiModal(doc, parent);

                if (dialog.Action == DrapeDialogAction.Ok)
                {
                    if (dialog.ResultSurfaces != null && dialog.ResultSurfaces.Count > 0)
                    {
                        FitSrfToTargetLogic.BakeResults(doc, dialog.ResultSurfaces, startingSrfRefs, targetRefs.ToArray());
                    }
                    break;
                }
                else if (dialog.Action == DrapeDialogAction.ReselectTargets)
                {
                    // Basic reselect logic
                    goTargets.EnablePreSelect(false, true);
                    goTargets.GetMultiple(1, 0);
                    if (goTargets.CommandResult() == Result.Success)
                        targetRefs = goTargets.Objects().ToList();
                }
                else if (dialog.Action == DrapeDialogAction.PickCustomSurface)
                {
                    goSrf.EnablePreSelect(false, true);
                    goSrf.GetMultiple(1, 0);
                    if (goSrf.CommandResult() == Result.Success)
                        startingSrfRefs = goSrf.Objects().ToList();
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