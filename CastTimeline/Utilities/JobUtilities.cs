using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CastTimeline.Utilities
{
    public static class JobUtilities
    {
        private static readonly Dictionary<string, uint> JobIds = new()
        {
            ["Gladiator"]    = 2,
            ["Pugilist"]     = 3,
            ["Marauder"]     = 4,
            ["Lancer"]       = 5,
            ["Archer"]       = 6,
            ["Counjerer"]    = 7,
            ["Thaumaturge"]  = 8,
            ["Paladin"]      = 20,
            ["Monk"]         = 21,
            ["Warrior"]      = 22,
            ["Dragoon"]      = 23,
            ["Bard"]         = 24,
            ["WhiteMage"]    = 25,
            ["BlackMage"]    = 26,
            ["Arcanist"]     = 27,
            ["Summoner"]     = 28,
            ["Scholar"]      = 29,
            ["Rogue"]        = 91,
            ["Ninja"]        = 92,
            ["Machinist"]    = 96,
            ["DarkKnight"]   = 98,
            ["Astrologian"]  = 99,
            ["Samurai"]      = 111,
            ["RedMage"]      = 112,
            ["BlueMage"]     = 129,
            ["Gunbreaker"]   = 149,
            ["Dancer"]       = 150,
            ["Reaper"]       = 180,
            ["Sage"]         = 181,
            ["Viper"]        = 196,
            ["Pictomancer"]  = 197,
        };

        private static readonly Dictionary<uint, string> JobNames = new()
        {
            [2]   = "GLD",
            [3]   = "PGL",
            [4]   = "MRD",
            [5]   = "LNC",
            [6]   = "ARC",
            [7]   = "CNJ",
            [8]   = "THM",
            [20]  = "PLD",
            [21]  = "MNK",
            [22]  = "WAR",
            [23]  = "DRG",
            [24]  = "BRD",
            [25]  = "WHM",
            [26]  = "BLM",
            [27]  = "ACN",
            [28]  = "SMN",
            [29]  = "SCH",
            [91]  = "ROG",
            [92]  = "NIN",
            [96]  = "MCH",
            [98]  = "DRK",
            [99]  = "AST",
            [111] = "SAM",
            [112] = "RDM",
            [129] = "BLU",
            [149] = "GNB",
            [150] = "DNC",
            [180] = "RPR",
            [181] = "SGE",
            [196] = "VPR",
            [197] = "PCT",
        };

        private static readonly Dictionary<uint, Vector4> JobColorsVec4 = new()
        {
            // Tanks
            [20]  = new Vector4(168, 210, 230, 255) / 255,  // PLD
            [22]  = new Vector4(207,  38,  33, 255) / 255,  // WAR
            [98]  = new Vector4(209,  38, 204, 255) / 255,  // DRK
            [149] = new Vector4(121, 109,  48, 255) / 255,  // GNB
            // Healers
            [25]  = new Vector4(255, 240, 220, 255) / 255,  // WHM
            [29]  = new Vector4(134,  87, 255, 255) / 255,  // SCH
            [99]  = new Vector4(255, 231,  74, 255) / 255,  // AST
            [181] = new Vector4(128, 160, 240, 255) / 255,  // SGE
            // Melee DPS
            [21]  = new Vector4(214, 156,   0, 255) / 255,  // MNK
            [23]  = new Vector4( 65, 100, 205, 255) / 255,  // DRG
            [92]  = new Vector4(175,  25, 100, 255) / 255,  // NIN
            [111] = new Vector4(228, 109,   4, 255) / 255,  // SAM
            [180] = new Vector4(150,  90, 144, 255) / 255,  // RPR
            [196] = new Vector4( 16, 130,  16, 255) / 255,  // VPR
            // Ranged DPS
            [24]  = new Vector4(145, 186,  94, 255) / 255,  // BRD
            [96]  = new Vector4(110, 225, 214, 255) / 255,  // MCH
            [150] = new Vector4(226, 176, 175, 255) / 255,  // DNC
            // Magic DPS
            [26]  = new Vector4(165, 121, 214, 255) / 255,  // BLM
            [28]  = new Vector4( 45, 155, 120, 255) / 255,  // SMN
            [112] = new Vector4(232, 123, 123, 255) / 255,  // RDM
            [129] = new Vector4(  0, 185, 247, 255) / 255,  // BLU
            [197] = new Vector4(252, 146, 225, 255) / 255,  // PCT
            // Legacy classes
            [2]   = new Vector4(168, 210, 230, 255) / 255,  // GLD
            [3]   = new Vector4(214, 156,   0, 255) / 255,  // PGL
            [4]   = new Vector4(207,  38,  33, 255) / 255,  // MRD
            [5]   = new Vector4( 65, 100, 205, 255) / 255,  // LNC
            [6]   = new Vector4(145, 186,  94, 255) / 255,  // ARC
            [7]   = new Vector4(255, 240, 220, 255) / 255,  // CNJ
            [8]   = new Vector4(165, 121, 214, 255) / 255,  // THM
            [91]  = new Vector4(175,  25, 100, 255) / 255,  // ROG
        };

        // Pre-packed ImGui RGBA colors derived from JobColorsVec4 — computed once at class load.
        private static readonly Dictionary<uint, uint> JobColorsU32 =
            JobColorsVec4.ToDictionary(kv => kv.Key, kv => ImGui.GetColorU32(kv.Value));

        // Same colors at 0.6f alpha — used for cast trail rendering.
        private static readonly Dictionary<uint, uint> JobTrailColorsU32 =
            JobColorsVec4.ToDictionary(kv => kv.Key,
                kv => ImGui.GetColorU32(new Vector4(kv.Value.X, kv.Value.Y, kv.Value.Z, 0.6f)));

        private static readonly uint FallbackColorU32 =
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

        private static readonly uint FallbackTrailColorU32 =
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.6f));

        private static readonly Vector4 FallbackColorVec4 = new(0.5f, 0.5f, 0.5f, 1.0f);

        public static uint GetJobId(string jobName) =>
            JobIds.GetValueOrDefault(jobName, 0u);

        public static string GetJobName(uint jobId) =>
            JobNames.GetValueOrDefault(jobId, "UNK");

        public static Vector4 GetJobColorVec4(uint jobId) =>
            JobColorsVec4.GetValueOrDefault(jobId, FallbackColorVec4);

        public static uint GetJobColor(uint jobId) =>
            JobColorsU32.GetValueOrDefault(jobId, FallbackColorU32);

        public static uint GetJobTrailColor(uint jobId) =>
            JobTrailColorsU32.GetValueOrDefault(jobId, FallbackTrailColorU32);
    }
}
