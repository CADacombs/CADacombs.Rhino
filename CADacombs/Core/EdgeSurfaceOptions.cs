namespace CADacombs.Core
{
    /// <summary>
    /// Central, session-persistent data store for EdgeSrf configurations.
    /// </summary>
    public static class EdgeSrfOptions
    {
        /// <summary>
        /// False = AllCrvs (Global), True = NextCrv (Per-Curve)
        /// </summary>
        public static bool ApplyContToNextCrv { get; set; } = false;
        
        /// <summary>
        /// 0 = G0, 1 = G1, 2 = G2
        /// </summary>
        public static int Continuity { get; set; } = 1;
        
        public static bool Echo { get; set; } = true;
        public static bool Debug { get; set; } = false;
    }
}