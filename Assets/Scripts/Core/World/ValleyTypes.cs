using System;

namespace Murim.Core.World
{
    public readonly struct WaterNode
    {
        public WaterNode(int xCm, int zCm, int halfWidthCm, int levelCm)
        {
            XCm = xCm;
            ZCm = zCm;
            HalfWidthCm = halfWidthCm;
            LevelCm = levelCm;
        }

        public int XCm { get; }
        public int ZCm { get; }
        public int HalfWidthCm { get; }
        public int LevelCm { get; }
    }

    public readonly struct WaterPool
    {
        public WaterPool(int xCm, int zCm, int radiusCm, int levelCm)
        {
            XCm = xCm;
            ZCm = zCm;
            RadiusCm = radiusCm;
            LevelCm = levelCm;
        }

        public int XCm { get; }
        public int ZCm { get; }
        public int RadiusCm { get; }
        public int LevelCm { get; }
    }

    public readonly struct FallNode
    {
        public FallNode(int xCm, int yCm, int zCm)
        {
            XCm = xCm;
            YCm = yCm;
            ZCm = zCm;
        }

        public int XCm { get; }
        public int YCm { get; }
        public int ZCm { get; }
    }

    public readonly struct SlopeSite
    {
        public SlopeSite(int bottomXCm, int bottomZCm, int topXCm, int topZCm)
        {
            BottomXCm = bottomXCm;
            BottomZCm = bottomZCm;
            TopXCm = topXCm;
            TopZCm = topZCm;
        }

        public int BottomXCm { get; }
        public int BottomZCm { get; }
        public int TopXCm { get; }
        public int TopZCm { get; }
    }

    public sealed class ValleyLayout
    {
        public int AxisX { get; internal set; }
        public int HeadZ { get; internal set; }
        public int MidDepth { get; internal set; }
        public int BowK { get; internal set; }
        public int WfX { get; internal set; }
        public int Slope1X { get; internal set; }
        public int Slope2X { get; internal set; }
        public int ExitX { get; internal set; }
        public int SpringX { get; internal set; }
        public int FallZ { get; internal set; }
        public int ZPool { get; internal set; }
    }

    public enum PropFamily
    {
        Pine = 0,
        Bamboo = 1,
        Rock = 2,
        Lip = 3,
        Flora = 4,
    }

    public enum PropYRule
    {
        CellMin = 0,
        Bilinear = 1,
    }

    public readonly struct PropInstance
    {
        public PropInstance(byte variant, int xCm, int yCm, int zCm, ushort scalePct, byte tonePct)
        {
            Variant = variant;
            XCm = xCm;
            YCm = yCm;
            ZCm = zCm;
            ScalePct = scalePct;
            TonePct = tonePct;
        }

        public byte Variant { get; }
        public int XCm { get; }
        public int YCm { get; }
        public int ZCm { get; }
        public ushort ScalePct { get; }
        public byte TonePct { get; }
    }

    /// <summary>
    /// Immutable generated mountain valley data (§3.1.8).
    /// </summary>
    public sealed class ValleyWorld
    {
        public const int SizeM = 512;
        public const int VertsPerSide = 513;
        public const int VertexCount = VertsPerSide * VertsPerSide;
        public const int SurfaceLayerCount = 8;
        public const int FloorWestM = 72;
        public const int FloorEastM = 440;
        public const int FloorNorthM = 432;
        public const int WaterfallWidthCm = 300;

        internal ValleyWorld()
        {
        }

        public uint Seed { get; internal set; }
        public WorldConfig Config { get; internal set; }
        public short[] HeightCm { get; internal set; }
        public byte[] TierLevelPct { get; internal set; }
        public byte[] RidgePct { get; internal set; }
        public byte[] SurfaceWeights { get; internal set; }
        public WaterNode[] Stream { get; internal set; }
        public WaterNode[] Brook { get; internal set; }
        public WaterPool PlungePool { get; internal set; }
        public WaterPool SpringPool { get; internal set; }
        public FallNode[] Waterfall { get; internal set; }
        public SlopeSite[] Slopes { get; internal set; }
        public ValleyLayout Layout { get; internal set; }
        public PropInstance[] Props { get; internal set; }
        public int PlaneBottomCm { get; internal set; }
        public int PlaneCrestCm { get; internal set; }
        public int VistaUCm { get; internal set; }
        public int VistaVCm { get; internal set; }
        public ulong Hash { get; internal set; }
    }
}
