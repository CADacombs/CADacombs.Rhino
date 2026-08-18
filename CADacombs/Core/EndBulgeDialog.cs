using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace CADacombs.Core
{
    /// <summary>
    /// The base Eto Dialog for the EndBulge tool suite. 
    /// Handles the complex UI layout, homemade steppers, and state synchronization.
    /// </summary>
    public class EndBulgeDialog : Dialog<bool>
    {
        // ----------------------------------------------------
        // Control Dictionaries
        // ----------------------------------------------------
        protected Dictionary<string, Label> labels = new Dictionary<string, Label>();
        protected Dictionary<string, CheckBox> checkBoxes = new Dictionary<string, CheckBox>();
        protected Dictionary<string, RadioButtonList> radioButtonLists = new Dictionary<string, RadioButtonList>();
        protected Dictionary<string, NumericStepper> numericSteppers = new Dictionary<string, NumericStepper>();
        protected Dictionary<string, TextBox> textBoxes = new Dictionary<string, TextBox>();
        protected Dictionary<string, DropDown> dropDowns = new Dictionary<string, DropDown>();
        protected Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
        protected Dictionary<string, Button> btnUp = new Dictionary<string, Button>();
        protected Dictionary<string, Button> btnDown = new Dictionary<string, Button>();

        // ----------------------------------------------------
        // State Variables
        // ----------------------------------------------------
        public bool DialogOk { get; protected set; } = false;
        protected bool isSurface;
        
        protected bool _autoUpdating = false;
        protected bool _autoUpdatingSlider = false;
        
        protected double _exactScalePicked;
        protected double _exactScaleOpp;
        
        protected UITimer holdTimer;
        protected UITimer debounceTimer;
        protected int holdDirection = 0;
        protected string activeStepperKey = null;
        protected Dictionary<string, int> sliderPrevVals = new Dictionary<string, int>();

        protected int _lastClickedCont = 0;

        public EndBulgeConduit BaseConduit { get; set; }

        public EndBulgeDialog(bool isSurface = false)
        {
            this.isSurface = isSurface;
            Title = "EndBulge by SPB";
            
            _exactScalePicked = EndBulgeOptions.ScalePicked;
            _exactScaleOpp = EndBulgeOptions.ScaleOpp;

            CreateControls();
            SetupLayout();
            
            OnLinkedModeChanged(null, null);

            debounceTimer = new UITimer { Interval = 0.2 };
            debounceTimer.Elapsed += OnDebounceTimerElapsed;

            holdTimer = new UITimer { Interval = 0.15 };
            holdTimer.Elapsed += OnHoldTimerElapsed;

            Closed += OnFormClosed;
        }

        // ----------------------------------------------------
        // Virtual Methods (To be overridden by Crv/Srf)
        // ----------------------------------------------------
        public virtual void UpdatePreview() { }
        protected virtual void UpdateControlStates() { }

        // ----------------------------------------------------
        // UI Creation
        // ----------------------------------------------------
        protected virtual void CreateControls()
        {
            string termLow = isSurface ? "edge" : "end";
            Font smallFont = new Font(SystemFont.Default, 4);

            string[] contList = { "None", "G0", "G1", "G2", "G3" };
            
            // Radio button Spacing kept at 8 to maintain visual grouping without blowing out the width
            radioButtonLists["idxCont_Picked"] = new RadioButtonList { Spacing = new Size(8, 4) };
            radioButtonLists["idxCont_Picked"].DataStore = contList;
            radioButtonLists["idxCont_Picked"].SelectedIndex = EndBulgeOptions.ContinuityPicked;
            radioButtonLists["idxCont_Picked"].SelectedIndexChanged += OnContinuityChanged;
            labels["idxCont_Picked"] = new Label { Text = $"Picked {termLow}:" };

            radioButtonLists["idxCont_Opp"] = new RadioButtonList { Spacing = new Size(8, 4) };
            radioButtonLists["idxCont_Opp"].DataStore = contList;
            radioButtonLists["idxCont_Opp"].SelectedIndex = EndBulgeOptions.ContinuityOpp;
            radioButtonLists["idxCont_Opp"].SelectedIndexChanged += OnContinuityChanged;
            labels["idxCont_Opp"] = new Label { Text = $"Opp. {termLow}:" };

            radioButtonLists["bLinkedEnds"] = new RadioButtonList { Orientation = Orientation.Horizontal, Spacing = new Size(16, 4) };
            radioButtonLists["bLinkedEnds"].DataStore = new[] { "Independent", "Linked" };
            radioButtonLists["bLinkedEnds"].SelectedIndex = EndBulgeOptions.LinkedEnds ? 1 : 0;
            radioButtonLists["bLinkedEnds"].SelectedIndexChanged += OnLinkedModeChanged;
            labels["bLinkedEnds"] = new Label { Text = $"Adjust {termLow}s:" };

            labels["fIncrement"] = new Label { Text = "Incr.:" };
            textBoxes["fIncrement"] = new TextBox { Text = EndBulgeOptions.Increment.ToString() };
            textBoxes["fIncrement"].TextChanged += OnIncrementTextChanged;

            labels["iSliderSteps"] = new Label { Text = "Slider steps:" };
            dropDowns["iSliderSteps"] = new DropDown();
            dropDowns["iSliderSteps"].DataStore = new[] { "5", "10", "20", "50", "100", "1000" };
            dropDowns["iSliderSteps"].SelectedIndex = EndBulgeOptions.SliderStepsIndex;
            dropDowns["iSliderSteps"].SelectedIndexChanged += OnSliderStepsChanged;

            void CreateHomemadeStepper(string sKey, string labelText, bool isScale, double initVal)
            {
                labels[sKey] = new Label { Text = labelText };
                textBoxes[sKey] = new TextBox { Text = initVal.ToString("F4") };
                
                if (isScale) textBoxes[sKey].TextChanged += OnScaleTextChanged;
                else textBoxes[sKey].TextChanged += OnSlideTextChanged;

                sliderPrevVals[sKey] = 0;
                // REMOVED 'Width = 132' so the sliders can scale freely to match the top row
                sliders[sKey] = new Slider { SnapToTick = true, TickFrequency = 1 };
                sliders[sKey].ValueChanged += (s, e) => OnJogSliderChanged(sKey);
                sliders[sKey].MouseUp += (s, e) => ZeroSlider(sKey);

                btnUp[sKey] = new Button { Text = "▲", Width = 16, Height = 12, Font = smallFont, MinimumSize = new Size(16, 12) };
                btnDown[sKey] = new Button { Text = "▼", Width = 16, Height = 12, Font = smallFont, MinimumSize = new Size(16, 12) };

                btnUp[sKey].MouseDown += (s, e) => StartHoldTimer(1, sKey);
                btnUp[sKey].MouseUp += StopHoldTimer;
                btnUp[sKey].MouseLeave += StopHoldTimer;

                btnDown[sKey].MouseDown += (s, e) => StartHoldTimer(-1, sKey);
                btnDown[sKey].MouseUp += StopHoldTimer;
                btnDown[sKey].MouseLeave += StopHoldTimer;
            }

            CreateHomemadeStepper("fScale_Picked", "Scale:", true, EndBulgeOptions.ScalePicked);
            CreateHomemadeStepper("fSlideG2_Picked", "G2 slide:", false, EndBulgeOptions.SlideG2Picked);
            CreateHomemadeStepper("fSlideG3_Picked", "G3 slide:", false, EndBulgeOptions.SlideG3Picked);
            
            CreateHomemadeStepper("fScale_Opp", "Scale:", true, EndBulgeOptions.ScaleOpp);
            CreateHomemadeStepper("fSlideG2_Opp", "G2 slide:", false, EndBulgeOptions.SlideG2Opp);
            CreateHomemadeStepper("fSlideG3_Opp", "G3 slide:", false, EndBulgeOptions.SlideG3Opp);

            UpdateSliderRanges();

            checkBoxes["bShowGeom"] = new CheckBox { Text = isSurface ? "Surface" : "Curve", Checked = EndBulgeOptions.ShowGeom };
            checkBoxes["bShowGeom"].CheckedChanged += OnDisplayCheckedChanged;

            checkBoxes["bShowPolygon"] = new CheckBox { Text = "Control polygon", Checked = EndBulgeOptions.ShowPolygon };
            checkBoxes["bShowPolygon"].CheckedChanged += OnDisplayCheckedChanged;

            checkBoxes["bShowGraph"] = new CheckBox { Text = "CGraph", Checked = EndBulgeOptions.ShowGraph };
            checkBoxes["bShowGraph"].CheckedChanged += OnDisplayCheckedChanged;

            labels["iGraphScale"] = new Label { Text = "Scale:" };
            numericSteppers["iGraphScale"] = new NumericStepper { DecimalPlaces = 0, MinValue = 1, MaxValue = 10000, Value = EndBulgeOptions.GraphScale };
            numericSteppers["iGraphScale"].ValueChanged += OnDisplayCheckedChanged;

            labels["iGraphDensity"] = new Label { Text = "Density:" };
            numericSteppers["iGraphDensity"] = new NumericStepper { DecimalPlaces = 0, MinValue = 0, MaxValue = 100, Value = EndBulgeOptions.GraphDensity };
            numericSteppers["iGraphDensity"].ValueChanged += OnDisplayCheckedChanged;

            checkBoxes["bDeleteInput"] = new CheckBox { Text = "Delete input", Checked = EndBulgeOptions.DeleteInput };
            checkBoxes["bEcho"] = new CheckBox { Text = "Echo", Checked = EndBulgeOptions.Echo };

            foreach (var k in new[] { "fIncrement", "fScale_Picked", "fSlideG2_Picked", "fSlideG3_Picked", "fScale_Opp", "fSlideG2_Opp", "fSlideG3_Opp" })
                labels[k].Width = 50;

            // SHRUNK fixed widths so they don't overpower the top row
            textBoxes["fIncrement"].Width = 50;
            labels["iSliderSteps"].Width = 64;
            dropDowns["iSliderSteps"].Width = 60;

            foreach (var k in new[] { "fScale_Picked", "fSlideG2_Picked", "fSlideG3_Picked", "fScale_Opp", "fSlideG2_Opp", "fSlideG3_Opp" })
                textBoxes[k].Width = 60;

            numericSteppers["iGraphScale"].Width = 45;
            numericSteppers["iGraphDensity"].Width = 45;
        }

        protected virtual void SetupLayout()
        {
            string termCap = isSurface ? "Edge" : "End";

            Label Gap() => new Label { Width = 8 };

            StackLayout Wrap(Control c) => new StackLayout { Orientation = Orientation.Horizontal, Items = { c } };

            StackLayout BuildCombo(string key)
            {
                var stepper = new StackLayout { Spacing = 0, Items = { btnUp[key], btnDown[key] } };
                return new StackLayout { Orientation = Orientation.Horizontal, Spacing = 0, Items = { textBoxes[key], stepper } };
            }

            var root = new StackLayout { Padding = new Padding(10), Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch };

            root.Items.Add(new Label { Text = "Continuity Constraints", Font = new Font(SystemFont.Bold, 10) });
            var contGrid = new TableLayout { Spacing = new Size(4, 4) };
            contGrid.Rows.Add(new TableRow(labels["idxCont_Picked"], radioButtonLists["idxCont_Picked"], new TableCell { ScaleWidth = true }));
            contGrid.Rows.Add(new TableRow(labels["idxCont_Opp"], radioButtonLists["idxCont_Opp"], new TableCell { ScaleWidth = true }));
            root.Items.Add(contGrid);
            root.Items.Add(new Label { Height = 4 });

            root.Items.Add(new Label { Text = "Configurations", Font = new Font(SystemFont.Bold, 10) });
            var adjRow = new TableLayout { Spacing = new Size(8, 4) };
            adjRow.Rows.Add(new TableRow(labels["bLinkedEnds"], radioButtonLists["bLinkedEnds"], new TableCell { ScaleWidth = true }));
            root.Items.Add(adjRow);
            root.Items.Add(new Label { Height = 2 });

            // Changed back to DynamicLayout: The trailing 'null' forces left-justification and natural widths
            var incrGrid = new DynamicLayout { Spacing = new Size(4, 4) };
            incrGrid.AddRow(
                labels["fIncrement"], Wrap(textBoxes["fIncrement"]), 
                Gap(), 
                labels["iSliderSteps"], Wrap(dropDowns["iSliderSteps"]), 
                null // <--- Absorbs all extra space to the right
            );
            root.Items.Add(incrGrid);

            Button btnReset = new Button { Text = "Reset All Scale and Slide Values" };
            btnReset.Click += OnResetValuesClick;
            root.Items.Add(btnReset);
            root.Items.Add(new Label { Height = 6 });

            root.Items.Add(new Label { Text = $"Picked {termCap}", Font = new Font(SystemFont.Bold, 10) });
            var pickedGrid = new TableLayout { Spacing = new Size(4, 4) };
            pickedGrid.Rows.Add(new TableRow(labels["fScale_Picked"], BuildCombo("fScale_Picked"), Gap(), new TableCell(sliders["fScale_Picked"], true)));
            pickedGrid.Rows.Add(new TableRow(labels["fSlideG2_Picked"], BuildCombo("fSlideG2_Picked"), Gap(), new TableCell(sliders["fSlideG2_Picked"], true)));
            pickedGrid.Rows.Add(new TableRow(labels["fSlideG3_Picked"], BuildCombo("fSlideG3_Picked"), Gap(), new TableCell(sliders["fSlideG3_Picked"], true)));
            root.Items.Add(pickedGrid);
            root.Items.Add(new Label { Height = 6 });

            root.Items.Add(new Label { Text = $"Opposite {termCap}", Font = new Font(SystemFont.Bold, 10) });
            var oppGrid = new TableLayout { Spacing = new Size(4, 4) };
            oppGrid.Rows.Add(new TableRow(labels["fScale_Opp"], BuildCombo("fScale_Opp"), Gap(), new TableCell(sliders["fScale_Opp"], true)));
            oppGrid.Rows.Add(new TableRow(labels["fSlideG2_Opp"], BuildCombo("fSlideG2_Opp"), Gap(), new TableCell(sliders["fSlideG2_Opp"], true)));
            oppGrid.Rows.Add(new TableRow(labels["fSlideG3_Opp"], BuildCombo("fSlideG3_Opp"), Gap(), new TableCell(sliders["fSlideG3_Opp"], true)));
            root.Items.Add(oppGrid);
            root.Items.Add(new Label { Height = 4 });

            root.Items.Add(new Label { Text = "Display", Font = new Font(SystemFont.Bold, 10) });
            var displayGroup = new StackLayout { Spacing = 4, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            
            var dispChkStack = new TableLayout { Spacing = new Size(8, 4) };
            dispChkStack.Rows.Add(new TableRow(checkBoxes["bShowGeom"], checkBoxes["bShowPolygon"], new TableCell { ScaleWidth = true }));
            
            // Changed back to DynamicLayout: Trailing 'null' prevents the right-justified stretching
            var analysisGrid = new DynamicLayout { Spacing = new Size(4, 4) };
            analysisGrid.AddRow(
                checkBoxes["bShowGraph"], 
                Gap(), 
                labels["iGraphScale"], Wrap(numericSteppers["iGraphScale"]), 
                Gap(), 
                labels["iGraphDensity"], Wrap(numericSteppers["iGraphDensity"]),
                null // <--- Absorbs all extra space to the right
            );
            
            displayGroup.Items.Add(dispChkStack);
            displayGroup.Items.Add(analysisGrid);
            root.Items.Add(displayGroup);
            root.Items.Add(new Label { Height = 4 });

            var chkStack = new TableLayout { Spacing = new Size(20, 4) };
            chkStack.Rows.Add(new TableRow(checkBoxes["bDeleteInput"], checkBoxes["bEcho"], new TableCell { ScaleWidth = true }));
            root.Items.Add(chkStack);
            root.Items.Add(new Label { Height = 8 });

            Button btnOk = new Button { Text = "OK" };
            btnOk.Click += OnOKButtonClick;
            Button btnSave = new Button { Text = "Save Settings" };
            btnSave.Click += OnSaveSettingsButtonClick;
            Button btnCancel = new Button { Text = "Cancel" };
            btnCancel.Click += (s, e) => { Result = false; Close(); };

            DefaultButton = btnOk;
            AbortButton = btnCancel;

            var btnGrid = new DynamicLayout { Spacing = new Size(8, 4) };
            btnGrid.BeginHorizontal();
            btnGrid.Add(btnOk, true);
            btnGrid.Add(btnSave, true);
            btnGrid.Add(btnCancel, true);
            btnGrid.EndHorizontal();

            root.Items.Add(btnGrid);

            Content = root;
            AutoSize = true;
            Resizable = false;
        }
        protected void OnResetValuesClick(object sender, EventArgs e)
        {
            // Temporarily pause auto-updating so we don't trigger 6 separate preview recalculations
            _autoUpdating = true;
            
            // Reset the underlying scale tracking variables
            _exactScalePicked = 1.0;
            _exactScaleOpp = 1.0;

            // Reset the text boxes
            textBoxes["fScale_Picked"].Text = "1.0000";
            textBoxes["fSlideG2_Picked"].Text = "0.0000";
            textBoxes["fSlideG3_Picked"].Text = "0.0000";

            textBoxes["fScale_Opp"].Text = "1.0000";
            textBoxes["fSlideG2_Opp"].Text = "0.0000";
            textBoxes["fSlideG3_Opp"].Text = "0.0000";

            // Return all jog sliders to their center zero positions
            foreach (var key in sliders.Keys)
            {
                sliders[key].Value = 0;
                sliderPrevVals[key] = 0;
            }

            _autoUpdating = false;
            
            // Fire a single unified preview update
            UpdatePreview();
        }

        protected void UpdateSliderRanges()
        {
            int steps = int.Parse(dropDowns["iSliderSteps"].SelectedValue.ToString());
            foreach (var s in sliders.Values)
            {
                s.MinValue = -steps;
                s.MaxValue = steps;
            }
        }

        protected void OnSliderStepsChanged(object sender, EventArgs e) => UpdateSliderRanges();

        protected void OnJogSliderChanged(string targetKey)
        {
            if (_autoUpdatingSlider) return;
            
            int delta = sliders[targetKey].Value - sliderPrevVals[targetKey];
            sliderPrevVals[targetKey] = sliders[targetKey].Value;
            if (delta == 0) return;

            double? incrVal = ParseToFloat(textBoxes["fIncrement"].Text);
            if (incrVal == null) return;

            double change = delta * incrVal.Value;
            _autoUpdating = true;
            
            if (targetKey.Contains("Scale"))
            {
                double currentVal = targetKey.Contains("Picked") ? _exactScalePicked : _exactScaleOpp;
                double newVal = Math.Max(incrVal.Value, currentVal + change);
                if (targetKey.Contains("Picked")) _exactScalePicked = newVal;
                else _exactScaleOpp = newVal;
                textBoxes[targetKey].Text = newVal.ToString("F4");
            }
            else
            {
                double currentVal = ParseToFloat(textBoxes[targetKey].Text) ?? 0.0;
                textBoxes[targetKey].Text = (currentVal + change).ToString("F4");
            }

            _autoUpdating = false;
            SyncLinkedControls();
            UpdatePreview();
        }

        protected void ZeroSlider(string targetKey)
        {
            _autoUpdatingSlider = true;
            sliders[targetKey].Value = 0;
            sliderPrevVals[targetKey] = 0;
            _autoUpdatingSlider = false;
        }

        protected void SyncLinkedControls()
        {
            if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1)
            {
                if (textBoxes["fScale_Opp"].Text != textBoxes["fScale_Picked"].Text)
                    textBoxes["fScale_Opp"].Text = textBoxes["fScale_Picked"].Text;
                if (textBoxes["fSlideG2_Opp"].Text != textBoxes["fSlideG2_Picked"].Text)
                    textBoxes["fSlideG2_Opp"].Text = textBoxes["fSlideG2_Picked"].Text;
                if (textBoxes["fSlideG3_Opp"].Text != textBoxes["fSlideG3_Picked"].Text)
                    textBoxes["fSlideG3_Opp"].Text = textBoxes["fSlideG3_Picked"].Text;
            }
        }

        protected void OnContinuityChanged(object sender, EventArgs e)
        {
            if (_autoUpdating) return;

            // Identify exactly which radio list fired the event before updating the preview
            if (sender == radioButtonLists["idxCont_Picked"])
                _lastClickedCont = 0;
            else if (sender == radioButtonLists["idxCont_Opp"])
                _lastClickedCont = 1;

            UpdateControlStates();
            UpdatePreview();
        }

        protected void OnLinkedModeChanged(object sender, EventArgs e)
        {
            UpdateControlStates();
            if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1) SyncLinkedControls();
            UpdatePreview();
        }

        protected void OnScaleTextChanged(object sender, EventArgs e)
        {
            var txtBox = (TextBox)sender;
            double? val = ParseToFloat(txtBox.Text);
            
            if (!_autoUpdating && val != null)
            {
                if (txtBox == textBoxes["fScale_Picked"]) _exactScalePicked = val.Value;
                else if (txtBox == textBoxes["fScale_Opp"]) _exactScaleOpp = val.Value;
            }
            
            txtBox.BackgroundColor = (val != null && val.Value > RhinoMath.ZeroTolerance) ? Colors.White : Colors.LightPink;
            
            if (!_autoUpdating)
            {
                SyncLinkedControls();
                UpdatePreview();
            }
        }

        protected void OnSlideTextChanged(object sender, EventArgs e)
        {
            var txtBox = (TextBox)sender;
            double? val = ParseToFloat(txtBox.Text);
            txtBox.BackgroundColor = (val != null) ? Colors.White : Colors.LightPink;
            
            if (!_autoUpdating)
            {
                SyncLinkedControls();
                UpdatePreview();
            }
        }

        protected void OnIncrementTextChanged(object sender, EventArgs e)
        {
            var txtBox = (TextBox)sender;
            double? val = ParseToFloat(txtBox.Text);
            txtBox.BackgroundColor = (val != null && val.Value > RhinoMath.ZeroTolerance) ? Colors.White : Colors.LightPink;
        }

        protected virtual void OnDisplayCheckedChanged(object sender, EventArgs e)
        {
            EndBulgeOptions.ShowGeom = checkBoxes["bShowGeom"].Checked ?? true;
            EndBulgeOptions.ShowPolygon = checkBoxes["bShowPolygon"].Checked ?? true;
            EndBulgeOptions.ShowGraph = checkBoxes["bShowGraph"].Checked ?? true;
            EndBulgeOptions.GraphScale = (int)numericSteppers["iGraphScale"].Value;
            EndBulgeOptions.GraphDensity = (int)numericSteppers["iGraphDensity"].Value;
            RhinoDoc.ActiveDoc.Views.Redraw();
        }

        // Stepper Logic
        protected void StartHoldTimer(int direction, string key)
        {
            activeStepperKey = key;
            holdDirection = direction;
            AdjustStepper(direction, key);
            holdTimer.Start();
        }

        protected void StopHoldTimer(object sender, EventArgs e) => holdTimer.Stop();
        protected void OnHoldTimerElapsed(object sender, EventArgs e) => AdjustStepper(holdDirection, activeStepperKey);
        protected virtual void OnDebounceTimerElapsed(object sender, EventArgs e) => debounceTimer.Stop();

        protected void AdjustStepper(int direction, string key)
        {
            double? incrVal = ParseToFloat(textBoxes["fIncrement"].Text);
            if (incrVal == null) return;

            double? currentVal = key.Contains("Scale") 
                ? (key.Contains("Picked") ? _exactScalePicked : _exactScaleOpp) 
                : ParseToFloat(textBoxes[key].Text);

            if (currentVal == null) return;

            double newVal = currentVal.Value + (incrVal.Value * direction);
            
            if (key.Contains("Scale"))
            {
                newVal = Math.Max(incrVal.Value, newVal);
                if (key.Contains("Picked")) _exactScalePicked = newVal;
                else _exactScaleOpp = newVal;
            }

            _autoUpdating = true;
            textBoxes[key].Text = newVal.ToString("F4");
            _autoUpdating = false;

            SyncLinkedControls();
            UpdatePreview();
        }

        protected double? ParseToFloat(string text)
        {
            text = text.Trim();
            try
            {
                if (text.Contains("/"))
                {
                    var parts = text.Split('/');
                    return double.Parse(parts[0]) / double.Parse(parts[1]);
                }
                return double.Parse(text);
            }
            catch { return null; }
        }

        protected override void OnLoadComplete(EventArgs e)
        {
            base.OnLoadComplete(e);
            
            if (EndBulgeOptions.WindowLocation.HasValue && 
                EndBulgeOptions.WindowLocation.Value.X > 0 && 
                EndBulgeOptions.WindowLocation.Value.Y > 0)
            {
                this.Location = EndBulgeOptions.WindowLocation.Value;
            }
        }

        protected void SaveSettings()
        {
            EndBulgeOptions.LinkedEnds = radioButtonLists["bLinkedEnds"].SelectedIndex == 1;
            EndBulgeOptions.SliderStepsIndex = dropDowns["iSliderSteps"].SelectedIndex;
            
            if (ParseToFloat(textBoxes["fIncrement"].Text) is double i) EndBulgeOptions.Increment = i;

            if (ParseToFloat(textBoxes["fScale_Picked"].Text) is double sp) EndBulgeOptions.ScalePicked = sp;
            if (ParseToFloat(textBoxes["fSlideG2_Picked"].Text) is double s2p) EndBulgeOptions.SlideG2Picked = s2p;
            if (ParseToFloat(textBoxes["fSlideG3_Picked"].Text) is double s3p) EndBulgeOptions.SlideG3Picked = s3p;

            if (ParseToFloat(textBoxes["fScale_Opp"].Text) is double so) EndBulgeOptions.ScaleOpp = so;
            if (ParseToFloat(textBoxes["fSlideG2_Opp"].Text) is double s2o) EndBulgeOptions.SlideG2Opp = s2o;
            if (ParseToFloat(textBoxes["fSlideG3_Opp"].Text) is double s3o) EndBulgeOptions.SlideG3Opp = s3o;

            EndBulgeOptions.ContinuityPicked = radioButtonLists["idxCont_Picked"].SelectedIndex;
            EndBulgeOptions.ContinuityOpp = radioButtonLists["idxCont_Opp"].SelectedIndex;
            
            EndBulgeOptions.ShowGeom = checkBoxes["bShowGeom"].Checked ?? true;
            EndBulgeOptions.ShowPolygon = checkBoxes["bShowPolygon"].Checked ?? true;
            EndBulgeOptions.ShowGraph = checkBoxes["bShowGraph"].Checked ?? true;
            EndBulgeOptions.GraphScale = (int)numericSteppers["iGraphScale"].Value;
            EndBulgeOptions.GraphDensity = (int)numericSteppers["iGraphDensity"].Value;
            EndBulgeOptions.DeleteInput = checkBoxes["bDeleteInput"].Checked ?? true;
            EndBulgeOptions.Echo = checkBoxes["bEcho"].Checked ?? true;
            EndBulgeOptions.Debug = false;
        }

        protected void OnOKButtonClick(object sender, EventArgs e)
        {
            double? sPicked = ParseToFloat(textBoxes["fScale_Picked"].Text);
            double? sOpp = ParseToFloat(textBoxes["fScale_Opp"].Text);

            if (sPicked == null || sPicked <= RhinoMath.ZeroTolerance || sOpp == null || sOpp <= RhinoMath.ZeroTolerance)
            {
                RhinoApp.WriteLine("Invalid inputs. No changes were applied.");
                DialogOk = false;
                Close();
                return;
            }

            SaveSettings();
            DialogOk = true;
            Result = true;
            Close();
        }

        protected void OnSaveSettingsButtonClick(object sender, EventArgs e)
        {
            SaveSettings();
            RhinoApp.WriteLine("Settings saved as default.");
        }

        protected virtual void OnFormClosed(object sender, EventArgs e)
        {
            // Update the global options with the final location before destruction
            EndBulgeOptions.WindowLocation = this.Location;

            // Core cleanup handled in commands
        }
    }
}