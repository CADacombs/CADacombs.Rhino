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
            var authorLabel = new Label { Text = "Developed by Steven P. Burzinski" };
            
            // --- REFINED SUPPORT INFO ---
            var forumLabel = new Label { Text = "Bug reports, requests or custom script development:\nContact @spb on the" };
            var forumLinkBtn = new LinkButton { Text = "McNeel Forum" };
            forumLinkBtn.Click += (s, e) => 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discourse.mcneel.com/",
                    UseShellExecute = true
                });
            };

            var pmLinkBtn = new LinkButton { Text = "Direct PM to @spb" };
            pmLinkBtn.Click += (s, e) => 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discourse.mcneel.com/new-message?username=spb",
                    UseShellExecute = true
                });
            };

            // -----------------------------
            
            var licenseLabel = new Label 
            { 
                Text = "Licensed under the GNU LGPLv3.", 
                TextAlignment = TextAlignment.Center 
            };
            
            var licenseLink = new LinkButton { Text = "Read LICENSE.txt on GitHub" };
            licenseLink.Click += (s, e) => 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/CADacombs/CADacombs.Rhino/blob/main/LICENSE.txt",
                    UseShellExecute = true
                });
            };
            
            var githubLink = new LinkButton { Text = "Visit CADacombs on GitHub" };
            githubLink.Click += (s, e) => 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/CADacombs/CADacombs.Rhino",
                    UseShellExecute = true
                });
            };

            var packageManagerLink = new LinkButton { Text = "View in Package Manager" };
            packageManagerLink.Click += (s, e) => 
            {
                RhinoApp.RunScript("! _-PackageManager _Search CADacombs", false);
                Close();
            };

            var btnOk = new Button { Text = "OK", Width = 80 };
            btnOk.Click += (s, e) => Close();
            
            DefaultButton = btnOk;

            // Support Group (Spacing = 0 removes gaps between lines)
            var supportGroup = new StackLayout
            {
                Spacing = 0,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = 
                {
                    forumLabel,
                    forumLinkBtn,
                    pmLinkBtn
                }
            };

            // License Group (Spacing = 0 removes gap between label and link)
            var licenseGroup = new StackLayout
            {
                Spacing = 0,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = 
                {
                    licenseLabel,
                    licenseLink
                }
            };

            // Main Layout
            Content = new StackLayout
            {
                Padding = new Padding(30),
                Spacing = 6,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Items = 
                {
                    titleLabel,
                    versionLabel,
                    new Label { Height = 4 }, 
                    authorLabel,
                    new Label { Height = 8 }, 
                    
                    supportGroup,     // Tight support paragraph
                    
                    new Label { Height = 8 }, 
                    
                    licenseGroup,     // Tight license paragraph
                    
                    new Label { Height = 8 }, 
                    githubLink,
                    packageManagerLink,
                    new Label { Height = 16 }, 
                    btnOk
                }
            };

            // Add Clipboard Support (Ctrl+C)
            this.KeyDown += (s, e) =>
            {
                if ((e.Modifiers.HasFlag(Keys.Control) || e.Modifiers.HasFlag(Keys.Application)) && e.Key == Keys.C)
                {
                    string clipboardText = 
$@"CADacombs Version {version}
Developed by Steven P. Burzinski
Bug reports, requests, or custom script development:
Contact @spb on the McNeel Forum,
https://discourse.mcneel.com/
Licensed under the GNU LGPLv3.
https://github.com/CADacombs/CADacombs.Rhino";
                    
                    Clipboard.Instance.Text = clipboardText;
                    e.Handled = true; 
                }
            };
        }
    }
}