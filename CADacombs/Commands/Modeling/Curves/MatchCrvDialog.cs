using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CADacombs.Core;
using CADacombs.Core.Curves;
using CADacombs.Core.Reporting;

namespace CADacombs.Commands.Modeling.Curves
{
    public class MatchCrvDialog : CADacombsDialogBase
    {
        private Rhino.PersistentSettings _settings;
        private MatchCrvConduit _conduit;
        
        private Curve _crvA;
        private Curve _crvB;
        private bool _bT1WorkEndA;
        private bool _bT1WorkEndB;

        private RadioButtonList _rblContinuity;
        private RadioButtonList _rblPreserve;
        private RadioButtonList _rblModifyMode;
        
        private CheckBox _chkLimitDev;
        private TextBox _txtDevTol;
        
        private CheckBox _chkDeformLines;
        private CheckBox _chkDeformArcs;
        
        private CheckBox _chkIncrAsNeeded;
        private RadioButton _rbDeg;
        private RadioButton _rbSpan;
        
        private CheckBox _chkPreventInflections;
        
        private CheckBox _chkPreserveInternalCont;
        private RadioButton _rbIntG2;
        private RadioButton _rbIntG3;
        
        private CheckBox _chkSkipAtCont;
        private TextBox _txtPosTol;
        private TextBox _txtAngleTol;
        private TextBox _txtCurvTol;

        private CheckBox _chkDeleteInput;
        private CheckBox _chkDebug;

        private Button _btnOk;
        private Button _btnCancel;

        private bool _isUpdatingTextProgrammatically = false;
        private bool _previewPending = false;
        private UITimer _typingTimer;

        public NurbsCurve ResultCurveA { get; private set; }
        public NurbsCurve ResultCurveB { get; private set; }
        public string LastErrorMessage { get; private set; }

        public MatchCrvDialog(Rhino.PersistentSettings settings, Curve crvA, bool bT1WorkEndA, Curve crvB, bool bT1WorkEndB)
        {
            _settings = settings;
            _crvA = crvA;
            _crvB = crvB;
            _bT1WorkEndA = bT1WorkEndA;
            _bT1WorkEndB = bT1WorkEndB;

            _conduit = new MatchCrvConduit();
            _conduit.Enabled = true;

            _typingTimer = new UITimer { Interval = 1.0 };
            _typingTimer.Elapsed += OnTypingTimerElapsed;

            Title = "CADacombs Match Curve";
            Resizable = false;
            AutoSize = true;
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();

            this.Shown += (s, e) => UpdatePreview(true);
        }

        protected override Eto.Drawing.Point? LoadSavedLocation() => MatchCrvOptions.WindowLocation;
        protected override void SaveCurrentLocation(Eto.Drawing.Point location) => MatchCrvOptions.WindowLocation = location;

        private void CreateControls()
        {
            int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;

            _rblContinuity = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblContinuity.Items.Add(new ListItem { Text = "Position", Key = "0" });
            _rblContinuity.Items.Add(new ListItem { Text = "Tangency", Key = "1" });
            _rblContinuity.Items.Add(new ListItem { Text = "Curvature", Key = "2" });
            _rblContinuity.SelectedIndex = MatchCrvOptions.Continuity;
            _rblContinuity.SelectedIndexChanged += OnOptionChanged;

            _rblPreserve = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblPreserve.Items.Add(new ListItem { Text = "None", Key = "0" });
            _rblPreserve.Items.Add(new ListItem { Text = "Position", Key = "1" });
            _rblPreserve.Items.Add(new ListItem { Text = "Tangency", Key = "2" });
            _rblPreserve.Items.Add(new ListItem { Text = "Curvature", Key = "3" });
            _rblPreserve.SelectedIndex = MatchCrvOptions.PreserveOtherEnd;
            _rblPreserve.SelectedIndexChanged += OnOptionChanged;

            _rblModifyMode = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblModifyMode.Items.Add(new ListItem { Text = "First curve", Key = "0" });
            _rblModifyMode.Items.Add(new ListItem { Text = "Average curves", Key = "1" });
            _rblModifyMode.Items.Add(new ListItem { Text = "Minimize deviation\n(Either/Both)", Key = "2" });
            _rblModifyMode.SelectedIndex = MatchCrvOptions.ModifyMode;
            _rblModifyMode.SelectedIndexChanged += OnOptionChanged;

            _chkLimitDev = new CheckBox { Text = "Crv dev limit:" };
            _chkLimitDev.Checked = MatchCrvOptions.LimitCrvDev;
            _chkLimitDev.CheckedChanged += OnOptionChanged;
            _txtDevTol = new TextBox { Text = FormatUtils.FormatTolerance(MatchCrvOptions.DevTol, prec), Width = 60 };
            _txtDevTol.TextChanged += OnToleranceTextChanged;

            _chkSkipAtCont = new CheckBox { Text = "Skip if already continuous" };
            _chkSkipAtCont.Checked = MatchCrvOptions.SkipIfAlreadyAtContinuity;
            _chkSkipAtCont.CheckedChanged += OnOptionChanged;
            
            _txtPosTol = new TextBox { Text = FormatUtils.FormatTolerance(MatchCrvOptions.PosTol, prec), Width = 60 };
            _txtAngleTol = new TextBox { Text = FormatUtils.FormatTolerance(MatchCrvOptions.AngleTolDeg, prec), Width = 60 };
            _txtCurvTol = new TextBox { Text = FormatUtils.FormatTolerance(MatchCrvOptions.CurvatureMagTolPct, prec), Width = 60 };
            
            _txtPosTol.TextChanged += OnToleranceTextChanged;
            _txtAngleTol.TextChanged += OnToleranceTextChanged;
            _txtCurvTol.TextChanged += OnToleranceTextChanged;

            _chkDeformLines = new CheckBox { Text = "lines", Checked = MatchCrvOptions.DeformLines };
            _chkDeformLines.CheckedChanged += OnOptionChanged;
            _chkDeformArcs = new CheckBox { Text = "arcs", Checked = MatchCrvOptions.DeformArcs };
            _chkDeformArcs.CheckedChanged += OnOptionChanged;

            _chkIncrAsNeeded = new CheckBox { Text = "Incr as needed", Checked = MatchCrvOptions.IncrAsNeeded != 2 };
            _chkIncrAsNeeded.CheckedChanged += OnOptionChanged;

            _rbDeg = new RadioButton { Text = "Deg" };
            _rbSpan = new RadioButton(_rbDeg) { Text = "Span ct" };

            if (MatchCrvOptions.IncrAsNeeded == 1) _rbSpan.Checked = true;
            else _rbDeg.Checked = true;

            _rbDeg.CheckedChanged += OnOptionChanged;
            _rbSpan.CheckedChanged += OnOptionChanged;

            _chkPreventInflections = new CheckBox { Text = "Prevent extra inflection pts", Checked = MatchCrvOptions.PreventExtraInflections };
            _chkPreventInflections.CheckedChanged += OnOptionChanged;
            
            _chkPreserveInternalCont = new CheckBox { Text = "Preserve internal continuity", Checked = MatchCrvOptions.PreserveInternalContinuity };
            _chkPreserveInternalCont.CheckedChanged += OnOptionChanged;

            _rbIntG2 = new RadioButton { Text = "G2" };
            _rbIntG3 = new RadioButton(_rbIntG2) { Text = "G3" };
            
            if (MatchCrvOptions.MaxInternalContinuity == 3) _rbIntG3.Checked = true;
            else _rbIntG2.Checked = true;
            
            _rbIntG2.CheckedChanged += OnOptionChanged;
            _rbIntG3.CheckedChanged += OnOptionChanged;

            _chkDeleteInput = new CheckBox { Text = "Delete input", Checked = MatchCrvOptions.DeleteInput };
            _chkDebug = new CheckBox { Text = "Debug", Checked = MatchCrvOptions.Debug };
            _chkDebug.CheckedChanged += OnOptionChanged;

            _btnOk = new Button { Text = "OK", Width = 75 };
            _btnOk.Click += OnOkClicked;
            
            _btnCancel = new Button { Text = "Cancel", Width = 75 };
            _btnCancel.Click += (s, e) => { Result = false; Close(); };

            DefaultButton = _btnOk;
            AbortButton = _btnCancel;

            UpdateControlStates();
        }

        private void SetupLayout()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5) };

            layout.AddRow(new Label { Text = "Continuity" });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblContinuity });
            
            layout.AddRow(new Label { Text = "Preserve other end" });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblPreserve });
            
            layout.AddRow(new Label { Text = "Modify curve" });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblModifyMode });

            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });

            layout.AddRow(_chkSkipAtCont);
            layout.AddRow(new Panel { Padding = new Padding(24, 0, 0, 0), Content = new Label { Text = "within these tolerances:" } });

            var skipGrid = new TableLayout { Spacing = new Size(5, 4) };
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Position:", VerticalAlignment = VerticalAlignment.Center }, _txtPosTol, null));
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Angle (°):", VerticalAlignment = VerticalAlignment.Center }, _txtAngleTol, null));
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Rel crv (%):", VerticalAlignment = VerticalAlignment.Center }, _txtCurvTol, null));
            layout.AddRow(new Panel { Padding = new Padding(24, 0, 0, 0), Content = skipGrid });
            
            var devStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center, Items = { _chkLimitDev, _txtDevTol } };
            layout.AddRow(devStack);
            
            var deformStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, VerticalContentAlignment = VerticalAlignment.Center, Items = { new Label { Text = "Deform" }, _chkDeformLines, _chkDeformArcs } };
            layout.AddRow(deformStack);
            
            layout.AddRow(_chkIncrAsNeeded);
            var incrStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { _rbDeg, _rbSpan } };
            layout.AddRow(new Panel { Padding = new Padding(24, 0, 0, 0), Content = incrStack });
            
            layout.AddRow(_chkPreventInflections);
            layout.AddRow(_chkPreserveInternalCont);
            
            var maxIntStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, VerticalContentAlignment = VerticalAlignment.Center, Items = { new Label { Text = "up through" }, _rbIntG2, _rbIntG3 } };
            layout.AddRow(new Panel { Padding = new Padding(24, 0, 0, 0), Content = maxIntStack });
            
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            
            var bottomOpts = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 16, Items = { _chkDeleteInput, _chkDebug } };
            layout.AddRow(bottomOpts);

            var btnStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Center, Items = { _btnOk, _btnCancel } };
            layout.AddRow(new Panel { Height = 4 });
            layout.AddRow(btnStack);

            Content = layout;
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            UpdateControlStates();
            UpdatePreview(true);
        }

        private void OnToleranceTextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingTextProgrammatically) return;
            _previewPending = true;
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

                bool ProcessBox(TextBox tb)
                {
                    if (!double.TryParse(tb.Text, out _)) 
                    {
                        tb.BackgroundColor = Colors.LightPink;
                        return false;
                    }
                    tb.BackgroundColor = Colors.White;
                    return true;
                }

                allValid &= ProcessBox(_txtDevTol);
                allValid &= ProcessBox(_txtPosTol);
                allValid &= ProcessBox(_txtAngleTol);
                allValid &= ProcessBox(_txtCurvTol);

                _isUpdatingTextProgrammatically = false;
                if (allValid) UpdatePreview(true);
            });
        }

        private void UpdateControlStates()
        {
            _txtDevTol.Enabled = _chkLimitDev.Checked ?? false;
            
            bool skipChecked = _chkSkipAtCont.Checked ?? false;
            _txtPosTol.Enabled = skipChecked;
            _txtAngleTol.Enabled = skipChecked && _rblContinuity.SelectedIndex >= 1; 
            _txtCurvTol.Enabled = skipChecked && _rblContinuity.SelectedIndex == 2; 

            bool incrChecked = _chkIncrAsNeeded.Checked ?? false;
            _rbDeg.Enabled = incrChecked;
            _rbSpan.Enabled = incrChecked;
            
            bool preserveChecked = _chkPreserveInternalCont.Checked ?? false;
            _rbIntG2.Enabled = preserveChecked;
            _rbIntG3.Enabled = preserveChecked;
        }

        private void UpdatePreview(bool printReport = false)
        {
            if (_previewPending && !ValidateTolerances())
            {
                _btnOk.Enabled = false;
                return;
            }

            int cont = _rblContinuity.SelectedIndex;
            int preserve = _rblPreserve.SelectedIndex;
            int modify = _rblModifyMode.SelectedIndex;
            
            bool limitDev = _chkLimitDev.Checked ?? false;
            double devTol = limitDev ? double.Parse(_txtDevTol.Text) : double.MaxValue;
            
            double posTol = double.Parse(_txtPosTol.Text);
            double angleTol = double.Parse(_txtAngleTol.Text);
            double curvTol = double.Parse(_txtCurvTol.Text);
            
            bool deformLines = _chkDeformLines.Checked ?? false;
            bool deformArcs = _chkDeformArcs.Checked ?? false;
            int incr = (_chkIncrAsNeeded.Checked ?? false) ? (_rbDeg.Checked ? 0 : 1) : 2;
            
            bool preventExtraInf = _chkPreventInflections.Checked ?? false;
            bool preserveInternal = _chkPreserveInternalCont.Checked ?? false;
            int maxInternal = _rbIntG3.Checked ? 3 : 2;
            bool debug = _chkDebug.Checked ?? false;

            ResultCurveA?.Dispose(); ResultCurveA = null;
            ResultCurveB?.Dispose(); ResultCurveB = null;

            double maxDev = 0.0;
            double avgDev = 0.0;
            LastErrorMessage = null;

            if (modify == 0) // First Curve
            {
                var res = CurveMatchMath.MatchOneCurve(_crvA, _crvB, _bT1WorkEndA, _bT1WorkEndB, cont, preserve, deformLines, deformArcs, incr, devTol, preventExtraInf, preserveInternal, maxInternal, posTol, angleTol, curvTol, debug);
                ResultCurveA = res.Result;
                LastErrorMessage = res.ErrorMsg;

                if (ResultCurveA != null)
                {
                    maxDev = CurveMatchMath.GetMaximumDeviation(_crvA, ResultCurveA);
                    avgDev = CurveMatchMath.GetAverageDeviation(_crvA, ResultCurveA);
                }
            }
            else if (modify == 1) // Average
            {
                var bothResult = CurveMatchMath.MatchBothCurves(_crvA, _crvB, _bT1WorkEndA, _bT1WorkEndB, cont, preserve, deformLines, deformArcs, incr, devTol, preventExtraInf, preserveInternal, maxInternal, posTol, angleTol, curvTol, debug);
                ResultCurveA = bothResult.ResultA;
                ResultCurveB = bothResult.ResultB;
                LastErrorMessage = bothResult.ErrorMsg;

                if (ResultCurveA != null && ResultCurveB != null)
                {
                    maxDev = Math.Max(CurveMatchMath.GetMaximumDeviation(_crvA, ResultCurveA), CurveMatchMath.GetMaximumDeviation(_crvB, ResultCurveB));
                    avgDev = (CurveMatchMath.GetAverageDeviation(_crvA, ResultCurveA) + CurveMatchMath.GetAverageDeviation(_crvB, ResultCurveB)) / 2.0;
                }
            }
            else if (modify == 2) // Minimize Deviation
            {
                var resFirst = CurveMatchMath.MatchOneCurve(_crvA, _crvB, _bT1WorkEndA, _bT1WorkEndB, cont, preserve, deformLines, deformArcs, incr, devTol, preventExtraInf, preserveInternal, maxInternal, posTol, angleTol, curvTol, debug);
                var resSecond = CurveMatchMath.MatchOneCurve(_crvB, _crvA, _bT1WorkEndB, _bT1WorkEndA, cont, preserve, deformLines, deformArcs, incr, devTol, preventExtraInf, preserveInternal, maxInternal, posTol, angleTol, curvTol, debug);
                var bothResult = CurveMatchMath.MatchBothCurves(_crvA, _crvB, _bT1WorkEndA, _bT1WorkEndB, cont, preserve, deformLines, deformArcs, incr, devTol, preventExtraInf, preserveInternal, maxInternal, posTol, angleTol, curvTol, debug);

                NurbsCurve candA = resFirst.Result;
                NurbsCurve candB = resSecond.Result;

                double devA = candA != null ? CurveMatchMath.GetAverageDeviation(_crvA, candA) : double.MaxValue;
                double devB = candB != null ? CurveMatchMath.GetAverageDeviation(_crvB, candB) : double.MaxValue;
                double devBoth = (bothResult.ResultA != null && bothResult.ResultB != null) 
                    ? (CurveMatchMath.GetAverageDeviation(_crvA, bothResult.ResultA) + CurveMatchMath.GetAverageDeviation(_crvB, bothResult.ResultB)) / 2.0 
                    : double.MaxValue;

                double minDev = Math.Min(devA, Math.Min(devB, devBoth));

                if (minDev == double.MaxValue) 
                { 
                    ResultCurveA = null; 
                    ResultCurveB = null; 
                    LastErrorMessage = bothResult.ErrorMsg ?? resFirst.ErrorMsg ?? resSecond.ErrorMsg ?? "Result cannot be created with the current constraints.";
                }
                else if (minDev == devA)
                {
                    ResultCurveA = candA;
                    candB?.Dispose(); bothResult.ResultA?.Dispose(); bothResult.ResultB?.Dispose();
                }
                else if (minDev == devB)
                {
                    ResultCurveB = candB;
                    candA?.Dispose(); bothResult.ResultA?.Dispose(); bothResult.ResultB?.Dispose();
                }
                else
                {
                    ResultCurveA = bothResult.ResultA; ResultCurveB = bothResult.ResultB;
                    candA?.Dispose(); candB?.Dispose();
                }

                if (ResultCurveA != null && ResultCurveB == null)
                {
                    maxDev = CurveMatchMath.GetMaximumDeviation(_crvA, ResultCurveA);
                    avgDev = CurveMatchMath.GetAverageDeviation(_crvA, ResultCurveA);
                }
                else if (ResultCurveB != null && ResultCurveA == null)
                {
                    maxDev = CurveMatchMath.GetMaximumDeviation(_crvB, ResultCurveB);
                    avgDev = CurveMatchMath.GetAverageDeviation(_crvB, ResultCurveB);
                }
                else if (ResultCurveA != null && ResultCurveB != null)
                {
                    maxDev = Math.Max(CurveMatchMath.GetMaximumDeviation(_crvA, ResultCurveA), CurveMatchMath.GetMaximumDeviation(_crvB, ResultCurveB));
                    avgDev = (CurveMatchMath.GetAverageDeviation(_crvA, ResultCurveA) + CurveMatchMath.GetAverageDeviation(_crvB, ResultCurveB)) / 2.0;
                }
            }

            bool success = (ResultCurveA != null || ResultCurveB != null);
            _btnOk.Enabled = success;

            if (printReport)
            {
                if (!success)
                {
                    RhinoApp.WriteLine(LastErrorMessage ?? "Result cannot be created with the current constraints.");
                }
                else
                {
                    int prec = RhinoDoc.ActiveDoc.ModelDistanceDisplayPrecision;
                    
                    double? radPreA = RadiusMinimaMath.GetMinimumRadius(_crvA);
                    double? radPostA = ResultCurveA != null ? RadiusMinimaMath.GetMinimumRadius(ResultCurveA) : radPreA;
                    
                    double? radPreB = RadiusMinimaMath.GetMinimumRadius(_crvB);
                    double? radPostB = ResultCurveB != null ? RadiusMinimaMath.GetMinimumRadius(ResultCurveB) : radPreB;

                    double? minRadPre = (radPreA.HasValue && radPreB.HasValue) ? Math.Min(radPreA.Value, radPreB.Value) : (radPreA ?? radPreB);
                    double? minRadPost = (radPostA.HasValue && radPostB.HasValue) ? Math.Min(radPostA.Value, radPostB.Value) : (radPostA ?? radPostB);

                    string sRadPre = minRadPre.HasValue ? FormatUtils.FormatDistance(minRadPre.Value, prec) : "(None)";
                    string sRadPost = minRadPost.HasValue ? FormatUtils.FormatDistance(minRadPost.Value, prec) : "(None)";

                    RhinoApp.WriteLine($"Min Radius: {sRadPre} -> {sRadPost}  |  Max Dev: {FormatUtils.FormatDistance(maxDev, prec)}  |  Avg Dev: {FormatUtils.FormatDistance(avgDev, prec)}");
                }
            }

            _conduit.ResultA = ResultCurveA;
            _conduit.ResultB = ResultCurveB;
            RhinoDoc.ActiveDoc.Views.Redraw();
            _previewPending = false;
        }

        private bool ValidateTolerances()
        {
            return double.TryParse(_txtDevTol.Text, out _) &&
                   double.TryParse(_txtPosTol.Text, out _) &&
                   double.TryParse(_txtAngleTol.Text, out _) &&
                   double.TryParse(_txtCurvTol.Text, out _);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_typingTimer != null)
            {
                _typingTimer.Stop();
                _typingTimer.Dispose();
            }
            _conduit.Enabled = false;
            RhinoDoc.ActiveDoc.Views.Redraw();
            base.OnClosed(e);
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            if (!ValidateTolerances()) return;

            MatchCrvOptions.Continuity = _rblContinuity.SelectedIndex;
            MatchCrvOptions.PreserveOtherEnd = _rblPreserve.SelectedIndex;
            MatchCrvOptions.ModifyMode = _rblModifyMode.SelectedIndex;
            
            MatchCrvOptions.LimitCrvDev = _chkLimitDev.Checked ?? false;
            if (MatchCrvOptions.LimitCrvDev) MatchCrvOptions.DevTol = double.Parse(_txtDevTol.Text);

            MatchCrvOptions.SkipIfAlreadyAtContinuity = _chkSkipAtCont.Checked ?? false;
            if (MatchCrvOptions.SkipIfAlreadyAtContinuity)
            {
                MatchCrvOptions.PosTol = double.Parse(_txtPosTol.Text);
                MatchCrvOptions.AngleTolDeg = double.Parse(_txtAngleTol.Text);
                MatchCrvOptions.CurvatureMagTolPct = double.Parse(_txtCurvTol.Text);
            }

            MatchCrvOptions.DeformLines = _chkDeformLines.Checked ?? false;
            MatchCrvOptions.DeformArcs = _chkDeformArcs.Checked ?? false;
            MatchCrvOptions.IncrAsNeeded = (_chkIncrAsNeeded.Checked ?? false) ? (_rbDeg.Checked ? 0 : 1) : 2;
            
            MatchCrvOptions.PreventExtraInflections = _chkPreventInflections.Checked ?? false;
            MatchCrvOptions.PreserveInternalContinuity = _chkPreserveInternalCont.Checked ?? false;
            MatchCrvOptions.MaxInternalContinuity = _rbIntG3.Checked ? 3 : 2;

            MatchCrvOptions.DeleteInput = _chkDeleteInput.Checked ?? false;
            MatchCrvOptions.Debug = _chkDebug.Checked ?? false;

            MatchCrvOptions.Save(_settings);
            
            if (_previewPending) UpdatePreview(true);

            Result = true; Close();
        }
    }
}