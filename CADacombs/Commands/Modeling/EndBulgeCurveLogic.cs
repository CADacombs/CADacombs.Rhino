using System;
using System.Collections.Generic;
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
    public static class EndBulgeCurveLogic
    {
        public static Result Execute(RhinoDoc doc, bool isInteractive)
        {
            EndBulgeOptions.Dialog = isInteractive;

            ObjRef objRef = GetInputCLI(isInteractive);
            if (objRef == null) return Result.Cancel;

            return ExecuteWithRef(doc, isInteractive, objRef);
        }

        public static Result ExecuteWithRef(RhinoDoc doc, bool isInteractive, ObjRef objRef)
        {
            EndBulgeOptions.Dialog = isInteractive;

            if (EndBulgeOptions.Dialog)
            {
                return RunGUI(doc, objRef);
            }
            else
            {
                doc.Objects.UnselectAll();
                bool success = ProcessCurveObject(doc, objRef, null);
                if (success) doc.Views.Redraw();
                return success ? Result.Success : Result.Failure;
            }
        }

        private static Result RunGUI(RhinoDoc doc, ObjRef objRef)
        {
            RhinoApp.SetCommandPromptMessage("Continuing in dialog...");

            var parent = RhinoEtoApp.MainWindowForDocument(doc);
            var dialog = new EndBulgeCurveDialog(objRef);

            doc.Objects.Lock(objRef.ObjectId, true);

            dialog.UpdatePreview();
            dialog.BaseConduit.Enabled = true;
            doc.Views.Redraw();

            uint undoSn = doc.BeginUndoRecord("EndBulge Crv");

            try
            {
                dialog.ShowSemiModal(doc, parent);

                if (!EndBulgeOptions.Debug) doc.Views.RedrawEnabled = false;
                doc.Objects.UnselectAll();
                doc.Objects.Unlock(objRef.ObjectId, true);

                if (dialog.DialogOk && dialog.BaseConduit.Crv != null)
                {
                    ProcessCurveObject(doc, objRef, dialog.BaseConduit.Crv);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Script Error Encountered: {ex.Message}");
            }
            finally
            {
                dialog.BaseConduit.Enabled = false;
                doc.Objects.Unlock(objRef.ObjectId, true);
                doc.EndUndoRecord(undoSn);
                doc.Views.RedrawEnabled = true;
                doc.Views.Redraw();
            }

            return Result.Success;
        }

        private static bool ProcessCurveObject(RhinoDoc doc, ObjRef objRef, NurbsCurve precalcCurve)
        {
            NurbsCurve resultCurve = precalcCurve;

            if (resultCurve == null)
            {
                Curve baseCurve = objRef.Curve();
                NurbsCurve ncIn = baseCurve as NurbsCurve ?? baseCurve?.ToNurbsCurve();
                if (ncIn == null) return false;

                ncIn.ClosestPoint(objRef.SelectionPoint(), out double t_AtPicked);
                bool pickedIsT1 = t_AtPicked > ncIn.Domain.Mid;

                if (EndBulgeOptions.LinkedEnds)
                {
                    EndBulgeOptions.ScaleOpp = EndBulgeOptions.ScalePicked;
                    EndBulgeOptions.SlideG2Opp = EndBulgeOptions.SlideG2Picked;
                    EndBulgeOptions.SlideG3Opp = EndBulgeOptions.SlideG3Picked;
                }

                int iPickedEnd;
                double sT0, sT1, g2T0, g2T1, g3T0, g3T1;
                int iGT0, iGT1;

                if (pickedIsT1)
                {
                    iPickedEnd = 1;
                    sT1 = EndBulgeOptions.ScalePicked; g2T1 = EndBulgeOptions.SlideG2Picked; g3T1 = EndBulgeOptions.SlideG3Picked;
                    sT0 = EndBulgeOptions.ScaleOpp; g2T0 = EndBulgeOptions.SlideG2Opp; g3T0 = EndBulgeOptions.SlideG3Opp;
                    iGT1 = EndBulgeOptions.ContinuityPicked - 1; iGT0 = EndBulgeOptions.ContinuityOpp - 1;
                }
                else
                {
                    iPickedEnd = 0;
                    sT0 = EndBulgeOptions.ScalePicked; g2T0 = EndBulgeOptions.SlideG2Picked; g3T0 = EndBulgeOptions.SlideG3Picked;
                    sT1 = EndBulgeOptions.ScaleOpp; g2T1 = EndBulgeOptions.SlideG2Opp; g3T1 = EndBulgeOptions.SlideG3Opp;
                    iGT0 = EndBulgeOptions.ContinuityPicked - 1; iGT1 = EndBulgeOptions.ContinuityOpp - 1;
                }

                var mathResult = EndBulgeMath.CreateCurve(
                    ncIn, sT0, g2T0, g3T0, sT1, g2T1, g3T1, iGT0, iGT1, iPickedEnd, EndBulgeOptions.Debug);

                if (mathResult.Result == null)
                {
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine($"Curve was not generated. {mathResult.Error}");
                    return false;
                }
                resultCurve = mathResult.Result;
            }

            if (resultCurve.EpsilonEquals(objRef.Curve().ToNurbsCurve(), RhinoMath.ZeroTolerance))
            {
                RhinoApp.WriteLine("Resultant curve is the same as input curve. No changes were made to the document.");
                return true;
            }

            if (!EndBulgeOptions.DeleteInput || objRef.Edge() != null)
            {
                Guid gId = doc.Objects.AddCurve(resultCurve);
                if (gId == Guid.Empty)
                {
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Could not add curve.");
                    return false;
                }
                if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Curve was added.");
            }
            else
            {
                if (doc.Objects.Replace(objRef.ObjectId, resultCurve))
                {
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Replaced curve.");
                }
                else
                {
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Could not replace curve.");
                    return false;
                }
            }

            return true;
        }

        private static ObjRef GetInputCLI(bool isInteractive)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select curve to adjust");
            go.GeometryFilter = ObjectType.Curve;
            go.DisablePreSelect();
            go.AcceptNumber(true, true);

            go.SetCustomGeometryFilter((rhObject, geom, compIdx) =>
            {
                Curve crv = null;
                if (geom is BrepEdge edge) crv = edge.DuplicateCurve();
                else if (geom is Curve c) crv = c;
                else return false;

                NurbsCurve nc = crv as NurbsCurve;
                if (nc == null && crv is PolyCurve pc)
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
                int idxScaleP = 0, idxSlide2P = 0, idxSlide3P = 0;
                int idxScaleO = 0, idxSlide2O = 0, idxSlide3O = 0;
                int idxDelete = 0, idxEcho = 0;

                if (!isInteractive)
                {
                    idxDialog = go.AddOptionToggle("Dialog", ref optDialog);
                }

                if (!EndBulgeOptions.Dialog)
                {
                    idxContP = go.AddOptionList("MaintainPicked", contList, EndBulgeOptions.ContinuityPicked);
                    idxContO = go.AddOptionList("MaintainOpp", contList, EndBulgeOptions.ContinuityOpp);
                    idxLinked = go.AddOptionToggle("AdjustEnds", ref optLinked);
                    
                    if (EndBulgeOptions.LinkedEnds)
                    {
                        idxScaleP = go.AddOptionDouble("Scale", ref optScaleP);
                        idxSlide2P = go.AddOptionDouble("SlideG2", ref optSlide2P);
                        idxSlide3P = go.AddOptionDouble("SlideG3", ref optSlide3P);
                    }
                    else
                    {
                        idxScaleP = go.AddOptionDouble("Scale_Picked", ref optScaleP);
                        idxSlide2P = go.AddOptionDouble("SlideG2Picked", ref optSlide2P);
                        idxSlide3P = go.AddOptionDouble("SlideG3Picked", ref optSlide3P);
                        
                        idxScaleO = go.AddOptionDouble("Scale_Opp", ref optScaleO);
                        idxSlide2O = go.AddOptionDouble("SlideG2Opp", ref optSlide2O);
                        idxSlide3O = go.AddOptionDouble("SlideG3Opp", ref optSlide3O);
                    }
                    idxDelete = go.AddOptionToggle("DeleteInput", ref optDelete);
                    idxEcho = go.AddOptionToggle("Echo", ref optEcho);
                }
                
                var res = go.Get();
                if (res == GetResult.Cancel) return null;
                if (res == GetResult.Object) return go.Object(0);

                if (res == GetResult.Number)
                {
                    EndBulgeOptions.ScalePicked = go.Number();
                    continue;
                }

                if (res == GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == idxDialog) EndBulgeOptions.Dialog = optDialog.CurrentValue;
                    else if (opt.Index == idxLinked) EndBulgeOptions.LinkedEnds = optLinked.CurrentValue;
                    else if (opt.Index == idxDelete) EndBulgeOptions.DeleteInput = optDelete.CurrentValue;
                    else if (opt.Index == idxEcho) EndBulgeOptions.Echo = optEcho.CurrentValue;
                    else if (opt.Index == idxContP) EndBulgeOptions.ContinuityPicked = opt.CurrentListOptionIndex;
                    else if (opt.Index == idxContO) EndBulgeOptions.ContinuityOpp = opt.CurrentListOptionIndex;
                    else if (opt.Index == idxScaleP) EndBulgeOptions.ScalePicked = optScaleP.CurrentValue;
                    else if (opt.Index == idxSlide2P) EndBulgeOptions.SlideG2Picked = optSlide2P.CurrentValue;
                    else if (opt.Index == idxSlide3P) EndBulgeOptions.SlideG3Picked = optSlide3P.CurrentValue;
                    else if (opt.Index == idxScaleO) EndBulgeOptions.ScaleOpp = optScaleO.CurrentValue;
                    else if (opt.Index == idxSlide2O) EndBulgeOptions.SlideG2Opp = optSlide2O.CurrentValue;
                    else if (opt.Index == idxSlide3O) EndBulgeOptions.SlideG3Opp = optSlide3O.CurrentValue;
                }
            }
        }
    }
}