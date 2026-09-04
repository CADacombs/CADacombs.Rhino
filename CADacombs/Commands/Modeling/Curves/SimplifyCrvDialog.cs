using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;

namespace CADacombs.Commands.Modeling.Curves
{
    public class SimplifyCrvDialog : Dialog<bool>
    {
        private List<Curve> _inputCurves;
        private SimplifyCrvConduit _conduit;
        
        private CheckBox _chkSpansToLines;
        private CheckBox _chkSpansToArcs;
        private CheckBox _chkPolylineOutput;
        
        private Label _lblProcessedCount;
        private Panel _reportPanel;
        
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnPreview;

        private bool _isComplex;
        public List<Curve> ResultCurves { get; private set; }
        public bool AnyOptionChecked => _chkSpansToLines.Checked == true || _chkSpansToArcs.Checked == true || _chkPolylineOutput.Checked == true;
        
        // Expose this to the Command so it knows whether to skip replacing geometry
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
            
            // FIX: Removed height constraint and enabled AutoSize so the window wraps the table dynamically
            MinimumSize = new Size(380, 0); 
            AutoSize = true;
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();
            
            if (!_isComplex) UpdatePreview();
        }

        private void CreateControls()
        {
            _chkSpansToLines = new CheckBox { Text = "Convert Spans to Lines", Checked = true };
            _chkSpansToArcs = new CheckBox { Text = "Convert Spans to Arcs", Checked = true };
            _chkPolylineOutput = new CheckBox { Text = "Polyline output", Checked = true };

            _chkSpansToLines.CheckedChanged += OnOptionChanged;
            _chkSpansToArcs.CheckedChanged += OnOptionChanged;
            _chkPolylineOutput.CheckedChanged += OnOptionChanged;

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
                Items = { _chkSpansToLines, _chkSpansToArcs, _chkPolylineOutput }
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
            
            // FIX: Removed the vertical null spacer so the layout shrinks tight to the buttons
            layout.AddRow(buttonStack); 

            Content = layout;
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            if (_isComplex) 
            {
                _lblProcessedCount.Text = "Options changed. Click Preview to update.";
                _reportPanel.Content = null;
                // Force window to resize tight when report panel is cleared
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
                        _chkPolylineOutput.Checked ?? false);

                    ResultCurves.Add(result.ResultCurve);
                }
            }

            // Calculate stats and evaluate HasChanges flag
            var inStats = CurveStats.Analyze(_inputCurves);
            var outStats = CurveStats.Analyze(ResultCurves);
            HasChanges = inStats.HasDifferences(outStats);

            _lblProcessedCount.Text = $"Processed {_inputCurves.Count} curve(s).";
            BuildReportTable(inStats, outStats);

            // Tell Eto to recalculate its dimensions based on the new table content
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

            void AddRow(string name, int init, int final, bool isIndent1 = false, bool isIndent2 = false)
            {
                if (init == 0 && final == 0) return;

                string prefix = isIndent2 ? "      " : (isIndent1 ? "   " : "");
                int delta = final - init;
                string deltaStr = delta > 0 ? $"+{delta}" : (delta < 0 ? $"{delta}" : " 0");
                
                table.Rows.Add(new TableRow(
                    new Label { Text = prefix + name, Font = regFont },
                    new Label { Text = init.ToString(), Font = regFont },
                    new Label { Text = final.ToString(), Font = regFont },
                    new Label { Text = deltaStr, Font = regFont }
                ));
            }

            AddRow("Lines", inStats.TopLines, outStats.TopLines);
            AddRow("Polylines", inStats.TopPolylines, outStats.TopPolylines);
            AddRow("Segments", inStats.TopPolylineSegments, outStats.TopPolylineSegments, true);
            AddRow("Arcs", inStats.TopArcs, outStats.TopArcs);
            AddRow("NURBS", inStats.TopNurbs, outStats.TopNurbs);
            
            // FIX: Match Rhino terminology
            AddRow("Polycurves", inStats.TopPolyCurves, outStats.TopPolyCurves); 
            AddRow("Segment count", inStats.PcSgTotal, outStats.PcSgTotal, true);
            AddRow("Lines", inStats.PcSgLines, outStats.PcSgLines, true);
            AddRow("Polylines", inStats.PcSgPolylines, outStats.PcSgPolylines, true);
            AddRow("Segments", inStats.PcSgPolylineSegments, outStats.PcSgPolylineSegments, false, true);
            AddRow("Arcs", inStats.PcSgArcs, outStats.PcSgArcs, true);
            AddRow("NURBS", inStats.PcSgNurbs, outStats.PcSgNurbs, true);

            _reportPanel.Content = table;
        }
    }

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
            else if (c is LineCurve) // FIX: Strict type checking only
            {
                if (isTopLevel) stats.TopLines++; else stats.PcSgLines++;
            }
            else if (c is ArcCurve) // FIX: Strict type checking only
            {
                if (isTopLevel) stats.TopArcs++; else stats.PcSgArcs++;
            }
            else
            {
                // Anything that hasn't been explicitly converted falls through to here
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