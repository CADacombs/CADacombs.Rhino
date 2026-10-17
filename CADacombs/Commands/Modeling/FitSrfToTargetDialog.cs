using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling
{
    public class FitSrfConduit : DrapeConduit
    {
        public List<Line> PreviewErrorLines { get; set; } = new List<Line>();
        public Line PreviewMaxErrorLine { get; set; } = Line.Unset;
        public string PreviewMaxErrorText { get; set; } = "";
        public bool ShowErrorLines { get; set; } = false;

        protected override void DrawForeground(Rhino.Display.DrawEventArgs e)
        {
            base.DrawForeground(e);
            if (ShowErrorLines && PreviewErrorLines != null)
            {
                e.Display.DrawLines(PreviewErrorLines, System.Drawing.Color.Red, 4);
                if (PreviewMaxErrorLine.IsValid)
                {
                    e.Display.DrawLine(PreviewMaxErrorLine, System.Drawing.Color.Red, 6);
                    if (!string.IsNullOrEmpty(PreviewMaxErrorText))
                    {
                        e.Display.DrawDot(PreviewMaxErrorLine.PointAt(0.5), PreviewMaxErrorText, System.Drawing.Color.Red, System.Drawing.Color.White);
                    }
                }
            }
        }
    }

    public class FitSrfToTargetDialog : CADacombsDialogBase
    {
        public DrapeDialogAction Action { get; set; } = DrapeDialogAction.None;
        public List<ProjectionResult> Results { get; private set; }
        public List<NurbsSurface> ResultSurfaces => Results?.Select(r => r?.Surface).ToList();

        private ObjRef[] _targetRefs;
        private List<ObjRef> _startingSrfRefs;
        private FitSrfConduit _conduit;

        private Button _btnReselSrf;
        private Button _btnAddRemSrf;
        
        private DropDown _dropMappingDirection;
        private DropDown _dropSolverType;
        
        private DropDown _dropSamplingMode;
        private DropDown _dropWeightCurve;
        private NumericStepper _stepPercentDomain;
        private NumericStepper _stepEnergyStiffness;
        private CheckBox _chkConstrain1D;

        private TextBox _txtTolerance;
        private NumericStepper _stepTimeout;
        private DropDown _dropOutputLayer;
        private CheckBox _chkDeleteInput;
        private CheckBox _chkAddErrorLines;
        
        private CheckBox _chkShowSurface;
        private CheckBox _chkShowWireframe;
        private CheckBox _chkShowPolygon;
        private CheckBox _chkShowErrorLines;

        private Label _lblProcessedCount;
        private Label _lblMaxDev;
        
        private ProgressBar _pbTime;
        private ProgressBar _pbError;
        private DynamicLayout _progressLayout;
        
        private Button _btnSolve;
        private Button _btnOk;
        private Button _btnCancel;

        private bool _isComputing = false;
        private bool _cancelRequested = false;
        private bool _previewPending = false;
        private bool _isUpdatingTextProgrammatically = false;
        private int _computationId = 0;
        private UITimer _typingTimer;

        private Dictionary<string, (List<ProjectionResult>, List<string>, double, double)> _previewCache = new Dictionary<string, (List<ProjectionResult>, List<string>, double, double)>();

        public FitSrfToTargetDialog(ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, DrapeConduit originalConduit)
        {
            _targetRefs = targetRefs;
            _startingSrfRefs = startingSrfRefs;
            
            originalConduit.Enabled = false;
            _conduit = new FitSrfConduit { Enabled = true };

            Title = "CADacombs Fit Surface to Target";
            Resizable = false;
            AutoSize = true; 
            Padding = new Padding(12);

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            CreateControls();
            SetupLayout();
            UpdateAdvancedOptionsVisibility();

            this.Shown += (s, e) => 
            {
                if (_startingSrfRefs != null && _startingSrfRefs.Count > 0)
                {
                    Application.Instance.AsyncInvoke(() => UpdatePreview());
                }
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _conduit.Enabled = false;
            base.OnClosed(e);
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => FitSrfToTargetOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => FitSrfToTargetOptions.WindowLocation = location;

        private void CreateControls()
        {
            _btnReselSrf = new Button { Text = "Reselect" };
            _btnReselSrf.Click += (s, e) => { Action = DrapeDialogAction.PickCustomSurface; Close(); };
            
            _btnAddRemSrf = new Button { Text = "Add / remove" };
            _btnAddRemSrf.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveStartingSurfaces; Close(); };

            _dropMappingDirection = new DropDown();
            _dropMappingDirection.Items.Add("Closest point");
            _dropMappingDirection.Items.Add("Z-axis reversed (Down)");
            _dropMappingDirection.Items.Add("Z-axis (Up)");
            
            if (FitSrfToTargetOptions.MappingDirection == 0) _dropMappingDirection.SelectedIndex = 0;
            else if (!FitSrfToTargetOptions.FlipDirection) _dropMappingDirection.SelectedIndex = 1;
            else _dropMappingDirection.SelectedIndex = 2;
            
            _dropMappingDirection.SelectedIndexChanged += OnDropdownChanged;

            _dropSolverType = new DropDown();
            _dropSolverType.Items.Add("Direct control point");
            _dropSolverType.Items.Add("Direct Greville");
            _dropSolverType.Items.Add("Weighted / relaxed");
            _dropSolverType.Items.Add("Global least squares");
            _dropSolverType.Items.Add("Energy minimization");
            _dropSolverType.SelectedIndex = FitSrfToTargetOptions.SolverType;
            _dropSolverType.SelectedIndexChanged += OnDropdownChanged;

            _dropSamplingMode = new DropDown();
            _dropSamplingMode.Items.Add("Distributed");
            _dropSamplingMode.Items.Add("Nearest point");
            _dropSamplingMode.SelectedIndex = FitSrfToTargetOptions.SamplingMode;
            _dropSamplingMode.SelectedIndexChanged += OnDropdownChanged;

            _dropWeightCurve = new DropDown();
            _dropWeightCurve.Items.Add("Linear");
            _dropWeightCurve.Items.Add("Hyperbolic");
            _dropWeightCurve.Items.Add("Gaussian");
            _dropWeightCurve.SelectedIndex = FitSrfToTargetOptions.WeightCurve;
            _dropWeightCurve.SelectedIndexChanged += OnDropdownChanged;

            _stepPercentDomain = new NumericStepper { Value = FitSrfToTargetOptions.PercentOfDomain, MinValue = 0.001, MaxValue = 100.0, DecimalPlaces = 3, Width = 80, Increment = 1.0 };
            _stepPercentDomain.ValueChanged += OnNumericInput;

            _stepEnergyStiffness = new NumericStepper { Value = FitSrfToTargetOptions.EnergyStiffnessExponent, MinValue = -20, MaxValue = 20, DecimalPlaces = 0, Width = 80, Increment = 1 };
            _stepEnergyStiffness.ValueChanged += OnNumericInput;

            _chkConstrain1D = new CheckBox { Text = "Constrain to 1D displacement (Z-axis)", Checked = FitSrfToTargetOptions.Constrain1D };
            _chkConstrain1D.CheckedChanged += OnDropdownChanged;

            _txtTolerance = new TextBox { Text = FitSrfToTargetOptions.Tolerance.ToString("G"), Width = 80 };
            _txtTolerance.TextChanged += OnNumericInput;

            _stepTimeout = new NumericStepper { Value = FitSrfToTargetOptions.SolverTimeout, MinValue = 0.1, MaxValue = 60.0, DecimalPlaces = 1, Width = 80, Increment = 0.5 };
            _stepTimeout.ValueChanged += OnNumericInput;

            _dropOutputLayer = new DropDown();
            _dropOutputLayer.Items.Add("Input");
            _dropOutputLayer.Items.Add("Current");
            _dropOutputLayer.Items.Add("Target object");
            _dropOutputLayer.SelectedIndex = FitSrfToTargetOptions.OutputLayer;
            _dropOutputLayer.SelectedIndexChanged += (s, e) => 
            {
                FitSrfToTargetOptions.OutputLayer = _dropOutputLayer.SelectedIndex;
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            };

            _chkDeleteInput = new CheckBox { Text = "Delete input", Checked = FitSrfToTargetOptions.DeleteStartingSrf };
            _chkDeleteInput.CheckedChanged += (s, e) => { FitSrfToTargetOptions.DeleteStartingSrf = _chkDeleteInput.Checked ?? false; };

            _chkAddErrorLines = new CheckBox { Text = "Add error lines", Checked = FitSrfToTargetOptions.AddErrorLines };
            _chkAddErrorLines.CheckedChanged += (s, e) => { FitSrfToTargetOptions.AddErrorLines = _chkAddErrorLines.Checked ?? false; };

            _chkShowSurface = new CheckBox { Text = "Shaded", Checked = FitSrfToTargetOptions.ShowSurface };
            _chkShowWireframe = new CheckBox { Text = "Wireframe", Checked = FitSrfToTargetOptions.ShowWireframe };
            _chkShowPolygon = new CheckBox { Text = "Control polygon", Checked = FitSrfToTargetOptions.ShowPolygon };
            _chkShowErrorLines = new CheckBox { Text = "Error lines", Checked = FitSrfToTargetOptions.ShowErrorLines };

            EventHandler<EventArgs> displayEvent = (s, e) => 
            {
                _conduit.ShowSurface = _chkShowSurface.Checked ?? true;
                _conduit.ShowWireframe = _chkShowWireframe.Checked ?? true;
                _conduit.ShowPolygon = _chkShowPolygon.Checked ?? false;
                FitSrfToTargetOptions.ShowErrorLines = _chkShowErrorLines.Checked ?? false;
                _conduit.ShowErrorLines = FitSrfToTargetOptions.ShowErrorLines;
                RhinoDoc.ActiveDoc.Views.Redraw();
            };
            
            _chkShowSurface.CheckedChanged += displayEvent;
            _chkShowWireframe.CheckedChanged += displayEvent;
            _chkShowPolygon.CheckedChanged += displayEvent;
            _chkShowErrorLines.CheckedChanged += displayEvent;

            _lblProcessedCount = new Label { Text = "Processed 0 surface(s)." };
            _lblMaxDev = new Label { Text = "", Wrap = WrapMode.Word };

            _pbTime = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, Height = 8 };
            _pbError = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, Height = 8 };

            _btnSolve = new Button { Text = "Solve", Width = 75, Enabled = false };
            _btnSolve.Click += (s, e) => 
            {
                if (_isComputing)
                {
                    _cancelRequested = true;
                    _btnSolve.Text = "Stopping...";
                    _btnSolve.Enabled = false;
                }
                else
                {
                    UpdatePreview();
                }
            };

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
            
            var btnAddRemTargets = new Button { Text = "Add / remove" };
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
                Items = { new Label { Text = "Starting surface", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, _btnAddRemSrf, _btnReselSrf } 
            });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(new Label { Text = "Solver options", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var tableSolver = new TableLayout { Spacing = new Size(10, 5) };
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Mapping dir:", VerticalAlignment = VerticalAlignment.Center }, _dropMappingDirection, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Solver type:", VerticalAlignment = VerticalAlignment.Center }, _dropSolverType, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Sampling mode:", VerticalAlignment = VerticalAlignment.Center }, _dropSamplingMode, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Weight curve:", VerticalAlignment = VerticalAlignment.Center }, _dropWeightCurve, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Displacement:" }, _chkConstrain1D, new Panel()));
            layout.AddRow(tableSolver);
            
            var domainStiffStack = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 15, 
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { 
                    new Label { Text = "Domain %:" }, _stepPercentDomain,
                    new Label { Text = "Stiffness (1eX):" }, _stepEnergyStiffness
                } 
            };
            layout.AddRow(domainStiffStack);
            
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            
            var tolTimeStack = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 15, 
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { 
                    new Label { Text = "Tolerance:" }, _txtTolerance,
                    new Label { Text = "Timeout (s):" }, _stepTimeout
                } 
            };
            layout.AddRow(tolTimeStack);

            layout.AddRow(_btnSolve);

            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(new Label { Text = "Preview", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });

            var previewCheckStack = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { _chkShowSurface, _chkShowWireframe, _chkShowPolygon, _chkShowErrorLines } 
            };
            layout.AddRow(previewCheckStack);
            
            _progressLayout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Visible = false };
            _progressLayout.AddRow(new Label { Text = "Time:", Font = new Eto.Drawing.Font(SystemFont.Default, 8), VerticalAlignment = VerticalAlignment.Center }, _pbTime);
            _progressLayout.AddRow(new Label { Text = "Error:", Font = new Eto.Drawing.Font(SystemFont.Default, 8), VerticalAlignment = VerticalAlignment.Center }, _pbError);
            layout.AddRow(_progressLayout);

            layout.AddRow(new StackLayout { Orientation = Orientation.Vertical, Spacing = 5, Items = { _lblProcessedCount, _lblMaxDev } });
            
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            var checkStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, Items = { _chkDeleteInput, _chkAddErrorLines } };
            layout.AddRow(checkStack);

            var outputLayerStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, Items = { new Label { Text = "Output layer:", VerticalAlignment = VerticalAlignment.Center }, _dropOutputLayer } };
            layout.AddRow(outputLayerStack);
            
            var buttonsStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { new Panel { Size = new Size(20, -1) }, _btnOk, _btnCancel } };
            layout.AddRow(buttonsStack);

            Content = layout;
        }

        private void UpdateAdvancedOptionsVisibility()
        {
            int solverType = _dropSolverType.SelectedIndex;
            bool isWeighted = solverType == 2;
            bool isMatrix = solverType == 3 || solverType == 4;
            bool isZAxis = _dropMappingDirection.SelectedIndex > 0;
            
            _dropSamplingMode.Enabled = isWeighted;
            _dropWeightCurve.Enabled = isWeighted;
            _stepPercentDomain.Enabled = isWeighted;
            _stepEnergyStiffness.Enabled = solverType == 4;
            
            _chkConstrain1D.Enabled = isMatrix && isZAxis;
        }

        private void OnDropdownChanged(object sender, EventArgs e)
        {
            FitSrfToTargetOptions.MappingDirection = _dropMappingDirection.SelectedIndex == 0 ? 0 : 1;
            FitSrfToTargetOptions.FlipDirection = _dropMappingDirection.SelectedIndex == 2;
            
            UpdateAdvancedOptionsVisibility();
            Application.Instance.AsyncInvoke(() => UpdatePreview());
        }

        private void OnNumericInput(object sender, EventArgs e)
        {
            if (_isUpdatingTextProgrammatically) return;

            if (sender == _txtTolerance && Results != null && Results.Count > 0)
            {
                if (double.TryParse(_txtTolerance.Text, out double newTol))
                {
                    UpdateErrorLineFilter(newTol);
                }
            }

            SetPreviewRequired("Waiting for input...");
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void OnTypingTimerElapsed(object sender, EventArgs e)
        {
            _typingTimer.Stop();
            Application.Instance.AsyncInvoke(() => UpdatePreview());
        }

        private void SetPreviewRequired(string message)
        {
            _previewPending = true;
            _btnSolve.Enabled = true;
            _btnSolve.Text = "Solve";
            _btnSolve.TextColor = Colors.Red;
            _btnSolve.Font = new Eto.Drawing.Font(SystemFont.Bold, _btnSolve.Font.Size);
            _lblProcessedCount.Text = message;
            _lblProcessedCount.TextColor = SystemColors.ControlText;
            _lblMaxDev.Text = "";
            
            if (ParentWindow != null) this.Size = new Size(this.Width, -1);
        }

        private void UpdateErrorLineFilter(double tol)
        {
            if (Results != null)
            {
                _conduit.PreviewErrorLines = Results.SelectMany(r => r?.ErrorLines?.Where(l => l.Length > tol) ?? new List<Line>()).ToList();
                
                var maxRes = Results.Where(r => r != null && r.MaxErrorLine.IsValid && r.MaxDeviation > tol)
                                    .OrderByDescending(r => r.MaxDeviation).FirstOrDefault();
                                    
                if (maxRes != null)
                {
                    _conduit.PreviewMaxErrorLine = maxRes.MaxErrorLine;
                    int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
                    _conduit.PreviewMaxErrorText = $"Max: {FormatUtils.FormatDistance(maxRes.MaxDeviation, prec)}";
                }
                else
                {
                    _conduit.PreviewMaxErrorLine = Line.Unset;
                    _conduit.PreviewMaxErrorText = "";
                }
                
                RhinoDoc.ActiveDoc.Views.Redraw();
            }
        }

        private void ClearPreviewPending(int expectedCount, int missCount, double maxDev, int solverType, double tolerance)
        {
            _previewPending = false;
            _btnSolve.Enabled = false;
            _btnSolve.Text = "Solve";
            _btnSolve.TextColor = SystemColors.ControlText;
            _btnSolve.Font = new Eto.Drawing.Font(SystemFont.Default, _btnSolve.Font.Size);

            if (missCount == expectedCount && expectedCount > 0)
            {
                _lblProcessedCount.Text = "The starting surface completely misses the targets.";
                _lblProcessedCount.TextColor = Colors.DarkRed;
                _lblMaxDev.Text = "";
                return;
            }

            _lblProcessedCount.Text = $"Processed {expectedCount - missCount} surface(s).";
            _lblProcessedCount.TextColor = SystemColors.ControlText;

            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
            _lblMaxDev.Text = $"Max deviation from target(s): {FormatUtils.FormatDistance(maxDev, prec)}";
            
            if (maxDev > tolerance)
            {
                _lblMaxDev.TextColor = Colors.DarkRed;
                _lblMaxDev.Font = new Eto.Drawing.Font(SystemFont.Bold, _lblMaxDev.Font.Size);
            }
            else
            {
                _lblMaxDev.TextColor = Colors.DarkGreen;
                _lblMaxDev.Font = new Eto.Drawing.Font(SystemFont.Bold, _lblMaxDev.Font.Size);
            }
        }

        private void ClearPreview()
        {
            Results = null;
            _conduit.PreviewSurfaces = null;
            _conduit.PreviewBreps = null;
            _conduit.PreviewErrorLines = null;
            _conduit.PreviewMaxErrorLine = Line.Unset;
            _conduit.PreviewMaxErrorText = "";
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

        private async void UpdatePreview()
        {
            int currentCompId = ++_computationId;

            if (_isComputing)
            {
                _cancelRequested = true;
                _lblProcessedCount.Text = "Calculating...";
                _lblProcessedCount.TextColor = SystemColors.ControlText;
                _lblMaxDev.Text = "";
                while (_isComputing) await Task.Delay(50);
            }

            if (currentCompId != _computationId) return; 

            if (_targetRefs == null || _targetRefs.Length == 0) return;
            if (_startingSrfRefs == null || _startingSrfRefs.Count == 0)
            {
                ClearPreview();
                return;
            }

            double minTol = 1e-6 * RhinoMath.UnitScale(UnitSystem.Millimeters, RhinoDoc.ActiveDoc.ModelUnitSystem);
            bool allValid = true;

            if (double.TryParse(_txtTolerance.Text, out double tol))
            {
                if (tol < 0) tol = 10.0 * RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                if (tol < minTol) tol = minTol;
                FitSrfToTargetOptions.Tolerance = tol;
                _txtTolerance.BackgroundColor = Colors.White;
            }
            else { _txtTolerance.BackgroundColor = Colors.LightPink; allValid = false; }

            if (!allValid)
            {
                _btnSolve.Enabled = true;
                _btnSolve.Text = "Solve";
                _btnSolve.TextColor = Colors.Red;
                _lblProcessedCount.Text = "Invalid parameter entered.";
                _lblProcessedCount.TextColor = SystemColors.ControlText;
                _lblMaxDev.Text = "";
                return;
            }

            FitSrfToTargetOptions.MappingDirection = _dropMappingDirection.SelectedIndex == 0 ? 0 : 1;
            FitSrfToTargetOptions.FlipDirection = _dropMappingDirection.SelectedIndex == 2;
            FitSrfToTargetOptions.SolverType = _dropSolverType.SelectedIndex;
            FitSrfToTargetOptions.SamplingMode = _dropSamplingMode.SelectedIndex;
            FitSrfToTargetOptions.WeightCurve = _dropWeightCurve.SelectedIndex;
            FitSrfToTargetOptions.PercentOfDomain = _stepPercentDomain.Value;
            FitSrfToTargetOptions.EnergyStiffnessExponent = (int)_stepEnergyStiffness.Value;
            FitSrfToTargetOptions.Constrain1D = _chkConstrain1D.Checked ?? true;
            FitSrfToTargetOptions.SolverTimeout = _stepTimeout.Value;

            string cacheKey = $"{FitSrfToTargetOptions.MappingDirection}_{FitSrfToTargetOptions.FlipDirection}_{FitSrfToTargetOptions.SolverType}_{FitSrfToTargetOptions.SolverTimeout}_{FitSrfToTargetOptions.EnergyStiffnessExponent}_{FitSrfToTargetOptions.Constrain1D}_{FitSrfToTargetOptions.SamplingMode}_{FitSrfToTargetOptions.WeightCurve}_{FitSrfToTargetOptions.PercentOfDomain}";
            int expectedCount = _startingSrfRefs.Count;

            if (_previewCache.TryGetValue(cacheKey, out var cachedData))
            {
                double oldMaxDev = cachedData.Item3;
                double oldTol = cachedData.Item4;
                bool canReuse = false;

                if (oldMaxDev <= tol) canReuse = true; 
                else if (tol >= oldTol) canReuse = true; 

                if (canReuse)
                {
                    Results = cachedData.Item1;
                    _conduit.PreviewSurfaces = ResultSurfaces;
                    _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();
                    
                    UpdateErrorLineFilter(tol);
                    _conduit.ShowErrorLines = FitSrfToTargetOptions.ShowErrorLines;

                    int cachedMisses = ResultSurfaces.Count(s => s == null);
                    if (cachedMisses == expectedCount && expectedCount > 0)
                    {
                        _conduit.PreviewSurfaces.Clear();
                        _conduit.PreviewBreps.Clear();
                    }

                    ClearPreviewPending(expectedCount, cachedMisses, oldMaxDev, FitSrfToTargetOptions.SolverType, tol);
                    UpdateConduitColors();
                    RhinoDoc.ActiveDoc.Views.Redraw();
                    return;
                }
            }

            _isComputing = true;
            _cancelRequested = false;
            _btnSolve.Text = "Stop";
            _btnSolve.TextColor = Colors.Red;
            _btnSolve.Font = new Eto.Drawing.Font(SystemFont.Bold, _btnSolve.Font.Size);
            _btnSolve.Enabled = true;

            _lblProcessedCount.Text = "Calculating...";
            _lblProcessedCount.TextColor = SystemColors.ControlText;
            _lblMaxDev.Text = "";

            _progressLayout.Visible = true;
            _pbTime.Value = 0;
            _pbError.Value = 0;
            if (ParentWindow != null) this.Size = new Size(this.Width, -1);

            Action<SolverProgress> progressCallback = delegate (SolverProgress sp)
            { 
                if (sp != null)
                {
                    Application.Instance.AsyncInvoke(() => {
                        if (_pbTime != null && _pbError != null && _progressLayout.Visible)
                        {
                            int tVal = (int)((sp.ElapsedSeconds / sp.TimeoutSeconds) * 100);
                            _pbTime.Value = Math.Min(100, Math.Max(0, tVal));

                            if (sp.InitialError > 0 && sp.InitialError > sp.Tolerance)
                            {
                                double errRange = sp.InitialError - sp.Tolerance;
                                double currentProg = sp.InitialError - sp.CurrentError;
                                int eVal = (int)((currentProg / errRange) * 100);
                                if (sp.CurrentError <= sp.Tolerance) eVal = 100;
                                _pbError.Value = Math.Min(100, Math.Max(0, eVal));
                            }
                        }
                    });
                }
            };

            Func<bool> checkCancel = delegate { return _cancelRequested; };

            var tBreps = new List<Brep>();
            var tMeshes = new List<Mesh>();
            foreach (var r in _targetRefs)
            {
                var geom = r.Geometry();
                if (geom is SubD subD) { var lm = Mesh.CreateFromSubD(subD, 3); if (lm != null) tMeshes.Add(lm); else { var pb = subD.ToBrep(new SubDToBrepOptions()); if (pb != null) tBreps.Add(pb); } }
                else if (geom is Brep b) tBreps.Add(b);
                else if (geom is Mesh m) tMeshes.Add(m);
            }

            var startSrfs = new List<NurbsSurface>();
            foreach (var r in _startingSrfRefs)
            {
                Surface srf = r.Surface();
                if (srf == null && r.Brep()?.Faces.Count == 1) srf = r.Brep().Faces[0].UnderlyingSurface();
                startSrfs.Add(srf?.ToNurbsSurface());
            }

            double docTol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

            try
            {
                List<string> messages = null;
                double maxDev = 0;

                await Task.Run(() => 
                {
                    Results = FitSrfToTargetLogic.ComputeFitSurfacesCore(tBreps, tMeshes, startSrfs, docTol, out messages, out maxDev, progressCallback, checkCancel);
                });
                
                _conduit.PreviewSurfaces = ResultSurfaces;
                _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();
                
                UpdateErrorLineFilter(tol);
                _conduit.ShowErrorLines = FitSrfToTargetOptions.ShowErrorLines;

                int mCount = ResultSurfaces.Count(s => s == null);
                if (mCount == expectedCount && expectedCount > 0)
                {
                    _conduit.PreviewSurfaces.Clear();
                    _conduit.PreviewBreps.Clear();
                }

                ClearPreviewPending(expectedCount, mCount, maxDev, FitSrfToTargetOptions.SolverType, tol);

                foreach (var msg in messages)
                {
                    if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
                }

                _previewCache[cacheKey] = (Results, messages, maxDev, tol);
                
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            }
            catch (OperationCanceledException)
            {
                _previewPending = true;
                _btnSolve.Enabled = true;
                _btnSolve.Text = "Solve";
                _btnSolve.TextColor = Colors.Red;
                _lblProcessedCount.Text = "The solver was canceled.";
                _lblProcessedCount.TextColor = Colors.DarkRed;
                _lblMaxDev.Text = "";
                ClearPreview();
            }
            finally
            {
                _isComputing = false;
                _progressLayout.Visible = false;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);
            }
        }
    }
}