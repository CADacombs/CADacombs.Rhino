using System;

namespace CADacombs.Core.Reporting
{
    public static class FormatUtils
    {
        /// <summary>
        /// Formats distance values for command reports.
        /// </summary>
        public static string FormatDistance(double dist, int prec)
        {
            if (dist == 0.0) return "0";
            
            double threshold = Math.Pow(10.0, -prec);
            if (Math.Abs(dist) < threshold) return dist.ToString("0.0e0").ToLower();
            
            return Math.Round(dist, prec).ToString($"F{prec}");
        }

        /// <summary>
        /// Formats tolerance values for UI TextBoxes (max 15 significant digits, drops trailing zeros, uses scientific notation for microscopic numbers).
        /// </summary>
        public static string FormatTolerance(double val, int prec)
        {
            if (val == 0.0) return "0";
            
            double threshold = Math.Pow(10.0, -prec);
            if (Math.Abs(val) < threshold) return val.ToString("0.0e0").ToLower(); 
            
            return val.ToString("0.###############"); 
        }
    }
}