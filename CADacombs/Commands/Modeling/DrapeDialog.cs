using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling
{
    public enum DrapeDialogAction { None, Ok, Cancel, AddRemoveTargets, ReselectTargets, PickCustomSurface }

    public class DrapeDialog : CADacombsDialogBase
    {
        public DrapeDialogAction Action { get; set; } = DrapeDialogAction.None;
        public List<NurbsSurface> ResultSurfaces { get; private set; }

        private ObjRef[] _targetRefs;
        private List<ObjRef> _startingSrfRefs;
        private DrapeConduit _conduit;

        // Cache includes List<string> for messages and a double for MaxDeviation
        private Dictionary<string, (List<NurbsSurface>, List<Brep>, bool, List<string>, double)> _previewCache = new Dictionary<string, (List<NurbsSurface>, List<Brep>, bool, List<string>, double)>();

        private RadioButton _rbSelect;
        private RadioButton _rbCreate;
        private Button _btnReselSrf;

        private TextBox _txtSpanSpacing;
        private NumericStepper _stepSpansBeyond;
        
        private DropDown _dropFitMethod;
        private Label _lblSrfWarning;
        private TextBox _txtTolerance;
        private NumericStepper _stepTimeout;
        private DropDown _dropTargetMisses;
        private CheckBox _chkFlipCPlane;
        
        private CheckBox _chkShowSurface;
        private CheckBox _chkShowWireframe;
        private CheckBox _chkShowPolygon;

        private Label _lblProcessedCount;
        private Label _lblMaxDev;
        private ProgressBar _progressBar;
        
        private Button _btnPreview;
        private Button _btnOk;
        private Button _btnCancel;

        private bool _previewPending = false;
        private bool _isUpdatingTextProgrammatically = false;
        private UITimer _typingTimer;

        public DrapeDialog(ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, DrapeConduit conduit)
        {
            _targetRefs = targetRefs;
            _startingSrfRefs = startingSrfRefs;
            _conduit = conduit;

            Title = "CADacombs Drape";
            Resizable = false;
            AutoSize = true; // Essential for dynamic layout expansion
            Padding = new Padding(12);

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            CreateControls();
            SetupLayout();
            UpdateControlStates();

            UpdatePreview(true);
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
                
                if (DrapeOptions.UserProvidesStartingSrf && (_startingSrfRefs == null || _startingSrfRefs.Count == 0))
                {
                    ClearPreview();
                    Action = DrapeDialogAction.PickCustomSurface;
                    Close();
                }
                else
                {
                    UpdatePreview(true);
                }
            };
            
            _rbSelect.CheckedChanged += rbChanged;
            _rbCreate.CheckedChanged += rbChanged;

            _btnReselSrf = new Button { Text = "Reselect" };
            _btnReselSrf.Click += (s, e) => { Action = DrapeDialogAction.PickCustomSurface; Close(); };

            _txtSpanSpacing = new TextBox { Text = DrapeOptions.SpanSpacing.ToString("G"), Width = 60 };
            _txtSpanSpacing.TextChanged += OnToleranceTextChanged;

            _stepSpansBeyond = new NumericStepper { Value = DrapeOptions.SpansBeyondEachSide, MinValue = -10, MaxValue = 50, DecimalPlaces = 0, Width = 60 };
            _stepSpansBeyond.ValueChanged += OnOptionChanged;

            _dropFitMethod = new DropDown();
            _dropFitMethod.Items.Add("Drape with skirted borders");
            _dropFitMethod.Items.Add("Drape with hugging borders");
            _dropFitMethod.Items.Add("Project Greville points");
            _dropFitMethod.Items.Add("Project control points");
            _dropFitMethod.SelectedIndex = DrapeOptions.FitMethod;
            _dropFitMethod.SelectedIndexChanged += OnOptionChanged;

            _lblSrfWarning = new Label 
            { 
                Text = "Warning: Drape methods work best with starting surfaces\nof degree 3 with simple interior knots.", 
                TextColor = Colors.Red, 
                Visible = false,
                Wrap = WrapMode.Word,
                Font = new Eto.Drawing.Font(SystemFont.Default, 8)
            };

            _txtTolerance = new TextBox { Text = DrapeOptions.Tolerance.ToString("G"), Width = 60 };
            _txtTolerance.TextChanged += OnToleranceTextChanged;

            _stepTimeout = new NumericStepper { Value = DrapeOptions.SolverTimeout, MinValue = 0.1, MaxValue = 60.0, DecimalPlaces = 1, Width = 60, Increment = 0.5 };
            _stepTimeout.ValueChanged += OnOptionChanged;

            _chkFlipCPlane = new CheckBox { Text = "Flip drape direction", Checked = DrapeOptions.FlipCPlane };
            _chkFlipCPlane.CheckedChanged += OnOptionChanged;

            _dropTargetMisses = new DropDown();
            _dropTargetMisses.Items.Add("Fix to starting surface");
            _dropTargetMisses.Items.Add("Use lowest neighbor hits");
            _dropTargetMisses.Items.Add("Linearly extrapolate from hits");
            _dropTargetMisses.SelectedIndex = DrapeOptions.TargetMisses;
            _dropTargetMisses.SelectedIndexChanged += OnOptionChanged;

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

            _lblProcessedCount = new Label { Text = "Processed 0 surface(s)." };
            _lblMaxDev = new Label { Text = "Max dev: 0", TextColor = Colors.DimGray };
            _progressBar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 0, Visible = false, Height = 10 };

            _btnPreview = new Button { Text = "Preview", Width = 75, Enabled = false };
            _btnPreview.Click += (s, e) => UpdatePreview(false);

            _btnOk = new Button { Text = "OK" };
            _btnOk.Click += (s, e) => 
            { 
                if (_previewPending) UpdatePreview(false);
                Action = DrapeDialogAction.Ok; 
                Close(); 
            };
            
            _btnCancel = new Button { Text = "Cancel" };
            _btnCancel.Click += (s, e) => { Action = DrapeDialogAction.Cancel; Close(); };

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;
        }

        private void SetupLayout()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            layout.AddRow(new Label { Text = "Targets", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var btnAddRemTargets = new Button { Text = "Add / Remove" };
            btnAddRemTargets.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveTargets; Close(); };
            
            var btnReselTargets = new Button { Text = "Reselect" };
            btnReselTargets.Click += (s, e) => { ClearPreview(); Action = DrapeDialogAction.ReselectTargets; Close(); };

            var targetStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5, Items = { btnAddRemTargets, btnReselTargets, null } };
            layout.AddRow(targetStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

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

            layout.AddRow(new Label { Text = "General", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var genGrid = new DynamicLayout { Spacing = new Size(10, 5) };
            
            // Dropdown indented on a new row
            genGrid.AddRow(new Label { Text = "Fit method:", VerticalAlignment = VerticalAlignment.Center });
            var fitStack = new StackLayout { Padding = new Padding(15, 0, 0, 0), Items = { _dropFitMethod } };
            genGrid.AddRow(fitStack);
            
            var warnStack = new StackLayout { Padding = new Padding(15, 0, 0, 0), Items = { _lblSrfWarning } };
            genGrid.AddRow(warnStack);

            // Wrapped in StackLayouts so they don't stretch fully right
            var tolStack = new StackLayout { Orientation = Orientation.Horizontal, Items = { _txtTolerance } };
            genGrid.AddRow(new Label { Text = "Tolerance:", VerticalAlignment = VerticalAlignment.Center }, tolStack, null);
            
            var missesStack = new StackLayout { Orientation = Orientation.Horizontal, Items = { _dropTargetMisses } };
            genGrid.AddRow(new Label { Text = "Action for misses:", VerticalAlignment = VerticalAlignment.Center }, missesStack, null);
            
            layout.AddRow(genGrid);

            // Flip CPlane moved above Timeout
            layout.AddRow(new StackLayout { Padding = new Padding(0, 5, 0, 5), Items = { _chkFlipCPlane } });

            // Timeout moved to the bottom of General section
            var timeoutPreviewStack = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { new Label { Text = "Solver timeout (s):", VerticalAlignment = VerticalAlignment.Center }, _stepTimeout, new Label { Width = 10 }, _btnPreview }
            };
            layout.AddRow(timeoutPreviewStack);
            
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(new Label { Text = "Display", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, Items = { _chkShowSurface, _chkShowWireframe, _chkShowPolygon } });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            
            layout.AddRow(_progressBar);
            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, Items = { _lblProcessedCount, _lblMaxDev } });
            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { _btnOk, _btnCancel } });

            Content = layout;
        }

        private void UpdateControlStates()
        {
            bool isSelect = _rbSelect.Checked;
            
            _btnReselSrf.Enabled = isSelect;
            _txtSpanSpacing.Enabled = !isSelect;
            _stepSpansBeyond.Enabled = !isSelect;
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            UpdatePreview(true);
        }

        private void OnToleranceTextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingTextProgrammatically) return;
            
            _previewPending = true;
            _lblProcessedCount.Text = "Waiting for input...";
            _lblMaxDev.Text = "";
            
            _btnPreview.Enabled = false;
            _btnPreview.TextColor = SystemColors.ControlText;
            _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Default, _btnPreview.Font.Size);

            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void OnTypingTimerElapsed(object sender, EventArgs e)
        {
            _typingTimer.Stop();
            Application.Instance.AsyncInvoke(() => UpdatePreview(true));
        }

        private void SetPreviewRequired(string message)
        {
            _previewPending = true;
            _btnPreview.Enabled = true;
            _btnPreview.TextColor = Colors.Red;
            _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Bold, _btnPreview.Font.Size);
            _lblProcessedCount.Text = message;
            _lblMaxDev.Text = "";
            
            // Force redraw height
            if (ParentWindow != null) this.Size = new Size(this.Width, -1);
        }

        private void ClearPreviewPending(int processedCount, double maxDev)
        {
            _previewPending = false;
            _btnPreview.Enabled = false;
            _btnPreview.TextColor = SystemColors.ControlText;
            _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Default, _btnPreview.Font.Size);

            _lblProcessedCount.Text = $"Processed {processedCount} surface(s).";

            if (DrapeOptions.FitMethod == 2)
            {
                int prec = Rhino.RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
                _lblMaxDev.Text = $"Max dev: {FormatUtils.FormatDistance(maxDev, prec)}";
            }
            else
            {
                _lblMaxDev.Text = "";
            }
        }

        private bool IsStartingSrfSupported(NurbsSurface ns)
        {
            if (ns == null) return false;
            if (ns.Degree(0) != 3 || ns.Degree(1) != 3) return false;
            if (ns.IsClosed(0) || ns.IsClosed(1)) return false;

            for (int iDir = 0; iDir < 2; iDir++)
            {
                var knots = iDir == 1 ? ns.KnotsV : ns.KnotsU;
                int degree = ns.Degree(iDir);
                int iK = ns.IsPeriodic(iDir) ? 0 : degree;
                int count = ns.IsPeriodic(iDir) ? knots.Count : knots.Count - degree;
                
                while (iK < count)
                {
                    if (knots.KnotMultiplicity(iK) > 1) return false;
                    iK++;
                }
            }
            return true;
        }

        private void ClearPreview()
        {
            ResultSurfaces = null;
            _conduit.PreviewSurfaces = null;
            _conduit.PreviewBreps = null;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
        }

        private void UpdatePreview(bool autoRun = true)
        {
            if (_targetRefs == null || _targetRefs.Length == 0) return;
            
            bool isSelect = DrapeOptions.UserProvidesStartingSrf;

            if (isSelect && (_startingSrfRefs == null || _startingSrfRefs.Count == 0))
            {
                ClearPreview();
                return;
            }

            _isUpdatingTextProgrammatically = true;
            bool allValid = true;

            if (double.TryParse(_txtSpanSpacing.Text, out double spacing) && spacing > Rhino.RhinoMath.ZeroTolerance)
            {
                DrapeOptions.SpanSpacing = spacing;
                _txtSpanSpacing.BackgroundColor = Colors.White;
            }
            else
            {
                _txtSpanSpacing.BackgroundColor = Colors.LightPink;
                allValid = false;
            }

            if (double.TryParse(_txtTolerance.Text, out double tol) && tol >= Rhino.RhinoMath.ZeroTolerance)
            {
                DrapeOptions.Tolerance = tol;
                _txtTolerance.BackgroundColor = Colors.White;
            }
            else
            {
                _txtTolerance.BackgroundColor = Colors.LightPink;
                allValid = false;
            }
            
            _isUpdatingTextProgrammatically = false;

            if (!allValid)
            {
                SetPreviewRequired("Invalid tolerance entered.");
                return;
            }
                
            DrapeOptions.SpansBeyondEachSide = (int)_stepSpansBeyond.Value;
            DrapeOptions.SolverTimeout = _stepTimeout.Value;
            DrapeOptions.FlipCPlane = _chkFlipCPlane.Checked ?? false;
            DrapeOptions.TargetMisses = _dropTargetMisses.SelectedIndex;
            DrapeOptions.FitMethod = _dropFitMethod.SelectedIndex;

            bool showWarning = false;
            if (isSelect && _startingSrfRefs != null && _startingSrfRefs.Count > 0 && (DrapeOptions.FitMethod == 0 || DrapeOptions.FitMethod == 1))
            {
                foreach(var srfRef in _startingSrfRefs)
                {
                    Surface srf = srfRef.Surface();
                    if (srf == null && srfRef.Brep()?.Faces.Count == 1)
                        srf = srfRef.Brep().Faces[0].UnderlyingSurface();
                        
                    if (srf != null && !IsStartingSrfSupported(srf.ToNurbsSurface()))
                    {
                        showWarning = true;
                        break;
                    }
                }
            }
            
            // Dynamic resizing logic for the warning toggle
            bool oldVisible = _lblSrfWarning.Visible;
            _lblSrfWarning.Visible = showWarning;
            if (oldVisible != showWarning && ParentWindow != null)
            {
                this.Size = new Size(this.Width, -1);
            }

            string cacheKey = isSelect 
                ? $"Select_{DrapeOptions.TargetMisses}_{DrapeOptions.FlipCPlane}_{DrapeOptions.FitMethod}_{DrapeOptions.Tolerance}_{DrapeOptions.SolverTimeout}" 
                : $"Create_{DrapeOptions.SpanSpacing}_{DrapeOptions.SpansBeyondEachSide}_{DrapeOptions.TargetMisses}_{DrapeOptions.FlipCPlane}_{DrapeOptions.FitMethod}_{DrapeOptions.Tolerance}_{DrapeOptions.SolverTimeout}";

            int expectedCount = isSelect ? _startingSrfRefs.Count : 1;

            if (_previewCache.TryGetValue(cacheKey, out var cachedData))
            {
                ResultSurfaces = cachedData.Item1;
                _conduit.PreviewSurfaces = ResultSurfaces;
                _conduit.PreviewBreps = cachedData.Item2;
                _dropTargetMisses.Enabled = cachedData.Item3;
                
                ClearPreviewPending(expectedCount, cachedData.Item5);

                foreach (var msg in cachedData.Item4)
                {
                    if (!string.IsNullOrEmpty(msg)) Rhino.RhinoApp.WriteLine(msg);
                }
            }
            else
            {
                _progressBar.Visible = true;
                _progressBar.Value = 0;
                _progressBar.MaxValue = expectedCount;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);

                List<ObjRef> activeStartingSrfs = isSelect ? _startingSrfRefs : null;

                Action progressCallback = () => 
                { 
                    _progressBar.Value++; 
                    Rhino.RhinoApp.Wait(); 
                };

                ResultSurfaces = DrapeLogic.ComputeDrapeSurfaces(Rhino.RhinoDoc.ActiveDoc, _targetRefs, activeStartingSrfs, out bool hasMisses, out List<string> messages, out double maxDev, progressCallback);
                
                _conduit.PreviewSurfaces = ResultSurfaces;
                _conduit.PreviewBreps = ResultSurfaces?.Select(s => s?.ToBrep()).ToList();
                _dropTargetMisses.Enabled = hasMisses;

                ClearPreviewPending(expectedCount, maxDev);

                foreach (var msg in messages)
                {
                    if (!string.IsNullOrEmpty(msg)) Rhino.RhinoApp.WriteLine(msg);
                }

                _previewCache[cacheKey] = (ResultSurfaces, _conduit.PreviewBreps, hasMisses, messages, maxDev);
                _progressBar.Visible = false;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);
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