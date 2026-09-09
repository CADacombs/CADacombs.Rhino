using System;
using Eto.Drawing;
using Eto.Forms;

namespace CADacombs.Core
{
    /// <summary>
    /// Base class for all CADacombs Eto dialogs.
    /// Provides reusable logic for window position memory, screen-bounds validation, and DRY architecture.
    /// </summary>
    public abstract class CADacombsDialogBase : Dialog<bool>
    {
        /// <summary>
        /// Derived classes must return the location from their specific options/settings store.
        /// </summary>
        protected abstract Point? LoadSavedLocation();

        /// <summary>
        /// Derived classes must save the location to their specific options/settings store.
        /// </summary>
        protected abstract void SaveCurrentLocation(Point location);

        protected override void OnLoadComplete(EventArgs e)
        {
            base.OnLoadComplete(e);

            Point? savedLoc = LoadSavedLocation();

            if (savedLoc.HasValue)
            {
                // Create a small rectangle representing the top-left of the dialog (the title bar).
                // We use 50x20 to ensure there is enough of the title bar on-screen for the user to click and drag.
                Rectangle windowTitleBar = new Rectangle(savedLoc.Value, new Size(50, 20));

                bool isVisible = false;

                // Check against all active monitors to see if the title bar intersects with any screen.
                // This prevents the dialog from spawning permanently off-screen if a monitor was unplugged.
                foreach (var screen in Screen.Screens)
                {
                    if (screen.Bounds.Intersects(windowTitleBar))
                    {
                        isVisible = true;
                        break;
                    }
                }

                if (isVisible)
                {
                    this.Location = savedLoc.Value;
                }
                // If not visible on any active screen, we do nothing and let Eto fall back to its default centering.
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Automatically capture the exact location when the user closes the dialog
            SaveCurrentLocation(this.Location);
        }
    }
}