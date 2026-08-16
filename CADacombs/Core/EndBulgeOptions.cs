using System;

namespace CADacombs.Core
{
    /// <summary>
    /// Acts as the central, session-persistent data store for all EndBulge configurations.
    /// This replaces the Python 'scriptcontext.sticky' and dictionary architecture.
    /// </summary>
    public static class EndBulgeOptions
    {
        // ----------------------------------------------------
        // Global / Application States
        // ----------------------------------------------------
        public static bool Dialog { get; set; } = true;
        public static bool LinkedEnds { get; set; } = true;
        public static double Increment { get; set; } = 0.05;
        public static int SliderStepsIndex { get; set; } = 1; // Corresponds to '10' in the UI dropdown

        public static bool DeleteInput { get; set; } = true;
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;

        // ----------------------------------------------------
        // Display & Analysis States
        // ----------------------------------------------------
        public static bool ShowPolygon { get; set; } = true;
        public static bool ShowGeom { get; set; } = true;
        public static bool ShowGraph { get; set; } = true;
        public static int GraphScale { get; set; } = 100;
        public static int GraphDensity { get; set; } = 1;

        // ----------------------------------------------------
        // Picked End / Edge States
        // ----------------------------------------------------
        /// <summary>
        /// Continuity mapping: 0 = None, 1 = G0, 2 = G1, 3 = G2, 4 = G3
        /// Storing 3 defaults to G2, but matches your Python default index behavior.
        /// </summary>
        public static int ContinuityPicked { get; set; } = 3;
        public static double ScalePicked { get; set; } = 1.0;
        public static double SlideG2Picked { get; set; } = 0.0;
        public static double SlideG3Picked { get; set; } = 0.0;

        // ----------------------------------------------------
        // Opposite End / Edge States
        // ----------------------------------------------------
        public static int ContinuityOpp { get; set; } = 3;
        public static double ScaleOpp { get; set; } = 1.0;
        public static double SlideG2Opp { get; set; } = 0.0;
        public static double SlideG3Opp { get; set; } = 0.0;

        // ----------------------------------------------------
        // Helper Methods
        // ----------------------------------------------------
        
        /// <summary>
        /// Instantly syncs the Opposite end configurations to match the Picked end.
        /// </summary>
        public static void SyncLinkedControls()
        {
            if (LinkedEnds)
            {
                ScaleOpp = ScalePicked;
                SlideG2Opp = SlideG2Picked;
                SlideG3Opp = SlideG3Picked;
            }
        }
    }
}