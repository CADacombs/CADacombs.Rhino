using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling
{
    // Note: Re-use DrapeDialogAction or define a specific FitSrfToTargetDialogAction
    public class FitSrfToTargetDialog : CADacombsDialogBase
    {
        public DrapeDialogAction Action { get; set; } = DrapeDialogAction.None;
        public List<NurbsSurface> ResultSurfaces { get; private set; }

        private ObjRef[] _targetRefs;
        private List<ObjRef> _startingSrfRefs;
        private DrapeConduit _conduit; // Can rename to FitSrfConduit later

        // UI Elements
        private Button _btnReselSrf;
        private Button _btnAddRemSrf;
        
        private DropDown _dropMappingDirection;
        private DropDown _dropSolverType;
        
        // Advanced Solver Options
        private DropDown _dropSamplingMode;
        private DropDown _dropWeightCurve;
        private NumericStepper _stepPercentDomain;

        private TextBox _txtTolerance;
        private NumericStepper _stepTimeout;
        private DropDown _dropOutputLayer;
        private CheckBox _chkDeleteInput;
        
        private CheckBox _chkShowSurface;
        private CheckBox _chkShowWireframe;
        private CheckBox _chkShowPolygon;

        private Label _lblProcessedCount;
        private Label _lblMaxDev;
        private ProgressBar _progressBar;
        
        private Button _btnPreview;
        private Button _btnOk;
        private Button _btnCancel;

        private bool _isComputing = false;
        private bool _cancelRequested = false;
        private bool _previewPending = false;
        private bool _isUpdatingTextProgrammatically = false;
        private UITimer _typingTimer;

        public FitSrfToTargetDialog(ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, DrapeConduit conduit)
        {
            _targetRefs = targetRefs;
            _startingSrfRefs = startingSrfRefs;
            _conduit = conduit;

            Title = "CADacombs Fit Surface To Target";
            Resizable = false;
            AutoSize = true; 
            Padding = new Padding(12);

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            CreateControls();
            SetupLayout();
            UpdateAdvancedOptionsVisibility();

            // Fire initial preview if valid
            if (_startingSrfRefs != null && _startingSrfRefs.Count > 0)
            {
                UpdatePreview(true);
            }
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => FitSrfToTargetOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => FitSrfToTargetOptions.WindowLocation = location;

        private void CreateControls()
        {
            _btnReselSrf = new Button { Text = "Reselect" };
            _btnReselSrf.Click += (s, e) => { Action = DrapeDialogAction.PickCustomSurface; Close(); };
            
            _btnAddRemSrf = new Button { Text = "Add / Remove" };
            _btnAddRemSrf.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveStartingSurfaces; Close(); };

            _dropMappingDirection = new DropDown();
            _dropMappingDirection.Items.Add("Closest Point (Pull)");
            _dropMappingDirection.Items.Add("Z-Axis (Raycast Projection)");
            _dropMappingDirection.SelectedIndex = FitSrfToTargetOptions.MappingDirection;
            _dropMappingDirection.SelectedIndexChanged += OnOptionChanged;

            _dropSolverType = new DropDown();
            _dropSolverType.Items.Add("Direct Control Point");
            _dropSolverType.Items.Add("Direct Greville");
            _dropSolverType.Items.Add("Python Port (Legacy)");
            _dropSolverType.Items.Add("Weighted / Relaxed");
            _dropSolverType.SelectedIndex = FitSrfToTargetOptions.SolverType;
            _dropSolverType.SelectedIndexChanged += (s, e) => { UpdateAdvancedOptionsVisibility(); OnOptionChanged(s, e); };

            _dropSamplingMode = new DropDown();
            _dropSamplingMode.Items.Add("Distributed");
            _dropSamplingMode.Items.Add("NearestPoint");
            _dropSamplingMode.SelectedIndex = FitSrfToTargetOptions.SamplingMode;
            _dropSamplingMode.SelectedIndexChanged += OnOptionChanged;

            _dropWeightCurve = new DropDown();
            _dropWeightCurve.Items.Add("Linear");
            _dropWeightCurve.Items.Add("Hyperbolic");
            _dropWeightCurve.SelectedIndex = FitSrfToTargetOptions.WeightCurve;
            _dropWeightCurve.SelectedIndexChanged += OnOptionChanged;

            _stepPercentDomain = new NumericStepper { Value = FitSrfToTargetOptions.PercentOfDomain, MinValue = 0.001, MaxValue = 100.0, DecimalPlaces = 3, Width = 80, Increment = 1.0 };
            _stepPercentDomain.ValueChanged += OnOptionChanged;

            _txtTolerance = new TextBox { Text = FitSrfToTargetOptions.Tolerance.ToString("G"), Width = 80 };
            _txtTolerance.TextChanged += OnToleranceTextChanged;

            _stepTimeout = new NumericStepper { Value = FitSrfToTargetOptions.SolverTimeout, MinValue = 0.1, MaxValue = 60.0, DecimalPlaces = 1, Width = 80, Increment = 0.5 };
            _stepTimeout.ValueChanged += OnOptionChanged;

            _dropOutputLayer = new DropDown();
            _dropOutputLayer.Items.Add("Input");
            _dropOutputLayer.Items.Add("Current");
            _dropOutputLayer.Items.Add("TargetObject");
            _dropOutputLayer.SelectedIndex = FitSrfToTargetOptions.OutputLayer;
            _dropOutputLayer.SelectedIndexChanged += (s, e) => 
            {
                FitSrfToTargetOptions.OutputLayer = _dropOutputLayer.SelectedIndex;
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            };

            _chkDeleteInput = new CheckBox { Text = "Delete input", Checked = FitSrfToTargetOptions.DeleteStartingSrf };
            _chkDeleteInput.CheckedChanged += (s, e) => 
            { 
                FitSrfToTargetOptions.DeleteStartingSrf = _chkDeleteInput.Checked ?? false; 
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            };

            _chkShowSurface = new CheckBox { Text = "Shaded", Checked = FitSrfToTargetOptions.ShowSurface };
            _chkShowWireframe = new CheckBox { Text = "Wireframe", Checked = FitSrfToTargetOptions.ShowWireframe };
            _chkShowPolygon = new CheckBox { Text = "CPolygon", Checked = FitSrfToTargetOptions.ShowPolygon };

            EventHandler<EventArgs> displayEvent = (s, e) => 
            {
                _conduit.ShowSurface = _chkShowSurface.Checked ?? true;
                _conduit.ShowWireframe = _chkShowWireframe.Checked ?? true;
                _conduit.ShowPolygon = _chkShowPolygon.Checked ?? false;
                RhinoDoc.ActiveDoc.Views.Redraw();
            };
            _chkShowSurface.CheckedChanged += displayEvent;
            _chkShowWireframe.CheckedChanged += displayEvent;
            _chkShowPolygon.CheckedChanged += displayEvent;

            _lblProcessedCount = new Label { Text = "Processed 0 surface(s)." };
            _lblMaxDev = new Label { Text = "", TextColor = Colors.DimGray, Wrap = WrapMode.Word };
            _progressBar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 0, Visible = false, Height = 10 };

            _btnPreview = new Button { Text = "Preview", Width = 75, Enabled = false };
            _btnPreview.Click += (s, e) => { /* Preview Logic Placeholder */ };

            _btnOk = new Button { Text = "OK", Width = 75 };
            _btnOk.Click += (s, e) => { Action = DrapeDialogAction.Ok; Close(); };
            
            _btnCancel = new Button { Text = "Cancel", Width = 75 };
            _btnCancel.Click += (s, e) => { Action = DrapeDialogAction.Cancel; Close(); };

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;
        }

        private void SetupLayout()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            var btnAddRemTargets = new Button { Text = "Add / Remove" };
            btnAddRemTargets.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveTargets; Close(); };
            var btnReselTargets = new Button { Text = "Reselect" };
            btnReselTargets.Click += (s, e) => { Action = DrapeDialogAction.ReselectTargets; Close(); };

            layout.AddRow(new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { new Label { Text = "Targets", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, btnAddRemTargets, btnReselTargets } 
            });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { new Label { Text = "Starting Surface", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, _btnAddRemSrf, _btnReselSrf } 
            });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(new Label { Text = "Solver Options", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var tableSolver = new TableLayout { Spacing = new Size(10, 5) };
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Mapping Dir:", VerticalAlignment = VerticalAlignment.Center }, _dropMappingDirection, null));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Solver Type:", VerticalAlignment = VerticalAlignment.Center }, _dropSolverType, null));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Sampling Mode:", VerticalAlignment = VerticalAlignment.Center }, _dropSamplingMode, null));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Weight Curve:", VerticalAlignment = VerticalAlignment.Center }, _dropWeightCurve, null));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Domain %:", VerticalAlignment = VerticalAlignment.Center }, _stepPercentDomain, null));
            layout.AddRow(tableSolver);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            
            var tableGen = new TableLayout { Spacing = new Size(10, 5) };
            tableGen.Rows.Add(new TableRow(new Label { Text = "Tolerance:", VerticalAlignment = VerticalAlignment.Center }, _txtTolerance, null));
            tableGen.Rows.Add(new TableRow(new Label { Text = "Timeout (s):", VerticalAlignment = VerticalAlignment.Center }, _stepTimeout, null));
            tableGen.Rows.Add(new TableRow(new Label { Text = "Output layer:", VerticalAlignment = VerticalAlignment.Center }, _dropOutputLayer, null));
            layout.AddRow(tableGen);

            layout.AddRow(_chkDeleteInput);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            var displayHeaderStack = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { new Label { Text = "Display", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, _chkShowSurface, _chkShowWireframe, _chkShowPolygon } 
            };
            layout.AddRow(displayHeaderStack);
            
            layout.AddRow(_progressBar);
            layout.AddRow(new StackLayout { Orientation = Orientation.Vertical, Spacing = 5, Items = { _lblProcessedCount, _lblMaxDev } });
            layout.AddRow(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { _btnPreview, new Panel{Width=20}, _btnOk, _btnCancel } });

            Content = layout;
        }

        private void UpdateAdvancedOptionsVisibility()
        {
            // Only show Sampling Mode, Weight Curve, and Domain % for solvers 2 (Python) and 3 (Weighted/Relaxed)
            bool isAdvanced = _dropSolverType.SelectedIndex >= 2;
            _dropSamplingMode.Enabled = isAdvanced;
            _dropWeightCurve.Enabled = isAdvanced;
            _stepPercentDomain.Enabled = isAdvanced;
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            FitSrfToTargetOptions.MappingDirection = _dropMappingDirection.SelectedIndex;
            FitSrfToTargetOptions.SolverType = _dropSolverType.SelectedIndex;
            FitSrfToTargetOptions.SamplingMode = _dropSamplingMode.SelectedIndex;
            FitSrfToTargetOptions.WeightCurve = _dropWeightCurve.SelectedIndex;
            FitSrfToTargetOptions.PercentOfDomain = _stepPercentDomain.Value;

            UpdatePreview(true);
        }

        private void OnToleranceTextChanged(object sender, EventArgs e) { /* Identical to DrapeDialog */ }
        private void OnTypingTimerElapsed(object sender, EventArgs e) { /* Identical to DrapeDialog */ }

        // Cache to prevent recalculating if the user clicks around the UI without changing solver parameters
        private Dictionary<string, (List<NurbsSurface>, List<string>, double)> _previewCache = new Dictionary<string, (List<NurbsSurface>, List<string>, double)>();

        private void SetPreviewRequired(string message)
        {
            _previewPending = true;
            _btnPreview.Enabled = true;
            _btnPreview.Text = "Preview";
            _btnPreview.TextColor = Colors.Red;
            _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Bold, _btnPreview.Font.Size);
            _lblProcessedCount.Text = message;
            _lblMaxDev.Text = "";
            _lblMaxDev.TextColor = Colors.DimGray;
            _lblMaxDev.Font = new Eto.Drawing.Font(SystemFont.Default, _lblMaxDev.Font.Size);
            
            if (ParentWindow != null) this.Size = new Size(this.Width, -1);
        }

        private void ClearPreviewPending(int processedCount, double maxDev)
        {
            _previewPending = false;
            _btnPreview.Enabled = false;
            _btnPreview.Text = "Preview";
            _btnPreview.TextColor = SystemColors.ControlText;
            _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Default, _btnPreview.Font.Size);

            _lblProcessedCount.Text = $"Processed {processedCount} surface(s).";

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            _lblMaxDev.Text = $"Max. deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";
            
            if (maxDev > FitSrfToTargetOptions.Tolerance)
            {
                _lblMaxDev.TextColor = Colors.DarkRed;
                _lblMaxDev.Font = new Eto.Drawing.Font(SystemFont.Bold, _lblMaxDev.Font.Size);
            }
            else
            {
                _lblMaxDev.TextColor = Colors.DimGray;
                _lblMaxDev.Font = new Eto.Drawing.Font(SystemFont.Default, _lblMaxDev.Font.Size);
            }
        }

        private void ClearPreview()
        {
            ResultSurfaces = null;
            _conduit.PreviewSurfaces = null;
            _conduit.PreviewBreps = null;
            _conduit.PreviewColors.Clear();
            RhinoDoc.ActiveDoc.Views.Redraw();
        }

        private void UpdateConduitColors()
        {
            if (ResultSurfaces == null) return;

            var doc = RhinoDoc.ActiveDoc;
            var colors = new List<System.Drawing.Color>();

            for (int i = 0; i < ResultSurfaces.Count; i++)
            {
                if (ResultSurfaces[i] == null)
                {
                    colors.Add(System.Drawing.Color.Black);
                    continue;
                }

                System.Drawing.Color c = doc.Layers.CurrentLayer.Color;
                
                if (FitSrfToTargetOptions.OutputLayer == 0 && _startingSrfRefs != null && i < _startingSrfRefs.Count && _startingSrfRefs[i].Object() != null)
                {
                    var attr = _startingSrfRefs[i].Object().Attributes;
                    c = attr.ColorSource == ObjectColorSource.ColorFromObject ? attr.ObjectColor : doc.Layers[attr.LayerIndex].Color;
                }
                else if (FitSrfToTargetOptions.OutputLayer == 2 && _targetRefs != null && _targetRefs.Length > 0 && _targetRefs[0].Object() != null)
                {
                    var attr = _targetRefs[0].Object().Attributes;
                    c = attr.ColorSource == ObjectColorSource.ColorFromObject ? attr.ObjectColor : doc.Layers[attr.LayerIndex].Color;
                }
                
                colors.Add(c);
            }
            _conduit.PreviewColors = colors;
        }

        private void UpdatePreview(bool autoRun = true)
        {
            if (_targetRefs == null || _targetRefs.Length == 0) return;
            
            if (_startingSrfRefs == null || _startingSrfRefs.Count == 0)
            {
                ClearPreview();
                return;
            }

            double minTol = 1e-6 * RhinoMath.UnitScale(UnitSystem.Millimeters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            
            _isUpdatingTextProgrammatically = true;
            bool allValid = true;

            if (double.TryParse(_txtTolerance.Text, out double tol))
            {
                if (tol < 0) tol = 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                if (tol < minTol) tol = minTol;
                
                FitSrfToTargetOptions.Tolerance = tol;
                _txtTolerance.Text = tol.ToString("G");
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
                SetPreviewRequired("Invalid parameter entered.");
                return;
            }
                
            FitSrfToTargetOptions.SolverTimeout = _stepTimeout.Value;
            FitSrfToTargetOptions.MappingDirection = _dropMappingDirection.SelectedIndex;
            FitSrfToTargetOptions.SolverType = _dropSolverType.SelectedIndex;
            FitSrfToTargetOptions.SamplingMode = _dropSamplingMode.SelectedIndex;
            FitSrfToTargetOptions.WeightCurve = _dropWeightCurve.SelectedIndex;
            FitSrfToTargetOptions.PercentOfDomain = _stepPercentDomain.Value;

            string cacheKey = $"{FitSrfToTargetOptions.MappingDirection}_{FitSrfToTargetOptions.SolverType}_{FitSrfToTargetOptions.Tolerance}_{FitSrfToTargetOptions.SolverTimeout}_{FitSrfToTargetOptions.SamplingMode}_{FitSrfToTargetOptions.WeightCurve}_{FitSrfToTargetOptions.PercentOfDomain}";

            int expectedCount = _startingSrfRefs.Count;

            if (_previewCache.TryGetValue(cacheKey, out var cachedData))
            {
                ResultSurfaces = cachedData.Item1;
                _conduit.PreviewSurfaces = ResultSurfaces;
                _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();
                
                ClearPreviewPending(expectedCount, cachedData.Item3);

                foreach (var msg in cachedData.Item2)
                {
                    if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
                }
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            }
            else
            {
                _isComputing = true;
                _cancelRequested = false;
                _btnPreview.Text = "Stop";
                _btnPreview.TextColor = Colors.Red;
                _btnPreview.Font = new Eto.Drawing.Font(SystemFont.Bold, _btnPreview.Font.Size);
                _btnPreview.Enabled = true;

                _progressBar.Visible = true;
                _progressBar.Value = 0;
                _progressBar.MaxValue = expectedCount;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);

                Action progressCallback = delegate 
                { 
                    _progressBar.Value++; 
                    RhinoApp.Wait(); 
                };

                Func<bool> checkCancel = delegate
                {
                    RhinoApp.Wait();
                    return _cancelRequested;
                };

                try
                {
                    List<string> messages;
                    double maxDev;

                    ResultSurfaces = FitSrfToTargetLogic.ComputeFitSurfaces(RhinoDoc.ActiveDoc, _targetRefs, _startingSrfRefs, out messages, out maxDev, progressCallback, checkCancel);
                    
                    _conduit.PreviewSurfaces = ResultSurfaces;
                    _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();

                    ClearPreviewPending(expectedCount, maxDev);

                    foreach (var msg in messages)
                    {
                        if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
                    }

                    _previewCache[cacheKey] = (ResultSurfaces, messages, maxDev);
                    
                    UpdateConduitColors();
                    RhinoDoc.ActiveDoc.Views.Redraw();
                }
                catch (OperationCanceledException)
                {
                    SetPreviewRequired("Calculation cancelled. Click Preview to compute.");
                    ClearPreview();
                }
                finally
                {
                    _isComputing = false;
                    _progressBar.Visible = false;
                    if (ParentWindow != null) this.Size = new Size(this.Width, -1);
                }
            }
        }
    }
}