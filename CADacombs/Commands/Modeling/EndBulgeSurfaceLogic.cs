using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public static class EndBulgeSurfaceLogic
    {
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
                Brep originalGeom = objRef.Brep()?.DuplicateBrep();
                Guid gId = ProcessBrepObject(doc, objRef, null, originalGeom);
                if (gId != Guid.Empty) doc.Views.Redraw();
                return gId != Guid.Empty ? Result.Success : Result.Failure;
            }
        }

        private static Result RunGUI(RhinoDoc doc, ObjRef objRef)
        {
            RhinoApp.SetCommandPromptMessage("Continuing in dialog...");

            var parent = RhinoEtoApp.MainWindowForDocument(doc);
            var dialog = new EndBulgeSurfaceDialog(objRef);
            var conduit = new EndBulgeSurfaceConduit();
            dialog.BaseConduit = conduit;

            dialog.UpdatePreview();
            conduit.Enabled = true;
            doc.Views.Redraw();

            uint undoSn = doc.BeginUndoRecord("EndBulge Srf");

            try
            {
                dialog.ShowSemiModal(doc, parent);

                if (dialog.DialogOk && conduit.Surface != null)
                {
                    ProcessBrepObject(doc, objRef, conduit.Surface, dialog.OriginalGeom);
                }
                else
                {
                    ReplaceAndPreserveModes(doc, objRef.ObjectId, dialog.OriginalGeom);
                    doc.Objects.Show(objRef.ObjectId, true);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Script Error Encountered: {ex.Message}");
                ReplaceAndPreserveModes(doc, objRef.ObjectId, dialog.OriginalGeom);
                doc.Objects.Show(objRef.ObjectId, true);
            }
            finally
            {
                conduit.Enabled = false;
                doc.EndUndoRecord(undoSn);
                doc.Views.Redraw();
            }

            return Result.Success;
        }

        public static bool ReplaceAndPreserveModes(RhinoDoc doc, Guid objId, Brep newGeom)
        {
            var obj = doc.Objects.FindId(objId);
            if (obj == null || newGeom == null) return false;

            var activeModes = new List<VisualAnalysisMode>();
            
            Guid[] knownGuids = new[] {
                VisualAnalysisMode.RhinoZebraStripeAnalysisModeId,
                VisualAnalysisMode.RhinoEmapAnalysisModeId,
                VisualAnalysisMode.RhinoDraftAngleAnalysisModeId
            };

            foreach (var guid in knownGuids)
            {
                var mode = VisualAnalysisMode.Find(guid);
                if (mode != null && obj.InVisualAnalysisMode(mode))
                {
                    activeModes.Add(mode);
                }
            }

            bool rc = doc.Objects.Replace(objId, newGeom);

            if (rc && activeModes.Count > 0)
            {
                var newObj = doc.Objects.FindId(objId);
                if (newObj != null)
                {
                    foreach (var mode in activeModes)
                        newObj.EnableVisualAnalysisMode(mode, true);
                }
            }

            return rc;
        }

        public static NurbsCurve ExtractTempCurve(NurbsSurface ns, char direction, int index)
        {
            NurbsCurve nc;
            if (direction == 'U')
            {
                nc = new NurbsCurve(3, ns.IsRational, ns.OrderU, ns.Points.CountU);
                for (int i = 0; i < ns.KnotsU.Count; i++) nc.Knots[i] = ns.KnotsU[i];
                for (int i = 0; i < ns.Points.CountU; i++)
                {
                    var pt = ns.Points.GetControlPoint(i, index);
                    nc.Points.SetPoint(i, pt.Location, pt.Weight);
                }
            }
            else
            {
                nc = new NurbsCurve(3, ns.IsRational, ns.OrderV, ns.Points.CountV);
                for (int i = 0; i < ns.KnotsV.Count; i++) nc.Knots[i] = ns.KnotsV[i];
                for (int i = 0; i < ns.Points.CountV; i++)
                {
                    var pt = ns.Points.GetControlPoint(index, i);
                    nc.Points.SetPoint(i, pt.Location, pt.Weight);
                }
            }
            return nc;
        }

        public static (NurbsSurface Result, string Error, (int MaxModT0, int MaxModT1, bool Overlap)? Info) CreateSurface(
            NurbsSurface nsIn, string boundary,
            double fScalePicked, double fSlideG2Picked, double fSlideG3Picked,
            double fScaleOpp, double fSlideG2Opp, double fSlideG3Opp,
            int iGPicked, int iGOpp, bool bDebug)
        {
            if (Math.Abs(fScalePicked - 1.0) > RhinoMath.ZeroTolerance || Math.Abs(fScaleOpp - 1.0) > RhinoMath.ZeroTolerance) { }
            else if (Math.Abs(fSlideG2Picked) > RhinoMath.ZeroTolerance || Math.Abs(fSlideG3Picked) > RhinoMath.ZeroTolerance ||
                     Math.Abs(fSlideG2Opp) > RhinoMath.ZeroTolerance || Math.Abs(fSlideG3Opp) > RhinoMath.ZeroTolerance) { }
            else return (null, "All scale and slide values result in no change to the geometry.", null);

            NurbsSurface nsOut = (NurbsSurface)nsIn.Duplicate();
            (int, int, bool)? globalInfo = null;

            if (boundary == "U0" || boundary == "U1")
            {
                int iPickedEnd = boundary == "U0" ? 0 : 1;
                for (int v = 0; v < nsIn.Points.CountV; v++)
                {
                    var ncTemp = ExtractTempCurve(nsIn, 'U', v);
                    
                    double s0, g20, g30, s1, g21, g31;
                    int i0, i1;

                    if (iPickedEnd == 0)
                    {
                        s0 = fScalePicked; g20 = fSlideG2Picked; g30 = fSlideG3Picked; i0 = iGPicked;
                        s1 = fScaleOpp; g21 = fSlideG2Opp; g31 = fSlideG3Opp; i1 = iGOpp;
                    }
                    else
                    {
                        s1 = fScalePicked; g21 = fSlideG2Picked; g31 = fSlideG3Picked; i1 = iGPicked;
                        s0 = fScaleOpp; g20 = fSlideG2Opp; g30 = fSlideG3Opp; i0 = iGOpp;
                    }

                    var res = EndBulgeMath.CreateCurve(ncTemp, s0, g20, g30, s1, g21, g31, i0, i1, iPickedEnd, false);
                    if (res.Result == null) return (null, res.Error, null);
                    if (v == 0) globalInfo = res.Info;

                    for (int u = 0; u < nsIn.Points.CountU; u++)
                    {
                        var pt = res.Result.Points[u];
                        nsOut.Points.SetControlPoint(u, v, new ControlPoint(pt.Location, pt.Weight));
                    }
                }
            }
            else
            {
                int iPickedEnd = boundary == "V0" ? 0 : 1;
                for (int u = 0; u < nsIn.Points.CountU; u++)
                {
                    var ncTemp = ExtractTempCurve(nsIn, 'V', u);
                    
                    double s0, g20, g30, s1, g21, g31;
                    int i0, i1;

                    if (iPickedEnd == 0)
                    {
                        s0 = fScalePicked; g20 = fSlideG2Picked; g30 = fSlideG3Picked; i0 = iGPicked;
                        s1 = fScaleOpp; g21 = fSlideG2Opp; g31 = fSlideG3Opp; i1 = iGOpp;
                    }
                    else
                    {
                        s1 = fScalePicked; g21 = fSlideG2Picked; g31 = fSlideG3Picked; i1 = iGPicked;
                        s0 = fScaleOpp; g20 = fSlideG2Opp; g30 = fSlideG3Opp; i0 = iGOpp;
                    }

                    var res = EndBulgeMath.CreateCurve(ncTemp, s0, g20, g30, s1, g21, g31, i0, i1, iPickedEnd, false);
                    if (res.Result == null) return (null, res.Error, null);
                    if (u == 0) globalInfo = res.Info;

                    for (int v = 0; v < nsIn.Points.CountV; v++)
                    {
                        var pt = res.Result.Points[v];
                        nsOut.Points.SetControlPoint(u, v, new ControlPoint(pt.Location, pt.Weight));
                    }
                }
            }

            return (nsOut, null, globalInfo);
        }

        private static Guid ProcessBrepObject(RhinoDoc doc, ObjRef objRef, NurbsSurface nsPrecalc, Brep originalGeom)
        {
            var edge = objRef.Edge();
            var face = edge.Brep.Faces[edge.AdjacentFaces()[0]];
            var nsIn = face.ToNurbsSurface();

            double tMid = edge.Domain.Mid;
            Point3d ptMid = edge.PointAt(tMid);
            face.ClosestPoint(ptMid, out double u, out double v);

            Interval domU = face.Domain(0);
            Interval domV = face.Domain(1);
            double dU0 = Math.Abs(u - domU.Min);
            double dU1 = Math.Abs(domU.Max - u);
            double dV0 = Math.Abs(v - domV.Min);
            double dV1 = Math.Abs(domV.Max - v);
            double minD = Math.Min(Math.Min(dU0, dU1), Math.Min(dV0, dV1));

            string boundary = (minD == dU0) ? "U0" : (minD == dU1) ? "U1" : (minD == dV0) ? "V0" : "V1";

            NurbsSurface nsRes = nsPrecalc;

            if (nsRes == null)
            {
                if (EndBulgeOptions.LinkedEnds)
                {
                    EndBulgeOptions.ContinuityOpp = EndBulgeOptions.ContinuityPicked; // NEW: Sync continuity
                    EndBulgeOptions.ScaleOpp = EndBulgeOptions.ScalePicked;
                    EndBulgeOptions.SlideG2Opp = EndBulgeOptions.SlideG2Picked;
                    EndBulgeOptions.SlideG3Opp = EndBulgeOptions.SlideG3Picked;
                }

                var res = CreateSurface(nsIn, boundary,
                    EndBulgeOptions.ScalePicked, EndBulgeOptions.SlideG2Picked, EndBulgeOptions.SlideG3Picked,
                    EndBulgeOptions.ScaleOpp, EndBulgeOptions.SlideG2Opp, EndBulgeOptions.SlideG3Opp,
                    EndBulgeOptions.ContinuityPicked - 1, EndBulgeOptions.ContinuityOpp - 1, EndBulgeOptions.Debug);

                if (res.Result == null)
                {
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine($"Surface was not generated. {res.Error}");
                    if (originalGeom != null)
                    {
                        ReplaceAndPreserveModes(doc, objRef.ObjectId, originalGeom);
                        doc.Objects.Show(objRef.ObjectId, true);
                    }
                    return Guid.Empty;
                }
                nsRes = res.Result;
            }

            var srfInToNurbs = face.UnderlyingSurface().ToNurbsSurface();
            if (nsRes.EpsilonEquals(srfInToNurbs, RhinoMath.ZeroTolerance))
            {
                RhinoApp.WriteLine("Resultant surface is the same as input surface. No changes were made.");
                if (originalGeom != null)
                {
                    ReplaceAndPreserveModes(doc, objRef.ObjectId, originalGeom);
                    doc.Objects.Show(objRef.ObjectId, true);
                }
                return Guid.Empty;
            }

            Guid gBOut = Guid.Empty;

            if (!EndBulgeOptions.DeleteInput)
            {
                if (originalGeom != null)
                {
                    ReplaceAndPreserveModes(doc, objRef.ObjectId, originalGeom);
                    doc.Objects.Show(objRef.ObjectId, true);
                }

                gBOut = doc.Objects.AddSurface(nsRes);
                if (gBOut == Guid.Empty && EndBulgeOptions.Echo) RhinoApp.WriteLine("Could not add surface.");
                else if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Surface was added.");
            }
            else
            {
                if (ReplaceAndPreserveModes(doc, objRef.ObjectId, nsRes.ToBrep()))
                {
                    doc.Objects.Show(objRef.ObjectId, true);
                    gBOut = objRef.ObjectId;
                    if (EndBulgeOptions.Echo) RhinoApp.WriteLine("Replaced surface.");
                }
                else if (EndBulgeOptions.Echo)
                {
                    RhinoApp.WriteLine("Could not replace surface.");
                }
            }

            return gBOut;
        }
    }
}