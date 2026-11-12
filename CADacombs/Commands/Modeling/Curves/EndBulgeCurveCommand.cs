using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling.Curves
{
    public class EndBulgeCrvCommand : Command
    {
        public EndBulgeCrvCommand()
        {
            Instance = this;
        }

        public static EndBulgeCrvCommand Instance { get; private set; }

        public override string EnglishName => "ccEndBulgeCrv";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Load persistent settings before creating options toggles
            EndBulgeOptions.Load(this.Settings);

            bool isInteractive = (mode == RunMode.Interactive);
            EndBulgeOptions.Dialog = isInteractive;

            var go = new GetObject();
            go.DisablePreSelect();
            go.AcceptNumber(true, true);
            go.SetCommandPrompt("Select curve to adjust");
            go.GeometryFilter = ObjectType.Curve;
            go.GeometryAttributeFilter = GeometryAttributeFilter.AcceptAllAttributes;

            go.SetCustomGeometryFilter((rhObject, geom, compIdx) =>
            {
                Curve rgC = geom as Curve;
                if (rgC == null) return false;

                NurbsCurve nc = rgC as NurbsCurve;
                if (nc == null && rgC is PolyCurve pc)
                {
                    if (pc.SegmentCount > 1)
                    {
                        RhinoApp.WriteLine("PolyCurve with multiple segments is ignored.");
                        return false;
                    }
                    nc = pc.ToNurbsCurve();
                }
                
                if (nc == null) return false;
                if (nc.IsPeriodic)
                {
                    RhinoApp.WriteLine("Periodic curves are not supported.");
                    return false;
                }
                if (nc.Degree == 1)
                {
                    RhinoApp.WriteLine("Ignored degree 1 NURBS curve.");
                    return false;
                }

                return true;
            });

            var optDialog = new OptionToggle(EndBulgeOptions.Dialog, "No", "Yes");
            var optLinked = new OptionToggle(EndBulgeOptions.LinkedEnds, "Independent", "Linked");
            var optDelete = new OptionToggle(EndBulgeOptions.DeleteInput, "No", "Yes");
            var optEcho = new OptionToggle(EndBulgeOptions.Echo, "No", "Yes");

            var optScaleP = new OptionDouble(EndBulgeOptions.ScalePicked);
            var optSlide2P = new OptionDouble(EndBulgeOptions.SlideG2Picked);
            var optSlide3P = new OptionDouble(EndBulgeOptions.SlideG3Picked);
            var optScaleO = new OptionDouble(EndBulgeOptions.ScaleOpp);
            var optSlide2O = new OptionDouble(EndBulgeOptions.SlideG2Opp);
            var optSlide3O = new OptionDouble(EndBulgeOptions.SlideG3Opp);

            string[] contList = { "None", "G0", "G1", "G2", "G3" };
            ObjRef objref_In = null;

            while (true)
            {
                go.ClearCommandOptions();
                
                optScaleP.CurrentValue = EndBulgeOptions.ScalePicked;
                optSlide2P.CurrentValue = EndBulgeOptions.SlideG2Picked;
                optSlide3P.CurrentValue = EndBulgeOptions.SlideG3Picked;
                optScaleO.CurrentValue = EndBulgeOptions.ScaleOpp;
                optSlide2O.CurrentValue = EndBulgeOptions.SlideG2Opp;
                optSlide3O.CurrentValue = EndBulgeOptions.SlideG3Opp;

                int idxDialog = 0, idxLinked = 0, idxContP = 0, idxContO = 0;
                int idxScaleP = 0, idxSlide2P = 0, idxSlide3P = 0, idxScaleO = 0, idxSlide2O = 0, idxSlide3O = 0;
                int idxDelete = 0, idxEcho = 0;

                if (!isInteractive)
                {
                    idxDialog = go.AddOptionToggle("Dialog", ref optDialog);
                }

                if (!EndBulgeOptions.Dialog)
                {
                    idxLinked = go.AddOptionToggle("AdjustEnds", ref optLinked);
                    idxContP = go.AddOptionList("PickedEnd", contList, EndBulgeOptions.ContinuityPicked);
                    
                    if (!EndBulgeOptions.LinkedEnds)
                    {
                        idxContO = go.AddOptionList("OppEnd", contList, EndBulgeOptions.ContinuityOpp);
                    }
                    
                    if (EndBulgeOptions.LinkedEnds)
                    {
                        idxScaleP = go.AddOptionDouble("Scale", ref optScaleP);
                        idxSlide2P = go.AddOptionDouble("G2Slide", ref optSlide2P);
                        idxSlide3P = go.AddOptionDouble("G3Slide", ref optSlide3P);
                    }
                    else
                    {
                        idxScaleP = go.AddOptionDouble("ScalePicked", ref optScaleP);
                        idxSlide2P = go.AddOptionDouble("G2SlidePicked", ref optSlide2P);
                        idxSlide3P = go.AddOptionDouble("G3SlidePicked", ref optSlide3P);
                        
                        idxScaleO = go.AddOptionDouble("ScaleOpp", ref optScaleO);
                        idxSlide2O = go.AddOptionDouble("G2SlideOpp", ref optSlide2O);
                        idxSlide3O = go.AddOptionDouble("G3SlideOpp", ref optSlide3O);
                    }

                    idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                    idxEcho = go.AddOptionToggle("Echo", ref optEcho);
                }
                
                var res = go.Get();
                
                if (res == GetResult.Cancel) return Result.Cancel;
                if (res == GetResult.Object)
                {
                    objref_In = go.Object(0);
                    break;
                }
                if (res == GetResult.Number)
                {
                    EndBulgeOptions.ScalePicked = go.Number();
                    if (EndBulgeOptions.LinkedEnds) EndBulgeOptions.ScaleOpp = EndBulgeOptions.ScalePicked;
                    continue;
                }
                if (res == GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxDialog) EndBulgeOptions.Dialog = optDialog.CurrentValue;
                    else if (opt.Index == idxDelete) EndBulgeOptions.DeleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxEcho) EndBulgeOptions.Echo = optEcho.CurrentValue;
                    else if (opt.Index == idxLinked) 
                    {
                        EndBulgeOptions.LinkedEnds = optLinked.CurrentValue;
                        if (EndBulgeOptions.LinkedEnds)
                        {
                            EndBulgeOptions.ContinuityOpp = EndBulgeOptions.ContinuityPicked;
                            EndBulgeOptions.ScaleOpp = EndBulgeOptions.ScalePicked;
                            EndBulgeOptions.SlideG2Opp = EndBulgeOptions.SlideG2Picked;
                            EndBulgeOptions.SlideG3Opp = EndBulgeOptions.SlideG3Picked;
                        }
                    }
                    else if (opt.Index == idxContP) 
                    {
                        EndBulgeOptions.ContinuityPicked = opt.CurrentListOptionIndex;
                        if (EndBulgeOptions.LinkedEnds) EndBulgeOptions.ContinuityOpp = EndBulgeOptions.ContinuityPicked;
                    }
                    else if (opt.Index == idxContO) EndBulgeOptions.ContinuityOpp = opt.CurrentListOptionIndex;
                    else if (opt.Index == idxScaleP) 
                    {
                        EndBulgeOptions.ScalePicked = optScaleP.CurrentValue;
                        if (EndBulgeOptions.LinkedEnds) EndBulgeOptions.ScaleOpp = EndBulgeOptions.ScalePicked;
                    }
                    else if (opt.Index == idxSlide2P) 
                    {
                        EndBulgeOptions.SlideG2Picked = optSlide2P.CurrentValue;
                        if (EndBulgeOptions.LinkedEnds) EndBulgeOptions.SlideG2Opp = EndBulgeOptions.SlideG2Picked;
                    }
                    else if (opt.Index == idxSlide3P) 
                    {
                        EndBulgeOptions.SlideG3Picked = optSlide3P.CurrentValue;
                        if (EndBulgeOptions.LinkedEnds) EndBulgeOptions.SlideG3Opp = EndBulgeOptions.SlideG3Picked;
                    }
                    else if (opt.Index == idxScaleO) EndBulgeOptions.ScaleOpp = optScaleO.CurrentValue;
                    else if (opt.Index == idxSlide2O) EndBulgeOptions.SlideG2Opp = optSlide2O.CurrentValue;
                    else if (opt.Index == idxSlide3O) EndBulgeOptions.SlideG3Opp = optSlide3O.CurrentValue;
                }
            }

            // Save overrides back to disk so CLI usage remembers states
            EndBulgeOptions.Save();

            return EndBulgeCurveLogic.ExecuteWithRef(doc, isInteractive, objref_In);
        }
    }
}