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
    public class ConvertToArcCommand : Command
    {
        public ConvertToArcCommand()
        {
            Instance = this;
        }

        public static ConvertToArcCommand Instance { get; private set; }

        public override string EnglishName => "ccConvertCrvToArc";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curves"); //[cite: 7]
            go.GeometryFilter = ObjectType.Curve;

            // Custom filter to skip existing ArcCurves[cite: 7]
            go.SetCustomGeometryFilter((rhObj, geom, compIdx) =>
            {
                if (geom is BrepEdge edge)
                    return !(edge.DuplicateCurve() is ArcCurve); //[cite: 7]
                return !(geom is ArcCurve); //[cite: 7]
            });

            go.AlreadySelectedObjectSelect = true; //[cite: 7]
            go.DeselectAllBeforePostSelect = false; //[cite: 7]
            go.EnableClearObjectsOnEntry(false); //[cite: 7]
            go.EnableUnselectObjectsOnExit(false); //[cite: 7]
            go.AcceptNumber(true, true); //[cite: 7]

            while (true)
            {
                go.ClearCommandOptions(); //[cite: 7]

                var optTolByRatio = new OptionToggle(ConvertToArcOptions.TolByRatio, "No", "Yes"); //[cite: 7]
                var optTolRatio = new OptionDouble(ConvertToArcOptions.TolRatio, true, 1.0); //[cite: 7]
                var optDevTol = new OptionDouble(ConvertToArcOptions.DevTol); //[cite: 7]
                var optTanTol = new OptionDouble(ConvertToArcOptions.TanTol); //[cite: 7]
                var optMinLen = new OptionDouble(ConvertToArcOptions.MinNewCrvLen, true, 0.0); //[cite: 7]
                var optMaxRad = new OptionDouble(ConvertToArcOptions.MaxRadius, true, doc.ModelAbsoluteTolerance); //[cite: 7]
                var optReplace = new OptionToggle(ConvertToArcOptions.Replace, "No", "Yes"); //[cite: 7]
                var optEcho = new OptionToggle(ConvertToArcOptions.Echo, "No", "Yes"); //[cite: 7]
                var optDebug = new OptionToggle(ConvertToArcOptions.Debug, "No", "Yes"); //[cite: 7]

                int idxTolByRatio = go.AddOptionToggle("TolByRatio", ref optTolByRatio); //[cite: 7]
                
                int idxTolRatio = 0;
                int idxDevTol = 0;
                int idxRh0 = 0, idx1eN9 = 0, idx1eN6 = 0, idxDeci = 0, idxModelTol = 0, idxDeca = 0;

                if (ConvertToArcOptions.TolByRatio) //[cite: 7]
                {
                    idxTolRatio = go.AddOptionDouble("Ratio", ref optTolRatio); //[cite: 7]
                }
                else
                {
                    idxDevTol = go.AddOptionDouble("DevTol", ref optDevTol); //[cite: 7]
                    idxRh0 = go.AddOption("Rh0"); //[cite: 7]
                    idx1eN9 = go.AddOption("1eN9"); //[cite: 7]
                    idx1eN6 = go.AddOption("1eN6"); //[cite: 7]
                    idxDeci = go.AddOption("Deci"); //[cite: 7]
                    idxModelTol = go.AddOption("ModelTol"); //[cite: 7]
                    idxDeca = go.AddOption("Deca"); //[cite: 7]
                }

                int idxTanTol = go.AddOptionDouble("TanTol", ref optTanTol); //[cite: 7]
                int idxMinLen = go.AddOptionDouble("MinArcLength", ref optMinLen); //[cite: 7]
                int idxMaxRad = go.AddOptionDouble("MaxRadius", ref optMaxRad); //[cite: 7]
                int idxReplace = go.AddOptionToggle("Replace", ref optReplace); //[cite: 7]
                int idxEcho = go.AddOptionToggle("Echo", ref optEcho); //[cite: 7]
                int idxDebug = go.AddOptionToggle("Debug", ref optDebug); //[cite: 7]

                GetResult res = go.GetMultiple(1, 0); //[cite: 7]

                if (res == GetResult.Cancel)
                    return Result.Cancel;

                if (res == GetResult.Object) //[cite: 7]
                {
                    ObjRef[] objRefs = go.Objects();
                    go.Dispose();
                    return ConvertToArcLogic.Execute(doc, objRefs);
                }

                if (res == GetResult.Number) //[cite: 7]
                {
                    double num = go.Number();
                    if (num >= 0.0)
                        ConvertToArcOptions.DevTol = num;
                    continue;
                }

                if (res == GetResult.Option)
                {
                    var e = go.Option();
                    if (e.Index == idxTolByRatio) ConvertToArcOptions.TolByRatio = optTolByRatio.CurrentValue;
                    else if (e.Index == idxTolRatio) ConvertToArcOptions.TolRatio = optTolRatio.CurrentValue;
                    else if (e.Index == idxDevTol) ConvertToArcOptions.DevTol = optDevTol.CurrentValue;
                    else if (e.Index == idxRh0) ConvertToArcOptions.DevTol = RhinoMath.ZeroTolerance; //[cite: 7]
                    else if (e.Index == idx1eN9) ConvertToArcOptions.DevTol = 1e-9; //[cite: 7]
                    else if (e.Index == idx1eN6) ConvertToArcOptions.DevTol = 1e-6; //[cite: 7]
                    else if (e.Index == idxDeci) ConvertToArcOptions.DevTol = 0.1 * doc.ModelAbsoluteTolerance; //[cite: 7]
                    else if (e.Index == idxModelTol) ConvertToArcOptions.DevTol = doc.ModelAbsoluteTolerance; //[cite: 7]
                    else if (e.Index == idxDeca) ConvertToArcOptions.DevTol = 10.0 * doc.ModelAbsoluteTolerance; //[cite: 7]
                    else if (e.Index == idxTanTol) ConvertToArcOptions.TanTol = optTanTol.CurrentValue;
                    else if (e.Index == idxMinLen) ConvertToArcOptions.MinNewCrvLen = optMinLen.CurrentValue;
                    else if (e.Index == idxMaxRad) ConvertToArcOptions.MaxRadius = optMaxRad.CurrentValue;
                    else if (e.Index == idxReplace) ConvertToArcOptions.Replace = optReplace.CurrentValue;
                    else if (e.Index == idxEcho) ConvertToArcOptions.Echo = optEcho.CurrentValue;
                    else if (e.Index == idxDebug) ConvertToArcOptions.Debug = optDebug.CurrentValue;
                }
            }
        }
    }
}