using System;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Shared constants and deterministic vertical-model calculations for the valley.
    /// </summary>
    public static class ValleyConstants
    {
        public const float TickSeconds = 1f / 60f;
        public const float Sin45 = 0.70710678f;
        public const float Cos45 = 0.70710678f;
        public const float Sqrt2 = 1.41421356f;

        public const float FloorHeight = 3f;
        public const int GroundFloorCount = 4;
        public const int AirLayerCount = 3;

        public const float AirLayer1Altitude = 5f;
        public const float AirLayer2Altitude = 11f;
        public const float AirLayer3Altitude = 17f;

        public const float MinAirClearance = 1.5f;
        public const float MaxWalkEdgeDelta = 0.5f;
        public const float MinCliffEdgeDelta = 1.5f;
        public const float FlatFloorTolerance = 0.5f;

        public const float FullViewCutY = float.PositiveInfinity;
        public const float CutYOffset = 0.02f;

        public const int GridWidth = 160;
        public const int GridDepth = 96;
        public const int VertexWidth = 161;
        public const int VertexDepth = 97;

        public const int NorthUncutZ = 90;
        public const int NorthBackRowZ = 96;
        public const float NorthBackRowHeight = 13f;
        public const float MaxAuthoredHeight = 13f;
        public const float HeightStep = 0.5f;

        public const float BambooObstacleHeight = 6f;
        public const float MaxSolidTopInAirBounds = 15.5f;
        public const int MaxExplicitProps = 400;
        public const int MaxCoverCells = 4000;

        public const int AuthoredStartX = 70;
        public const int AuthoredStartZ = 28;
        public const int AuthoredWalkableMinX = 16;
        public const int AuthoredWalkableMaxXExclusive = 144;
        public const int AuthoredWalkableMinZ = 12;
        public const int AuthoredWalkableMaxZExclusive = 90;

        public static readonly AirBounds AuthoredAirBounds = new AirBounds(8f, 152f, 4f, 89f);

        public static int BandOf(float y)
        {
            int band = (int)MathF.Floor(y / FloorHeight) + 1;
            if (band < 1)
            {
                return 1;
            }
            return band > 5 ? 5 : band;
        }

        public static float AltitudeOfLayer(int layer)
        {
            return layer switch
            {
                1 => AirLayer1Altitude,
                2 => AirLayer2Altitude,
                3 => AirLayer3Altitude,
                _ => throw new ArgumentOutOfRangeException(nameof(layer), "Air layer must be 1, 2, or 3.")
            };
        }

        public static float CutYForFloor(int viewedFloor)
        {
            if (viewedFloor == 0)
            {
                return FullViewCutY;
            }
            if (viewedFloor < 1 || viewedFloor > GroundFloorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(viewedFloor), "Viewed floor must be 0..4.");
            }
            return FloorHeight * viewedFloor - CutYOffset;
        }
    }
}
