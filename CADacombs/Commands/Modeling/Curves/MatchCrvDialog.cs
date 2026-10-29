using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using CADacombs.Core;
using CADacombs.Core.Curves;

namespace CADacombs.Commands.Modeling.Curves
{
    public class MatchCrvDialog : CADacombsDialogBase
    {
        private Rhino.PersistentSettings _settings;

        private RadioButtonList _rblContinuity;
        private RadioButtonList _rblPreserve;
        private RadioButtonList _rblModifyMode;
        
        private CheckBox _chkLimitDev;
        private TextBox _txtDevTol;
        
        private CheckBox _chkDeformLines;
        
        private CheckBox _chkSkipAtCont;
        private TextBox _txtPosTol;
        private TextBox _txtAngleTol;
        private TextBox _txtCurvTol;

        private CheckBox _chkRebuildRationals;
        private CheckBox _chkKeepArcs;

        private CheckBox _chkDeleteInput;
        private CheckBox _chkEcho;
        private CheckBox _chkDebug;

        private Button _btnOk;
        private Button _btnCancel;

        public MatchCrvDialog(Rhino.PersistentSettings settings)
        {
            _settings = settings;

            Title = "Match Curve";
            Resizable = false;
            AutoSize = true;
            Padding = new Padding(12);

            CreateControls();
            SetupLayout();
        }

        protected override Point? LoadSavedLocation() => MatchCrvOptions.WindowLocation;
        protected override void SaveCurrentLocation(Point location) => MatchCrvOptions.WindowLocation = location;

        private void CreateControls()
        {
            _rblContinuity = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblContinuity.Items.Add(new ListItem { Text = "Position", Key = "0" });
            _rblContinuity.Items.Add(new ListItem { Text = "Tangency", Key = "1" });
            _rblContinuity.Items.Add(new ListItem { Text = "Curvature", Key = "2" });
            _rblContinuity.SelectedIndex = MatchCrvOptions.Continuity;
            _rblContinuity.SelectedIndexChanged += (s, e) => UpdateControlStates();

            _rblPreserve = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblPreserve.Items.Add(new ListItem { Text = "None", Key = "0" });
            _rblPreserve.Items.Add(new ListItem { Text = "Position", Key = "1" });
            _rblPreserve.Items.Add(new ListItem { Text = "Tangency", Key = "2" });
            _rblPreserve.Items.Add(new ListItem { Text = "Curvature", Key = "3" });
            _rblPreserve.Items.Add(new ListItem { Text = "Periodic", Key = "4" });
            _rblPreserve.SelectedIndex = MatchCrvOptions.PreserveOtherEnd;

            _rblModifyMode = new RadioButtonList { Orientation = Orientation.Vertical, Spacing = new Size(0, 4) };
            _rblModifyMode.Items.Add(new ListItem { Text = "First curve", Key = "0" });
            _rblModifyMode.Items.Add(new ListItem { Text = "Average curves", Key = "1" });
            _rblModifyMode.Items.Add(new ListItem { Text = "Minimize deviation\n(Either/Both)", Key = "2" });
            _rblModifyMode.SelectedIndex = MatchCrvOptions.ModifyMode;

            _chkLimitDev = new CheckBox { Text = "Limit deviation:" };
            _chkLimitDev.Checked = MatchCrvOptions.LimitCrvDev;
            _chkLimitDev.CheckedChanged += (s, e) => UpdateControlStates();
            _txtDevTol = new TextBox { Text = MatchCrvOptions.DevTol.ToString("G"), Width = 60 };

            _chkSkipAtCont = new CheckBox { Text = "Skip if already continuous" };
            _chkSkipAtCont.Checked = MatchCrvOptions.SkipIfAlreadyAtContinuity;
            _chkSkipAtCont.CheckedChanged += (s, e) => UpdateControlStates();
            
            _txtPosTol = new TextBox { Text = MatchCrvOptions.PosTol.ToString("G"), Width = 60 };
            _txtAngleTol = new TextBox { Text = MatchCrvOptions.AngleTolDeg.ToString("G"), Width = 60 };
            _txtCurvTol = new TextBox { Text = MatchCrvOptions.CurvatureMagTolPct.ToString("G"), Width = 60 };

            _chkDeformLines = new CheckBox { Text = "Deform lines", Checked = MatchCrvOptions.DeformLines };
            
            _chkRebuildRationals = new CheckBox { Text = "Rebuild rationals", Checked = MatchCrvOptions.RebuildRationals };
            _chkRebuildRationals.CheckedChanged += (s, e) => UpdateControlStates();
            _chkKeepArcs = new CheckBox { Text = "Keep arcs", Checked = MatchCrvOptions.KeepArcs };

            _chkDeleteInput = new CheckBox { Text = "Delete input", Checked = MatchCrvOptions.DeleteInput };
            _chkEcho = new CheckBox { Text = "Echo", Checked = MatchCrvOptions.Echo };
            _chkDebug = new CheckBox { Text = "Debug", Checked = MatchCrvOptions.Debug };

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
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 8) };

            layout.AddRow(new Label { Text = "Continuity", TextColor = Colors.DimGray });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblContinuity });
            layout.AddRow(new Panel { Height = 4 }); 
            
            layout.AddRow(new Label { Text = "Preserve other end", TextColor = Colors.DimGray });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblPreserve });
            layout.AddRow(new Panel { Height = 4 }); 
            
            layout.AddRow(new Label { Text = "Modify curve", TextColor = Colors.DimGray });
            layout.AddRow(new Panel { Padding = new Padding(12, 0, 0, 0), Content = _rblModifyMode });

            layout.AddRow(new Panel { Height = 8 });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(new Panel { Height = 4 });

            // Skip block layout
            layout.AddRow(_chkSkipAtCont);
            var skipGrid = new TableLayout { Spacing = new Size(5, 6) };
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Pos tol:", VerticalAlignment = VerticalAlignment.Center }, _txtPosTol, null));
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Angle tol (°):", VerticalAlignment = VerticalAlignment.Center }, _txtAngleTol, null));
            skipGrid.Rows.Add(new TableRow(new Label { Text = "Rel mag diff tol (%):", VerticalAlignment = VerticalAlignment.Center }, _txtCurvTol, null));
            layout.AddRow(new Panel { Padding = new Padding(24, 0, 0, 0), Content = skipGrid });
            
            var devStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center, Items = { _chkLimitDev, _txtDevTol } };
            layout.AddRow(devStack);
            
            layout.AddRow(_chkDeformLines);
            
            var rationalsStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 16, Items = { _chkRebuildRationals, _chkKeepArcs } };
            layout.AddRow(rationalsStack);
            
            layout.AddRow(new Panel { Height = 8 });
            layout.AddRow(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            layout.AddRow(new Panel { Height = 4 });

            var bottomOpts = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 16, Items = { _chkDeleteInput, _chkEcho, _chkDebug } };
            layout.AddRow(bottomOpts);
            layout.AddRow(new Panel { Height = 8 });

            var btnStack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Center, Items = { _btnOk, _btnCancel } };
            layout.AddRow(btnStack);

            Content = layout;
        }

        private void UpdateControlStates()
        {
            _txtDevTol.Enabled = _chkLimitDev.Checked ?? false;
            
            bool skipChecked = _chkSkipAtCont.Checked ?? false;
            _txtPosTol.Enabled = skipChecked;
            _txtAngleTol.Enabled = skipChecked && _rblContinuity.SelectedIndex >= 1; // Only Tan or Curvature
            _txtCurvTol.Enabled = skipChecked && _rblContinuity.SelectedIndex == 2; // Only Curvature
            
            _chkKeepArcs.Enabled = _chkRebuildRationals.Checked ?? false;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            if (_chkLimitDev.Checked == true && !double.TryParse(_txtDevTol.Text, out _))
            {
                _txtDevTol.BackgroundColor = Colors.LightPink; return;
            }
            if (_chkSkipAtCont.Checked == true)
            {
                if (!double.TryParse(_txtPosTol.Text, out _)) { _txtPosTol.BackgroundColor = Colors.LightPink; return; }
                if (!double.TryParse(_txtAngleTol.Text, out _)) { _txtAngleTol.BackgroundColor = Colors.LightPink; return; }
                if (!double.TryParse(_txtCurvTol.Text, out _)) { _txtCurvTol.BackgroundColor = Colors.LightPink; return; }
            }

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
            MatchCrvOptions.RebuildRationals = _chkRebuildRationals.Checked ?? false;
            MatchCrvOptions.KeepArcs = _chkKeepArcs.Checked ?? false;

            MatchCrvOptions.DeleteInput = _chkDeleteInput.Checked ?? false;
            MatchCrvOptions.Echo = _chkEcho.Checked ?? false;
            MatchCrvOptions.Debug = _chkDebug.Checked ?? false;

            MatchCrvOptions.Save(_settings);
            Result = true; Close();
        }
    }
}