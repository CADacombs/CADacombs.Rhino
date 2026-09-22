using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Eto.Forms;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public class EndBulgeSurfaceDialog : EndBulgeDialog
    {
        private ObjRef _objRef;
        private NurbsSurface _nsIn;
        private string _boundary;
        public Brep OriginalGeom { get; private set; }
        public Guid TempPreviewId { get; set; } = Guid.Empty;

        public EndBulgeSurfaceDialog(ObjRef objRef) : base(isSurface: true)
        {
            _objRef = objRef;
            
            var edge = objRef.Edge();
            var face = edge.Brep.Faces[edge.AdjacentFaces()[0]];
            _nsIn = face.ToNurbsSurface();

            OriginalGeom = objRef.Brep().DuplicateBrep();

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

            if (minD == dU0) _boundary = "U0";
            else if (minD == dU1) _boundary = "U1";
            else if (minD == dV0) _boundary = "V0";
            else _boundary = "V1";

            var doc = RhinoDoc.ActiveDoc;
            var obj = doc.Objects.FindId(_objRef.ObjectId);
            
            if (obj != null)
            {
                bool isZebra = obj.InVisualAnalysisMode(VisualAnalysisMode.Find(VisualAnalysisMode.RhinoZebraStripeAnalysisModeId));
                bool isEmap = obj.InVisualAnalysisMode(VisualAnalysisMode.Find(VisualAnalysisMode.RhinoEmapAnalysisModeId));
                bool isDraft = obj.InVisualAnalysisMode(VisualAnalysisMode.Find(VisualAnalysisMode.RhinoDraftAngleAnalysisModeId));
                bool isCurv = obj.InVisualAnalysisMode(VisualAnalysisMode.Find(VisualAnalysisMode.RhinoCurvatureColorAnalyisModeId));
                
                if (isZebra && radioButtons.ContainsKey("rbZebra")) radioButtons["rbZebra"].Checked = true;
                else if (isEmap && radioButtons.ContainsKey("rbEmap")) radioButtons["rbEmap"].Checked = true;
                else if (isDraft && radioButtons.ContainsKey("rbDraft")) radioButtons["rbDraft"].Checked = true;
                else if (isCurv && radioButtons.ContainsKey("rbCurv")) radioButtons["rbCurv"].Checked = true;
                else
                {
                    var vp = doc.Views.ActiveView.ActiveViewport;
                    Guid overrideId = obj.Attributes.HasDisplayModeOverride(vp.Id) 
                        ? obj.Attributes.GetDisplayModeOverride(vp.Id) 
                        : vp.DisplayMode.Id;

                    if (overrideId == DisplayModeDescription.WireframeId && radioButtons.ContainsKey("rbNoShading"))
                    {
                        radioButtons["rbNoShading"].Checked = true;
                    }
                }
            }
            
            OnDisplayCheckedChanged(null, null);
            RefreshTopologyLimits();
        }

        protected override void OnUpgradeClicked(object sender, EventArgs e)
        {
            int currentDeg = (_boundary == "U0" || _boundary == "U1") ? _nsIn.OrderU - 1 : _nsIn.OrderV - 1;
            int newDeg = currentDeg % 2 == 0 ? currentDeg + 1 : currentDeg + 2;
            
            if (_boundary == "U0" || _boundary == "U1") _nsIn.IncreaseDegreeU(newDeg);
            else _nsIn.IncreaseDegreeV(newDeg);

            int pIdx = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int oIdx = radioButtonLists["idxCont_Opp"].SelectedIndex;

            RefreshTopologyLimits();
            
            NurbsCurve tempCurve;
            if (_boundary == "U0" || _boundary == "U1") tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0);
            else tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0);

            if (tempCurve != null)
            {
                bool pickedIsT1 = (_boundary == "U1" || _boundary == "V1");
                bool canG3Picked = EndBulgeMath.CanMaintainG3(tempCurve, pickedIsT1);
                bool canG3Opp = EndBulgeMath.CanMaintainG3(tempCurve, !pickedIsT1);

                bool prevAuto = _autoUpdating;
                _autoUpdating = true;
                
                // Select next higher continuity, securely clamped to available bounds
                radioButtonLists["idxCont_Picked"].SelectedIndex = Math.Min(pIdx + 1, canG3Picked ? 4 : 3);
                radioButtonLists["idxCont_Opp"].SelectedIndex = Math.Min(oIdx + 1, canG3Opp ? 4 : 3);
                
                _autoUpdating = prevAuto;
            }

            UpdateControlStates();
            UpdatePreview();
        }

        private void RefreshTopologyLimits()
        {
            NurbsCurve tempCurve;
            if (_boundary == "U0" || _boundary == "U1") tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0);
            else tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0);

            if (tempCurve == null) return;

            int N = tempCurve.Points.Count;
            int currentDeg = tempCurve.Degree;

            if (currentDeg >= 7) btnUpgrade.Visible = false;
            else
            {
                btnUpgrade.Visible = true;
                int nextDeg = currentDeg % 2 == 0 ? currentDeg + 1 : currentDeg + 2;
                btnUpgrade.Text = $"  Upgrade Deg {currentDeg} to {nextDeg}  "; 
            }

            bool prevAuto = _autoUpdating;
            _autoUpdating = true; 

            if (radioButtonLists["idxCont_Picked"].SelectedIndex > N / 2) radioButtonLists["idxCont_Picked"].SelectedIndex = N / 2;
            if (radioButtonLists["idxCont_Opp"].SelectedIndex > N / 2) radioButtonLists["idxCont_Opp"].SelectedIndex = N / 2;

            bool pickedIsT1 = (_boundary == "U1" || _boundary == "V1");
            bool canG3Picked = EndBulgeMath.CanMaintainG3(tempCurve, pickedIsT1);
            bool canG3Opp = EndBulgeMath.CanMaintainG3(tempCurve, !pickedIsT1);

            int pIdx = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int oIdx = radioButtonLists["idxCont_Opp"].SelectedIndex;

            radioButtonLists["idxCont_Picked"].DataStore = canG3Picked ? new[] { "None", "G0", "G1", "G2", "G3" } : new[] { "None", "G0", "G1", "G2" };
            radioButtonLists["idxCont_Opp"].DataStore = canG3Opp ? new[] { "None", "G0", "G1", "G2", "G3" } : new[] { "None", "G0", "G1", "G2" };

            radioButtonLists["idxCont_Picked"].SelectedIndex = Math.Min(pIdx, canG3Picked ? 4 : 3);
            radioButtonLists["idxCont_Opp"].SelectedIndex = Math.Min(oIdx, canG3Opp ? 4 : 3);

            _autoUpdating = prevAuto;
            UpdateControlStates();
        }

        public override void UpdatePreview()
        {
            if (BaseConduit == null || _nsIn == null) return;
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;
            
            var doc = RhinoDoc.ActiveDoc;
            var obj = doc.Objects.FindId(_objRef.ObjectId);
            if (obj != null) conduit.SurfaceColor = obj.Attributes.DrawColor(doc);

            double? fScale_Picked = ParseToFloat(textBoxes["fScale_Picked"].Text);
            double? fScale_Opp = ParseToFloat(textBoxes["fScale_Opp"].Text);

            if (fScale_Picked == null || fScale_Picked <= RhinoMath.ZeroTolerance ||
                fScale_Opp == null || fScale_Opp <= RhinoMath.ZeroTolerance)
            {
                conduit.Surface = null;
                conduit.PreviewBrep = null;
                doc.Views.Redraw();
                return;
            }

            double fSlideG2_Picked = ParseToFloat(textBoxes["fSlideG2_Picked"].Text) ?? 0.0;
            double fSlideG3_Picked = ParseToFloat(textBoxes["fSlideG3_Picked"].Text) ?? 0.0;
            double fSlideG2_Opp = ParseToFloat(textBoxes["fSlideG2_Opp"].Text) ?? 0.0;
            double fSlideG3_Opp = ParseToFloat(textBoxes["fSlideG3_Opp"].Text) ?? 0.0;

            int idxCont_Picked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int idxCont_Opp = radioButtonLists["idxCont_Opp"].SelectedIndex;
            
            int N = (_boundary == "U0" || _boundary == "U1") 
                ? EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0).Points.Count 
                : EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0).Points.Count;

            if (idxCont_Picked + idxCont_Opp > N)
            {
                if (!_autoUpdating)
                {
                    _autoUpdating = true;
                    if (_lastClickedCont == 1) { idxCont_Picked = Math.Max(0, N - idxCont_Opp); radioButtonLists["idxCont_Picked"].SelectedIndex = idxCont_Picked; }
                    else { idxCont_Opp = Math.Max(0, N - idxCont_Picked); radioButtonLists["idxCont_Opp"].SelectedIndex = idxCont_Opp; }

                    if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1) { radioButtonLists["bLinkedEnds"].SelectedIndex = 0; OnLinkedModeChanged(null, null); }
                    else UpdateControlStates();

                    _autoUpdating = false;
                }
            }

            bool bDebug = false;

            var result = EndBulgeSurfaceLogic.CreateSurface(
                _nsIn, _boundary,
                fScale_Picked.Value, fSlideG2_Picked, fSlideG3_Picked,
                fScale_Opp.Value, fSlideG2_Opp, fSlideG3_Opp,
                idxCont_Picked - 1, idxCont_Opp - 1, bDebug);

            if (result.Info != null)
            {
                int actual_T0 = result.Info.Value.MaxModT0;
                int actual_T1 = result.Info.Value.MaxModT1;
                bool bOverlap = result.Info.Value.Overlap;

                int actual_Picked, actual_Opp;
                if (_boundary == "U1" || _boundary == "V1") { actual_Picked = actual_T1; actual_Opp = actual_T0; }
                else { actual_Picked = actual_T0; actual_Opp = actual_T1; }

                if (!_autoUpdating)
                {
                    _autoUpdating = true;
                    bool changed = false;

                    if (actual_Picked != idxCont_Picked - 1) { radioButtonLists["idxCont_Picked"].SelectedIndex = actual_Picked + 1; changed = true; }
                    if (actual_Opp != idxCont_Opp - 1) { radioButtonLists["idxCont_Opp"].SelectedIndex = actual_Opp + 1; changed = true; }

                    if ((bOverlap || changed) && radioButtonLists["bLinkedEnds"].SelectedIndex == 1)
                    {
                        radioButtonLists["bLinkedEnds"].SelectedIndex = 0;
                        OnLinkedModeChanged(null, null);
                        changed = true;
                    }

                    if (changed) UpdateControlStates();
                    _autoUpdating = false;
                }
            }

            conduit.Surface = result.Result ?? (NurbsSurface)_nsIn.Duplicate();
            conduit.PreviewBrep = conduit.Surface?.ToBrep();
            
            if (checkBoxes.ContainsKey("bUseNativePreview")) conduit.UseNativePreview = checkBoxes["bUseNativePreview"].Checked ?? false;
            if (checkBoxes.ContainsKey("bShowWireframe")) conduit.ShowWireframe = checkBoxes["bShowWireframe"].Checked ?? true;

            string selectedMode = "Shaded";
            foreach (var rb in radioButtons)
            {
                if (rb.Value.Checked)
                {
                    selectedMode = rb.Value.Text;
                    break;
                }
            }
            conduit.ConduitDisplayString = selectedMode;
            
            if (conduit.Surface != null) conduit.CgCurves = EndBulgeSurfaceConduit.GetCurvatureIsocurves(conduit.Surface);
            else conduit.CgCurves.Clear();

            bool prevUndo = doc.UndoRecordingEnabled;
            bool wasModified = doc.Modified;
            doc.UndoRecordingEnabled = false;
            doc.Objects.Hide(_objRef.ObjectId, true);
            doc.UndoRecordingEnabled = prevUndo;
            doc.Modified = wasModified;

            conduit.IsSwapped = false;
            doc.Views.Redraw();

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        protected override void OnDisplayCheckedChanged(object sender, EventArgs e)
        {
            base.OnDisplayCheckedChanged(sender, e);
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;

            bool useDocDisplay = checkBoxes.ContainsKey("bUseNativePreview") && (checkBoxes["bUseNativePreview"].Checked ?? false);
            bool showSurface = checkBoxes.ContainsKey("bShowGeom") && (checkBoxes["bShowGeom"].Checked ?? false);
            
            if (checkBoxes.ContainsKey("bShowGeom"))
                checkBoxes["bShowGeom"].Enabled = useDocDisplay;

            if (checkBoxes.ContainsKey("bShowWireframe"))
                checkBoxes["bShowWireframe"].Enabled = !useDocDisplay;
            
            bool conduitModesEnabled = showSurface && !useDocDisplay;
            foreach (var rb in radioButtons.Values)
            {
                rb.Enabled = conduitModesEnabled;
            }

            if (conduit != null)
            {
                conduit.UseNativePreview = useDocDisplay;

                if (checkBoxes.ContainsKey("bShowWireframe"))
                    conduit.ShowWireframe = checkBoxes["bShowWireframe"].Checked ?? true;
                
                foreach (var rb in radioButtons.Values)
                {
                    if (rb.Checked) conduit.ConduitDisplayString = rb.Text;
                }

                bool prevUndo = RhinoDoc.ActiveDoc.UndoRecordingEnabled;
                bool wasModified = RhinoDoc.ActiveDoc.Modified;
                RhinoDoc.ActiveDoc.UndoRecordingEnabled = false;

                if (useDocDisplay)
                {
                    if (TempPreviewId == Guid.Empty && conduit.PreviewBrep != null)
                    {
                        var origObj = RhinoDoc.ActiveDoc.Objects.FindId(_objRef.ObjectId);
                        TempPreviewId = RhinoDoc.ActiveDoc.Objects.AddBrep(conduit.PreviewBrep, origObj?.Attributes);
                        EndBulgeSurfaceLogic.CopyAnalysisModes(RhinoDoc.ActiveDoc, _objRef.ObjectId, TempPreviewId);
                    }

                    if (!conduit.IsSwapped && conduit.PreviewBrep != null && TempPreviewId != Guid.Empty)
                    {
                        EndBulgeSurfaceLogic.ReplaceAndPreserveModes(RhinoDoc.ActiveDoc, TempPreviewId, conduit.PreviewBrep);
                        conduit.IsSwapped = true;
                    }

                    if (showSurface && TempPreviewId != Guid.Empty)
                        RhinoDoc.ActiveDoc.Objects.Show(TempPreviewId, true);
                    else if (TempPreviewId != Guid.Empty)
                        RhinoDoc.ActiveDoc.Objects.Hide(TempPreviewId, true);
                        
                    RhinoDoc.ActiveDoc.Objects.Hide(_objRef.ObjectId, true);
                }
                else
                {
                    if (TempPreviewId != Guid.Empty) RhinoDoc.ActiveDoc.Objects.Hide(TempPreviewId, true);
                    RhinoDoc.ActiveDoc.Objects.Hide(_objRef.ObjectId, true);
                    conduit.IsSwapped = false;
                }
                
                RhinoDoc.ActiveDoc.UndoRecordingEnabled = prevUndo;
                RhinoDoc.ActiveDoc.Modified = wasModified;
                RhinoDoc.ActiveDoc.Views.Redraw();
            }
        }

        protected override void OnDebounceTimerElapsed(object sender, EventArgs e)
        {
            base.OnDebounceTimerElapsed(sender, e);
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;

            if (conduit != null)
            {
                if (conduit.UseNativePreview)
                {
                    if (conduit.Surface != null)
                    {
                        var newBrep = conduit.PreviewBrep ?? conduit.Surface.ToBrep();
                        if (newBrep != null)
                        {
                            bool prevUndo = RhinoDoc.ActiveDoc.UndoRecordingEnabled;
                            bool wasModified = RhinoDoc.ActiveDoc.Modified;
                            RhinoDoc.ActiveDoc.UndoRecordingEnabled = false;

                            if (TempPreviewId == Guid.Empty)
                            {
                                var origObj = RhinoDoc.ActiveDoc.Objects.FindId(_objRef.ObjectId);
                                TempPreviewId = RhinoDoc.ActiveDoc.Objects.AddBrep(newBrep, origObj?.Attributes);
                                EndBulgeSurfaceLogic.CopyAnalysisModes(RhinoDoc.ActiveDoc, _objRef.ObjectId, TempPreviewId);
                            }

                            EndBulgeSurfaceLogic.ReplaceAndPreserveModes(RhinoDoc.ActiveDoc, TempPreviewId, newBrep);

                            if (checkBoxes.ContainsKey("bShowGeom") && checkBoxes["bShowGeom"].Checked == true)
                                RhinoDoc.ActiveDoc.Objects.Show(TempPreviewId, true);
                            else
                                RhinoDoc.ActiveDoc.Objects.Hide(TempPreviewId, true);

                            RhinoDoc.ActiveDoc.UndoRecordingEnabled = prevUndo;
                            RhinoDoc.ActiveDoc.Modified = wasModified;

                            conduit.IsSwapped = true;
                            RhinoDoc.ActiveDoc.Views.Redraw();
                        }
                    }
                }
                else
                {
                    // Forces a redraw 0.2s after interaction to paint lazy-evaluated Brep wires
                    RhinoDoc.ActiveDoc.Views.Redraw();
                }
            }
        }

        protected override void UpdateControlStates()
        {
            if (_nsIn == null || string.IsNullOrEmpty(_boundary)) return;
            
            NurbsCurve tempCurve;
            if (_boundary == "U0" || _boundary == "U1") tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0);
            else tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0);

            if (tempCurve == null) return;

            bool isLinked = radioButtonLists["bLinkedEnds"].SelectedIndex == 1;
            int idxPicked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int idxOpp = radioButtonLists["idxCont_Opp"].SelectedIndex;
            int N = tempCurve.Points.Count;

            if (isLinked)
            {
                int target = (_lastClickedCont == 1) ? idxOpp : idxPicked;
                if (target * 2 > N) target = N / 2;

                if (idxPicked != target || idxOpp != target)
                {
                    _autoUpdating = true; 
                    radioButtonLists["idxCont_Picked"].SelectedIndex = target;
                    radioButtonLists["idxCont_Opp"].SelectedIndex = target;
                    idxPicked = target;
                    idxOpp = target;
                    _autoUpdating = false;
                }
            }

            int allocP, allocO;

            if (idxPicked + idxOpp > N)
            {
                if (idxPicked > idxOpp) { allocP = Math.Min(idxPicked, N); allocO = N - allocP; }
                else if (idxOpp > idxPicked) { allocO = Math.Min(idxOpp, N); allocP = N - allocO; }
                else { allocP = Math.Min(idxPicked, N); allocO = N - allocP; }
            }
            else { allocP = idxPicked; allocO = idxOpp; }

            int free = N - allocP - allocO;
            int scaleLimitP, scaleLimitO;

            if (isLinked)
            {
                int half = free / 2;
                scaleLimitP = allocP + half;
                scaleLimitO = allocO + half;
            }
            else
            {
                if (free > 0) { int half = free / 2; int extra = free % 2; scaleLimitP = allocP + half + extra; scaleLimitO = allocO + half; }
                else { scaleLimitP = allocP; scaleLimitO = allocO; }
            }

            bool allowScaleP = idxPicked >= 2;
            bool allowScaleO = idxOpp >= 2;
            bool allowG2P = (scaleLimitP >= 3) && (idxPicked >= 3);
            bool allowG3P = (scaleLimitP >= 4) && (idxPicked >= 4);
            bool allowG2O = (scaleLimitO >= 3) && (idxOpp >= 3);
            bool allowG3O = (scaleLimitO >= 4) && (idxOpp >= 4);

            void ApplyControlState(string key, bool enableUI, bool forceReset, string resetText)
            {
                if (labels.ContainsKey(key)) labels[key].Enabled = enableUI; 
                textBoxes[key].Enabled = enableUI;
                btnUp[key].Enabled = enableUI;
                btnDown[key].Enabled = enableUI;
                sliders[key].Enabled = enableUI;

                if (forceReset && textBoxes[key].Text != resetText)
                {
                    bool prevAuto = _autoUpdating;
                    _autoUpdating = true; 
                    textBoxes[key].Text = resetText;
                    sliders[key].Value = 0; 
                    sliderPrevVals[key] = 0;
                    _autoUpdating = prevAuto;
                }
            }

            ApplyControlState("fScale_Picked", allowScaleP, !allowScaleP, "1.0000");
            ApplyControlState("fSlideG2_Picked", allowG2P, !allowG2P, "0.0000");
            ApplyControlState("fSlideG3_Picked", allowG3P, !allowG3P, "0.0000");
            ApplyControlState("fScale_Opp", allowScaleO, !allowScaleO, "1.0000");
            ApplyControlState("fSlideG2_Opp", allowG2O, !allowG2O, "0.0000");
            ApplyControlState("fSlideG3_Opp", allowG3O, !allowG3O, "0.0000");
        }
    }
}