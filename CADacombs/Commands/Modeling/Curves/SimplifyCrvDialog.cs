using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;
using CADacombs.Core; // Required for CADacombsDialogBase

namespace CADacombs.Commands.Modeling.Curves
{
    public class SimplifyCrvDialog : CADacombsDialogBase
    {
        private List<Curve> _inputCurves;
        private SimplifyCrvConduit _conduit;
        
        private CheckBox _chkSpansToLines;
        private CheckBox _chkSpansToArcs;
        private CheckBox _chkPolylineOutput;
        private CheckBox _chkSplitAllKnots;
        private CheckBox _chkSplitFullyMultiple;
        private CheckBox _chkAdjustG1;
        
        private Label _lblProcessedCount;
        private Panel _reportPanel;
        
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnPreview;

        private bool _isComplex;
        public List<Curve> ResultCurves { get; private set; }
        public bool AnyOptionChecked => _chkSpansToLines.Checked == true || _chkSpansToArcs.Checked == true || _chkPolylineOutput.Checked == true;
        
        public bool HasChanges { get; private set; } 

        public SimplifyCrvDialog(List<Curve> inputCurves, SimplifyCrvConduit conduit)
        {
            _inputCurves = inputCurves;
            _conduit = conduit;
            
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
            
            if (!_isComplex) UpdatePreview();
        }

        // --- DialogBase Implementations ---
        protected override Eto.Drawing.Point? LoadSavedLocation() => SimplifyCrvOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => SimplifyCrvOptions.WindowLocation = location;

        private void CreateControls()
        {
            _chkSpansToLines = new CheckBox { Text = "Convert spans to lines", Checked = SimplifyCrvOptions.ConvertLines };
            _chkSpansToArcs = new CheckBox { Text = "Convert spans to arcs", Checked = SimplifyCrvOptions.ConvertArcs };
            
            _chkPolylineOutput = new CheckBox 
            { 
                Text = "Merge contiguous lines to polylines", 
                Checked = SimplifyCrvOptions.MergePolylines,
                ToolTip = "Groups contiguous line segments into single Polyline objects. Requires 'Convert spans to lines' to process linear NURBS spans."
            };
            _chkSplitAllKnots = new CheckBox { Text = "Split at all multiple knots", Checked = SimplifyCrvOptions.SplitAllKnots };
            _chkSplitFullyMultiple = new CheckBox { Text = "Split at fully multiple knots", Checked = SimplifyCrvOptions.SplitFullyMultiple };
            _chkAdjustG1 = new CheckBox { Text = "Adjust G1", Checked = SimplifyCrvOptions.AdjustG1 };

            // Manage the disabled state initially
            if (_chkSplitAllKnots.Checked == true) _chkSplitFullyMultiple.Enabled = false;

            // Event Listeners
            _chkSpansToLines.CheckedChanged += OnOptionChanged;
            _chkSpansToArcs.CheckedChanged += OnOptionChanged;
            _chkPolylineOutput.CheckedChanged += OnOptionChanged;
            _chkAdjustG1.CheckedChanged += OnOptionChanged;
            _chkSplitFullyMultiple.CheckedChanged += OnOptionChanged;

            _chkSplitAllKnots.CheckedChanged += (s, e) =>
            {
                _chkSplitFullyMultiple.Enabled = !(_chkSplitAllKnots.Checked ?? false);
                OnOptionChanged(s, e);
            };

            _lblProcessedCount = new Label { Text = "Processed 0 curve(s)." };
            _reportPanel = new Panel();

            _btnOk = new Button { Text = "OK", Width = 75 };
            _btnOk.Click += (s, e) => { Result = true; Close(); };

            _btnCancel = new Button { Text = "Cancel", Width = 75 };
            _btnCancel.Click += (s, e) => { Result = false; Close(); };

            _btnPreview = new Button { Text = "Preview", Width = 75, Enabled = _isComplex };
            _btnPreview.Click += (s, e) => UpdatePreview();

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;
        }

        private void SetupLayout()
        {
            var optionsStack = new StackLayout
            {
                Spacing = 5,
                Items = { 
                    _chkSpansToLines, 
                    _chkSpansToArcs, 
                    _chkSplitAllKnots, 
                    _chkSplitFullyMultiple, 
                    _chkAdjustG1,
                    _chkPolylineOutput // Moved to the very bottom
                }
            };

            var buttonStack = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Items = { _btnOk, _btnCancel, _btnPreview }
            };

            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 10) };
            
            layout.AddRow(optionsStack);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(_lblProcessedCount);
            layout.AddRow(_reportPanel);
            layout.AddRow(buttonStack); 

            Content = layout;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Save states regardless of OK or Cancel
            SimplifyCrvOptions.ConvertLines = _chkSpansToLines.Checked ?? false;
            SimplifyCrvOptions.ConvertArcs = _chkSpansToArcs.Checked ?? false;
            SimplifyCrvOptions.MergePolylines = _chkPolylineOutput.Checked ?? false;
            SimplifyCrvOptions.SplitAllKnots = _chkSplitAllKnots.Checked ?? false;
            SimplifyCrvOptions.SplitFullyMultiple = _chkSplitFullyMultiple.Checked ?? false;
            SimplifyCrvOptions.AdjustG1 = _chkAdjustG1.Checked ?? false;
            
            base.OnClosed(e); // Saves the window location via CADacombsDialogBase
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            if (_isComplex) 
            {
                _lblProcessedCount.Text = "Options changed. Click Preview to update.";
                _reportPanel.Content = null;
                if (ParentWindow != null) this.Size = new Size(this.Width, -1);
            }
            else UpdatePreview();
        }

        private void UpdatePreview()
        {
            _conduit.HighlightLines = _chkSpansToLines.Checked ?? false;
            _conduit.HighlightArcs = _chkSpansToArcs.Checked ?? false;
            ResultCurves = new List<Curve>();

            if (!AnyOptionChecked)
            {
                foreach (var c in _inputCurves) ResultCurves.Add(c.DuplicateCurve());
            }
            else
            {
                double distTol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                double angleTol = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
                
                foreach (var inputCurve in _inputCurves)
                {
                    var result = SimplifyCrvLogic.ExecutePipeline(
                        inputCurve, distTol, angleTol,
                        _chkSpansToLines.Checked ?? false, 
                        _chkSpansToArcs.Checked ?? false,
                        _chkPolylineOutput.Checked ?? false,
                        _chkSplitAllKnots.Checked ?? false, 
                        _chkSplitFullyMultiple.Checked ?? false, 
                        _chkAdjustG1.Checked ?? false
                        );

                    ResultCurves.Add(result.ResultCurve);
                }
            }

            var inStats = CurveStats.Analyze(_inputCurves);
            var outStats = CurveStats.Analyze(ResultCurves);
            HasChanges = inStats.HasDifferences(outStats);

            _lblProcessedCount.Text = $"Processed {_inputCurves.Count} curve(s).";
            BuildReportTable(inStats, outStats);

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
                AddRow("NURBS curves", inStats.PcSgNurbs, outStats.PcSgNurbs, 1);
            }

            _reportPanel.Content = table;
        }
    }

    // (CurveStats class remains identical, append it here)
    public class CurveStats
    {
        public int TopLines, TopPolylines, TopPolylineSegments, TopArcs, TopNurbs, TopPolyCurves;
        public int PcSgTotal, PcSgLines, PcSgPolylines, PcSgPolylineSegments, PcSgArcs, PcSgNurbs;

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
            else
            {
                if (isTopLevel) stats.TopNurbs++; else stats.PcSgNurbs++;
            }
        }

        public bool HasDifferences(CurveStats other)
        {
            return TopLines != other.TopLines || TopPolylines != other.TopPolylines || 
                   TopPolylineSegments != other.TopPolylineSegments || TopArcs != other.TopArcs || 
                   TopNurbs != other.TopNurbs || TopPolyCurves != other.TopPolyCurves || 
                   PcSgTotal != other.PcSgTotal || PcSgLines != other.PcSgLines || 
                   PcSgPolylines != other.PcSgPolylines || PcSgPolylineSegments != other.PcSgPolylineSegments || 
                   PcSgArcs != other.PcSgArcs || PcSgNurbs != other.PcSgNurbs;
        }
    }
}