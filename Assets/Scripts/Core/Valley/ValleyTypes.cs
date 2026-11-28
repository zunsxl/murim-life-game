using System;
using System.Collections.Generic;

namespace Murim.Core.Valley
{
    public enum CellKind : byte
    {
        Meadow,
        Earth,
        Granite,
        Damp,
        EarthenSlope,
        RockySlope,
        Cliff,
        WetRock,
        Bamboo,
        RiverChannel,
        RiverRiffle,
        Waterfall
    }

    public enum CoverKind : byte
    {
        None,
        ShortGrass,
        TallGrass,
        SparseGrass,
        WhiteFlowers,
        YellowFlowers
    }

    public enum PropKind : byte
    {
        BoulderUpright,
        BoulderSquat,
        BoulderLow,
        PineUpright,
        PineLeaning,
        PineSpreading,
        ShrubRound,
        ShrubSpreading,
        CliffCrest,
        CliffToe,
        RiverRocks,
        FallFoam
    }

    public enum RayHitKind : byte
    {
        None,
        Ground,
        Cap
    }

    public readonly struct PropPlacement : IEquatable<PropPlacement>
    {
        public readonly PropKind Kind;
        public readonly int Cx;
        public readonly int Cz;
        public readonly bool FlipX;

        public PropPlacement(PropKind kind, int cx, int cz, bool flipX)
        {
            Kind = kind;
            Cx = cx;
            Cz = cz;
            FlipX = flipX;
        }

        public bool Equals(PropPlacement other)
        {
            return Kind == other.Kind && Cx == other.Cx && Cz == other.Cz && FlipX == other.FlipX;
        }

        public override bool Equals(object obj) => obj is PropPlacement other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Cx, Cz, FlipX);
    }

    public readonly struct AirBounds : IEquatable<AirBounds>
    {
        public readonly float MinX;
        public readonly float MaxX;
        public readonly float MinZ;
        public readonly float MaxZ;

        public AirBounds(float minX, float maxX, float minZ, float maxZ)
        {
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public bool Contains(float x, float z)
        {
            return x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        }

        public bool ContainsCellCenter(int cx, int cz)
        {
            return Contains(cx + 0.5f, cz + 0.5f);
        }

        public bool Equals(AirBounds other)
        {
            return MinX == other.MinX && MaxX == other.MaxX && MinZ == other.MinZ && MaxZ == other.MaxZ;
        }

        public override bool Equals(object obj) => obj is AirBounds other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(MinX, MaxX, MinZ, MaxZ);
    }

    public readonly struct RayHit
    {
        public readonly RayHitKind Kind;
        public readonly Vec3 Point;
        public readonly int CellX;
        public readonly int CellZ;

        public RayHit(RayHitKind kind, Vec3 point, int cellX, int cellZ)
        {
            Kind = kind;
            Point = point;
            CellX = cellX;
            CellZ = cellZ;
        }

        public static RayHit None => new RayHit(RayHitKind.None, Vec3.Zero, -1, -1);
    }

    public sealed class LayoutInput
    {
        public string[] Heights { get; }
        public string[] Cells { get; }
        public string[] Cover { get; }
        public IReadOnlyList<PropPlacement> Props { get; }
        public AirBounds AirBounds { get; }
        public int StartX { get; }
        public int StartZ { get; }

        public LayoutInput(
            string[] heights,
            string[] cells,
            string[] cover,
            IReadOnlyList<PropPlacement> props,
            AirBounds airBounds,
            int startX,
            int startZ)
        {
            Heights = heights;
            Cells = cells;
            Cover = cover;
            Props = props;
            AirBounds = airBounds;
            StartX = startX;
            StartZ = startZ;
        }
    }

    public sealed class LayoutFormatException : Exception
    {
        public LayoutFormatException(string message)
            : base(message)
        {
        }
    }
}
