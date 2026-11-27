using System.Collections.Generic;

namespace Murim.Core.World
{
    /// <summary>
    /// FNV-1a 64-bit world hash over all generated outputs in exact §3.1.8 byte order.
    /// </summary>
    internal static class ValleyHash
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(ValleyWorld world)
        {
            ulong h = OffsetBasis;
            h = FeedUInt(h, world.Seed);
            h = FeedConfig(h, world.Config);
            h = FeedShorts(h, world.HeightCm);
            h = FeedBytes(h, world.TierLevelPct);
            h = FeedBytes(h, world.RidgePct);
            h = FeedBytes(h, world.SurfaceWeights);
            h = FeedWaterNodes(h, world.Stream);
            h = FeedWaterNodes(h, world.Brook);
            h = FeedPool(h, world.PlungePool);
            h = FeedPool(h, world.SpringPool);
            h = FeedFallNodes(h, world.Waterfall);
            h = FeedSlopes(h, world.Slopes);
            h = FeedProps(h, world.Props);
            h = FeedInt(h, world.PlaneBottomCm);
            h = FeedInt(h, world.PlaneCrestCm);
            h = FeedInt(h, world.VistaUCm);
            h = FeedInt(h, world.VistaVCm);
            return h;
        }

        private static ulong FeedConfig(ulong h, WorldConfig c)
        {
            h = FeedInt(h, c.MidTierHeightM);
            h = FeedInt(h, c.UpperTierHeightM);
            h = FeedInt(h, c.SideRidgeHeightM);
            h = FeedInt(h, c.NorthRidgeHeightM);
            h = FeedInt(h, c.TreeDensityPct);
            h = FeedInt(h, c.RockDensityPct);
            return FeedInt(h, c.FloraDensityPct);
        }

        private static ulong FeedShorts(ulong h, IReadOnlyList<short> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                ushort v = (ushort)values[i];
                h = FeedByte(h, (byte)v);
                h = FeedByte(h, (byte)(v >> 8));
            }

            return h;
        }

        private static ulong FeedBytes(ulong h, IReadOnlyList<byte> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                h = FeedByte(h, values[i]);
            }

            return h;
        }

        private static ulong FeedWaterNodes(ulong h, IReadOnlyList<WaterNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                WaterNode n = nodes[i];
                h = FeedInt(h, n.XCm);
                h = FeedInt(h, n.ZCm);
                h = FeedInt(h, n.HalfWidthCm);
                h = FeedInt(h, n.LevelCm);
            }

            return h;
        }

        private static ulong FeedPool(ulong h, WaterPool pool)
        {
            h = FeedInt(h, pool.XCm);
            h = FeedInt(h, pool.ZCm);
            h = FeedInt(h, pool.RadiusCm);
            return FeedInt(h, pool.LevelCm);
        }

        private static ulong FeedFallNodes(ulong h, IReadOnlyList<FallNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                FallNode n = nodes[i];
                h = FeedInt(h, n.XCm);
                h = FeedInt(h, n.YCm);
                h = FeedInt(h, n.ZCm);
            }

            return h;
        }

        private static ulong FeedSlopes(ulong h, IReadOnlyList<SlopeSite> slopes)
        {
            for (int i = 0; i < slopes.Count; i++)
            {
                SlopeSite s = slopes[i];
                h = FeedInt(h, s.BottomXCm);
                h = FeedInt(h, s.BottomZCm);
                h = FeedInt(h, s.TopXCm);
                h = FeedInt(h, s.TopZCm);
            }

            return h;
        }

        private static ulong FeedProps(ulong h, IReadOnlyList<PropInstance> props)
        {
            for (int i = 0; i < props.Count; i++)
            {
                PropInstance p = props[i];
                h = FeedByte(h, p.Variant);
                h = FeedInt(h, p.XCm);
                h = FeedInt(h, p.YCm);
                h = FeedInt(h, p.ZCm);
                h = FeedByte(h, (byte)p.ScalePct);
                h = FeedByte(h, (byte)(p.ScalePct >> 8));
                h = FeedByte(h, p.TonePct);
            }

            return h;
        }

        private static ulong FeedInt(ulong h, int value)
        {
            return FeedUInt(h, (uint)value);
        }

        private static ulong FeedUInt(ulong h, uint value)
        {
            h = FeedByte(h, (byte)value);
            h = FeedByte(h, (byte)(value >> 8));
            h = FeedByte(h, (byte)(value >> 16));
            return FeedByte(h, (byte)(value >> 24));
        }

        private static ulong FeedByte(ulong h, byte b)
        {
            unchecked
            {
                h ^= b;
                return h * Prime;
            }
        }
    }
}
