using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;

namespace CADacombs.Commands.Modeling
{
    public enum DrapeDialogAction { None, Ok, Cancel, AddRemoveTargets, ReselectTargets, PickCustomSurface }

    public class DrapeDialog : CADacombsDialogBase
    {
        public DrapeDialogAction Action { get; set; } = DrapeDialogAction.None;
        public NurbsSurface ResultSurface { get; private set; }

        private ObjRef[] _targetRefs;
        private ObjRef _startingSrfRef;
        private DrapeConduit _conduit;

        private Dictionary<string, (NurbsSurface, Brep)> _previewCache = new Dictionary<string, (NurbsSurface, Brep)>();

        private RadioButton _rbSelect;
        private RadioButton _rbCreate;
        private Button _btnReselSrf;

        private TextBox _txtSpanSpacing;
        private NumericStepper _stepSpansBeyond;
        private CheckBox _chkFlipCPlane;
        private DropDown _dropTargetMisses;
        
        private CheckBox _chkShowSurface;
        private CheckBox _chkShowWireframe;
        private CheckBox _chkShowPolygon;

        private UITimer _typingTimer;

        public DrapeDialog(ObjRef[] targetRefs, ObjRef startingSrfRef, DrapeConduit conduit)
        {
            _targetRefs = targetRefs;
            _startingSrfRef = startingSrfRef;
            _conduit = conduit;

            Title = "CADacombs Drape";
            Resizable = false;
            Padding = new Padding(12);

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            CreateControls();
            SetupLayout();
            UpdateControlStates();

            UpdatePreview();
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => DrapeOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => DrapeOptions.WindowLocation = location;

        private void CreateControls()
        {
            _rbSelect = new RadioButton { Text = "Select:" };
            _rbCreate = new RadioButton(_rbSelect) { Text = "Create:" };

            if (DrapeOptions.UserProvidesStartingSrf) _rbSelect.Checked = true;
            else _rbCreate.Checked = true;

            EventHandler<EventArgs> rbChanged = (s, e) => 
            {
                DrapeOptions.UserProvidesStartingSrf = _rbSelect.Checked;
                UpdateControlStates();
                
                if (DrapeOptions.UserProvidesStartingSrf && _startingSrfRef == null)
                {
                    ClearPreview();
                    Action = DrapeDialogAction.PickCustomSurface;
                    Close();
                }
                else
                {
                    UpdatePreview();
                }
            };
            
            _rbSelect.CheckedChanged += rbChanged;
            _rbCreate.CheckedChanged += rbChanged;

            _btnReselSrf = new Button { Text = "Reselect" };
            _btnReselSrf.Click += (s, e) => { Action = DrapeDialogAction.PickCustomSurface; Close(); };

            _txtSpanSpacing = new TextBox { Text = DrapeOptions.SpanSpacing.ToString("G"), Width = 60 };
            _txtSpanSpacing.TextChanged += ResetTypingTimer;

            _stepSpansBeyond = new NumericStepper { Value = DrapeOptions.SpansBeyondEachSide, MinValue = -10, MaxValue = 50, DecimalPlaces = 0, Width = 60 };
            _stepSpansBeyond.ValueChanged += ResetTypingTimer;

            _chkFlipCPlane = new CheckBox { Text = "Flip drape direction", Checked = DrapeOptions.FlipCPlane };
            _chkFlipCPlane.CheckedChanged += (s, e) => UpdatePreview();

            _dropTargetMisses = new DropDown();
            _dropTargetMisses.Items.Add("Fix to starting surface");
            _dropTargetMisses.Items.Add("Use lowest neighbor hits");
            _dropTargetMisses.Items.Add("Linearly extrapolate from hits");
            _dropTargetMisses.SelectedIndex = DrapeOptions.TargetMisses;
            _dropTargetMisses.SelectedIndexChanged += (s, e) => UpdatePreview();

            _chkShowSurface = new CheckBox { Text = "Shaded surface", Checked = DrapeOptions.ShowSurface };
            _chkShowWireframe = new CheckBox { Text = "Wireframe", Checked = DrapeOptions.ShowWireframe };
            _chkShowPolygon = new CheckBox { Text = "Control polygon", Checked = DrapeOptions.ShowPolygon };

            _conduit.ShowSurface = DrapeOptions.ShowSurface;
            _conduit.ShowWireframe = DrapeOptions.ShowWireframe;
            _conduit.ShowPolygon = DrapeOptions.ShowPolygon;

            EventHandler<EventArgs> displayEvent = (s, e) => 
            {
                _conduit.ShowSurface = _chkShowSurface.Checked ?? true;
                _conduit.ShowWireframe = _chkShowWireframe.Checked ?? true;
                _conduit.ShowPolygon = _chkShowPolygon.Checked ?? false;
                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            };

            _chkShowSurface.CheckedChanged += displayEvent;
            _chkShowWireframe.CheckedChanged += displayEvent;
            _chkShowPolygon.CheckedChanged += displayEvent;
        }

        private void SetupLayout()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            // TARGETS
            layout.AddRow(new Label { Text = "Targets", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var btnAddRemTargets = new Button { Text = "Add / Remove" };
            btnAddRemTargets.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveTargets; Close(); };
            
            var btnReselTargets = new Button { Text = "Reselect" };
            btnReselTargets.Click += (s, e) => { ClearPreview(); Action = DrapeDialogAction.ReselectTargets; Close(); };

            var targetStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5, Items = { btnAddRemTargets, btnReselTargets, null } };
            layout.AddRow(targetStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            // STARTING SURFACE
            layout.AddRow(new Label { Text = "Starting surface", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var srfGrid = new DynamicLayout { Spacing = new Size(10, 5) };
            var selectStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5, Items = { _btnReselSrf, null } };
            
            var createGrid = new DynamicLayout { Spacing = new Size(10, 5) };
            createGrid.AddRow(new Label { Text = "Span spacing:", VerticalAlignment = VerticalAlignment.Center }, _txtSpanSpacing, null);
            createGrid.AddRow(new Label { Text = "Spans beyond target:", VerticalAlignment = VerticalAlignment.Center }, _stepSpansBeyond, null);

            srfGrid.AddRow(_rbSelect, selectStack, null);
            srfGrid.AddRow(_rbCreate, createGrid, null);
            
            layout.AddRow(srfGrid);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            // GENERAL
            layout.AddRow(new Label { Text = "General", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var genGrid = new DynamicLayout { Spacing = new Size(10, 5) };
            genGrid.AddRow(new Label { Text = "Action for misses:", VerticalAlignment = VerticalAlignment.Center }, _dropTargetMisses, null);
            layout.AddRow(genGrid);
            layout.AddRow(_chkFlipCPlane);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            // DISPLAY
            layout.AddRow(new Label { Text = "Display", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, Items = { _chkShowSurface, _chkShowWireframe, _chkShowPolygon } });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            // BUTTONS
            var btnOk = new Button { Text = "OK" };
            btnOk.Click += (s, e) => { Action = DrapeDialogAction.Ok; Close(); };
            var btnCancel = new Button { Text = "Cancel" };
            btnCancel.Click += (s, e) => { Action = DrapeDialogAction.Cancel; Close(); };

            DefaultButton = btnOk;
            AbortButton = btnCancel;

            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { btnOk, btnCancel } });

            Content = layout;
        }

        private void UpdateControlStates()
        {
            bool isSelect = _rbSelect.Checked;
            
            _btnReselSrf.Enabled = isSelect;
            _txtSpanSpacing.Enabled = !isSelect;
            _stepSpansBeyond.Enabled = !isSelect;
        }

        private void ResetTypingTimer(object sender, EventArgs e)
        {
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void OnTypingTimerElapsed(object sender, EventArgs e)
        {
            _typingTimer.Stop();
            Application.Instance.AsyncInvoke(UpdatePreview);
        }

        private void ClearPreview()
        {
            ResultSurface = null;
            _conduit.PreviewSurface = null;
            _conduit.PreviewBrep = null;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
        }

        private void UpdatePreview()
        {
            if (_targetRefs == null || _targetRefs.Length == 0) return;
            
            bool isSelect = DrapeOptions.UserProvidesStartingSrf;

            if (isSelect && _startingSrfRef == null)
            {
                ClearPreview();
                return;
            }

            if (double.TryParse(_txtSpanSpacing.Text, out double spacing) && spacing > Rhino.RhinoMath.ZeroTolerance)
            {
                DrapeOptions.SpanSpacing = spacing;
                _txtSpanSpacing.BackgroundColor = Colors.White;
            }
            else
            {
                _txtSpanSpacing.BackgroundColor = Colors.LightPink;
                return;
            }
                
            DrapeOptions.SpansBeyondEachSide = (int)_stepSpansBeyond.Value;
            DrapeOptions.FlipCPlane = _chkFlipCPlane.Checked ?? false;
            DrapeOptions.TargetMisses = _dropTargetMisses.SelectedIndex;

            string cacheKey = isSelect 
                ? $"Select_{DrapeOptions.TargetMisses}_{DrapeOptions.FlipCPlane}" 
                : $"Create_{DrapeOptions.SpanSpacing}_{DrapeOptions.SpansBeyondEachSide}_{DrapeOptions.TargetMisses}_{DrapeOptions.FlipCPlane}";

            if (_previewCache.TryGetValue(cacheKey, out var cachedData))
            {
                ResultSurface = cachedData.Item1;
                _conduit.PreviewSurface = ResultSurface;
                _conduit.PreviewBrep = cachedData.Item2;
            }
            else
            {
                ObjRef activeStartingSrf = isSelect ? _startingSrfRef : null;

                ResultSurface = DrapeLogic.ComputeDrapeSurface(Rhino.RhinoDoc.ActiveDoc, _targetRefs, activeStartingSrf);
                
                _conduit.PreviewSurface = ResultSurface;
                _conduit.PreviewBrep = ResultSurface?.ToBrep();

                _previewCache[cacheKey] = (ResultSurface, _conduit.PreviewBrep);
            }

            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
        }

        protected override void OnClosed(EventArgs e)
        {
            _typingTimer.Stop();
            _typingTimer.Dispose();

            DrapeOptions.ShowSurface = _chkShowSurface.Checked ?? true;
            DrapeOptions.ShowWireframe = _chkShowWireframe.Checked ?? true;
            DrapeOptions.ShowPolygon = _chkShowPolygon.Checked ?? false;

            base.OnClosed(e);
        }
    }
}