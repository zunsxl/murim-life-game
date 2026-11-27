using System;
using System.Collections.Generic;

namespace Murim.Core.World
{
    public sealed class PropVariantSpec
    {
        internal PropVariantSpec(
            byte variant,
            string name,
            PropFamily family,
            int footprintCm,
            int heightCm)
        {
            Variant = variant;
            Name = name;
            Family = family;
            FootprintCm = footprintCm;
            HeightCm = heightCm;
        }

        public byte Variant { get; }
        public string Name { get; }
        public PropFamily Family { get; }
        public int FootprintCm { get; }
        public int HeightCm { get; }
        public int CanopyRadiusCm { get; internal set; }
        public ushort MinScalePct { get; internal set; }
        public ushort MaxScalePct { get; internal set; }
        public bool Solid { get; internal set; }
        public PropYRule YRule { get; internal set; }
        public int ShadowWidthCm { get; internal set; }
    }

    /// <summary>
    /// Static catalog of the 17 prop variants and family caps (§3.2.1, §3.2.3).
    /// </summary>
    public static class PropCatalog
    {
        public const byte PineAncient = 0;
        public const byte PineTall = 1;
        public const byte PineLeaning = 2;
        public const byte PineYoung = 3;
        public const byte BambooDense = 4;
        public const byte BambooEdge = 5;
        public const byte BambooYoung = 6;
        public const byte RockOutcrop = 7;
        public const byte RockBoulder = 8;
        public const byte RockRiverstone = 9;
        public const byte RockRidged = 10;
        public const byte RockCluster = 11;
        public const byte BankLip = 12;
        public const byte FloraWildflower = 13;
        public const byte FloraShrub = 14;
        public const byte FloraFern = 15;
        public const byte FloraTussock = 16;

        public const int VariantCount = 17;

        public const int PineCap = 1200;
        public const int BambooCap = 800;
        public const int RockCap = 1600;
        public const int LipCap = 500;
        public const int FloraCap = 10000;

        public const int FloraWildflowerCap = 4000;
        public const int FloraShrubCap = 1200;
        public const int FloraFernCap = 800;
        public const int FloraTussockCap = 5000;

        public static readonly IReadOnlyList<PropVariantSpec> Variants = Array.AsReadOnly(BuildVariants());

        public static PropVariantSpec Get(byte variant)
        {
            return Variants[variant];
        }

        public static int GetFamilyCap(PropFamily family)
        {
            switch (family)
            {
                case PropFamily.Pine:
                    return PineCap;
                case PropFamily.Bamboo:
                    return BambooCap;
                case PropFamily.Rock:
                    return RockCap;
                case PropFamily.Lip:
                    return LipCap;
                case PropFamily.Flora:
                    return FloraCap;
                default:
                    return 0;
            }
        }

        public static int GetVariantCap(byte variant)
        {
            switch (variant)
            {
                case FloraWildflower:
                    return FloraWildflowerCap;
                case FloraShrub:
                    return FloraShrubCap;
                case FloraFern:
                    return FloraFernCap;
                case FloraTussock:
                    return FloraTussockCap;
                default:
                    return int.MaxValue;
            }
        }

        private static PropVariantSpec[] BuildVariants()
        {
            return new[]
            {
                new PropVariantSpec(PineAncient, "pine_ancient", PropFamily.Pine, 350, 1600)
                { CanopyRadiusCm = 600, MinScalePct = 105, MaxScalePct = 105, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 900 },
                new PropVariantSpec(PineTall, "pine_tall", PropFamily.Pine, 250, 1400)
                { CanopyRadiusCm = 450, MinScalePct = 85, MaxScalePct = 120, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 700 },
                new PropVariantSpec(PineLeaning, "pine_leaning", PropFamily.Pine, 200, 900)
                { CanopyRadiusCm = 400, MinScalePct = 85, MaxScalePct = 115, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 600 },
                new PropVariantSpec(PineYoung, "pine_young", PropFamily.Pine, 150, 550)
                { CanopyRadiusCm = 250, MinScalePct = 80, MaxScalePct = 120, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 400 },
                new PropVariantSpec(BambooDense, "bamboo_dense", PropFamily.Bamboo, 300, 1200)
                { CanopyRadiusCm = 300, MinScalePct = 90, MaxScalePct = 115, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 600 },
                new PropVariantSpec(BambooEdge, "bamboo_edge", PropFamily.Bamboo, 250, 900)
                { CanopyRadiusCm = 250, MinScalePct = 85, MaxScalePct = 115, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 500 },
                new PropVariantSpec(BambooYoung, "bamboo_young", PropFamily.Bamboo, 150, 500)
                { CanopyRadiusCm = 150, MinScalePct = 80, MaxScalePct = 120, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 300 },
                new PropVariantSpec(RockOutcrop, "rock_outcrop", PropFamily.Rock, 400, 450)
                { CanopyRadiusCm = 400, MinScalePct = 85, MaxScalePct = 125, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 700 },
                new PropVariantSpec(RockBoulder, "rock_boulder", PropFamily.Rock, 150, 180)
                { CanopyRadiusCm = 150, MinScalePct = 70, MaxScalePct = 130, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 280 },
                new PropVariantSpec(RockRiverstone, "rock_riverstone", PropFamily.Rock, 90, 40)
                { CanopyRadiusCm = 90, MinScalePct = 70, MaxScalePct = 130, Solid = false, YRule = PropYRule.CellMin, ShadowWidthCm = 150 },
                new PropVariantSpec(RockRidged, "rock_ridged", PropFamily.Rock, 180, 220)
                { CanopyRadiusCm = 180, MinScalePct = 75, MaxScalePct = 125, Solid = true, YRule = PropYRule.CellMin, ShadowWidthCm = 320 },
                new PropVariantSpec(RockCluster, "rock_cluster", PropFamily.Rock, 100, 35)
                { CanopyRadiusCm = 100, MinScalePct = 80, MaxScalePct = 120, Solid = false, YRule = PropYRule.CellMin, ShadowWidthCm = 180 },
                new PropVariantSpec(BankLip, "bank_lip", PropFamily.Lip, 150, 60)
                { CanopyRadiusCm = 150, MinScalePct = 90, MaxScalePct = 110, Solid = false, YRule = PropYRule.Bilinear, ShadowWidthCm = 0 },
                new PropVariantSpec(FloraWildflower, "flora_wildflower", PropFamily.Flora, 60, 30)
                { CanopyRadiusCm = 60, MinScalePct = 80, MaxScalePct = 120, Solid = false, YRule = PropYRule.Bilinear, ShadowWidthCm = 0 },
                new PropVariantSpec(FloraShrub, "flora_shrub", PropFamily.Flora, 80, 90)
                { CanopyRadiusCm = 80, MinScalePct = 80, MaxScalePct = 125, Solid = false, YRule = PropYRule.Bilinear, ShadowWidthCm = 0 },
                new PropVariantSpec(FloraFern, "flora_fern", PropFamily.Flora, 50, 60)
                { CanopyRadiusCm = 50, MinScalePct = 80, MaxScalePct = 120, Solid = false, YRule = PropYRule.Bilinear, ShadowWidthCm = 0 },
                new PropVariantSpec(FloraTussock, "flora_tussock", PropFamily.Flora, 40, 35)
                { CanopyRadiusCm = 40, MinScalePct = 75, MaxScalePct = 125, Solid = false, YRule = PropYRule.Bilinear, ShadowWidthCm = 0 },
            };
        }
    }
}
