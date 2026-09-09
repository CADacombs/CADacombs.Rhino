using System;

namespace CADacombs.Core.Reporting
{
    public static class FormatUtils
    {
        /// <summary>
        /// Formats distance values for command reports using scientific/decimal precision rules.
        /// </summary>
        public static string FormatDistance(double dist, int prec)
        {
            if (dist < 1e-6) return "0";
            if (dist < Math.Pow(10.0, -(prec - 2))) return dist.ToString("0.0e0");
            return Math.Round(dist, prec).ToString($"F{prec}");
        }
    }
}