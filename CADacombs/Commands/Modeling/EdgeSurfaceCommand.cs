using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class EdgeSrfCommand : Command
    {
        public EdgeSrfCommand()
        {
            Instance = this;
        }

        public static EdgeSrfCommand Instance { get; private set; }

        public override string EnglishName => "ccEdgeSrf";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            List<ObjRef> selectedRefs = new List<ObjRef>();
            List<int> continuities = new List<int>();

            while (true)
            {
                if (EdgeSrfOptions.ApplyContToNextCrv)
                {
                    selectedRefs.Clear();
                    continuities.Clear();

                    while (true)
                    {
                        var rc = GetInput_PerCrv(doc, selectedRefs, out ObjRef objRef, out int cont, out bool switchedMode);
                        
                        // --- BUG FIX: Remove highlights exactly like the Python script ---
                        foreach (var o in selectedRefs)
                        {
                            if (o.Edge() == null)
                                o.Object().Highlight(false);
                            else
                                o.Object().HighlightSubObject(o.GeometryComponentIndex, false);
                        }
                        // -----------------------------------------------------------------

                        if (rc == Result.Cancel) return Result.Cancel;
                        
                        if (switchedMode) 
                        {
                            doc.Objects.UnselectAll();
                            break; 
                        }

                        if (rc == Result.Success && objRef == null)
                        {
                            if (selectedRefs.Count < 2) return Result.Cancel;
                            break; 
                        }

                        if (objRef != null)
                        {
                            selectedRefs.Add(objRef);
                            continuities.Add(cont);
                            if (selectedRefs.Count == 4) break; 
                        }
                    }

                    if (!EdgeSrfOptions.ApplyContToNextCrv) continue; 
                    if (selectedRefs.Count >= 2) break; 
                }
                else
                {
                    var rc = GetInput_Global(doc, out ObjRef[] objRefs, out int cont, out bool switchedMode);
                    if (rc == Result.Cancel) return Result.Cancel;

                    if (switchedMode)
                    {
                        doc.Objects.UnselectAll();
                        continue;
                    }

                    if (rc == Result.Success && objRefs != null)
                    {
                        selectedRefs.AddRange(objRefs);
                        for (int i = 0; i < objRefs.Length; i++) continuities.Add(cont);
                        break; 
                    }
                }
            }

            // Execute the logic
            return EdgeSrfLogic.Execute(doc, selectedRefs, continuities);
        }

        private Result GetInput_Global(RhinoDoc doc, out ObjRef[] outRefs, out int outCont, out bool switchedMode)
        {
            outRefs = null;
            outCont = EdgeSrfOptions.Continuity;
            switchedMode = false;

            var go = new GetObject();
            go.GeometryFilter = ObjectType.Curve;
            go.GeometryAttributeFilter = GeometryAttributeFilter.OpenCurve;
            go.EnableUnselectObjectsOnExit(false);
            go.AcceptNumber(true, true);

            while (true)
            {
                if (!SetupOptions(go, out int idxCont, out int idxApply, out OptionToggle optApply, out OptionToggle optEcho, out OptionToggle optDebug)) return Result.Cancel;

                go.SetCommandPrompt($"Select 2, 3, or 4 open curves (G{EdgeSrfOptions.Continuity})");
                var res = go.GetMultiple(2, 4);

                if (res == GetResult.Cancel) return Result.Cancel;

                if (res == GetResult.Object)
                {
                    outRefs = go.Objects();
                    outCont = EdgeSrfOptions.Continuity;
                    return Result.Success;
                }

                if (ProcessOptionResult(go, res, idxCont, idxApply, optApply, optEcho, optDebug))
                {
                    switchedMode = true;
                    return Result.Success;
                }
            }
        }

        private Result GetInput_PerCrv(RhinoDoc doc, List<ObjRef> alreadySelected, out ObjRef outRef, out int outCont, out bool switchedMode)
        {
            outRef = null;
            outCont = EdgeSrfOptions.Continuity;
            switchedMode = false;

            var go = new GetObject();
            go.GeometryFilter = ObjectType.Curve;
            go.EnableUnselectObjectsOnExit(false);
            go.AcceptNothing(true);
            go.AcceptNumber(true, true);

            // Highlight already selected and prevent re-selection
            List<System.Guid> selectedIds = new List<System.Guid>();
            List<int> selectedEdgeIndices = new List<int>();

            foreach (var o in alreadySelected)
            {
                selectedIds.Add(o.ObjectId);
                if (o.Edge() == null)
                {
                    selectedEdgeIndices.Add(-1);
                    o.Object().Highlight(true);
                }
                else
                {
                    selectedEdgeIndices.Add(o.Edge().EdgeIndex);
                    o.Object().HighlightSubObject(o.GeometryComponentIndex, true);
                }
            }
            if (alreadySelected.Count > 0) doc.Views.Redraw();

            go.SetCustomGeometryFilter((rhObj, geom, compIdx) =>
            {
                if (!(geom is Curve c) || c.IsClosed) return false;
                if (compIdx.ComponentIndexType == ComponentIndexType.BrepEdge)
                {
                    for (int i = 0; i < selectedIds.Count; i++)
                    {
                        if (rhObj.Id == selectedIds[i] && compIdx.Index == selectedEdgeIndices[i]) return false;
                    }
                    return true;
                }
                return !selectedIds.Contains(rhObj.Id);
            });

            while (true)
            {
                if (!SetupOptions(go, out int idxCont, out int idxApply, out OptionToggle optApply, out OptionToggle optEcho, out OptionToggle optDebug)) return Result.Cancel;

                string prompt = alreadySelected.Count == 0 ? "Select first open curve" : "Select next open curve";
                go.SetCommandPrompt($"{prompt} (G{EdgeSrfOptions.Continuity})");
                
                var res = go.Get();

                if (res == GetResult.Cancel) return Result.Cancel;
                if (res == GetResult.Nothing) return Result.Success;

                if (res == GetResult.Object)
                {
                    outRef = go.Object(0);
                    outCont = EdgeSrfOptions.Continuity;
                    doc.Objects.UnselectAll();
                    return Result.Success;
                }

                if (ProcessOptionResult(go, res, idxCont, idxApply, optApply, optEcho, optDebug))
                {
                    switchedMode = true;
                    return Result.Success;
                }
            }
        }

        private bool SetupOptions(GetObject go, out int idxCont, out int idxApply, out OptionToggle optApply, out OptionToggle optEcho, out OptionToggle optDebug)
        {
            go.ClearCommandOptions();
            string[] contList = { "G0", "G1", "G2" };
            optApply = new OptionToggle(EdgeSrfOptions.ApplyContToNextCrv, "AllCrvs", "NextCrv");
            optEcho = new OptionToggle(EdgeSrfOptions.Echo, "No", "Yes");
            optDebug = new OptionToggle(EdgeSrfOptions.Debug, "No", "Yes");

            idxCont = go.AddOptionList("Continuity", contList, EdgeSrfOptions.Continuity);
            idxApply = go.AddOptionToggle("ApplyContTo", ref optApply);
            go.AddOptionToggle("Echo", ref optEcho);
            go.AddOptionToggle("Debug", ref optDebug);
            
            return true;
        }

        private bool ProcessOptionResult(GetObject go, GetResult res, int idxCont, int idxApply, OptionToggle optApply, OptionToggle optEcho, OptionToggle optDebug)
        {
            go.DeselectAllBeforePostSelect = false;
            go.EnablePreSelect(false, true);
            go.EnableClearObjectsOnEntry(false);

            if (res == GetResult.Number)
            {
                int val = (int)go.Number();
                if (val >= 0 && val <= 2) EdgeSrfOptions.Continuity = val;
                return false;
            }

            if (res == GetResult.Option)
            {
                var opt = go.Option();
                if (opt.Index == idxCont) EdgeSrfOptions.Continuity = opt.CurrentListOptionIndex;
                else if (opt.Index == idxApply) 
                {
                    EdgeSrfOptions.ApplyContToNextCrv = optApply.CurrentValue;
                    return true; // Signal mode switch
                }
                else if (opt.EnglishName == "Echo") EdgeSrfOptions.Echo = optEcho.CurrentValue;
                else if (opt.EnglishName == "Debug") EdgeSrfOptions.Debug = optDebug.CurrentValue;
            }
            return false;
        }
    }
}