using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Rhino.UI;

namespace CADacombs.Commands.Modeling.Curves
{
    public class SimplifyCrvCommand : Command
    {
        public SimplifyCrvCommand() { Instance = this; }
        public static SimplifyCrvCommand Instance { get; private set; }
        public override string EnglishName => "spb_SimplifyCrv";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curves to simplify");
            go.GeometryFilter = ObjectType.Curve;
            go.SubObjectSelect = false;
            go.GroupSelect = true;
            
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            ObjRef[] objRefs = go.Objects();
            List<Curve> inputCurves = new List<Curve>();
            
            foreach (var objRef in objRefs)
            {
                Curve c = objRef.Curve();
                if (c != null) inputCurves.Add(c);
            }

            doc.Objects.UnselectAll();
            doc.Views.Redraw();

            var conduit = new SimplifyCrvConduit();
            conduit.Enabled = true;

            var dialog = new SimplifyCrvDialog(inputCurves, conduit);
            var parent = RhinoEtoApp.MainWindowForDocument(doc);
            
            dialog.ShowSemiModal(doc, parent);

            conduit.Enabled = false;
            doc.Views.Redraw();

            // FIX: Cancel if no options are checked, user clicked Cancel, OR if all deltas are 0
            if (!dialog.Result || !dialog.AnyOptionChecked || !dialog.HasChanges || dialog.ResultCurves == null)
            {
                return Result.Cancel;
            }

            int replacedCount = 0;
            for (int i = 0; i < objRefs.Length; i++)
            {
                if (i < dialog.ResultCurves.Count && dialog.ResultCurves[i] != null)
                {
                    if (doc.Objects.Replace(objRefs[i].ObjectId, dialog.ResultCurves[i]))
                    {
                        replacedCount++;
                    }
                }
            }
            
            if (replacedCount > 0)
            {
                RhinoApp.WriteLine($"Successfully simplified {replacedCount} curve(s).");
            }
            
            doc.Views.Redraw();
            return Result.Success;
        }
    }
}