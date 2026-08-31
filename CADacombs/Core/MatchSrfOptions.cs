using System;

namespace CADacombs.Core
{
    /// <summary>
    /// Acts as the central, session-persistent data store for MatchSrf configurations.
    /// Replaces the Python 'scriptcontext.sticky' and dictionary architecture.
    /// </summary>
    public static class MatchSrfOptions
    {
        // ----------------------------------------------------
        // Core Mathematical Constraints
        // ----------------------------------------------------
        
        /// <summary>
        /// 0 = G0, 1 = G1, 2 = G2
        /// </summary>
        public static int Continuity { get; set; } = 1;

        /// <summary>
        /// 0 = None, 1 = G0, 2 = G1, 3 = G2
        /// </summary>
        public static int PreserveOtherEnd { get; set; } = 2;

        /// <summary>
        /// True = Increase SpanCt (Maintain Degree)
        /// False = Increase Degree (Maintain SpanCt)
        /// </summary>
        public static bool MaintainDegree { get; set; } = true;
        
        // ----------------------------------------------------
        // Document & UI States
        // ----------------------------------------------------
        public static bool Replace { get; set; } = true;
        public static bool Echo { get; set; } = true;
        
        // ----------------------------------------------------
        // Developer / Debug States
        // ----------------------------------------------------
        public static bool Debug { get; set; } = false;
        public static bool AddRefs { get; set; } = false;
    }
}