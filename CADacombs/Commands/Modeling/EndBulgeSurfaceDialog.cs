using System;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    /// <summary>
    /// The surface-specific implementation of the EndBulge dialog.
    /// Handles boundary detection, surface previews, and Visual Analysis mode preservation.
    /// </summary>
    public class EndBulgeSurfaceDialog : EndBulgeDialog
    {
        private ObjRef _objRef;
        private NurbsSurface _nsIn;
        private string _boundary;
        public Brep OriginalGeom { get; private set; }

        public EndBulgeSurfaceDialog(ObjRef objRef) : base(isSurface: true)
        {
            _objRef = objRef;
            
            var edge = objRef.Edge();
            var face = edge.Brep.Faces[edge.AdjacentFaces()[0]];
            _nsIn = face.ToNurbsSurface();

            // GUARANTEED BACKUP: Explicitly pull the Brep geometry to prevent extracting the 1D Edge Curve
            OriginalGeom = objRef.Brep().DuplicateBrep();

            // Find Boundary
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

            NurbsCurve tempCurve;
            if (_boundary == "U0" || _boundary == "U1")
                tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0);
            else
                tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0);

            // Establish Continuity UI Limits based on the temporary curve
            if (tempCurve != null)
            {
                bool pickedIsT1 = (_boundary == "U1" || _boundary == "V1");
                bool canG3Picked = EndBulgeMath.CanMaintainG3(tempCurve, pickedIsT1);
                bool canG3Opp = EndBulgeMath.CanMaintainG3(tempCurve, !pickedIsT1);

                if (!canG3Picked)
                {
                    radioButtonLists["idxCont_Picked"].DataStore = new[] { "None", "G0", "G1", "G2" };
                    if (EndBulgeOptions.ContinuityPicked == 4) 
                        radioButtonLists["idxCont_Picked"].SelectedIndex = 3;
                }
                
                if (!canG3Opp)
                {
                    radioButtonLists["idxCont_Opp"].DataStore = new[] { "None", "G0", "G1", "G2" };
                    if (EndBulgeOptions.ContinuityOpp == 4) 
                        radioButtonLists["idxCont_Opp"].SelectedIndex = 3;
                }
            }

            UpdateControlStates();
        }

        public override void UpdatePreview()
        {
            if (BaseConduit == null || _nsIn == null) return;
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;

            double? fScale_Picked = ParseToFloat(textBoxes["fScale_Picked"].Text);
            double? fScale_Opp = ParseToFloat(textBoxes["fScale_Opp"].Text);

            if (fScale_Picked == null || fScale_Picked <= RhinoMath.ZeroTolerance ||
                fScale_Opp == null || fScale_Opp <= RhinoMath.ZeroTolerance)
            {
                conduit.Surface = null;
                RhinoDoc.ActiveDoc.Views.Redraw();
                return;
            }

            double fSlideG2_Picked = ParseToFloat(textBoxes["fSlideG2_Picked"].Text) ?? 0.0;
            double fSlideG3_Picked = ParseToFloat(textBoxes["fSlideG3_Picked"].Text) ?? 0.0;
            double fSlideG2_Opp = ParseToFloat(textBoxes["fSlideG2_Opp"].Text) ?? 0.0;
            double fSlideG3_Opp = ParseToFloat(textBoxes["fSlideG3_Opp"].Text) ?? 0.0;

            int idxCont_Picked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int idxCont_Opp = radioButtonLists["idxCont_Opp"].SelectedIndex;
            
            // Validate and downgrade the opposite side if they demand too many CVs
            int N = (_boundary == "U0" || _boundary == "U1") 
                ? EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0).Points.Count 
                : EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0).Points.Count;

            if (idxCont_Picked + idxCont_Opp > N)
            {
                if (!_autoUpdating)
                {
                    _autoUpdating = true;
                    if (_lastClickedCont == 1) // Opp was clicked, downgrade Picked
                    {
                        idxCont_Picked = Math.Max(0, N - idxCont_Opp);
                        radioButtonLists["idxCont_Picked"].SelectedIndex = idxCont_Picked;
                    }
                    else // Picked was clicked, downgrade Opp
                    {
                        idxCont_Opp = Math.Max(0, N - idxCont_Picked);
                        radioButtonLists["idxCont_Opp"].SelectedIndex = idxCont_Opp;
                    }

                    // FORCE UNLINK: If the UI had to be downgraded, break the link immediately
                    if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1)
                    {
                        radioButtonLists["bLinkedEnds"].SelectedIndex = 0;
                        OnLinkedModeChanged(null, null);
                    }
                    else
                    {
                        // NEW: In Independent mode, manually refresh the sliders to match the newly downgraded radio button
                        UpdateControlStates();
                    }

                    _autoUpdating = false;
                }
            }

            bool bDebug = false; // Disconnected from UI for safe removal

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
                if (_boundary == "U1" || _boundary == "V1")
                {
                    actual_Picked = actual_T1;
                    actual_Opp = actual_T0;
                }
                else
                {
                    actual_Picked = actual_T0;
                    actual_Opp = actual_T1;
                }

                if (!_autoUpdating)
                {
                    _autoUpdating = true;
                    bool changed = false;

                    if (actual_Picked != idxCont_Picked - 1)
                    {
                        radioButtonLists["idxCont_Picked"].SelectedIndex = actual_Picked + 1;
                        changed = true;
                    }
                    if (actual_Opp != idxCont_Opp - 1)
                    {
                        radioButtonLists["idxCont_Opp"].SelectedIndex = actual_Opp + 1;
                        changed = true;
                    }

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
            
            if (conduit.Surface != null)
                conduit.CgCurves = EndBulgeSurfaceConduit.GetCurvatureIsocurves(conduit.Surface);
            else
                conduit.CgCurves.Clear();

            // Hide the document object while actively sliding (Conduit takes over)
            RhinoDoc.ActiveDoc.Objects.Hide(_objRef.ObjectId, true);
            conduit.IsSwapped = false;
            RhinoDoc.ActiveDoc.Views.Redraw();

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        protected override void OnDisplayCheckedChanged(object sender, EventArgs e)
        {
            base.OnDisplayCheckedChanged(sender, e);
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;

            if (conduit != null && conduit.IsSwapped)
            {
                if (checkBoxes["bShowGeom"].Checked == true)
                    RhinoDoc.ActiveDoc.Objects.Show(_objRef.ObjectId, true);
                else
                    RhinoDoc.ActiveDoc.Objects.Hide(_objRef.ObjectId, true);
                
                RhinoDoc.ActiveDoc.Views.Redraw();
            }
        }

        protected override void OnDebounceTimerElapsed(object sender, EventArgs e)
        {
            base.OnDebounceTimerElapsed(sender, e);
            var conduit = (EndBulgeSurfaceConduit)BaseConduit;

            if (conduit?.Surface != null)
            {
                var newBrep = conduit.Surface.ToBrep();
                if (newBrep != null)
                {
                    // The Golden Rule: Rhino refuses to replace hidden objects.
                    // We must unhide the original geometry right BEFORE we swap it.
                    RhinoDoc.ActiveDoc.Objects.Show(_objRef.ObjectId, true);
                    
                    // Now the replace will succeed, and Zebra is preserved
                    EndBulgeSurfaceLogic.ReplaceAndPreserveModes(RhinoDoc.ActiveDoc, _objRef.ObjectId, newBrep);

                    // If the user doesn't want to see the surface, re-hide it instantly
                    if (checkBoxes["bShowGeom"].Checked != true)
                        RhinoDoc.ActiveDoc.Objects.Hide(_objRef.ObjectId, true);

                    conduit.IsSwapped = true;
                    RhinoDoc.ActiveDoc.Views.Redraw();
                }
            }
        }

        protected override void UpdateControlStates()
        {
            if (_nsIn == null || string.IsNullOrEmpty(_boundary)) return;
            
            NurbsCurve tempCurve;
            if (_boundary == "U0" || _boundary == "U1")
                tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'U', 0);
            else
                tempCurve = EndBulgeSurfaceLogic.ExtractTempCurve(_nsIn, 'V', 0);

            if (tempCurve == null) return;

            bool isLinked = radioButtonLists["bLinkedEnds"].SelectedIndex == 1;
            int idxPicked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int idxOpp = radioButtonLists["idxCont_Opp"].SelectedIndex;

            int N = tempCurve.Points.Count;

            // NEW: Enforce strict symmetry for continuity constraints in Linked mode
            if (isLinked)
            {
                // Sync to the side that was just clicked. (If Linked was just toggled, default to syncing to Picked)
                int target = (_lastClickedCont == 1) ? idxOpp : idxPicked;
                
                // If the requested continuity exceeds symmetrical point availability, downgrade it
                if (target * 2 > N)
                {
                    target = N / 2;
                }

                // Force the UI radio buttons to match immediately
                if (idxPicked != target || idxOpp != target)
                {
                    _autoUpdating = true; // Prevent recursive preview updates
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
                if (idxPicked > idxOpp)
                {
                    allocP = Math.Min(idxPicked, N);
                    allocO = N - allocP;
                }
                else if (idxOpp > idxPicked)
                {
                    allocO = Math.Min(idxOpp, N);
                    allocP = N - allocO;
                }
                else
                {
                    allocP = Math.Min(idxPicked, N);
                    allocO = N - allocP;
                }
            }
            else
            {
                allocP = idxPicked;
                allocO = idxOpp;
            }

            int free = N - allocP - allocO;
            int scaleLimitP, scaleLimitO;

            if (isLinked)
            {
                // NEW: In Linked mode, any leftover odd middle point cannot be used symmetrically, so it is ignored.
                int half = free / 2;
                scaleLimitP = allocP + half;
                scaleLimitO = allocO + half;
            }
            else
            {
                if (free > 0)
                {
                    int half = free / 2;
                    int extra = free % 2;
                    scaleLimitP = allocP + half + extra;
                    scaleLimitO = allocO + half;
                }
                else
                {
                    scaleLimitP = allocP;
                    scaleLimitO = allocO;
                }
            }

            // Determine if the active continuity tier permits the controls (2 = G1, 3 = G2, 4 = G3)
            bool allowScaleP = idxPicked >= 2;
            bool allowScaleO = idxOpp >= 2;

            bool allowG2P = (scaleLimitP >= 3) && (idxPicked >= 3);
            bool allowG3P = (scaleLimitP >= 4) && (idxPicked >= 4);
            bool allowG2O = (scaleLimitO >= 3) && (idxOpp >= 3);
            bool allowG3O = (scaleLimitO >= 4) && (idxOpp >= 4);

            // Local helper to cleanly toggle UI state and safely reset values when disabled
            void ApplyControlState(string key, bool enableUI, bool forceReset, string resetText)
            {
                if (labels.ContainsKey(key)) labels[key].Enabled = enableUI; // NEW: Grays out the text label

                textBoxes[key].Enabled = enableUI;
                btnUp[key].Enabled = enableUI;
                btnDown[key].Enabled = enableUI;
                sliders[key].Enabled = enableUI;

                if (forceReset && textBoxes[key].Text != resetText)
                {
                    bool prevAuto = _autoUpdating;
                    _autoUpdating = true; // Suspend events to prevent double-firing UpdatePreview
                    textBoxes[key].Text = resetText;
                    sliders[key].Value = 0; // 0 is the center/default physical position for all your sliders
                    sliderPrevVals[key] = 0;
                    _autoUpdating = prevAuto;
                }
            }

            // Update Picked side
            ApplyControlState("fScale_Picked", allowScaleP, !allowScaleP, "1.0000");
            ApplyControlState("fSlideG2_Picked", allowG2P, !allowG2P, "0.0000");
            ApplyControlState("fSlideG3_Picked", allowG3P, !allowG3P, "0.0000");
            
            // Update Opposite side (Now perfectly mirrors Picked side logic so either side can drive in Linked mode)
            ApplyControlState("fScale_Opp", allowScaleO, !allowScaleO, "1.0000");
            ApplyControlState("fSlideG2_Opp", allowG2O, !allowG2O, "0.0000");
            ApplyControlState("fSlideG3_Opp", allowG3O, !allowG3O, "0.0000");
        }
    }
}