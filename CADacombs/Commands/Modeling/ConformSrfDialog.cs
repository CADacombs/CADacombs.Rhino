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

    public class ConformSrfDialog : CADacombsDialogBase
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
        
        private CheckBox _chkUseStiffness;
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
        private bool _isUpdatingTextProgrammatically = false;
        private int _computationId = 0;
        private UITimer _typingTimer;

        private Dictionary<string, (List<ProjectionResult>, List<string>, double, double, double, double, bool)> _previewCache = new Dictionary<string, (List<ProjectionResult>, List<string>, double, double, double, double, bool)>();

        public ConformSrfDialog(ObjRef[] targetRefs, List<ObjRef> startingSrfRefs, DrapeConduit originalConduit)
        {
            _targetRefs = targetRefs;
            _startingSrfRefs = startingSrfRefs;
            
            originalConduit.Enabled = false;
            _conduit = new FitSrfConduit { Enabled = true };

            Title = "CADacombs Conform Surface";
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

        protected override Eto.Drawing.Point? LoadSavedLocation() => ConformSrfOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => ConformSrfOptions.WindowLocation = location;

        private void CreateControls()
        {
            _btnReselSrf = new Button { Text = "Reselect" };
            _btnReselSrf.Click += (s, e) => { Action = DrapeDialogAction.PickCustomSurface; Close(); };
            
            _btnAddRemSrf = new Button { Text = "Add/Remove" };
            _btnAddRemSrf.Click += (s, e) => { Action = DrapeDialogAction.AddRemoveStartingSurfaces; Close(); };

            _dropMappingDirection = new DropDown();
            _dropMappingDirection.Items.Add("Pull (Closest point)");
            _dropMappingDirection.Items.Add("Project down (-Z)");
            _dropMappingDirection.Items.Add("Project up (+Z)");
            
            if (ConformSrfOptions.MappingDirection == 0) _dropMappingDirection.SelectedIndex = 0;
            else if (!ConformSrfOptions.FlipDirection) _dropMappingDirection.SelectedIndex = 1;
            else _dropMappingDirection.SelectedIndex = 2;
            
            _dropMappingDirection.SelectedIndexChanged += OnDropdownChanged;

            _dropSolverType = new DropDown();
            _dropSolverType.Items.Add("Direct control point");
            _dropSolverType.Items.Add("Direct Greville");
            _dropSolverType.Items.Add("Weighted / relaxed");
            _dropSolverType.Items.Add("Global least squares");
            
            int savedType = ConformSrfOptions.SolverType;
            if (savedType > 3) savedType = 3; 
            _dropSolverType.SelectedIndex = savedType;
            _dropSolverType.SelectedIndexChanged += OnDropdownChanged;

            _dropSamplingMode = new DropDown();
            _dropSamplingMode.Items.Add("Distributed");
            _dropSamplingMode.Items.Add("Nearest point");
            _dropSamplingMode.SelectedIndex = ConformSrfOptions.SamplingMode;
            _dropSamplingMode.SelectedIndexChanged += OnDropdownChanged;

            _dropWeightCurve = new DropDown();
            _dropWeightCurve.Items.Add("Linear");
            _dropWeightCurve.Items.Add("Hyperbolic");
            _dropWeightCurve.Items.Add("Gaussian");
            _dropWeightCurve.SelectedIndex = ConformSrfOptions.WeightCurve;
            _dropWeightCurve.SelectedIndexChanged += OnDropdownChanged;

            _stepPercentDomain = new NumericStepper { Value = ConformSrfOptions.PercentOfDomain, DecimalPlaces = 2, Increment = 1.0 };
            _stepPercentDomain.ValueChanged += OnNumericInput;

            _chkUseStiffness = new CheckBox { Checked = ConformSrfOptions.UseStiffness };
            _chkUseStiffness.CheckedChanged += OnDropdownChanged;

            _stepEnergyStiffness = new NumericStepper { Value = ConformSrfOptions.EnergyStiffnessExponent, DecimalPlaces = 0, Increment = 1 };
            _stepEnergyStiffness.ValueChanged += OnNumericInput;

            _chkConstrain1D = new CheckBox { Text = "Constrain to 1D displacement (Z-axis)", Checked = ConformSrfOptions.Constrain1D };
            _chkConstrain1D.CheckedChanged += OnDropdownChanged;

            _txtTolerance = new TextBox { Text = ConformSrfOptions.Tolerance.ToString("G") };
            _txtTolerance.TextChanged += OnNumericInput;

            _stepTimeout = new NumericStepper { Value = ConformSrfOptions.SolverTimeout, DecimalPlaces = 1, Increment = 0.5 };
            _stepTimeout.ValueChanged += OnNumericInput;

            _dropOutputLayer = new DropDown();
            _dropOutputLayer.Items.Add("Input");
            _dropOutputLayer.Items.Add("Current");
            _dropOutputLayer.Items.Add("Target object");
            _dropOutputLayer.SelectedIndex = ConformSrfOptions.OutputLayer;
            _dropOutputLayer.SelectedIndexChanged += (s, e) => 
            {
                ConformSrfOptions.OutputLayer = _dropOutputLayer.SelectedIndex;
                UpdateConduitColors();
                RhinoDoc.ActiveDoc.Views.Redraw();
            };

            _chkDeleteInput = new CheckBox { Text = "Delete source", Checked = ConformSrfOptions.DeleteStartingSrf };
            _chkDeleteInput.CheckedChanged += (s, e) => { ConformSrfOptions.DeleteStartingSrf = _chkDeleteInput.Checked ?? false; };

            _chkAddErrorLines = new CheckBox { Text = "Add error lines", Checked = ConformSrfOptions.AddErrorLines };
            _chkAddErrorLines.CheckedChanged += (s, e) => { ConformSrfOptions.AddErrorLines = _chkAddErrorLines.Checked ?? false; };

            _chkShowSurface = new CheckBox { Text = "Shaded", Checked = ConformSrfOptions.ShowSurface };
            _chkShowWireframe = new CheckBox { Text = "Wireframe", Checked = ConformSrfOptions.ShowWireframe };
            _chkShowPolygon = new CheckBox { Text = "Control polygon", Checked = ConformSrfOptions.ShowPolygon };
            _chkShowErrorLines = new CheckBox { Text = "Error lines", Checked = ConformSrfOptions.ShowErrorLines };

            EventHandler<EventArgs> displayEvent = (s, e) => 
            {
                _conduit.ShowSurface = _chkShowSurface.Checked ?? true;
                _conduit.ShowWireframe = _chkShowWireframe.Checked ?? true;
                _conduit.ShowPolygon = _chkShowPolygon.Checked ?? false;
                ConformSrfOptions.ShowErrorLines = _chkShowErrorLines.Checked ?? false;
                _conduit.ShowErrorLines = ConformSrfOptions.ShowErrorLines;
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

            _btnSolve = new Button { Text = "Solve", Enabled = false };
            _btnSolve.TextColor = Colors.Gray; 
            _btnSolve.Click += (s, e) => 
            {
                if (_isComputing)
                {
                    _cancelRequested = true;
                    _btnSolve.Text = "Stopping...";
                    _btnSolve.Enabled = false;
                    _btnSolve.TextColor = Colors.Gray;
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
            
            layout.AddRow(new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { new Label { Text = "Surfaces", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, _btnAddRemSrf, _btnReselSrf } 
            });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            var btnAddRemTargets = new Button { Text = "Add/Remove" };
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

            layout.AddRow(new Label { Text = "Solver options", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) });
            
            var tableSolver = new TableLayout { Spacing = new Size(10, 5) };
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Mapping direction:", VerticalAlignment = VerticalAlignment.Center }, _dropMappingDirection, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Solver type:", VerticalAlignment = VerticalAlignment.Center }, _dropSolverType, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Sampling mode:", VerticalAlignment = VerticalAlignment.Center }, _dropSamplingMode, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Weight curve:", VerticalAlignment = VerticalAlignment.Center }, _dropWeightCurve, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Domain %:", VerticalAlignment = VerticalAlignment.Center }, _stepPercentDomain, new Panel()));
            
            var stiffLabelStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5, VerticalContentAlignment = VerticalAlignment.Center, Items = { _chkUseStiffness, new Label { Text = "Stiffness: 10^", VerticalAlignment = VerticalAlignment.Center } } };
            tableSolver.Rows.Add(new TableRow(stiffLabelStack, _stepEnergyStiffness, new Panel()));
            
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Tolerance:", VerticalAlignment = VerticalAlignment.Center }, _txtTolerance, new Panel()));
            tableSolver.Rows.Add(new TableRow(new Label { Text = "Timeout (s):", VerticalAlignment = VerticalAlignment.Center }, _stepTimeout, new Panel()));
            
            var solverGroup = new StackLayout { 
                Orientation = Orientation.Vertical, 
                Spacing = 5, 
                Items = { tableSolver, _chkConstrain1D } 
            };
            layout.AddRow(solverGroup);
            
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(_btnSolve);

            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            var previewHeaderStack = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { 
                    new Label { Text = "Preview", Font = new Eto.Drawing.Font(SystemFont.Bold, 10) }, 
                    _chkShowSurface, 
                    _chkShowWireframe 
                } 
            };
            layout.AddRow(previewHeaderStack);

            var previewCheckStack2 = new StackLayout { 
                Orientation = Orientation.Horizontal, 
                Spacing = 10, 
                VerticalContentAlignment = VerticalAlignment.Center, 
                Items = { _chkShowPolygon, _chkShowErrorLines } 
            };
            layout.AddRow(previewCheckStack2);
            
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

        private void ForceControlRefresh(Control control)
        {
            if (control.HasFocus)
            {
                _btnCancel.Focus();
                control.Focus();
                if (control is TextBox tb)
                {
                    tb.CaretIndex = tb.Text.Length;
                }
            }
        }

        private void UpdateAdvancedOptionsVisibility()
        {
            int solverType = _dropSolverType.SelectedIndex;
            bool isWeighted = solverType == 2;
            bool isMatrix = solverType == 3;
            bool isZAxis = _dropMappingDirection.SelectedIndex > 0;
            
            _dropSamplingMode.Enabled = isWeighted;
            _dropWeightCurve.Enabled = isWeighted;
            _stepPercentDomain.Enabled = isWeighted;
            
            _chkUseStiffness.Enabled = isMatrix;
            _stepEnergyStiffness.Enabled = isMatrix && (_chkUseStiffness.Checked ?? false);
            
            _chkConstrain1D.Enabled = isMatrix && isZAxis;
        }

        private void OnDropdownChanged(object sender, EventArgs e)
        {
            ConformSrfOptions.MappingDirection = _dropMappingDirection.SelectedIndex == 0 ? 0 : 1;
            ConformSrfOptions.FlipDirection = _dropMappingDirection.SelectedIndex == 2;
            ConformSrfOptions.UseStiffness = _chkUseStiffness.Checked ?? false;
            
            UpdateAdvancedOptionsVisibility();
            Application.Instance.AsyncInvoke(() => UpdatePreview());
        }

        private void OnNumericInput(object sender, EventArgs e)
        {
            if (_isUpdatingTextProgrammatically) return;

            if (sender == _stepPercentDomain)
            {
                double val = _stepPercentDomain.Value;
                if (val > 50.0)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepPercentDomain.Value = 50.0;
                        ForceControlRefresh(_stepPercentDomain);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
                else if (val < 0.0)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepPercentDomain.Value = 0.01;
                        ForceControlRefresh(_stepPercentDomain);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
            }
            else if (sender == _stepEnergyStiffness)
            {
                double val = _stepEnergyStiffness.Value;
                if (val > 10)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepEnergyStiffness.Value = 10;
                        ForceControlRefresh(_stepEnergyStiffness);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
                else if (val < -10)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepEnergyStiffness.Value = -10;
                        ForceControlRefresh(_stepEnergyStiffness);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
            }
            else if (sender == _stepTimeout)
            {
                double val = _stepTimeout.Value;
                if (val > 60.0)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepTimeout.Value = 60.0;
                        ForceControlRefresh(_stepTimeout);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
                else if (val < 0.0)
                {
                    Application.Instance.AsyncInvoke(() => {
                        _isUpdatingTextProgrammatically = true;
                        _stepTimeout.Value = 0.1;
                        ForceControlRefresh(_stepTimeout);
                        _isUpdatingTextProgrammatically = false;
                    });
                }
                
                double currentTimeout = _stepTimeout.Value;
                if (currentTimeout > 60.0) currentTimeout = 60.0;
                if (currentTimeout < 0.1) currentTimeout = 0.1;
                
                ConformSrfOptions.SolverTimeout = currentTimeout;
                int sType = _dropSolverType.SelectedIndex;

                if (sType == 0 || sType == 3) return; 
                
                string cacheKey = $"{ConformSrfOptions.MappingDirection}_{ConformSrfOptions.FlipDirection}_{ConformSrfOptions.SolverType}_{ConformSrfOptions.UseStiffness}_{ConformSrfOptions.EnergyStiffnessExponent}_{ConformSrfOptions.Constrain1D}_{ConformSrfOptions.SamplingMode}_{ConformSrfOptions.WeightCurve}_{ConformSrfOptions.PercentOfDomain}";
                
                if (_previewCache.TryGetValue(cacheKey, out var cachedData))
                {
                    double oldTimeout = cachedData.Item5;
                    double maxElapsed = cachedData.Item6;
                    bool allConverged = cachedData.Item7;

                    bool finishedNaturally = allConverged || (maxElapsed < oldTimeout);

                    if (finishedNaturally && currentTimeout >= maxElapsed)
                    {
                        _previewCache[cacheKey] = (cachedData.Item1, cachedData.Item2, cachedData.Item3, cachedData.Item4, currentTimeout, maxElapsed, allConverged);
                        return; 
                    }
                }
            }
            else if (sender == _txtTolerance)
            {
                if (double.TryParse(_txtTolerance.Text, out double newTol))
                {
                    ConformSrfOptions.Tolerance = newTol; 

                    if (Results != null && Results.Count > 0)
                    {
                        UpdateErrorLineFilter(newTol);
                        
                        int sType = _dropSolverType.SelectedIndex;
                        if (sType == 0 || sType == 3) 
                        {
                            double maxDev = Results.Max(r => r?.MaxDeviation ?? 0);
                            ClearPreviewPending(_startingSrfRefs.Count, Results.Count(s => s == null), maxDev, sType, newTol);
                            return; 
                        }
                        
                        string cacheKey = $"{ConformSrfOptions.MappingDirection}_{ConformSrfOptions.FlipDirection}_{ConformSrfOptions.SolverType}_{ConformSrfOptions.UseStiffness}_{ConformSrfOptions.EnergyStiffnessExponent}_{ConformSrfOptions.Constrain1D}_{ConformSrfOptions.SamplingMode}_{ConformSrfOptions.WeightCurve}_{ConformSrfOptions.PercentOfDomain}";
                        if (_previewCache.TryGetValue(cacheKey, out var cachedData))
                        {
                            double oldMaxDev = cachedData.Item3;
                            if (oldMaxDev <= newTol)
                            {
                                double maxDev = Results.Max(r => r?.MaxDeviation ?? 0);
                                ClearPreviewPending(_startingSrfRefs.Count, Results.Count(s => s == null), maxDev, sType, newTol);
                                return;
                            }
                        }
                    }
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
            _btnSolve.Enabled = false;
            _btnSolve.Text = "Solve";
            _btnSolve.TextColor = Colors.Gray;
            _btnSolve.Font = new Eto.Drawing.Font(SystemFont.Default, _btnSolve.Font.Size);

            if (missCount == expectedCount && expectedCount > 0)
            {
                _lblProcessedCount.Text = "The source surface completely misses the targets.";
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
                
                if (ConformSrfOptions.OutputLayer == 0 && _startingSrfRefs != null && i < _startingSrfRefs.Count && _startingSrfRefs[i].Object() != null)
                {
                    var attr = _startingSrfRefs[i].Object().Attributes;
                    c = attr.ColorSource == ObjectColorSource.ColorFromObject ? attr.ObjectColor : doc.Layers[attr.LayerIndex].Color;
                }
                else if (ConformSrfOptions.OutputLayer == 2 && _targetRefs != null && _targetRefs.Length > 0 && _targetRefs[0].Object() != null)
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
                
                string tolStr = tol.ToString("G");
                if (_txtTolerance.Text != tolStr)
                {
                    _isUpdatingTextProgrammatically = true;
                    _txtTolerance.Text = tolStr;
                    ForceControlRefresh(_txtTolerance);
                    _isUpdatingTextProgrammatically = false;
                }

                ConformSrfOptions.Tolerance = tol;
                _txtTolerance.BackgroundColor = Colors.White;
            }
            else { _txtTolerance.BackgroundColor = Colors.LightPink; allValid = false; }

            double pd = _stepPercentDomain.Value;
            if (pd > 50.0) pd = 50.0;
            if (pd < 0.01) pd = 0.01;
            if (_stepPercentDomain.Value != pd)
            {
                _isUpdatingTextProgrammatically = true;
                _stepPercentDomain.Value = pd;
                ForceControlRefresh(_stepPercentDomain);
                _isUpdatingTextProgrammatically = false;
            }
            ConformSrfOptions.PercentOfDomain = pd;

            double stiff = _stepEnergyStiffness.Value;
            if (stiff > 10) stiff = 10;
            if (stiff < -10) stiff = -10;
            if (_stepEnergyStiffness.Value != stiff)
            {
                _isUpdatingTextProgrammatically = true;
                _stepEnergyStiffness.Value = stiff;
                ForceControlRefresh(_stepEnergyStiffness);
                _isUpdatingTextProgrammatically = false;
            }
            ConformSrfOptions.EnergyStiffnessExponent = (int)stiff;

            double timeo = _stepTimeout.Value;
            if (timeo > 60.0) timeo = 60.0;
            if (timeo < 0.1) timeo = 0.1;
            if (_stepTimeout.Value != timeo)
            {
                _isUpdatingTextProgrammatically = true;
                _stepTimeout.Value = timeo;
                ForceControlRefresh(_stepTimeout);
                _isUpdatingTextProgrammatically = false;
            }
            ConformSrfOptions.SolverTimeout = timeo;

            if (!allValid)
            {
                _btnSolve.Enabled = false;
                _btnSolve.Text = "Solve";
                _btnSolve.TextColor = Colors.Gray;
                _lblProcessedCount.Text = "Invalid parameter entered.";
                _lblProcessedCount.TextColor = SystemColors.ControlText;
                _lblMaxDev.Text = "";
                return;
            }

            ConformSrfOptions.MappingDirection = _dropMappingDirection.SelectedIndex == 0 ? 0 : 1;
            ConformSrfOptions.FlipDirection = _dropMappingDirection.SelectedIndex == 2;
            ConformSrfOptions.SolverType = _dropSolverType.SelectedIndex;
            ConformSrfOptions.SamplingMode = _dropSamplingMode.SelectedIndex;
            ConformSrfOptions.WeightCurve = _dropWeightCurve.SelectedIndex;
            ConformSrfOptions.UseStiffness = _chkUseStiffness.Checked ?? false;
            ConformSrfOptions.Constrain1D = _chkConstrain1D.Checked ?? true;

            double timeout = ConformSrfOptions.SolverTimeout;
            string cacheKey = $"{ConformSrfOptions.MappingDirection}_{ConformSrfOptions.FlipDirection}_{ConformSrfOptions.SolverType}_{ConformSrfOptions.UseStiffness}_{ConformSrfOptions.EnergyStiffnessExponent}_{ConformSrfOptions.Constrain1D}_{ConformSrfOptions.SamplingMode}_{ConformSrfOptions.WeightCurve}_{ConformSrfOptions.PercentOfDomain}";
            int expectedCount = _startingSrfRefs.Count;

            if (_previewCache.TryGetValue(cacheKey, out var cachedData))
            {
                double oldMaxDev = cachedData.Item3;
                double oldTol = cachedData.Item4;
                double oldTimeout = cachedData.Item5;
                double maxElapsed = cachedData.Item6;
                bool allConverged = cachedData.Item7;
                
                bool timeReuse = false;
                bool finishedNaturally = allConverged || (maxElapsed < oldTimeout);

                if (oldTimeout == timeout) timeReuse = true;
                else if (finishedNaturally && timeout >= maxElapsed) timeReuse = true;

                bool tolReuse = false;
                int solverType = ConformSrfOptions.SolverType;
                
                if (solverType == 0 || solverType == 3) 
                {
                    tolReuse = true; 
                }
                else
                {
                    if (oldTol == tol) tolReuse = true;
                    else if (oldMaxDev <= tol) tolReuse = true; 
                    else if (tol >= oldTol) tolReuse = true; 
                }

                if (timeReuse && tolReuse)
                {
                    Results = cachedData.Item1;
                    _previewCache[cacheKey] = (Results, cachedData.Item2, oldMaxDev, tol, timeout, maxElapsed, allConverged);
                    
                    _conduit.PreviewSurfaces = ResultSurfaces;
                    _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();
                    
                    UpdateErrorLineFilter(tol);
                    _conduit.ShowErrorLines = ConformSrfOptions.ShowErrorLines;

                    int cachedMisses = ResultSurfaces != null ? ResultSurfaces.Count(s => s == null) : 0;
                    if (cachedMisses == expectedCount && expectedCount > 0)
                    {
                        _conduit.PreviewSurfaces?.Clear();
                        _conduit.PreviewBreps?.Clear();
                    }

                    ClearPreviewPending(expectedCount, cachedMisses, oldMaxDev, ConformSrfOptions.SolverType, tol);
                    UpdateConduitColors();
                    
                    if (Results != null)
                    {
                        string solverName = "";
                        if (solverType == 0) solverName = "Direct control point";
                        else if (solverType == 1) solverName = "Direct Greville";
                        else if (solverType == 2) solverName = "Weighted / relaxed";
                        else if (solverType == 3) solverName = ConformSrfOptions.UseStiffness ? "Energy minimization" : "Global least squares";

                        foreach(var r in Results)
                        {
                            if (r != null && !string.IsNullOrEmpty(r.Message))
                            {
                                string status = "";
                                if (r.MaxDeviation > tol)
                                {
                                    status = $" <- {r.StopReason}";
                                }
                                RhinoApp.WriteLine($"{solverName}: {r.Message}{status}");
                            }
                        }
                    }

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
                bool wasCanceled = false;

                await Task.Run(() => 
                {
                    try
                    {
                        Results = ConformSrfLogic.ComputeFitSurfacesCore(tBreps, tMeshes, startSrfs, docTol, out messages, out maxDev, progressCallback, checkCancel);
                    }
                    catch (OperationCanceledException)
                    {
                        wasCanceled = true;
                    }
                });
                
                if (wasCanceled)
                {
                    _btnSolve.Enabled = true;
                    _btnSolve.Text = "Solve";
                    _btnSolve.TextColor = Colors.Red;
                    _lblProcessedCount.Text = "The solver was canceled.";
                    _lblProcessedCount.TextColor = Colors.DarkRed;
                    _lblMaxDev.Text = "";
                    ClearPreview();
                }
                else
                {
                    _conduit.PreviewSurfaces = ResultSurfaces;
                    _conduit.PreviewBreps = ResultSurfaces != null ? ResultSurfaces.Select(s => s?.ToBrep()).ToList() : new List<Brep>();
                    
                    UpdateErrorLineFilter(tol);
                    _conduit.ShowErrorLines = ConformSrfOptions.ShowErrorLines;

                    int mCount = ResultSurfaces != null ? ResultSurfaces.Count(s => s == null) : 0;
                    if (mCount == expectedCount && expectedCount > 0)
                    {
                        _conduit.PreviewSurfaces?.Clear();
                        _conduit.PreviewBreps?.Clear();
                    }

                    ClearPreviewPending(expectedCount, mCount, maxDev, ConformSrfOptions.SolverType, tol);

                    if (messages != null)
                    {
                        foreach (var msg in messages)
                        {
                            if (!string.IsNullOrEmpty(msg)) RhinoApp.WriteLine(msg);
                        }
                    }

                    double maxElapsed = Results != null && Results.Any(r => r != null) ? Results.Max(r => r?.ElapsedSeconds ?? 0) : 0;
                    bool allConverged = Results != null && Results.All(r => r == null || r.Converged);
                    _previewCache[cacheKey] = (Results, messages, maxDev, tol, timeout, maxElapsed, allConverged);
                    
                    UpdateConduitColors();
                    RhinoDoc.ActiveDoc.Views.Redraw();
                }
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