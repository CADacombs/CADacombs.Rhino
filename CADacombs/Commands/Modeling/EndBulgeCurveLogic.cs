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
        public static Result ExecuteWithRef(RhinoDoc doc, bool isInteractive, ObjRef objRef)
        {
            // --- NEW: Strict Symmetrical Point-Count Clamping ---
            // Enforces the N/2 ceiling independently on both sides so neither side
            // can deform the opposite end on geometries with low point counts.
            Curve baseCurve = objRef.Curve();
            NurbsCurve ncIn = baseCurve as NurbsCurve ?? baseCurve?.ToNurbsCurve();
            if (ncIn != null)
            {
                int N = ncIn.Points.Count;
                EndBulgeOptions.ContinuityPicked = Math.Min(EndBulgeOptions.ContinuityPicked, N / 2);
                EndBulgeOptions.ContinuityOpp = Math.Min(EndBulgeOptions.ContinuityOpp, N / 2);
            }

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

            // --- FIXED: Suspend undo tracking for temporary UI locking ---
            bool prevUndo = doc.UndoRecordingEnabled;
            doc.UndoRecordingEnabled = false;
            doc.Objects.Lock(objRef.ObjectId, true);
            doc.UndoRecordingEnabled = prevUndo;

            dialog.UpdatePreview();
            dialog.BaseConduit.Enabled = true;
            doc.Views.Redraw();

            try
            {
                dialog.ShowSemiModal(doc, parent);

                doc.UndoRecordingEnabled = false;
                if (!EndBulgeOptions.Debug) doc.Views.RedrawEnabled = false;
                doc.Objects.UnselectAll();
                doc.Objects.Unlock(objRef.ObjectId, true);
                doc.UndoRecordingEnabled = prevUndo;

                if (dialog.DialogOk && dialog.BaseConduit.Crv != null)
                {
                    // Only create the undo record for the actual geometry modification
                    uint undoSn = doc.BeginUndoRecord("EndBulge Crv");
                    ProcessCurveObject(doc, objRef, dialog.BaseConduit.Crv);
                    doc.EndUndoRecord(undoSn);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                RhinoApp.WriteLine($"Script Error Encountered: {ex.Message}");
#endif
            }
            finally
            {
                dialog.BaseConduit.Enabled = false;
                
                // Safety unlock in case an exception fired early
                doc.UndoRecordingEnabled = false;
                doc.Objects.Unlock(objRef.ObjectId, true);
                doc.UndoRecordingEnabled = prevUndo;
                
                doc.Views.RedrawEnabled = true;
                doc.Views.Redraw();
            }

            return dialog.DialogOk ? Result.Success : Result.Cancel;
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
                    EndBulgeOptions.ContinuityOpp = EndBulgeOptions.ContinuityPicked; // NEW: Sync continuity
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
    }
}