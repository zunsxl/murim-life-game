using System;
using System.Collections.Generic;

namespace Murim.Core.World
{
    /// <summary>
    /// Water feature construction and terrain carving (§3.1.5).
    /// </summary>
    internal static class ValleyWater
    {
        public static void BuildAndCarve(ValleyGenerationState state)
        {
            ValleyLayout layout = state.Layout;
            layout.FallZ = Primitives.FindFirstZAtLeast(state.S1, layout.WfX, 512);
            layout.ZPool = layout.FallZ - 11;

            int midCm = state.Config.MidTierHeightM * 100;
            state.PlungeLevel = TiltAt(layout.ZPool, layout.HeadZ) - 100;
            state.BrookLevel = midCm + 180;

            state.PlungePool = new WaterPool(layout.WfX * 100, layout.ZPool * 100, 800, state.PlungeLevel);
            state.SpringPool = new WaterPool(layout.SpringX * 100, (layout.FallZ + 36) * 100, 450, state.BrookLevel);
            state.Brook = BuildBrook(layout, state.BrookLevel);
            state.Stream = BuildStream(state);

            CarveWaterways(state);
            state.Waterfall = BuildWaterfall(state);
        }

        private static int TiltAt(int zm, int headZ)
        {
            int clampedZ = zm < headZ ? zm : headZ;
            return 200 + Primitives.FloorDiv(150 * clampedZ, headZ);
        }

        private static WaterNode[] BuildBrook(ValleyLayout layout, int brookLevel)
        {
            var nodes = new WaterNode[16];
            int dxTotal = (layout.WfX - layout.SpringX) * 100;
            int zStart = (layout.FallZ + 32) * 100;
            for (int i = 0; i < 16; i++)
            {
                int x = layout.SpringX * 100 + Primitives.FloorDiv(dxTotal * i, 15);
                int z = zStart - Primitives.FloorDiv(2900 * i, 15);
                nodes[i] = new WaterNode(x, z, 150, brookLevel);
            }

            return nodes;
        }

        private static WaterNode[] BuildStream(ValleyGenerationState state)
        {
            ValleyLayout layout = state.Layout;
            int[] cx = new int[9];
            int[] cz = new int[9];
            BuildStreamControlPoints(state, cx, cz);

            var rawX = new List<int>(97);
            var rawZ = new List<int>(97);
            SampleStreamSpline(cx, cz, rawX, rawZ);

            int count = rawX.Count;
            var nodes = new WaterNode[count];
            for (int i = 0; i < count; i++)
            {
                int hw = 250 + Primitives.FloorDiv(150 * i, count - 1);
                int zm = Math.Max(0, Primitives.FloorDiv(rawZ[i], 100));
                int level = TiltAt(zm, layout.HeadZ) - 100;
                nodes[i] = new WaterNode(rawX[i], rawZ[i], hw, level);
            }

            return nodes;
        }

        private static void BuildStreamControlPoints(ValleyGenerationState state, int[] cx, int[] cz)
        {
            ValleyLayout layout = state.Layout;
            int z0 = (layout.ZPool - 8) * 100;
            for (int k = 0; k <= 6; k++)
            {
                cz[k + 1] = z0 + Primitives.FloorDiv((-800 - z0) * k, 6);
            }

            cx[1] = layout.WfX * 100;
            cx[7] = layout.ExitX * 100;
            for (int k = 1; k <= 5; k++)
            {
                int zm = Primitives.FloorDiv(cz[k + 1], 100);
                int northRem = Math.Max(0, layout.HeadZ - zm);
                int lim = (int)Primitives.Isqrt(Primitives.FloorDiv((long)northRem * 30976L, layout.BowK)) - 30;
                lim = Math.Max(0, lim);
                int baseX = layout.WfX * 100 + Primitives.FloorDiv((layout.ExitX - layout.WfX) * 100 * k, 6) + state.Off[k - 1] * 100;
                cx[k + 1] = Primitives.Clamp(baseX, (layout.AxisX - lim) * 100, (layout.AxisX + lim) * 100);
            }

            cx[0] = 2 * cx[1] - cx[2];
            cz[0] = 2 * cz[1] - cz[2];
            cx[8] = 2 * cx[7] - cx[6];
            cz[8] = 2 * cz[7] - cz[6];
        }

        private static void SampleStreamSpline(int[] cx, int[] cz, List<int> outX, List<int> outZ)
        {
            for (int seg = 0; seg < 6; seg++)
            {
                for (int j = 0; j < 16; j++)
                {
                    int x = CatmullRom(cx[seg], cx[seg + 1], cx[seg + 2], cx[seg + 3], j);
                    int z = CatmullRom(cz[seg], cz[seg + 1], cz[seg + 2], cz[seg + 3], j);
                    if (z >= -400)
                    {
                        outX.Add(x);
                        outZ.Add(z);
                    }
                }
            }

            if (cz[7] >= -400)
            {
                outX.Add(cx[7]);
                outZ.Add(cz[7]);
            }
        }

        private static int CatmullRom(int p0, int p1, int p2, int p3, int j)
        {
            long a = 8192L * p1;
            long b = (long)(p2 - p0) * j * 256L;
            long c = (2L * p0 - 5L * p1 + 4L * p2 - p3) * j * j * 16L;
            long d = (3L * p1 - p0 - 3L * p2 + p3) * j * j * j;
            return (int)Primitives.FloorDiv(a + b + c + d, 8192L);
        }

        private readonly struct SegmentInfo
        {
            public SegmentInfo(WaterNode a, WaterNode b)
            {
                AX = a.XCm;
                AZ = a.ZCm;
                AbX = b.XCm - a.XCm;
                AbZ = b.ZCm - a.ZCm;
                Den = (long)AbX * AbX + (long)AbZ * AbZ;
                AHw = a.HalfWidthCm;
                DHw = b.HalfWidthCm - a.HalfWidthCm;
                ALvl = a.LevelCm;
                DLvl = b.LevelCm - a.LevelCm;
                MinX = a.XCm < b.XCm ? a.XCm : b.XCm;
                MaxX = a.XCm > b.XCm ? a.XCm : b.XCm;
                MinZ = a.ZCm < b.ZCm ? a.ZCm : b.ZCm;
                MaxZ = a.ZCm > b.ZCm ? a.ZCm : b.ZCm;
                MaxHw = a.HalfWidthCm > b.HalfWidthCm ? a.HalfWidthCm : b.HalfWidthCm;
            }

            public int AX { get; }
            public int AZ { get; }
            public int AbX { get; }
            public int AbZ { get; }
            public long Den { get; }
            public int AHw { get; }
            public int DHw { get; }
            public int ALvl { get; }
            public int DLvl { get; }
            public int MinX { get; }
            public int MaxX { get; }
            public int MinZ { get; }
            public int MaxZ { get; }
            public int MaxHw { get; }
        }

        private static SegmentInfo[] BuildSegmentInfos(WaterNode[] path)
        {
            var infos = new SegmentInfo[path.Length - 1];
            for (int i = 0; i < infos.Length; i++)
            {
                infos[i] = new SegmentInfo(path[i], path[i + 1]);
            }

            return infos;
        }

        private struct PassHit
        {
            public int BestE;
            public int BestLevel;
            public int PoolE;
        }

        private readonly struct SegmentGroup
        {
            public SegmentGroup(SegmentInfo[] segs, int start, int end)
            {
                Start = start;
                End = end;
                int minX = int.MaxValue;
                int maxX = int.MinValue;
                int minZ = int.MaxValue;
                int maxZ = int.MinValue;
                int maxHw = 0;
                for (int i = start; i < end; i++)
                {
                    SegmentInfo s = segs[i];
                    if (s.MinX < minX) minX = s.MinX;
                    if (s.MaxX > maxX) maxX = s.MaxX;
                    if (s.MinZ < minZ) minZ = s.MinZ;
                    if (s.MaxZ > maxZ) maxZ = s.MaxZ;
                    if (s.MaxHw > maxHw) maxHw = s.MaxHw;
                }

                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                MaxHw = maxHw;
            }

            public int Start { get; }
            public int End { get; }
            public int MinX { get; }
            public int MaxX { get; }
            public int MinZ { get; }
            public int MaxZ { get; }
            public int MaxHw { get; }
        }

        private static SegmentGroup[] BuildSegmentGroups(SegmentInfo[] segs)
        {
            const int groupSize = 8;
            int groupCount = (segs.Length + groupSize - 1) / groupSize;
            var groups = new SegmentGroup[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                int start = g * groupSize;
                int end = start + groupSize;
                if (end > segs.Length)
                {
                    end = segs.Length;
                }

                groups[g] = new SegmentGroup(segs, start, end);
            }

            return groups;
        }

        private sealed class PassData
        {
            public PassData(WaterNode[] path, WaterPool pool)
            {
                Segs = BuildSegmentInfos(path);
                Groups = BuildSegmentGroups(Segs);
                Pool = pool;
            }

            public SegmentInfo[] Segs { get; }
            public SegmentGroup[] Groups { get; }
            public WaterPool Pool { get; }
        }

        private static void CarveWaterways(ValleyGenerationState state)
        {
            var brookPass = new PassData(state.Brook, state.SpringPool);
            var streamPass = new PassData(state.Stream, state.PlungePool);
            int[] height = state.Height;
            int[] wd = state.Wd;

            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                int pz = z * 100;
                int rowOffset = z * ValleyWorld.VertsPerSide;
                for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
                {
                    int idx = rowOffset + x;
                    int px = x * 100;
                    int h = height[idx];

                    int wd1 = CarvePass(px, pz, brookPass, ref h);
                    int wd2 = CarvePass(px, pz, streamPass, ref h);

                    height[idx] = Primitives.Clamp(h, 0, 12000);
                    wd[idx] = wd1 < wd2 ? wd1 : wd2;
                }
            }
        }

        private static int CarvePass(int px, int pz, PassData pass, ref int height)
        {
            WaterPool pool = pass.Pool;
            long pdx = px - pool.XCm;
            long pdz = pz - pool.ZCm;
            int poolE = (int)Primitives.Isqrt(pdx * pdx + pdz * pdz) - pool.RadiusCm;

            var hit = new PassHit { BestE = int.MaxValue, BestLevel = 0, PoolE = poolE };
            SegmentGroup[] groups = pass.Groups;
            for (int g = 0; g < groups.Length; g++)
            {
                SegmentGroup grp = groups[g];
                int cap = hit.BestE < poolE ? hit.BestE : poolE;
                long bound = (long)cap + grp.MaxHw;
                int dx = px < grp.MinX ? grp.MinX - px : (px > grp.MaxX ? px - grp.MaxX : 0);
                int dz = pz < grp.MinZ ? grp.MinZ - pz : (pz > grp.MaxZ ? pz - grp.MaxZ : 0);
                if (bound < 0 || (long)dx * dx + (long)dz * dz >= (bound + 1L) * (bound + 1L))
                {
                    continue;
                }

                EvaluateSegmentRange(px, pz, grp, pass, ref hit);
            }

            if (poolE < hit.BestE)
            {
                hit.BestE = poolE;
                hit.BestLevel = pool.LevelCm;
            }

            int a = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((600 - hit.BestE) * 1024, 400)));
            int c = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((200 - hit.BestE) * 1024, 250)));
            height = height + (((hit.BestLevel + 50 - height) * a) >> 10);
            height = height + (((hit.BestLevel - 70 - height) * c) >> 10);

            return hit.BestE > 0 ? hit.BestE : 0;
        }

        private static void EvaluateSegmentRange(
            int px,
            int pz,
            SegmentGroup grp,
            PassData pass,
            ref PassHit hit)
        {
            SegmentInfo[] segs = pass.Segs;
            for (int i = grp.Start; i < grp.End; i++)
            {
                SegmentInfo s = segs[i];
                if (s.Den == 0)
                {
                    continue;
                }

                int cap = hit.BestE < hit.PoolE ? hit.BestE : hit.PoolE;
                long bound = (long)cap + s.MaxHw;
                int dx = px < s.MinX ? s.MinX - px : (px > s.MaxX ? px - s.MaxX : 0);
                int dz = pz < s.MinZ ? s.MinZ - pz : (pz > s.MaxZ ? pz - s.MaxZ : 0);
                if (bound < 0 || (long)dx * dx + (long)dz * dz >= (bound + 1L) * (bound + 1L))
                {
                    continue;
                }

                long dot = (long)(px - s.AX) * s.AbX + (long)(pz - s.AZ) * s.AbZ;
                int t = Primitives.Clamp01((int)Primitives.FloorDiv(dot * 1024L, s.Den));
                int qx = s.AX + (int)(((long)s.AbX * t) >> 10);
                int qz = s.AZ + (int)(((long)s.AbZ * t) >> 10);
                long distSq = (long)(px - qx) * (px - qx) + (long)(pz - qz) * (pz - qz);
                int delta = (int)Primitives.Isqrt(distSq);
                int hw = s.AHw + (int)(((long)s.DHw * t) >> 10);
                int e = delta - hw;
                if (e < hit.BestE)
                {
                    hit.BestE = e;
                    hit.BestLevel = s.ALvl + (int)(((long)s.DLvl * t) >> 10);
                }
            }
        }

        private static FallNode[] BuildWaterfall(ValleyGenerationState state)
        {
            ValleyLayout layout = state.Layout;
            int zTop = (layout.FallZ + 3) * 100;
            int zBottom = (layout.FallZ - 3) * 100;
            int count = (zTop - zBottom) / 50 + 1;
            var nodes = new FallNode[count];
            int xCm = layout.WfX * 100;

            for (int i = 0; i < count; i++)
            {
                int zCm = zTop - i * 50;
                int yCm;
                if (i == 0)
                {
                    yCm = state.BrookLevel;
                }
                else
                {
                    int h = SampleHeightZ(state.Height, layout.WfX, zCm);
                    yCm = Math.Max(h + 20, state.PlungeLevel);
                }

                nodes[i] = new FallNode(xCm, yCm, zCm);
            }

            return nodes;
        }

        private static int SampleHeightZ(int[] height, int x, int zCm)
        {
            int zm = Primitives.FloorDiv(zCm, 100);
            int rem = zCm - zm * 100;
            int h0 = height[zm * ValleyWorld.VertsPerSide + x];
            if (rem == 0)
            {
                return h0;
            }

            int h1 = height[(zm + 1) * ValleyWorld.VertsPerSide + x];
            return h0 + Primitives.FloorDiv((h1 - h0) * rem, 100);
        }
    }
}
