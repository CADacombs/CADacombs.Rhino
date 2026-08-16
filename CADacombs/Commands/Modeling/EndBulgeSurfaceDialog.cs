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
        private int _lastClickedCont = 0; // 0 for Picked, 1 for Opp

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

            radioButtonLists["idxCont_Picked"].SelectedIndexChanged += (s, e) => { if (!_autoUpdating) _lastClickedCont = 0; };
            radioButtonLists["idxCont_Opp"].SelectedIndexChanged += (s, e) => { if (!_autoUpdating) _lastClickedCont = 1; };

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
            
            // Re-use the point allocation constraints calculation identical to Curve logic
            // (Assuming generic N is pulled from the cross-section of the surface)
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

            // Enable or disable UI elements
            textBoxes["fSlideG2_Picked"].Enabled = scaleLimitP >= 3;
            btnUp["fSlideG2_Picked"].Enabled = scaleLimitP >= 3;
            btnDown["fSlideG2_Picked"].Enabled = scaleLimitP >= 3;
            sliders["fSlideG2_Picked"].Enabled = scaleLimitP >= 3;

            textBoxes["fSlideG3_Picked"].Enabled = scaleLimitP >= 4;
            btnUp["fSlideG3_Picked"].Enabled = scaleLimitP >= 4;
            btnDown["fSlideG3_Picked"].Enabled = scaleLimitP >= 4;
            sliders["fSlideG3_Picked"].Enabled = scaleLimitP >= 4;

            textBoxes["fScale_Opp"].Enabled = !isLinked;
            btnUp["fScale_Opp"].Enabled = !isLinked;
            btnDown["fScale_Opp"].Enabled = !isLinked;
            sliders["fScale_Opp"].Enabled = !isLinked;

            if (isLinked)
            {
                textBoxes["fSlideG2_Opp"].Enabled = false;
                btnUp["fSlideG2_Opp"].Enabled = false;
                btnDown["fSlideG2_Opp"].Enabled = false;
                sliders["fSlideG2_Opp"].Enabled = false;

                textBoxes["fSlideG3_Opp"].Enabled = false;
                btnUp["fSlideG3_Opp"].Enabled = false;
                btnDown["fSlideG3_Opp"].Enabled = false;
                sliders["fSlideG3_Opp"].Enabled = false;
            }
            else
            {
                textBoxes["fSlideG2_Opp"].Enabled = scaleLimitO >= 3;
                btnUp["fSlideG2_Opp"].Enabled = scaleLimitO >= 3;
                btnDown["fSlideG2_Opp"].Enabled = scaleLimitO >= 3;
                sliders["fSlideG2_Opp"].Enabled = scaleLimitO >= 3;

                textBoxes["fSlideG3_Opp"].Enabled = scaleLimitO >= 4;
                btnUp["fSlideG3_Opp"].Enabled = scaleLimitO >= 4;
                btnDown["fSlideG3_Opp"].Enabled = scaleLimitO >= 4;
                sliders["fSlideG3_Opp"].Enabled = scaleLimitO >= 4;
            }
        }
    }
}