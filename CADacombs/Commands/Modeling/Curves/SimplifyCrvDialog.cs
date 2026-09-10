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
        private CheckBox[] _chkDegrees;
        
        private CheckBox _chkSpansToLines;
        private CheckBox _chkSpansToArcs;
        private CheckBox _chkAdjustG1;
        private CheckBox _chkSpansToBeziers;
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

        private bool _isComplex;

        public List<Curve> ResultCurves { get; private set; }
        public double MaxDeviation { get; private set; }
        
        public bool AnyOptionChecked => _chkSpansToLines.Checked == true || 
                                        _chkSpansToArcs.Checked == true || 
                                        _chkAdjustG1.Checked == true ||
                                        _chkSpansToBeziers.Checked == true || 
                                        _chkSplitAllKnots.Checked == true ||
                                        _chkSplitFullyMultiple.Checked == true ||
                                        _chkPolylineOutput.Checked == true;
        
        public bool HasChanges { get; private set; } 

        public SimplifyCrvDialog(List<Curve> inputCurves, SimplifyCrvConduit conduit)
        {
            _inputCurves = inputCurves;
            _conduit = conduit;
            
            if (SimplifyCrvOptions.DistanceTolerance < 0)
                SimplifyCrvOptions.DistanceTolerance = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            if (SimplifyCrvOptions.AngleTolerance < 0)
                SimplifyCrvOptions.AngleTolerance = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;

            int totalSpans = 0;
            foreach (var crv in _inputCurves) totalSpans += crv.SpanCount;
            _isComplex = totalSpans > 500;

            Title = "CADacombs SimplifyCrv";
            Resizable = false;
            MinimumSize = new Size(380, 0); 
            AutoSize = true;
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();
            
            this.Shown += (s, e) => 
            {
                bool requiresManual = _isComplex || (_chkSpansToBeziers.Checked ?? false);
                _btnPreview.Enabled = requiresManual;
                if (!requiresManual) UpdatePreview();
            };
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => SimplifyCrvOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => SimplifyCrvOptions.WindowLocation = location;

        private void CreateControls()
        {
            _txtDistTol = new TextBox { Text = SimplifyCrvOptions.DistanceTolerance.ToString(), Width = 60 };
            _txtAngleTol = new TextBox { Text = SimplifyCrvOptions.AngleTolerance.ToString(), Width = 60 };

            _chkDegrees = new CheckBox[9];
            for (int i = 0; i < 9; i++)
            {
                _chkDegrees[i] = new CheckBox 
                { 
                    Text = (i + 1).ToString(), 
                    Checked = SimplifyCrvOptions.TargetDegrees.Contains(i + 1)
                };
                _chkDegrees[i].CheckedChanged += OnOptionChanged;
            }

            _chkSpansToLines = new CheckBox { Text = "Convert spans to lines", Checked = SimplifyCrvOptions.ConvertLines };
            _chkSpansToArcs = new CheckBox { Text = "Convert spans to arcs", Checked = SimplifyCrvOptions.ConvertArcs };
            _chkAdjustG1 = new CheckBox { Text = "Adjust G1", Checked = SimplifyCrvOptions.AdjustG1 };
            _chkSpansToBeziers = new CheckBox { Text = "Convert spans to Beziers", Checked = SimplifyCrvOptions.ConvertBeziers };
            _chkSplitAllKnots = new CheckBox { Text = "Split at all multiple knots", Checked = SimplifyCrvOptions.SplitAllKnots };
            _chkSplitFullyMultiple = new CheckBox { Text = "Split at fully multiple knots", Checked = SimplifyCrvOptions.SplitFullyMultiple };
            _chkPolylineOutput = new CheckBox { Text = "Merge contiguous lines to polylines", Checked = SimplifyCrvOptions.MergePolylines };
            
            if (_chkSplitAllKnots.Checked == true) _chkSplitFullyMultiple.Enabled = false;

            _txtDistTol.TextChanged += OnOptionChanged;
            _txtAngleTol.TextChanged += OnOptionChanged;
            _chkSpansToLines.CheckedChanged += OnOptionChanged;
            _chkSpansToArcs.CheckedChanged += OnOptionChanged;
            _chkAdjustG1.CheckedChanged += OnOptionChanged;
            _chkSpansToBeziers.CheckedChanged += OnOptionChanged;
            _chkPolylineOutput.CheckedChanged += OnOptionChanged;
            _chkSplitFullyMultiple.CheckedChanged += OnOptionChanged;

            _chkSplitAllKnots.CheckedChanged += (s, e) =>
            {
                _chkSplitFullyMultiple.Enabled = !(_chkSplitAllKnots.Checked ?? false);
                OnOptionChanged(s, e);
            };

            _lblProcessedCount = new Label { Text = "Processed 0 curve(s)." };
            _lblMaxDev = new Label { Text = "Max Dev: 0.0", TextColor = Colors.DimGray };
            _reportPanel = new Panel();
            
            _progressBar = new ProgressBar { MinValue = 0, MaxValue = _inputCurves.Count, Value = 0, Visible = false, Height = 10 };

            _btnOk = new Button { Text = "OK", Width = 75 };
            _btnOk.Click += (s, e) => { Result = true; Close(); };

            _btnCancel = new Button { Text = "Cancel", Width = 75 };
            _btnCancel.Click += (s, e) => { Result = false; Close(); };

            _btnPreview = new Button { Text = "Preview", Width = 75, Enabled = true };
            _btnPreview.Click += (s, e) => UpdatePreview();

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;
        }

        private void SetupLayout()
        {
            var tolLayout = new DynamicLayout { DefaultSpacing = new Size(5, 5) };
            tolLayout.AddRow(new Label { Text = "Distance tol.:", VerticalAlignment = VerticalAlignment.Center }, _txtDistTol, 
                             new Label { Text = "Angle tol. (°):", VerticalAlignment = VerticalAlignment.Center }, _txtAngleTol);

            var degStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 4 };
            degStack.Items.Add(new Label { Text = "Allowed degrees: ", VerticalAlignment = VerticalAlignment.Center });
            foreach (var c in _chkDegrees) degStack.Items.Add(c);

            var optionsStack = new StackLayout
            {
                Spacing = 5,
                Items = { 
                    _chkSpansToLines, 
                    _chkSpansToArcs, 
                    _chkAdjustG1,
                    _chkSpansToBeziers, 
                    _chkSplitAllKnots, 
                    _chkSplitFullyMultiple, 
                    _chkPolylineOutput 
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
                Items = { _btnOk, _btnCancel, _btnPreview }
            };

            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            layout.AddRow(tolLayout);
            layout.AddRow(degStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(optionsStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(_progressBar);
            layout.AddRow(resultsHeader);
            layout.AddRow(_reportPanel);
            layout.AddRow(buttonStack); 

            Content = layout;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (double.TryParse(_txtDistTol.Text, out double dTol)) SimplifyCrvOptions.DistanceTolerance = dTol;
            if (double.TryParse(_txtAngleTol.Text, out double aTol)) SimplifyCrvOptions.AngleTolerance = aTol;

            SimplifyCrvOptions.ConvertLines = _chkSpansToLines.Checked ?? false;
            SimplifyCrvOptions.ConvertArcs = _chkSpansToArcs.Checked ?? false;
            SimplifyCrvOptions.AdjustG1 = _chkAdjustG1.Checked ?? false;
            SimplifyCrvOptions.ConvertBeziers = _chkSpansToBeziers.Checked ?? false;
            
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
            bool requiresManual = _isComplex || (_chkSpansToBeziers.Checked ?? false);
            _btnPreview.Enabled = requiresManual;

            if (requiresManual) 
            {
                _lblProcessedCount.Text = "Options changed. Click Preview to update.";
                _lblMaxDev.Text = "";
                _reportPanel.Content = null;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);
            }
            else 
            {
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            _conduit.HighlightLines = _chkSpansToLines.Checked ?? false;
            _conduit.HighlightArcs = _chkSpansToArcs.Checked ?? false;
            ResultCurves = new List<Curve>();
            MaxDeviation = 0.0;

            if (!AnyOptionChecked)
            {
                foreach (var c in _inputCurves) ResultCurves.Add(c.DuplicateCurve());
            }
            else
            {
                _progressBar.Visible = true;
                _progressBar.Value = 0;

                using (var escape = new EscapeTracker())
                {
                    if (!double.TryParse(_txtDistTol.Text, out double distTol)) distTol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                    if (!double.TryParse(_txtAngleTol.Text, out double angleTolDegrees)) angleTolDegrees = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceDegrees;
                    double angleTolRad = Rhino.RhinoMath.ToRadians(angleTolDegrees);
                    
                    List<int> targets = new List<int>();
                    for (int i = 0; i < 9; i++) if (_chkDegrees[i].Checked == true) targets.Add(i + 1);
                    
                    int curvesProcessed = 0;
                    foreach (var inputCurve in _inputCurves)
                    {
                        if (escape.IsCanceled)
                        {
                            Rhino.RhinoApp.WriteLine("Calculation cancelled by user.");
                            _lblProcessedCount.Text = "Calculation cancelled.";
                            break; 
                        }

                        var result = SimplifyCrvLogic.ExecutePipeline(
                            inputCurve, distTol, angleTolRad,
                            _chkSpansToLines.Checked ?? false, 
                            _chkSpansToArcs.Checked ?? false,
                            _chkAdjustG1.Checked ?? false,
                            _chkSpansToBeziers.Checked ?? false,
                            targets,
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

                    if (!escape.IsCanceled)
                    {
                        var inStats = CurveStats.Analyze(_inputCurves);
                        var outStats = CurveStats.Analyze(ResultCurves);
                        HasChanges = inStats.HasDifferences(outStats);

                        _lblProcessedCount.Text = $"Processed {curvesProcessed} curve(s).";
                        _lblMaxDev.Text = $"Max Dev: {MaxDeviation:E3}";
                        BuildReportTable(inStats, outStats);
                    }
                }
                _progressBar.Visible = false;
            }

            if (ParentWindow != null) this.Size = new Size(this.Width, -1);

            _conduit.PreviewCurves = ResultCurves;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
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
            AddRow("Beziers", inStats.TopBeziers, outStats.TopBeziers, 1);
            AddRow("NURBS curves", inStats.TopNurbs, outStats.TopNurbs, 1);
            AddRow("Polycurves", inStats.TopPolyCurves, outStats.TopPolyCurves, 1);
            
            if (inStats.PcSgTotal > 0 || outStats.PcSgTotal > 0)
            {
                AddRow("Polycurve segments", 0, 0, 0, true);
                AddRow("Total segments", inStats.PcSgTotal, outStats.PcSgTotal, 1);
                AddRow("Lines", inStats.PcSgLines, outStats.PcSgLines, 1);
                AddRow("Polylines", inStats.PcSgPolylines, outStats.PcSgPolylines, 1);
                AddRow("Segments", inStats.PcSgPolylineSegments, outStats.PcSgPolylineSegments, 2);
                AddRow("Arcs", inStats.PcSgArcs, outStats.PcSgArcs, 1);
                AddRow("Beziers", inStats.PcSgBeziers, outStats.PcSgBeziers, 1);
                AddRow("NURBS curves", inStats.PcSgNurbs, outStats.PcSgNurbs, 1);
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