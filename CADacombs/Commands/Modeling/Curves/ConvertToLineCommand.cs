using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class ConvertToLineCommand : Command
    {
        public ConvertToLineCommand()
        {
            Instance = this;
        }

        public static ConvertToLineCommand Instance { get; private set; }

        public override string EnglishName => "spb_ConvertCrvToLine";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curves");
            go.GeometryFilter = ObjectType.Curve;

            // Custom filter to skip existing LineCurves
            go.SetCustomGeometryFilter((rhObj, geom, compIdx) =>
            {
                if (geom is BrepEdge edge)
                    return !(edge.DuplicateCurve() is LineCurve);
                return !(geom is LineCurve);
            });

            go.AlreadySelectedObjectSelect = true;
            go.DeselectAllBeforePostSelect = false;
            go.EnableClearObjectsOnEntry(false);
            go.EnableUnselectObjectsOnExit(false);
            go.AcceptNumber(true, true);

            while (true)
            {
                go.ClearCommandOptions();

                var optTolByRatio = new OptionToggle(ConvertToLineOptions.TolByRatio, "No", "Yes");
                var optTolRatio = new OptionDouble(ConvertToLineOptions.TolRatio, true, 1.0);
                var optDevTol = new OptionDouble(ConvertToLineOptions.DevTol);
                var optMinLen = new OptionDouble(ConvertToLineOptions.MinNewCrvLen);
                var optArcs = new OptionToggle(ConvertToLineOptions.ProcessArcs, "No", "Yes");
                var opt2PtNurbs = new OptionToggle(ConvertToLineOptions.Process2PtNurbs, "No", "Yes");
                var optReplace = new OptionToggle(ConvertToLineOptions.Replace, "Add", "Replace");
                var optEcho = new OptionToggle(ConvertToLineOptions.Echo, "No", "Yes");
                var optDebug = new OptionToggle(ConvertToLineOptions.Debug, "No", "Yes");

                int idxTolByRatio = go.AddOptionToggle("TolByRatio", ref optTolByRatio);
                
                int idxTolRatio = 0;
                int idxDevTol = 0;
                int idxONZeroTol = 0, idx1eN9 = 0, idx1eN6 = 0, idxDeci = 0, idxModelTol = 0, idxDeca = 0;

                if (ConvertToLineOptions.TolByRatio)
                {
                    idxTolRatio = go.AddOptionDouble("Ratio", ref optTolRatio);
                }
                else
                {
                    idxDevTol = go.AddOptionDouble("DevTol", ref optDevTol);
                    idxONZeroTol = go.AddOption("ONZeroTol");
                    idx1eN9 = go.AddOption("1eN9");
                    idx1eN6 = go.AddOption("1eN6");
                    idxDeci = go.AddOption("Deci");
                    idxModelTol = go.AddOption("ModelTol");
                    idxDeca = go.AddOption("Deca");
                }

                int idxMinLen = go.AddOptionDouble("MinLineLength", ref optMinLen);
                int idxArcs = go.AddOptionToggle("ProcessArcs", ref optArcs);
                int idx2PtNurbs = go.AddOptionToggle("Process2PtNurbs", ref opt2PtNurbs);
                int idxReplace = go.AddOptionToggle("Action", ref optReplace);
                int idxEcho = go.AddOptionToggle("Echo", ref optEcho);
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug);

                GetResult res = go.GetMultiple(1, 0);

                if (res == GetResult.Cancel)
                    return Result.Cancel;

                if (res == GetResult.Object)
                {
                    ObjRef[] objRefs = go.Objects();
                    go.Dispose();
                    return ConvertToLineLogic.Execute(doc, objRefs);
                }

                if (res == GetResult.Number)
                {
                    double num = go.Number();
                    if (num >= 0.0)
                        ConvertToLineOptions.DevTol = num;
                    continue;
                }

                if (res == GetResult.Option)
                {
                    var e = go.Option();
                    if (e.Index == idxTolByRatio) ConvertToLineOptions.TolByRatio = optTolByRatio.CurrentValue;
                    else if (e.Index == idxTolRatio) ConvertToLineOptions.TolRatio = optTolRatio.CurrentValue;
                    else if (e.Index == idxDevTol) ConvertToLineOptions.DevTol = optDevTol.CurrentValue;
                    else if (e.Index == idxONZeroTol) ConvertToLineOptions.DevTol = RhinoMath.ZeroTolerance;
                    else if (e.Index == idx1eN9) ConvertToLineOptions.DevTol = 1e-9;
                    else if (e.Index == idx1eN6) ConvertToLineOptions.DevTol = 1e-6;
                    else if (e.Index == idxDeci) ConvertToLineOptions.DevTol = 0.1 * doc.ModelAbsoluteTolerance;
                    else if (e.Index == idxModelTol) ConvertToLineOptions.DevTol = doc.ModelAbsoluteTolerance;
                    else if (e.Index == idxDeca) ConvertToLineOptions.DevTol = 10.0 * doc.ModelAbsoluteTolerance;
                    else if (e.Index == idxMinLen) ConvertToLineOptions.MinNewCrvLen = optMinLen.CurrentValue;
                    else if (e.Index == idxArcs) ConvertToLineOptions.ProcessArcs = optArcs.CurrentValue;
                    else if (e.Index == idx2PtNurbs) ConvertToLineOptions.Process2PtNurbs = opt2PtNurbs.CurrentValue;
                    else if (e.Index == idxReplace) ConvertToLineOptions.Replace = optReplace.CurrentValue;
                    else if (e.Index == idxEcho) ConvertToLineOptions.Echo = optEcho.CurrentValue;
                    else if (e.Index == idxDebug) ConvertToLineOptions.Debug = optDebug.CurrentValue;
                }
            }
        }
    }
}