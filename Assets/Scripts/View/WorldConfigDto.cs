using System;
using Murim.Core.World;

namespace Murim.View
{
    /// <summary>
    /// Flat JSON DTO for <c>world.json</c> (§2.5, §3.1.9).
    /// </summary>
    [Serializable]
    public sealed class WorldConfigDto
    {
        public int schemaVersion;
        public int midTierHeightM;
        public int upperTierHeightM;
        public int sideRidgeHeightM;
        public int northRidgeHeightM;
        public int treeDensityPct;
        public int rockDensityPct;
        public int floraDensityPct;

        public WorldConfigValues ToValues()
        {
            return new WorldConfigValues
            {
                MidTierHeightM = midTierHeightM,
                UpperTierHeightM = upperTierHeightM,
                SideRidgeHeightM = sideRidgeHeightM,
                NorthRidgeHeightM = northRidgeHeightM,
                TreeDensityPct = treeDensityPct,
                RockDensityPct = rockDensityPct,
                FloraDensityPct = floraDensityPct,
            };
        }
    }
}
