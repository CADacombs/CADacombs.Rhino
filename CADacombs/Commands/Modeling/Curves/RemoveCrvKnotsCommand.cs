using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling.Curves
{
    public class RemoveCrvKnotsCommand : Command
    {
        public RemoveCrvKnotsCommand() { Instance = this; }
        public static RemoveCrvKnotsCommand Instance { get; private set; }
        
        public override string EnglishName => "ccRemoveCrvKnots";

        // Sticky Options
        private double _devTol = -1.0; // Will initialize to 0.1 * ModelAbsoluteTolerance on first run
        private int _preserveEnd = (int)KnotRemovalUtils.PreserveEndType.Tangency; // 0=None, 1=Tangency, 2=Curvature
        private bool _deleteInput = true;

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (_devTol < 0) _devTol = 0.1 * doc.ModelAbsoluteTolerance;

            var go = new GetObject();
            go.SetCommandPrompt("Select curves to optimize knots");
            go.GeometryFilter = ObjectType.Curve;
            go.GroupSelect = true;
            go.SubObjectSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);

            OptionDouble optDevTol = new OptionDouble(_devTol);
            OptionToggle optDelete = new OptionToggle(_deleteInput, "No", "Yes");
            string[] preserveOptions = { "None", "Tangency", "Curvature" };

            while (true)
            {
                go.ClearCommandOptions();
                
                int optTolIdx = go.AddOptionDouble("DevTol", ref optDevTol);
                int optPreserveIdx = go.AddOptionList("PreserveEnd", preserveOptions, _preserveEnd);
                int optDeleteIdx = go.AddOptionToggle("DeleteInput", ref optDelete);

                var res = go.GetMultiple(1, 0);

                if (res == Rhino.Input.GetResult.Cancel) return Result.Cancel;
                if (res == Rhino.Input.GetResult.Object) break;

                if (res == Rhino.Input.GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optTolIdx) _devTol = optDevTol.CurrentValue;
                    else if (opt.Index == optPreserveIdx) _preserveEnd = opt.CurrentListOptionIndex;
                    else if (opt.Index == optDeleteIdx) _deleteInput = optDelete.CurrentValue;
                }
            }

            int processedCount = 0;
            int totalKnotsRemoved = 0;
            double overallMaxDev = 0.0;
            List<Guid> newObjectIds = new List<Guid>();

            foreach (var objRef in go.Objects())
            {
                Curve crv = objRef.Curve();
                if (crv == null) continue;

                NurbsCurve ncIn = crv.ToNurbsCurve();
                if (ncIn == null) continue;

                int originalKnotCount = ncIn.Knots.Count;

                // Send the curve to our smart brute-force engine
                var result = KnotRemovalUtils.OptimizeKnots(ncIn, _devTol, (KnotRemovalUtils.PreserveEndType)_preserveEnd);

                if (result.OptimizedCurve != null && result.OptimizedCurve.Knots.Count < originalKnotCount)
                {
                    int removed = originalKnotCount - result.OptimizedCurve.Knots.Count;
                    totalKnotsRemoved += removed;
                    
                    if (result.MaxDeviation > overallMaxDev) 
                        overallMaxDev = result.MaxDeviation;

                    if (_deleteInput)
                    {
                        if (doc.Objects.Replace(objRef.ObjectId, result.OptimizedCurve))
                            processedCount++;
                    }
                    else
                    {
                        Guid id = doc.Objects.AddCurve(result.OptimizedCurve, objRef.Object().Attributes);
                        if (id != Guid.Empty)
                        {
                            newObjectIds.Add(id);
                            processedCount++;
                        }
                    }
                }
            }

            if (processedCount > 0)
            {
                int prec = doc.ModelDistanceDisplayPrecision;
                RhinoApp.WriteLine($"Optimized {processedCount} curve(s). Removed {totalKnotsRemoved} total knots. Max deviation: {FormatUtils.FormatDistance(overallMaxDev, prec)}.");
                
                if (!_deleteInput && newObjectIds.Count > 0)
                {
                    doc.Objects.UnselectAll();
                    foreach (Guid id in newObjectIds) doc.Objects.Select(id);
                }
            }
            else
            {
                RhinoApp.WriteLine("No knots could be removed within the specified tolerance.");
            }

            doc.Views.Redraw();
            return Result.Success;
        }
    }
}