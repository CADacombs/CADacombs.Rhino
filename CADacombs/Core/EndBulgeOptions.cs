using System;
using Rhino;

namespace CADacombs.Core
{
    /// <summary>
    /// Acts as the central, session-persistent data store for all EndBulge configurations.
    /// This replaces the Python 'scriptcontext.sticky' and dictionary architecture.
    /// </summary>
    public static class EndBulgeOptions
    {
        // Holds a global reference to the active command's settings registry
        public static Rhino.PersistentSettings PluginSettings { get; set; }

        // ----------------------------------------------------
        // Global / Application States
        // ----------------------------------------------------
        public static bool Dialog { get; set; } = true;
        public static Eto.Drawing.Point? WindowLocation { get; set; } = null;
        public static bool LinkedEnds { get; set; } = false;
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
        public static bool ShowGraph { get; set; } = false;
        public static int GraphScale { get; set; } = 100;
        public static int GraphDensity { get; set; } = 1;
        public static bool UseNativePreview { get; set; } = false;

        // ----------------------------------------------------
        // Picked End / Edge States
        // ----------------------------------------------------
        /// <summary>
        /// Continuity mapping: 0 = None, 1 = G0, 2 = G1, 3 = G2, 4 = G3
        /// </summary>
        public static int ContinuityPicked { get; set; } = 4;
        public static double ScalePicked { get; set; } = 1.0;
        public static double SlideG2Picked { get; set; } = 0.0;
        public static double SlideG3Picked { get; set; } = 0.0;

        // ----------------------------------------------------
        // Opposite End / Edge States
        // ----------------------------------------------------
        public static int ContinuityOpp { get; set; } = 4;
        public static double ScaleOpp { get; set; } = 1.0;
        public static double SlideG2Opp { get; set; } = 0.0;
        public static double SlideG3Opp { get; set; } = 0.0;

        // ----------------------------------------------------
        // Persistent Registry Methods
        // ----------------------------------------------------
        public static void Load(Rhino.PersistentSettings settings)
        {
            if (settings == null) return;
            PluginSettings = settings;

            LinkedEnds = settings.GetBool("EndBulge_LinkedEnds", LinkedEnds);
            Increment = settings.GetDouble("EndBulge_Increment", Increment);
            SliderStepsIndex = settings.GetInteger("EndBulge_SliderStepsIndex", SliderStepsIndex);
            DeleteInput = settings.GetBool("EndBulge_DeleteInput", DeleteInput);
            
            ShowPolygon = settings.GetBool("EndBulge_ShowPolygon", ShowPolygon);
            ShowGraph = settings.GetBool("EndBulge_ShowGraph", ShowGraph);
            GraphScale = settings.GetInteger("EndBulge_GraphScale", GraphScale);
            GraphDensity = settings.GetInteger("EndBulge_GraphDensity", GraphDensity);
            
            ContinuityPicked = settings.GetInteger("EndBulge_ContinuityPicked", ContinuityPicked);
            ContinuityOpp = settings.GetInteger("EndBulge_ContinuityOpp", ContinuityOpp);
            
            UseNativePreview = settings.GetBool("EndBulge_UseNativePreview", UseNativePreview);
        }

        public static void Save()
        {
            if (PluginSettings == null) return;
            
            PluginSettings.SetBool("EndBulge_LinkedEnds", LinkedEnds);
            PluginSettings.SetDouble("EndBulge_Increment", Increment);
            PluginSettings.SetInteger("EndBulge_SliderStepsIndex", SliderStepsIndex);
            PluginSettings.SetBool("EndBulge_DeleteInput", DeleteInput);
            
            PluginSettings.SetBool("EndBulge_ShowPolygon", ShowPolygon);
            PluginSettings.SetBool("EndBulge_ShowGraph", ShowGraph);
            PluginSettings.SetInteger("EndBulge_GraphScale", GraphScale);
            PluginSettings.SetInteger("EndBulge_GraphDensity", GraphDensity);
            
            PluginSettings.SetInteger("EndBulge_ContinuityPicked", ContinuityPicked);
            PluginSettings.SetInteger("EndBulge_ContinuityOpp", ContinuityOpp);
            
            PluginSettings.SetBool("EndBulge_UseNativePreview", UseNativePreview);
        }
    }
}