namespace Murim.Core.Valley
{
    /// <summary>
    /// Lookup metadata for cell, ground-cover, and prop kinds from spec §3.1.
    /// </summary>
    public static class ValleyKindMetadata
    {
        public static bool TryParseCellKind(char ch, out CellKind kind)
        {
            switch (ch)
            {
                case 'm': kind = CellKind.Meadow; return true;
                case 'e': kind = CellKind.Earth; return true;
                case 'g': kind = CellKind.Granite; return true;
                case 'd': kind = CellKind.Damp; return true;
                case 's': kind = CellKind.EarthenSlope; return true;
                case 'r': kind = CellKind.RockySlope; return true;
                case 'c': kind = CellKind.Cliff; return true;
                case 'k': kind = CellKind.WetRock; return true;
                case 'b': kind = CellKind.Bamboo; return true;
                case 'w': kind = CellKind.RiverChannel; return true;
                case 'v': kind = CellKind.RiverRiffle; return true;
                case 'f': kind = CellKind.Waterfall; return true;
                default: kind = CellKind.Meadow; return false;
            }
        }

        public static bool TryParseCoverKind(char ch, out CoverKind cover)
        {
            switch (ch)
            {
                case '.': cover = CoverKind.None; return true;
                case 'a': cover = CoverKind.ShortGrass; return true;
                case 't': cover = CoverKind.TallGrass; return true;
                case 'k': cover = CoverKind.SparseGrass; return true;
                case 'w': cover = CoverKind.WhiteFlowers; return true;
                case 'y': cover = CoverKind.YellowFlowers; return true;
                default: cover = CoverKind.None; return false;
            }
        }

        public static bool IsWalkableKind(CellKind kind)
        {
            return kind == CellKind.Meadow
                || kind == CellKind.Earth
                || kind == CellKind.Granite
                || kind == CellKind.Damp
                || kind == CellKind.EarthenSlope
                || kind == CellKind.RockySlope;
        }

        public static bool IsSlopeKind(CellKind kind)
        {
            return kind == CellKind.EarthenSlope || kind == CellKind.RockySlope;
        }

        public static bool IsWaterKind(CellKind kind)
        {
            return kind == CellKind.RiverChannel || kind == CellKind.RiverRiffle;
        }

        public static float CellObstacleHeight(CellKind kind)
        {
            return kind == CellKind.Bamboo ? ValleyConstants.BambooObstacleHeight : 0f;
        }

        public static float PropBlockingRadius(PropKind kind)
        {
            return kind switch
            {
                PropKind.BoulderUpright => 0.9f,
                PropKind.BoulderSquat => 1.3f,
                PropKind.BoulderLow => 0.9f,
                PropKind.PineUpright => 0.6f,
                PropKind.PineLeaning => 0.6f,
                PropKind.PineSpreading => 0.6f,
                _ => 0f
            };
        }

        public static float PropObstacleHeight(PropKind kind)
        {
            return kind switch
            {
                PropKind.BoulderUpright => 2.0f,
                PropKind.BoulderSquat => 1.5f,
                PropKind.BoulderLow => 1.0f,
                PropKind.PineUpright => 8.0f,
                PropKind.PineLeaning => 7.0f,
                PropKind.PineSpreading => 6.0f,
                _ => 0f
            };
        }

        public static bool IsBlockingProp(PropKind kind)
        {
            return PropBlockingRadius(kind) > 0f;
        }

        public static bool IsPine(PropKind kind)
        {
            return kind == PropKind.PineUpright
                || kind == PropKind.PineLeaning
                || kind == PropKind.PineSpreading;
        }

        public static bool IsBoulder(PropKind kind)
        {
            return kind == PropKind.BoulderUpright
                || kind == PropKind.BoulderSquat
                || kind == PropKind.BoulderLow;
        }

        public static bool IsShrub(PropKind kind)
        {
            return kind == PropKind.ShrubRound || kind == PropKind.ShrubSpreading;
        }

        public static bool IsPropAllowedOn(PropKind prop, CellKind cell)
        {
            return prop switch
            {
                PropKind.BoulderUpright => IsWalkableKind(cell),
                PropKind.BoulderSquat => IsWalkableKind(cell),
                PropKind.BoulderLow => IsWalkableKind(cell),
                PropKind.ShrubRound => IsWalkableKind(cell),
                PropKind.ShrubSpreading => IsWalkableKind(cell),
                PropKind.PineUpright => IsWalkableKind(cell) || cell == CellKind.Cliff,
                PropKind.PineLeaning => IsWalkableKind(cell) || cell == CellKind.Cliff,
                PropKind.PineSpreading => IsWalkableKind(cell) || cell == CellKind.Cliff,
                PropKind.CliffCrest => cell == CellKind.Cliff || cell == CellKind.WetRock,
                PropKind.CliffToe => cell == CellKind.Cliff || cell == CellKind.WetRock,
                PropKind.RiverRocks => IsWaterKind(cell),
                PropKind.FallFoam => IsWaterKind(cell) || cell == CellKind.Waterfall,
                _ => false
            };
        }
    }
}
