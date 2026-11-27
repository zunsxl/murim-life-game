namespace Murim.Core.World
{
    /// <summary>
    /// Raw integer fields for constructing and validating a <see cref="WorldConfig"/> (§3.1.9).
    /// </summary>
    public struct WorldConfigValues
    {
        public int MidTierHeightM;
        public int UpperTierHeightM;
        public int SideRidgeHeightM;
        public int NorthRidgeHeightM;
        public int TreeDensityPct;
        public int RockDensityPct;
        public int FloraDensityPct;
    }

    /// <summary>
    /// Validated immutable world generation configuration (§3.1.9).
    /// </summary>
    public sealed class WorldConfig
    {
        public const int ExpectedSchemaVersion = 1;
        public const int MinMidTierHeightM = 8;
        public const int MaxMidTierHeightM = 16;
        public const int MinUpperTierDeltaM = 10;
        public const int MaxUpperTierDeltaM = 18;
        public const int MinSideRidgeHeightM = 20;
        public const int MaxSideRidgeHeightM = 40;
        public const int MinNorthRidgeHeightM = 22;
        public const int MaxNorthRidgeHeightM = 32;
        public const int MinDensityPct = 25;
        public const int MaxDensityPct = 150;

        public static WorldConfig Default { get; } = new WorldConfig(new WorldConfigValues
        {
            MidTierHeightM = 12,
            UpperTierHeightM = 26,
            SideRidgeHeightM = 30,
            NorthRidgeHeightM = 26,
            TreeDensityPct = 100,
            RockDensityPct = 100,
            FloraDensityPct = 100,
        });

        private WorldConfig(in WorldConfigValues values)
        {
            MidTierHeightM = values.MidTierHeightM;
            UpperTierHeightM = values.UpperTierHeightM;
            SideRidgeHeightM = values.SideRidgeHeightM;
            NorthRidgeHeightM = values.NorthRidgeHeightM;
            TreeDensityPct = values.TreeDensityPct;
            RockDensityPct = values.RockDensityPct;
            FloraDensityPct = values.FloraDensityPct;
        }

        public int MidTierHeightM { get; }
        public int UpperTierHeightM { get; }
        public int SideRidgeHeightM { get; }
        public int NorthRidgeHeightM { get; }
        public int TreeDensityPct { get; }
        public int RockDensityPct { get; }
        public int FloraDensityPct { get; }

        public WorldConfigValues ToValues()
        {
            return new WorldConfigValues
            {
                MidTierHeightM = MidTierHeightM,
                UpperTierHeightM = UpperTierHeightM,
                SideRidgeHeightM = SideRidgeHeightM,
                NorthRidgeHeightM = NorthRidgeHeightM,
                TreeDensityPct = TreeDensityPct,
                RockDensityPct = RockDensityPct,
                FloraDensityPct = FloraDensityPct,
            };
        }

        public static bool TryCreate(in WorldConfigValues values, out WorldConfig config, out string failedField)
        {
            config = null;
            failedField = Validate(in values);
            if (failedField != null)
            {
                return false;
            }

            config = new WorldConfig(in values);
            return true;
        }

        private static string Validate(in WorldConfigValues v)
        {
            if (v.MidTierHeightM < MinMidTierHeightM || v.MidTierHeightM > MaxMidTierHeightM)
            {
                return "midTierHeightM";
            }

            int minUpper = v.MidTierHeightM + MinUpperTierDeltaM;
            int maxUpper = v.MidTierHeightM + MaxUpperTierDeltaM;
            if (v.UpperTierHeightM < minUpper || v.UpperTierHeightM > maxUpper)
            {
                return "upperTierHeightM";
            }

            if (v.SideRidgeHeightM < MinSideRidgeHeightM || v.SideRidgeHeightM > MaxSideRidgeHeightM)
            {
                return "sideRidgeHeightM";
            }

            if (v.NorthRidgeHeightM < MinNorthRidgeHeightM || v.NorthRidgeHeightM > MaxNorthRidgeHeightM)
            {
                return "northRidgeHeightM";
            }

            return ValidateDensities(in v);
        }

        private static string ValidateDensities(in WorldConfigValues v)
        {
            if (v.TreeDensityPct < MinDensityPct || v.TreeDensityPct > MaxDensityPct)
            {
                return "treeDensityPct";
            }

            if (v.RockDensityPct < MinDensityPct || v.RockDensityPct > MaxDensityPct)
            {
                return "rockDensityPct";
            }

            if (v.FloraDensityPct < MinDensityPct || v.FloraDensityPct > MaxDensityPct)
            {
                return "floraDensityPct";
            }

            return null;
        }
    }
}
