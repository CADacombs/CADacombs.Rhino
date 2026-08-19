using System;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    /// <summary>
    /// The curve-specific implementation of the EndBulge dialog.
    /// Handles curve point logic, overrides preview updates, and manages slider limits.
    /// </summary>
    public class EndBulgeCurveDialog : EndBulgeDialog
    {
        private ObjRef _objRef;
        private NurbsCurve _ncIn;

        public EndBulgeCurveDialog(ObjRef objRef) : base(isSurface: false)
        {
            _objRef = objRef;
            var crv = objRef.Curve();
            _ncIn = crv?.ToNurbsCurve();

            if (_ncIn != null)
            {
                BaseConduit = new EndBulgeConduit();
                
                // Restrict G3 continuity if the curve topology doesn't mathematically support it
                _ncIn.ClosestPoint(_objRef.SelectionPoint(), out double t_AtPicked);
                bool pickedIsT1 = t_AtPicked > _ncIn.Domain.Mid;
                
                bool canG3Picked = EndBulgeMath.CanMaintainG3(_ncIn, pickedIsT1);
                bool canG3Opp = EndBulgeMath.CanMaintainG3(_ncIn, !pickedIsT1);

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

                UpdateControlStates();
            }
        }

        public override void UpdatePreview()
        {
            if (BaseConduit == null || _ncIn == null) return;

            double? fScale_Picked = ParseToFloat(textBoxes["fScale_Picked"].Text);
            double? fScale_Opp = ParseToFloat(textBoxes["fScale_Opp"].Text);

            if (fScale_Picked == null || fScale_Picked <= RhinoMath.ZeroTolerance ||
                fScale_Opp == null || fScale_Opp <= RhinoMath.ZeroTolerance)
            {
                BaseConduit.Crv = null;
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
            int N = _ncIn.Points.Count;
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

            _ncIn.ClosestPoint(_objRef.SelectionPoint(), out double t_AtPicked);
            bool pickedIsT1 = t_AtPicked > _ncIn.Domain.Mid;

            int iPickedEnd;
            double sT0, sT1, g2T0, g2T1, g3T0, g3T1;
            int iGT0, iGT1;

            // Route the UI values to the correct ends of the underlying NurbsCurve (T0 vs T1)
            if (pickedIsT1)
            {
                iPickedEnd = 1;
                sT1 = fScale_Picked.Value; g2T1 = fSlideG2_Picked; g3T1 = fSlideG3_Picked;
                sT0 = fScale_Opp.Value; g2T0 = fSlideG2_Opp; g3T0 = fSlideG3_Opp;
                iGT1 = idxCont_Picked - 1; iGT0 = idxCont_Opp - 1;
            }
            else
            {
                iPickedEnd = 0;
                sT0 = fScale_Picked.Value; g2T0 = fSlideG2_Picked; g3T0 = fSlideG3_Picked;
                sT1 = fScale_Opp.Value; g2T1 = fSlideG2_Opp; g3T1 = fSlideG3_Opp;
                iGT0 = idxCont_Picked - 1; iGT1 = idxCont_Opp - 1;
            }

            // Call the core math engine
            var result = EndBulgeMath.CreateCurve(
                _ncIn,
                sT0, g2T0, g3T0,
                sT1, g2T1, g3T1,
                iGT0, iGT1, iPickedEnd, bDebug);

            if (result.Info != null)
            {
                int actual_T0 = result.Info.Value.MaxModT0;
                int actual_T1 = result.Info.Value.MaxModT1;
                bool bOverlap = result.Info.Value.Overlap;

                int actual_Picked = pickedIsT1 ? actual_T1 : actual_T0;
                int actual_Opp = pickedIsT1 ? actual_T0 : actual_T1;

                // Sync UI down-grades if the math engine detects a point allocation overlap
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

            BaseConduit.Crv = result.Result ?? (NurbsCurve)_ncIn.Duplicate();
            RhinoDoc.ActiveDoc.Views.Redraw();

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        protected override void UpdateControlStates()
        {
            if (_ncIn == null) return;
            
            bool isLinked = radioButtonLists["bLinkedEnds"].SelectedIndex == 1;
            int idxPicked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            int idxOpp = radioButtonLists["idxCont_Opp"].SelectedIndex;

            int N = _ncIn.Points.Count;

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