using System;
using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.UI;
using Eto.Drawing;
using Eto.Forms;

namespace CADacombs.Commands
{
    public class CADacombsAboutCommand : Rhino.Commands.Command
    {
        // This is the command the user will type into the Rhino command line
        public override string EnglishName => "spb_CADacombsAbout";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Launch the custom Eto dialog we defined below
            var dialog = new CADacombsAboutDialog();
            dialog.ShowModal(RhinoEtoApp.MainWindow);
            
            return Result.Success;
        }
    }

    /// <summary>
    /// A lightweight, professional Eto Dialog for displaying plugin metadata.
    /// </summary>
    public class CADacombsAboutDialog : Dialog
    {
        public CADacombsAboutDialog()
        {
            Title = "About CADacombs";
            Resizable = false;
            WindowStyle = WindowStyle.Default;

            // Dynamically fetch the current version from CADacombs.csproj (e.g., "0.2.2")
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            // 1. UI Elements
            var titleLabel = new Label { Text = "CADacombs", Font = new Font(SystemFont.Bold, 16) };
            var versionLabel = new Label { Text = $"Version {version}", TextColor = Colors.Gray };
            var authorLabel = new Label { Text = "Developed by Steven P. Burzinski (spb)" };
            
            // A professional touch: A clickable link that runs a macro to open Yak
            var packageManagerLink = new LinkButton { Text = "View in Package Manager" };
            packageManagerLink.Click += (s, e) => 
            {
                RhinoApp.RunScript("! _-PackageManager _Search CADacombs", false);
                Close();
            };

            var btnOk = new Button { Text = "OK", Width = 80 };
            btnOk.Click += (s, e) => Close();
            
            // Allow the user to hit 'Enter' to close the dialog instantly
            DefaultButton = btnOk;

            // 2. Layout
            // Using a StackLayout with Center alignment perfectly stacks the elements
            Content = new StackLayout
            {
                Padding = new Padding(30),
                Spacing = 6,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = 
                {
                    titleLabel,
                    versionLabel,
                    new Label { Height = 4 }, // Spacer
                    authorLabel,
                    new Label { Height = 8 }, // Spacer
                    packageManagerLink,
                    new Label { Height = 16 }, // Spacer
                    btnOk
                }
            };
        }
    }
}