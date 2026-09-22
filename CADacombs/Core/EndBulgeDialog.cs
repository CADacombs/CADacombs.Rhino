using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace CADacombs.Core
{
    public class EndBulgeDialog : CADacombsDialogBase
    {
        protected Button btnUpgrade;
        protected Dictionary<string, Label> labels = new Dictionary<string, Label>();
        protected Dictionary<string, CheckBox> checkBoxes = new Dictionary<string, CheckBox>();
        protected Dictionary<string, RadioButtonList> radioButtonLists = new Dictionary<string, RadioButtonList>();
        protected Dictionary<string, NumericStepper> numericSteppers = new Dictionary<string, NumericStepper>();
        protected Dictionary<string, TextBox> textBoxes = new Dictionary<string, TextBox>();
        protected Dictionary<string, DropDown> dropDowns = new Dictionary<string, DropDown>();
        protected Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
        protected Dictionary<string, Button> btnUp = new Dictionary<string, Button>();
        protected Dictionary<string, Button> btnDown = new Dictionary<string, Button>();
        
        protected Dictionary<string, RadioButton> radioButtons = new Dictionary<string, RadioButton>();

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
        
        protected bool hasZebra = false, hasEmap = false, hasDraft = false, hasCurv = false;

        public EndBulgeConduit BaseConduit { get; set; }

        public EndBulgeDialog(bool isSurface = false)
        {
            this.isSurface = isSurface;
            Title = "CADacombs EndBulge";
            
            _exactScalePicked = EndBulgeOptions.ScalePicked;
            _exactScaleOpp = EndBulgeOptions.ScaleOpp;

            CreateControls();
            SetupLayout();
            
            OnLinkedModeChanged(null, null);

            debounceTimer = new UITimer { Interval = 0.2 };
            debounceTimer.Elapsed += OnDebounceTimerElapsed;

            holdTimer = new UITimer { Interval = 0.15 };
            holdTimer.Elapsed += OnHoldTimerElapsed;
        }

        public virtual void UpdatePreview() { }
        protected virtual void UpdateControlStates() { }

        protected virtual void CreateControls()
        {
            string termLow = isSurface ? "edge" : "end";
            Font smallFont = new Font(SystemFont.Default, 4);
            string[] contList = { "None", "G0", "G1", "G2", "G3" };

            btnUpgrade = new Button { Visible = false };
            btnUpgrade.Click += OnUpgradeClicked;
            
            radioButtonLists["idxCont_Picked"] = new RadioButtonList { Spacing = new Size(8, 4) };
            radioButtonLists["idxCont_Picked"].DataStore = contList;
            radioButtonLists["idxCont_Picked"].SelectedIndex = EndBulgeOptions.ContinuityPicked;
            radioButtonLists["idxCont_Picked"].SelectedIndexChanged += OnContinuityChanged;
            labels["idxCont_Picked"] = new Label { Text = $"Picked {termLow}:" };

            radioButtonLists["idxCont_Opp"] = new RadioButtonList { Spacing = new Size(8, 4) };
            radioButtonLists["idxCont_Opp"].DataStore = contList;
            radioButtonLists["idxCont_Opp"].SelectedIndex = EndBulgeOptions.ContinuityOpp;
            radioButtonLists["idxCont_Opp"].SelectedIndexChanged += OnContinuityChanged;
            labels["idxCont_Opp"] = new Label { Text = $"Opp {termLow}:" };

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
                
                string tip = isScale ? "Scales the distance between the boundary and the adjacent interior control point." : 
                                       "Translates the corresponding deeper control point parallel to the tangent vector.";
                labels[sKey].ToolTip = tip;
                textBoxes[sKey].ToolTip = tip;

                textBoxes[sKey].MouseWheel += (s, e) => 
                {
                    if (!textBoxes[sKey].Enabled) return;
                    int direction = e.Delta.Height > 0 ? 1 : (e.Delta.Height < 0 ? -1 : 0);
                    if (direction != 0) { AdjustStepper(direction, sKey); e.Handled = true; }
                };
                
                if (isScale) textBoxes[sKey].TextChanged += OnScaleTextChanged;
                else textBoxes[sKey].TextChanged += OnSlideTextChanged;

                sliderPrevVals[sKey] = 0;
                sliders[sKey] = new Slider { SnapToTick = true, TickFrequency = 1 };
                sliders[sKey].ValueChanged += (s, e) => OnJogSliderChanged(sKey);
                sliders[sKey].MouseUp += (s, e) => ZeroSlider(sKey);
                sliders[sKey].KeyUp += (s, e) => ZeroSlider(sKey);
                sliders[sKey].LostFocus += (s, e) => ZeroSlider(sKey);

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
            
            if (isSurface)
            {
                checkBoxes["bUseNativePreview"] = new CheckBox { Text = "Use document object display", Checked = EndBulgeOptions.UseNativePreview };
                checkBoxes["bUseNativePreview"].CheckedChanged += OnDisplayCheckedChanged;

                checkBoxes["bShowWireframe"] = new CheckBox { Text = "Wireframe", Checked = true };
                checkBoxes["bShowWireframe"].CheckedChanged += OnDisplayCheckedChanged;

                var methods = typeof(Rhino.Display.DisplayPipeline).GetMethods();
                foreach (var m in methods)
                {
                    if (m.Name == "DrawZebraPreview") hasZebra = true;
                    if (m.Name == "DrawEmapPreview") hasEmap = true;
                    if (m.Name == "DrawDraftAnglePreview") hasDraft = true;
                    if (m.Name == "DrawCurvaturePreview") hasCurv = true;
                }
                
                var rbNoShad = new RadioButton { Text = "No shading" };
                radioButtons["rbNoShading"] = rbNoShad;
                radioButtons["rbShaded"] = new RadioButton(rbNoShad) { Text = "Shaded" };
                if (hasZebra) radioButtons["rbZebra"] = new RadioButton(rbNoShad) { Text = "Zebra" };
                if (hasEmap) radioButtons["rbEmap"] = new RadioButton(rbNoShad) { Text = "EMap" };
                if (hasDraft) radioButtons["rbDraft"] = new RadioButton(rbNoShad) { Text = "Draft angle" };
                if (hasCurv) radioButtons["rbCurv"] = new RadioButton(rbNoShad) { Text = "Curvature" };

                radioButtons["rbShaded"].Checked = true;

                EventHandler<EventArgs> displayCheck = (s, e) => {
                    if (((RadioButton)s).Checked) OnDisplayCheckedChanged(s, e);
                };
                foreach (var rb in radioButtons.Values)
                {
                    rb.CheckedChanged += displayCheck;
                }
            }

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

            var modeGrid = new TableLayout { Spacing = new Size(8, 4) };
            modeGrid.Rows.Add(new TableRow(labels["bLinkedEnds"], radioButtonLists["bLinkedEnds"], new TableCell { ScaleWidth = true }));
            root.Items.Add(modeGrid);
            root.Items.Add(new Label { Height = 2 });

            var lblCont = new Label { Text = "Continuity Constraints", Font = new Font(SystemFont.Bold, 10) };
            var contHeader = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 10, VerticalContentAlignment = VerticalAlignment.Center, Items = { lblCont, btnUpgrade } };
            root.Items.Add(contHeader);
            var contGrid = new TableLayout { Spacing = new Size(4, 4) };
            contGrid.Rows.Add(new TableRow(labels["idxCont_Picked"], radioButtonLists["idxCont_Picked"], new TableCell { ScaleWidth = true }));
            contGrid.Rows.Add(new TableRow(labels["idxCont_Opp"], radioButtonLists["idxCont_Opp"], new TableCell { ScaleWidth = true }));
            root.Items.Add(contGrid);
            
            root.Items.Add(new Label { Height = 0 });
            root.Items.Add(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            root.Items.Add(new Label { Height = 0 });

            var incrGrid = new DynamicLayout { Spacing = new Size(4, 4) };
            incrGrid.AddRow(labels["fIncrement"], Wrap(textBoxes["fIncrement"]), Gap(), labels["iSliderSteps"], Wrap(dropDowns["iSliderSteps"]), null);
            root.Items.Add(incrGrid);

            Button btnReset = new Button { Text = "Reset All Scale and Slide Values" };
            btnReset.Click += OnResetValuesClick;
            root.Items.Add(btnReset);
            root.Items.Add(new Label { Height = 4 });

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
            
            root.Items.Add(new Label { Height = 0 });
            root.Items.Add(new Panel { Height = 1, BackgroundColor = Colors.LightGrey });
            root.Items.Add(new Label { Height = 0 });

            var displayHeaderRow = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 20, VerticalContentAlignment = VerticalAlignment.Center };
            displayHeaderRow.Items.Add(new Label { Text = "Display", Font = new Font(SystemFont.Bold, 10) });
            displayHeaderRow.Items.Add(checkBoxes["bShowPolygon"]);
            if (!isSurface) displayHeaderRow.Items.Add(checkBoxes["bShowGeom"]);
            root.Items.Add(displayHeaderRow);

            var displayGroup = new StackLayout { Spacing = 4, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var analysisGrid = new DynamicLayout { Spacing = new Size(4, 4) };
            
            if (isSurface)
            {
                analysisGrid.AddRow(
                    checkBoxes["bShowGraph"], Gap(), 
                    labels["iGraphScale"], Wrap(numericSteppers["iGraphScale"]), Gap(), 
                    labels["iGraphDensity"], Wrap(numericSteppers["iGraphDensity"]), null
                );
                
                displayGroup.Items.Add(analysisGrid);
                displayGroup.Items.Add(new Label { Height = 2 });
                
                var nativeRow = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 12, VerticalContentAlignment = VerticalAlignment.Center };
                nativeRow.Items.Add(checkBoxes["bUseNativePreview"]);
                nativeRow.Items.Add(checkBoxes["bShowGeom"]);
                displayGroup.Items.Add(nativeRow);
                displayGroup.Items.Add(new Label { Height = 2 });
                
                var conduitModesLayout = new DynamicLayout { Spacing = new Size(8, 4) };
                conduitModesLayout.AddRow(
                    checkBoxes["bShowWireframe"], 
                    radioButtons["rbNoShading"], 
                    radioButtons["rbShaded"], 
                    hasZebra ? radioButtons["rbZebra"] : null, 
                    null
                );
                
                if (hasEmap || hasDraft || hasCurv)
                {
                    conduitModesLayout.AddRow(
                        null, 
                        hasEmap ? radioButtons["rbEmap"] : null, 
                        hasDraft ? radioButtons["rbDraft"] : null, 
                        hasCurv ? radioButtons["rbCurv"] : null, 
                        null
                    );
                }
                displayGroup.Items.Add(conduitModesLayout);
            }
            else
            {
                analysisGrid.AddRow(
                    checkBoxes["bShowGraph"], Gap(), 
                    labels["iGraphScale"], Wrap(numericSteppers["iGraphScale"]), Gap(), 
                    labels["iGraphDensity"], Wrap(numericSteppers["iGraphDensity"]), null
                );
                displayGroup.Items.Add(analysisGrid);
            }
            
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
            Resizable = true;
        }

        protected virtual void OnUpgradeClicked(object sender, EventArgs e) { }
        
        protected void OnResetValuesClick(object sender, EventArgs e)
        {
            _autoUpdating = true;
            _exactScalePicked = 1.0;
            _exactScaleOpp = 1.0;

            textBoxes["fScale_Picked"].Text = "1.0000";
            textBoxes["fSlideG2_Picked"].Text = "0.0000";
            textBoxes["fSlideG3_Picked"].Text = "0.0000";

            textBoxes["fScale_Opp"].Text = "1.0000";
            textBoxes["fSlideG2_Opp"].Text = "0.0000";
            textBoxes["fSlideG3_Opp"].Text = "0.0000";

            foreach (var key in sliders.Keys)
            {
                sliders[key].Value = 0;
                sliderPrevVals[key] = 0;
            }

            _autoUpdating = false;
            UpdatePreview();
        }

        protected void UpdateSliderRanges()
        {
            int steps = int.Parse(dropDowns["iSliderSteps"].SelectedValue.ToString());
            foreach (var s in sliders.Values) { s.MinValue = -steps; s.MaxValue = steps; }
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
            SyncLinkedControls(targetKey);
            UpdatePreview();
        }

        protected void ZeroSlider(string targetKey)
        {
            _autoUpdatingSlider = true;
            sliders[targetKey].Value = 0;
            sliderPrevVals[targetKey] = 0;
            _autoUpdatingSlider = false;
        }

        protected void SyncLinkedControls(string sourceKey = "Picked")
        {
            if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1)
            {
                bool fromOpp = sourceKey != null && sourceKey.Contains("Opp");

                string srcScale = fromOpp ? "fScale_Opp" : "fScale_Picked";
                string dstScale = fromOpp ? "fScale_Picked" : "fScale_Opp";

                string srcG2 = fromOpp ? "fSlideG2_Opp" : "fSlideG2_Picked";
                string dstG2 = fromOpp ? "fSlideG2_Picked" : "fSlideG2_Opp";

                string srcG3 = fromOpp ? "fSlideG3_Opp" : "fSlideG3_Picked";
                string dstG3 = fromOpp ? "fSlideG3_Picked" : "fSlideG3_Opp";

                bool prevAuto = _autoUpdating;
                _autoUpdating = true; 

                if (textBoxes[dstScale].Text != textBoxes[srcScale].Text) textBoxes[dstScale].Text = textBoxes[srcScale].Text;
                if (textBoxes[dstG2].Text != textBoxes[srcG2].Text) textBoxes[dstG2].Text = textBoxes[srcG2].Text;
                if (textBoxes[dstG3].Text != textBoxes[srcG3].Text) textBoxes[dstG3].Text = textBoxes[srcG3].Text;

                if (fromOpp) _exactScalePicked = _exactScaleOpp;
                else _exactScaleOpp = _exactScalePicked;

                _autoUpdating = prevAuto;
            }
        }

        protected void OnContinuityChanged(object sender, EventArgs e)
        {
            if (_autoUpdating) return;
            if (sender == radioButtonLists["idxCont_Picked"]) _lastClickedCont = 0;
            else if (sender == radioButtonLists["idxCont_Opp"]) _lastClickedCont = 1;

            UpdateControlStates();
            UpdatePreview();
        }

        protected void OnLinkedModeChanged(object sender, EventArgs e)
        {
            UpdateControlStates();
            if (radioButtonLists["bLinkedEnds"].SelectedIndex == 1) SyncLinkedControls("Picked"); 
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
                string source = (txtBox == textBoxes["fScale_Opp"]) ? "Opp" : "Picked";
                SyncLinkedControls(source);
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
                string source = (txtBox == textBoxes["fSlideG2_Opp"] || txtBox == textBoxes["fSlideG3_Opp"]) ? "Opp" : "Picked";
                SyncLinkedControls(source);
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

            double? currentVal = key.Contains("Scale") ? (key.Contains("Picked") ? _exactScalePicked : _exactScaleOpp) : ParseToFloat(textBoxes[key].Text);
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

            SyncLinkedControls(key);
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

        protected override Point? LoadSavedLocation() => EndBulgeOptions.WindowLocation;
        protected override void SaveCurrentLocation(Point location) => EndBulgeOptions.WindowLocation = location;

        protected void SaveSettings()
        {
            EndBulgeOptions.LinkedEnds = radioButtonLists["bLinkedEnds"].SelectedIndex == 1;
            EndBulgeOptions.SliderStepsIndex = dropDowns["iSliderSteps"].SelectedIndex;
            
            if (checkBoxes.ContainsKey("bUseNativePreview")) EndBulgeOptions.UseNativePreview = checkBoxes["bUseNativePreview"].Checked ?? false;

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
    }
}