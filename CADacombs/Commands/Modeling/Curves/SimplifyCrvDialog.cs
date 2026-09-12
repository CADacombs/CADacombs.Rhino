using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;
using CADacombs.Core; 
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class SimplifyCrvDialog : CADacombsDialogBase
    {
        private List<Curve> _inputCurves;
        private SimplifyCrvConduit _conduit;
        
        private TextBox _txtDistTol;
        private TextBox _txtAngleTol;
        private TextBox _txtLineDistTol;
        private TextBox _txtArcBulgeTol;
        private TextBox _txtMinSegLen;
        
        private NumericStepper _stepPreviewThickness;
        private NumericStepper _stepPreviewTimeout;
        private CheckBox[] _chkDegrees;
        
        private RadioButtonList _rblConversionState;
        
        private CheckBox _chkSpansToLines;
        private CheckBox _chkSpansToArcs;
        private CheckBox _chkAdjustG1;
        private CheckBox _chkSpansToBeziers;
        private CheckBox _chkMakeUniform;
        private CheckBox _chkSplitAllKnots;
        private CheckBox _chkSplitFullyMultiple;
        private CheckBox _chkPolylineOutput;
        
        private Label _lblProcessedCount;
        private Label _lblMaxDev;
        private ProgressBar _progressBar;
        private Panel _reportPanel;
        
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnPreview;

        private bool _previewPending = false;
        private bool _isUpdatingTextProgrammatically = false;
        private UITimer _typingTimer;

        public List<Curve> ResultCurves { get; private set; }
        public double MaxDeviation { get; private set; }
        
        public bool AnyOptionChecked => _chkSpansToLines.Checked == true || 
                                        _chkSpansToArcs.Checked == true || 
                                        _chkAdjustG1.Checked == true ||
                                        _chkSpansToBeziers.Checked == true || 
                                        _chkMakeUniform.Checked == true ||
                                        _chkSplitAllKnots.Checked == true ||
                                        _chkSplitFullyMultiple.Checked == true ||
                                        _chkPolylineOutput.Checked == true;
        
        public bool HasChanges { get; private set; } 

        public SimplifyCrvDialog(List<Curve> inputCurves, SimplifyCrvConduit conduit)
        {
            _inputCurves = inputCurves;
            _conduit = conduit;
            
            if (SimplifyCrvOptions.DistanceTolerance < 0)
                SimplifyCrvOptions.DistanceTolerance = SimplifyCrvOptions.GetDefaultDistTol();
            if (SimplifyCrvOptions.AngleTolerance < 0)
                SimplifyCrvOptions.AngleTolerance = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
            if (SimplifyCrvOptions.LineDistanceTolerance < 0)
                SimplifyCrvOptions.LineDistanceTolerance = SimplifyCrvOptions.GetDefaultLineTol();
            if (SimplifyCrvOptions.ArcBulgeTolerance < 0)
                SimplifyCrvOptions.ArcBulgeTolerance = SimplifyCrvOptions.GetDefaultArcBulgeTol();
            if (SimplifyCrvOptions.MinSegmentLength < 0)
                SimplifyCrvOptions.MinSegmentLength = SimplifyCrvOptions.GetDefaultMinSegLength();

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            Title = "CADacombs SimplifyCrv";
            Resizable = false;
            MinimumSize = new Size(300, 0); 
            AutoSize = true;
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();
            
            this.Shown += (s, e) => 
            {
                UpdatePreview(true); // Attempt auto-run on open
            };
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => SimplifyCrvOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => SimplifyCrvOptions.WindowLocation = location;

        private void CreateControls()
        {
            _txtDistTol = new TextBox { Text = SimplifyCrvOptions.DistanceTolerance.ToString("G"), Width = 60, TextAlignment = TextAlignment.Left };
            _txtAngleTol = new TextBox { Text = SimplifyCrvOptions.AngleTolerance.ToString("G"), Width = 60, TextAlignment = TextAlignment.Left };
            _txtMinSegLen = new TextBox { Text = SimplifyCrvOptions.MinSegmentLength.ToString("G"), Width = 60, TextAlignment = TextAlignment.Left };
            
            _txtLineDistTol = new TextBox { Text = SimplifyCrvOptions.LineDistanceTolerance.ToString("G"), Width = 60, TextAlignment = TextAlignment.Left };
            _txtArcBulgeTol = new TextBox { Text = SimplifyCrvOptions.ArcBulgeTolerance.ToString("G"), Width = 60, TextAlignment = TextAlignment.Left };
            
            _stepPreviewThickness = new NumericStepper 
            { 
                Value = SimplifyCrvOptions.PreviewThickness, 
                MinValue = 1, 
                MaxValue = 100, 
                DecimalPlaces = 0, 
                Width = 60 
            };

            _stepPreviewTimeout = new NumericStepper
            {
                Value = SimplifyCrvOptions.AutoPreviewTimeout,
                MinValue = 1,
                MaxValue = 60,
                DecimalPlaces = 0,
                Width = 50
            };

            _chkDegrees = new CheckBox[9];
            for (int i = 0; i < 9; i++)
            {
                _chkDegrees[i] = new CheckBox 
                { 
                    Text = (i + 1).ToString(), 
                    Checked = SimplifyCrvOptions.TargetDegrees.Contains(i + 1)
                };
                _chkDegrees[i].CheckedChanged += (s, e) => 
                {
                    UpdateControlEnableStates();
                    UpdatePreview(true);
                };
            }

            _rblConversionState = new RadioButtonList { Orientation = Orientation.Horizontal, Spacing = new Size(15, 0) };
            _rblConversionState.Items.Add(new ListItem { Text = "Apply conversions", Key = "On" });
            _rblConversionState.Items.Add(new ListItem { Text = "Original (Compare)", Key = "Off" });
            _rblConversionState.SelectedKey = "On";

            _chkSpansToLines = new CheckBox { Text = "Convert spans to lines", Checked = SimplifyCrvOptions.ConvertLines };
            _chkSpansToArcs = new CheckBox { Text = "Convert spans to arcs", Checked = SimplifyCrvOptions.ConvertArcs };
            _chkAdjustG1 = new CheckBox { Text = "Adjust G1", Checked = SimplifyCrvOptions.AdjustG1 };
            _chkSpansToBeziers = new CheckBox { Text = "Convert NURBS sections to Beziers", Checked = SimplifyCrvOptions.ConvertBeziers };
            _chkMakeUniform = new CheckBox { Text = "Make remaining NURBS spans uniform", Checked = SimplifyCrvOptions.MakeUniform };
            _chkSplitAllKnots = new CheckBox { Text = "Split at all multiple knots", Checked = SimplifyCrvOptions.SplitAllKnots };
            _chkSplitFullyMultiple = new CheckBox { Text = "Split at fully multiple knots", Checked = SimplifyCrvOptions.SplitFullyMultiple };
            _chkPolylineOutput = new CheckBox { Text = "Merge contiguous lines to polylines", Checked = SimplifyCrvOptions.MergePolylines };
            
            _txtDistTol.TextChanged += OnToleranceTextChanged;
            _txtAngleTol.TextChanged += OnToleranceTextChanged;
            _txtLineDistTol.TextChanged += OnToleranceTextChanged;
            _txtArcBulgeTol.TextChanged += OnToleranceTextChanged;
            _txtMinSegLen.TextChanged += OnToleranceTextChanged;
            
            _stepPreviewThickness.ValueChanged += (s, e) => 
            {
                _conduit.PreviewThickness = (int)_stepPreviewThickness.Value;
                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            };
            
            _chkSpansToLines.CheckedChanged += OnOptionChanged;
            _chkSpansToArcs.CheckedChanged += OnOptionChanged;
            _chkAdjustG1.CheckedChanged += OnOptionChanged;
            _chkMakeUniform.CheckedChanged += OnOptionChanged;
            _chkPolylineOutput.CheckedChanged += OnOptionChanged;
            _chkSplitFullyMultiple.CheckedChanged += OnOptionChanged;

            _rblConversionState.SelectedIndexChanged += (s, e) =>
            {
                UpdateControlEnableStates();
                OnOptionChanged(s, e);
            };

            _chkSpansToBeziers.CheckedChanged += (s, e) =>
            {
                UpdateControlEnableStates();
                OnOptionChanged(s, e);
            };

            _chkSplitAllKnots.CheckedChanged += (s, e) =>
            {
                UpdateControlEnableStates();
                OnOptionChanged(s, e);
            };

            _lblProcessedCount = new Label { Text = "Processed 0 curve(s)." };
            _lblMaxDev = new Label { Text = "Max dev: 0.0", TextColor = Colors.DimGray };
            _reportPanel = new Panel();
            
            _progressBar = new ProgressBar { MinValue = 0, MaxValue = _inputCurves.Count, Value = 0, Visible = false, Height = 10 };

            _btnOk = new Button { Text = "OK", Width = 75 };
            _btnOk.Click += (s, e) => 
            { 
                if (!ValidateTolerances())
                {
                    Rhino.RhinoApp.WriteLine("Invalid tolerances entered. Please wait for validation or correct them.");
                    return;
                }

                if (_previewPending)
                {
                    bool success = UpdatePreview(false);
                    if (!success) return; 
                }
                Result = true; 
                Close(); 
            };

            _btnCancel = new Button { Text = "Cancel", Width = 75 };
            _btnCancel.Click += (s, e) => { Result = false; Close(); };

            _btnPreview = new Button { Text = "Preview", Width = 75, Enabled = false };
            _btnPreview.Click += (s, e) => UpdatePreview(false); 

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;

            UpdateControlEnableStates();
        }

        private void OnToleranceTextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingTextProgrammatically) return;
            
            // Neutral pending state while typing
            _previewPending = true;
            _lblProcessedCount.Text = "Waiting for input...";
            _lblMaxDev.Text = "";
            _reportPanel.Content = null;
            
            _btnPreview.Enabled = false;
            _btnPreview.TextColor = SystemColors.ControlText; // Keep neutral
            _btnPreview.Font = new Font(SystemFont.Default, _btnPreview.Font.Size);

            // Debounce: reset the 1-second timer on every keystroke
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void OnTypingTimerElapsed(object sender, EventArgs e)
        {
            _typingTimer.Stop();
            
            Application.Instance.AsyncInvoke(() => 
            {
                bool allValid = true;
                _isUpdatingTextProgrammatically = true;

                double ProcessBox(TextBox tb, double defaultVal)
                {
                    if (!double.TryParse(tb.Text, out double val)) 
                    {
                        tb.BackgroundColor = Colors.LightPink;
                        allValid = false;
                        return double.NaN;
                    }

                    bool changed = false;

                    // Clamping and Negative re-assignment
                    if (val < 0.0) 
                    {
                        val = defaultVal;
                        changed = true;
                    }
                    else if (val < Rhino.RhinoMath.ZeroTolerance) 
                    {
                        val = Rhino.RhinoMath.ZeroTolerance;
                        changed = true;
                    }

                    if (changed)
                    {
                        tb.Text = val.ToString("G"); 
                        tb.CaretIndex = 0; // Force scroll to the left so significant digits are visible
                    }
                    
                    tb.BackgroundColor = Colors.White;
                    return val;
                }

                ProcessBox(_txtDistTol, SimplifyCrvOptions.GetDefaultDistTol());
                ProcessBox(_txtAngleTol, Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees);
                ProcessBox(_txtLineDistTol, SimplifyCrvOptions.GetDefaultLineTol());
                ProcessBox(_txtArcBulgeTol, SimplifyCrvOptions.GetDefaultArcBulgeTol());
                ProcessBox(_txtMinSegLen, SimplifyCrvOptions.GetDefaultMinSegLength());

                _isUpdatingTextProgrammatically = false;

                if (allValid)
                {
                    UpdatePreview(true);
                }
                else
                {
                    SetPreviewRequired("Invalid tolerance entered. Calculation paused.");
                }
            });
        }

        private bool ValidateTolerances()
        {
            return double.TryParse(_txtDistTol.Text, out _) &&
                   double.TryParse(_txtAngleTol.Text, out _) &&
                   double.TryParse(_txtLineDistTol.Text, out _) &&
                   double.TryParse(_txtArcBulgeTol.Text, out _) &&
                   double.TryParse(_txtMinSegLen.Text, out _);
        }

        private void UpdateControlEnableStates()
        {
            bool globalEnabled = _rblConversionState.SelectedKey == "On";
            
            _chkSpansToLines.Enabled = globalEnabled;
            _chkSpansToArcs.Enabled = globalEnabled;
            _chkAdjustG1.Enabled = false; // TODO: Set to globalEnabled once smoothing routine is added.
            _chkSpansToBeziers.Enabled = globalEnabled;
            _chkMakeUniform.Enabled = globalEnabled;
            _chkSplitAllKnots.Enabled = globalEnabled;
            _chkSplitFullyMultiple.Enabled = globalEnabled && !(_chkSplitAllKnots.Checked ?? false);
            _chkPolylineOutput.Enabled = globalEnabled;
            
            _txtLineDistTol.Enabled = globalEnabled && (_chkSpansToLines.Checked ?? false);
            _txtArcBulgeTol.Enabled = globalEnabled && (_chkSpansToArcs.Checked ?? false);
            
            bool bezEnabled = globalEnabled && (_chkSpansToBeziers.Checked ?? false);
            if (_chkDegrees != null)
            {
                foreach (var chk in _chkDegrees) chk.Enabled = bezEnabled;
            }
        }

        private void SetupLayout()
        {
            Label Gap() => new Label { Width = 10 };

            var tolGrid = new TableLayout { Spacing = new Size(8, 6) };
            tolGrid.Rows.Add(new TableRow(
                new Label { Text = "Max crv dev:", VerticalAlignment = VerticalAlignment.Center }, _txtDistTol, Gap(),
                new Label { Text = "Max angle dev (°):", VerticalAlignment = VerticalAlignment.Center }, _txtAngleTol, Gap(),
                null // The null cell soaks up extra width, keeping the text boxes naturally sized
            ));
            
            tolGrid.Rows.Add(new TableRow(
                new Label { Text = "Min seg len:", VerticalAlignment = VerticalAlignment.Center }, _txtMinSegLen, Gap(),
                new Label { Text = "Preview thk:", VerticalAlignment = VerticalAlignment.Center }, _stepPreviewThickness, Gap(),
                null 
            ));

            var degStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 4 };
            degStack.Items.Add(new Label { Text = "NURBS degs: ", VerticalAlignment = VerticalAlignment.Center });
            foreach (var c in _chkDegrees) degStack.Items.Add(c);

            var lineStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, VerticalContentAlignment = VerticalAlignment.Center, Items = { _chkSpansToLines, new Label { Text = "Line dist tol:" }, _txtLineDistTol } };
            var arcStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, VerticalContentAlignment = VerticalAlignment.Center, Items = { _chkSpansToArcs, new Label { Text = "Min arc bulge:" }, _txtArcBulgeTol } };

            var optionsStack = new StackLayout
            {
                Spacing = 5,
                Items = { 
                    _rblConversionState,
                    new Panel { Height = 4 }, 
                    lineStack, 
                    arcStack, 
                    _chkAdjustG1,
                    _chkSpansToBeziers, 
                    _chkMakeUniform,
                    _chkSplitAllKnots, 
                    _chkSplitFullyMultiple, 
                    _chkPolylineOutput 
                }
            };

            var timeoutPreviewStack = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { 
                    new Label { Text = "Timeout (s):", VerticalAlignment = VerticalAlignment.Center }, _stepPreviewTimeout, Gap(),
                    _btnPreview 
                }
            };

            var resultsHeader = new StackLayout 
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Items = { _lblProcessedCount, _lblMaxDev }
            };

            var buttonStack = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _btnOk, _btnCancel }
            };

            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            layout.AddRow(tolGrid);
            layout.AddRow(degStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(optionsStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(timeoutPreviewStack); 
            layout.AddRow(_progressBar);
            layout.AddRow(resultsHeader);
            layout.AddRow(_reportPanel);
            layout.AddRow(buttonStack); 

            Content = layout;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_typingTimer != null)
            {
                _typingTimer.Stop();
                _typingTimer.Dispose();
            }

            if (double.TryParse(_txtDistTol.Text, out double dTol)) SimplifyCrvOptions.DistanceTolerance = dTol;
            if (double.TryParse(_txtLineDistTol.Text, out double lTol)) SimplifyCrvOptions.LineDistanceTolerance = lTol;
            if (double.TryParse(_txtMinSegLen.Text, out double sTol)) SimplifyCrvOptions.MinSegmentLength = sTol;
            if (double.TryParse(_txtArcBulgeTol.Text, out double abTol)) SimplifyCrvOptions.ArcBulgeTolerance = abTol;
            if (double.TryParse(_txtAngleTol.Text, out double aTol)) SimplifyCrvOptions.AngleTolerance = aTol;
            
            SimplifyCrvOptions.PreviewThickness = (int)_stepPreviewThickness.Value;
            SimplifyCrvOptions.AutoPreviewTimeout = (int)_stepPreviewTimeout.Value;

            SimplifyCrvOptions.ConvertLines = _chkSpansToLines.Checked ?? false;
            SimplifyCrvOptions.ConvertArcs = _chkSpansToArcs.Checked ?? false;
            SimplifyCrvOptions.AdjustG1 = _chkAdjustG1.Checked ?? false;
            SimplifyCrvOptions.ConvertBeziers = _chkSpansToBeziers.Checked ?? false;
            SimplifyCrvOptions.MakeUniform = _chkMakeUniform.Checked ?? false;
            
            SimplifyCrvOptions.TargetDegrees.Clear();
            for (int i = 0; i < 9; i++)
            {
                if (_chkDegrees[i].Checked == true) SimplifyCrvOptions.TargetDegrees.Add(i + 1);
            }
            
            SimplifyCrvOptions.SplitAllKnots = _chkSplitAllKnots.Checked ?? false;
            SimplifyCrvOptions.SplitFullyMultiple = _chkSplitFullyMultiple.Checked ?? false;
            SimplifyCrvOptions.MergePolylines = _chkPolylineOutput.Checked ?? false;
            
            base.OnClosed(e); 
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            UpdateControlEnableStates();
            UpdatePreview(true); // Attempt auto-preview
        }

        // Dedicated state for when manual intervention is actually required (like a timeout)
        private void SetPreviewRequired(string message)
        {
            _previewPending = true;
            
            _btnPreview.Enabled = true;
            _btnPreview.TextColor = Colors.Red;
            _btnPreview.Font = new Font(SystemFont.Bold, _btnPreview.Font.Size);
            
            _lblProcessedCount.Text = message;
            _lblMaxDev.Text = "";
            _reportPanel.Content = null;
            
            if (ParentWindow != null) this.Size = new Size(this.Width, -1);
        }

        private void ClearPreviewPending(int processedCount, double maxDev)
        {
            _previewPending = false;
            
            _btnPreview.Enabled = false; 
            _btnPreview.TextColor = SystemColors.ControlText; 
            _btnPreview.Font = new Font(SystemFont.Default, _btnPreview.Font.Size);

            _lblProcessedCount.Text = $"Processed {processedCount} curve(s).";
            _lblMaxDev.Text = $"Max dev: {maxDev:E3}";
        }

        private bool UpdatePreview(bool autoRun)
        {
            _conduit.HighlightLines = _chkSpansToLines.Checked ?? false;
            _conduit.HighlightArcs = _chkSpansToArcs.Checked ?? false;
            _conduit.PreviewThickness = (int)_stepPreviewThickness.Value;
            
            ResultCurves = new List<Curve>();
            MaxDeviation = 0.0;

            bool disableConversions = _rblConversionState.SelectedKey == "Off";

            if (!AnyOptionChecked || disableConversions)
            {
                foreach (var c in _inputCurves) ResultCurves.Add(c.DuplicateCurve());
                ClearPreviewPending(_inputCurves.Count, 0.0);
                
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);
                _conduit.PreviewCurves = ResultCurves;
                Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
                return true;
            }

            _progressBar.Visible = true;
            _progressBar.Value = 0;

            bool success = true;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            double timeoutMs = _stepPreviewTimeout.Value * 1000.0;

            using (var escape = new EscapeTracker())
            {
                double distTol = double.Parse(_txtDistTol.Text);
                double lineTol = double.Parse(_txtLineDistTol.Text);
                double angleTolRad = Rhino.RhinoMath.ToRadians(double.Parse(_txtAngleTol.Text));
                double arcBulgeTol = double.Parse(_txtArcBulgeTol.Text);
                double minSegLen = double.Parse(_txtMinSegLen.Text);
                
                List<int> targets = new List<int>();
                for (int i = 0; i < 9; i++) if (_chkDegrees[i].Checked == true) targets.Add(i + 1);
                
                int curvesProcessed = 0;
                foreach (var inputCurve in _inputCurves)
                {
                    if (escape.IsCanceled)
                    {
                        Rhino.RhinoApp.WriteLine("Calculation cancelled by user.");
                        SetPreviewRequired("Calculation cancelled.");
                        success = false;
                        break; 
                    }

                    if (autoRun && sw.ElapsedMilliseconds > timeoutMs)
                    {
                        SetPreviewRequired("Preview timed out. Click Preview to compute.");
                        success = false;
                        break;
                    }

                    var result = SimplifyCrvLogic.ExecutePipeline(
                        inputCurve, distTol, angleTolRad, lineTol, arcBulgeTol, minSegLen,
                        _chkSpansToLines.Checked ?? false, 
                        _chkSpansToArcs.Checked ?? false,
                        _chkAdjustG1.Checked ?? false,
                        _chkSpansToBeziers.Checked ?? false,
                        targets,
                        _chkMakeUniform.Checked ?? false,
                        _chkSplitAllKnots.Checked ?? false, 
                        _chkSplitFullyMultiple.Checked ?? false, 
                        _chkPolylineOutput.Checked ?? false
                        );

                    MaxDeviation = Math.Max(MaxDeviation, result.MaxDev);
                    ResultCurves.Add(result.ResultCurve);

                    curvesProcessed++;
                    _progressBar.Value++;
                    Rhino.RhinoApp.Wait(); 
                }

                if (success)
                {
                    var inStats = CurveStats.Analyze(_inputCurves);
                    var outStats = CurveStats.Analyze(ResultCurves);
                    HasChanges = inStats.HasDifferences(outStats);

                    ClearPreviewPending(curvesProcessed, MaxDeviation);
                    BuildReportTable(inStats, outStats);
                }
                else
                {
                    ResultCurves.Clear(); 
                }
            }
            _progressBar.Visible = false;

            if (ParentWindow != null) this.Size = new Size(this.Width, -1);
            
            _conduit.PreviewCurves = ResultCurves;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
            
            return success;
        }

        private void BuildReportTable(CurveStats inStats, CurveStats outStats)
        {
            var table = new TableLayout { Spacing = new Size(15, 2) };
            
            Font regFont = new Font(SystemFont.Default, 9);
            Font boldFont = new Font(SystemFont.Bold, 9);
            
            table.Rows.Add(new TableRow(
                new Label { Text = "Type", Font = boldFont },
                new Label { Text = "Init", Font = boldFont },
                new Label { Text = "Final", Font = boldFont },
                new Label { Text = "Delta", Font = boldFont }
            ));

            void AddRow(string name, int init, int final, int indentLevel = 0, bool isHeader = false)
            {
                if (init == 0 && final == 0 && !isHeader) return;

                string prefix = new string(' ', indentLevel * 3);
                int delta = final - init;
                string deltaStr = delta > 0 ? $"+{delta}" : (delta < 0 ? $"{delta}" : " 0");
                
                if (isHeader)
                {
                    table.Rows.Add(new TableRow(
                        new Label { Text = name, Font = boldFont },
                        new Label { Text = "" }, new Label { Text = "" }, new Label { Text = "" }
                    ));
                    return;
                }

                table.Rows.Add(new TableRow(
                    new Label { Text = prefix + name, Font = regFont },
                    new Label { Text = init.ToString(), Font = regFont },
                    new Label { Text = final.ToString(), Font = regFont },
                    new Label { Text = deltaStr, Font = regFont }
                ));
            }

            AddRow("Top-level objects", 0, 0, 0, true);
            AddRow("Lines", inStats.TopLines, outStats.TopLines, 1);
            AddRow("Polylines", inStats.TopPolylines, outStats.TopPolylines, 1);
            AddRow("Segments", inStats.TopPolylineSegments, outStats.TopPolylineSegments, 2);
            AddRow("Arcs", inStats.TopArcs, outStats.TopArcs, 1);
            
            AddRow("NURBS curves", inStats.TopNurbs + inStats.TopBeziers, outStats.TopNurbs + outStats.TopBeziers, 1);
            AddRow("Beziers", inStats.TopBeziers, outStats.TopBeziers, 2);
            
            AddRow("Polycurves", inStats.TopPolyCurves, outStats.TopPolyCurves, 1);
            
            if (inStats.PcSgTotal > 0 || outStats.PcSgTotal > 0)
            {
                AddRow("Polycurve segments", 0, 0, 0, true);
                AddRow("Total segments", inStats.PcSgTotal, outStats.PcSgTotal, 1);
                AddRow("Lines", inStats.PcSgLines, outStats.PcSgLines, 1);
                AddRow("Polylines", inStats.PcSgPolylines, outStats.PcSgPolylines, 1);
                AddRow("Segments", inStats.PcSgPolylineSegments, outStats.PcSgPolylineSegments, 2);
                AddRow("Arcs", inStats.PcSgArcs, outStats.PcSgArcs, 1);
                
                AddRow("NURBS curves", inStats.PcSgNurbs + inStats.PcSgBeziers, outStats.PcSgNurbs + outStats.PcSgBeziers, 1);
                AddRow("Beziers", inStats.PcSgBeziers, outStats.PcSgBeziers, 2);
            }

            _reportPanel.Content = table;
        }
    }

    public class CurveStats
    {
        public int TopLines, TopPolylines, TopPolylineSegments, TopArcs, TopBeziers, TopNurbs, TopPolyCurves;
        public int PcSgTotal, PcSgLines, PcSgPolylines, PcSgPolylineSegments, PcSgArcs, PcSgBeziers, PcSgNurbs;

        public static CurveStats Analyze(IEnumerable<Curve> curves)
        {
            var stats = new CurveStats();
            foreach (var c in curves) AnalyzeSingle(c, stats, true);
            return stats;
        }

        private static void AnalyzeSingle(Curve c, CurveStats stats, bool isTopLevel)
        {
            if (c == null) return;

            if (c is PolyCurve pc)
            {
                if (isTopLevel) stats.TopPolyCurves++;
                stats.PcSgTotal += pc.SegmentCount;
                for (int i = 0; i < pc.SegmentCount; i++) AnalyzeSingle(pc.SegmentCurve(i), stats, false);
            }
            else if (c is PolylineCurve plc)
            {
                if (isTopLevel) { stats.TopPolylines++; stats.TopPolylineSegments += Math.Max(0, plc.PointCount - 1); }
                else { stats.PcSgPolylines++; stats.PcSgPolylineSegments += Math.Max(0, plc.PointCount - 1); }
            }
            else if (c is LineCurve) 
            {
                if (isTopLevel) stats.TopLines++; else stats.PcSgLines++;
            }
            else if (c is ArcCurve) 
            {
                if (isTopLevel) stats.TopArcs++; else stats.PcSgArcs++;
            }
            else if (c is NurbsCurve nc && nc.SpanCount == 1 && !nc.IsRational)
            {
                if (isTopLevel) stats.TopBeziers++; else stats.PcSgBeziers++;
            }
            else
            {
                if (isTopLevel) stats.TopNurbs++; else stats.PcSgNurbs++;
            }
        }

        public bool HasDifferences(CurveStats other)
        {
            return TopLines != other.TopLines || TopPolylines != other.TopPolylines || 
                   TopPolylineSegments != other.TopPolylineSegments || TopArcs != other.TopArcs || 
                   TopBeziers != other.TopBeziers || TopNurbs != other.TopNurbs || 
                   TopPolyCurves != other.TopPolyCurves || 
                   PcSgTotal != other.PcSgTotal || PcSgLines != other.PcSgLines || 
                   PcSgPolylines != other.PcSgPolylines || PcSgPolylineSegments != other.PcSgPolylineSegments || 
                   PcSgArcs != other.PcSgArcs || PcSgBeziers != other.PcSgBeziers || 
                   PcSgNurbs != other.PcSgNurbs;
        }
    }
}