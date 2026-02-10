namespace Tasks.CoverageMapConfiguration
{
    /// <summary>
    /// Simple RGB color representation that is platform-agnostic.
    /// Can be converted to WPF Color, System.Drawing.Color, etc. in the GUI layer.
    /// </summary>
    public readonly struct RgbColor
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public RgbColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public override string ToString() => $"RGB({R}, {G}, {B})";
    }

    /// <summary>
    /// Configuration data for coverage map visualization.
    /// Contains color palettes for proteases and PTM mass-to-name/color mappings.
    /// </summary>
    public static class CoverageMapConfiguration
    {
        #region Protease Color Palette

        /// <summary>
        /// Palette of 29 distinct colors for protease visualization.
        /// Assign colors to proteases based on their index in the protease list.
        /// </summary>
        public static readonly IReadOnlyList<RgbColor> ProteaseColorPalette = new List<RgbColor>
        {
            new(130, 88, 159),   // Purple
            new(0, 148, 50),     // Green
            new(181, 52, 113),   // Magenta
            new(52, 152, 219),   // Blue
            new(230, 126, 34),   // Orange
            new(27, 20, 100),    // Dark Blue
            new(253, 167, 223),  // Pink
            new(99, 110, 114),   // Gray
            new(255, 221, 89),   // Yellow
            new(162, 155, 254),  // Light Purple
            new(58, 227, 116),   // Light Green
            new(252, 66, 123),   // Hot Pink
            new(126, 214, 223),  // Cyan
            new(249, 127, 81),   // Coral
            new(189, 195, 199),  // Silver
            new(241, 196, 15),   // Gold
            new(0, 98, 102),     // Teal
            new(142, 68, 173),   // Violet
            new(225, 112, 85),   // Salmon
            new(255, 184, 184),  // Light Pink
            new(61, 193, 211),   // Sky Blue
            new(224, 86, 253),   // Bright Purple
            new(196, 229, 56),   // Lime
            new(255, 71, 87),    // Red
            new(88, 177, 159),   // Sea Green
            new(111, 30, 81),    // Maroon
            new(129, 236, 236),  // Aqua
            new(179, 57, 57),    // Dark Red
            new(232, 67, 147)    // Deep Pink
        };

        /// <summary>
        /// Gets a color for a protease based on its index in the protease list.
        /// Wraps around if there are more proteases than colors.
        /// </summary>
        public static RgbColor GetProteaseColor(int proteaseIndex)
        {
            return ProteaseColorPalette[proteaseIndex % ProteaseColorPalette.Count];
        }

        /// <summary>
        /// Creates a dictionary mapping protease names to colors.
        /// </summary>
        public static Dictionary<string, RgbColor> CreateProteaseColorMap(IEnumerable<string> proteaseNames)
        {
            var colorMap = new Dictionary<string, RgbColor>();
            int index = 0;
            foreach (var protease in proteaseNames)
            {
                if (!colorMap.ContainsKey(protease))
                {
                    colorMap[protease] = GetProteaseColor(index++);
                }
            }
            return colorMap;
        }

        #endregion

        #region PTM Configuration

        /// <summary>
        /// Default color for unknown/unmapped modifications (Orange)
        /// </summary>
        public static readonly RgbColor DefaultModificationColor = new(255, 165, 0);

        /// <summary>
        /// Maps PTM monoisotopic masses (rounded to 4 decimal places) to their common names.
        /// </summary>
        public static readonly IReadOnlyDictionary<double, string> PtmMassToName = new Dictionary<double, string>
        {
            { 42.0106, "Acetylation" },
            { 541.0611, "ADP-Ribosylation" },
            { 70.0419, "Butyrylation" },
            { 43.9898, "Carboxylation" },
            { 0.9840, "Citrullination" },
            { 68.0262, "Crotonylation" },
            { 28.0313, "Dimethylation" },
            { 27.9949, "Formylation" },
            { 114.0317, "Glutarylation" },
            { 203.0794, "HexNAc" },
            { 87.0446, "Hydroxybutyrylation" },
            { 15.9949, "Hydroxylation" },
            { 86.0004, "Malonylation" },
            { 14.0157, "Methylation" },
            { 28.9902, "Nitrosylation" },
            { 79.9663, "Phosphorylation" },
            { 229.0140, "Pyridoxal Phosphate" },
            { 100.0160, "Succinylation" },
            { 79.9568, "Sulfonation" },
            { 42.0470, "Trimethylation" }
        };

        /// <summary>
        /// Maps PTM names to their display colors.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, RgbColor> PtmNameToColor = new Dictionary<string, RgbColor>
        {
            { "Acetylation", new(0, 255, 255) },           // Aqua
            { "ADP-Ribosylation", new(102, 205, 170) },    // MediumAquamarine
            { "Butyrylation", new(50, 205, 50) },          // LimeGreen
            { "Carboxylation", new(230, 230, 250) },       // Lavender
            { "Citrullination", new(123, 104, 238) },      // MediumSlateBlue
            { "Crotonylation", new(255, 160, 122) },       // LightSalmon
            { "Dimethylation", new(219, 112, 147) },       // PaleVioletRed
            { "Formylation", new(255, 255, 0) },           // Yellow
            { "Glutarylation", new(189, 183, 107) },       // DarkKhaki
            { "HexNAc", new(176, 224, 230) },              // PowderBlue
            { "Hydroxybutyrylation", new(147, 112, 219) }, // MediumPurple
            { "Hydroxylation", new(255, 99, 71) },         // Tomato
            { "Malonylation", new(176, 196, 222) },        // LightSteelBlue
            { "Methylation", new(255, 192, 203) },         // Pink
            { "Nitrosylation", new(221, 160, 221) },       // Plum
            { "Phosphorylation", new(127, 255, 0) },       // Chartreuse
            { "Pyridoxal Phosphate", new(240, 128, 128) }, // LightCoral
            { "Succinylation", new(30, 144, 255) },        // DodgerBlue
            { "Sulfonation", new(152, 251, 152) },         // PaleGreen
            { "Trimethylation", new(199, 21, 133) },       // MediumVioletRed
            { "Other", new(255, 165, 0) }                  // Orange (default)
        };

        /// <summary>
        /// Gets the PTM name for a given mass, or null if not found.
        /// </summary>
        /// <param name="mass">The monoisotopic mass to look up</param>
        /// <param name="roundingDecimals">Number of decimal places for rounding (default: 4)</param>
        public static string GetPtmName(double mass, int roundingDecimals = 4)
        {
            double roundedMass = Math.Round(mass, roundingDecimals, MidpointRounding.AwayFromZero);
            return PtmMassToName.TryGetValue(roundedMass, out var name) ? name : null;
        }

        /// <summary>
        /// Gets the color for a PTM by its name.
        /// </summary>
        public static RgbColor GetPtmColor(string ptmName)
        {
            if (string.IsNullOrEmpty(ptmName))
                return DefaultModificationColor;

            return PtmNameToColor.TryGetValue(ptmName, out var color) ? color : DefaultModificationColor;
        }

        /// <summary>
        /// Gets the color for a PTM by its mass.
        /// </summary>
        public static RgbColor GetPtmColorByMass(double mass, int roundingDecimals = 4)
        {
            var ptmName = GetPtmName(mass, roundingDecimals);
            return GetPtmColor(ptmName ?? "Other");
        }

        #endregion
    }
}
