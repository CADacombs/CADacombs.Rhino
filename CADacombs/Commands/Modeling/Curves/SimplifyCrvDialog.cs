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
        
        private CheckBox _chkNativeSimplify;
        private CheckBox _chkSpansToLines;
        private CheckBox _chkSpansToArcs;
        private Label _lblReport;
        
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnPreview;

        private bool _isComplex;
        public List<Curve> ResultCurves { get; private set; }

        public SimplifyCrvDialog(List<Curve> inputCurves, SimplifyCrvConduit conduit)
        {
            _inputCurves = inputCurves;
            _conduit = conduit;
            
            int totalSpans = 0;
            foreach (var crv in _inputCurves) totalSpans += crv.SpanCount;
            _isComplex = totalSpans > 500;

            Title = "Simplify Curve";
            Resizable = false;
            
            // FIX: Set explicit MinimumSize to prevent Eto WPF auto-squishing controls
            MinimumSize = new Size(340, 240);
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();
            
            if (!_isComplex) UpdatePreview();
        }

        private void CreateControls()
        {
            _chkNativeSimplify = new CheckBox { Text = "Native Simplify (Merge & Remove Kinks)", Checked = true };
            _chkSpansToLines = new CheckBox { Text = "Convert Spans to Lines", Checked = true };
            _chkSpansToArcs = new CheckBox { Text = "Convert Spans to Arcs", Checked = true };

            _chkNativeSimplify.CheckedChanged += OnOptionChanged;
            _chkSpansToLines.CheckedChanged += OnOptionChanged;
            _chkSpansToArcs.CheckedChanged += OnOptionChanged;

            _lblReport = new Label 
            { 
                Text = "Waiting for preview...", 
                Height = 45,
                Font = new Font(SystemFont.Default, 9)
            };

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
            var layout = new DynamicLayout { Spacing = new Size(6, 8) };
            
            layout.AddRow(_chkNativeSimplify);
            layout.AddRow(_chkSpansToLines);
            layout.AddRow(_chkSpansToArcs);
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(_lblReport);
            layout.AddRow(null); // Flexible vertical spacer
            
            layout.BeginHorizontal();
            layout.Add(null, true); // Push buttons to right
            layout.Add(_btnPreview);
            layout.Add(_btnOk);
            layout.Add(_btnCancel);
            layout.EndHorizontal();

            Content = layout;
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            if (_isComplex) _lblReport.Text = "Options changed. Click Preview to update.";
            else UpdatePreview();
        }

        private void UpdatePreview()
        {
            double devTol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            ResultCurves = new List<Curve>();
            
            int processed = 0;
            int totalReducedSegments = 0;

            foreach (var inputCurve in _inputCurves)
            {
                int originalSegs = (inputCurve is PolyCurve pcOriginal) ? pcOriginal.SegmentCount : 1;
                
                var result = SimplifyCrvLogic.ExecutePipeline(
                    inputCurve, 
                    devTol, 
                    100.0 * devTol,
                    _chkNativeSimplify.Checked ?? false,
                    _chkSpansToLines.Checked ?? false, 
                    _chkSpansToArcs.Checked ?? false);

                int newSegs = (result.ResultCurve is PolyCurve pcNew) ? pcNew.SegmentCount : 1;
                totalReducedSegments += (originalSegs - newSegs);

                ResultCurves.Add(result.ResultCurve);
                processed++;
            }

            _lblReport.Text = $"Processed {processed} curve(s).\nTotal segment delta: {totalReducedSegments}";

            _conduit.PreviewCurves = ResultCurves;
            Rhino.RhinoDoc.ActiveDoc.Views.Redraw();
        }
    }
}