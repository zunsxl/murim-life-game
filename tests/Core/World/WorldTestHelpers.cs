using System;
using System.Collections.Generic;
using Murim.Core.World;

namespace Murim.Core.Tests.World
{
    internal static class WorldTestHelpers
    {
        public static readonly uint[] SweepSeeds = BuildSweepSeeds();
        private static readonly Lazy<ValleyWorld[]> CachedSweepWorlds = new Lazy<ValleyWorld[]>(BuildSweepWorlds);

        public static ValleyWorld[] GetSweepWorlds()
        {
            return CachedSweepWorlds.Value;
        }

        private static ValleyWorld[] BuildSweepWorlds()
        {
            var worlds = new ValleyWorld[SweepSeeds.Length];
            for (int i = 0; i < SweepSeeds.Length; i++)
            {
                worlds[i] = ValleyGenerator.Generate(SweepSeeds[i], WorldConfig.Default);
            }

            return worlds;
        }

        public static WorldConfig MakeConfig(
            int mid,
            int upper,
            int side,
            int north,
            int tree = 100,
            int rock = 100,
            int flora = 100)
        {
            var values = new WorldConfigValues
            {
                MidTierHeightM = mid,
                UpperTierHeightM = upper,
                SideRidgeHeightM = side,
                NorthRidgeHeightM = north,
                TreeDensityPct = tree,
                RockDensityPct = rock,
                FloraDensityPct = flora,
            };
            if (!WorldConfig.TryCreate(in values, out WorldConfig config, out string failedField))
            {
                throw new InvalidOperationException("Invalid test WorldConfig: " + failedField);
            }

            return config;
        }

        public static int ComputeGradePct(short[] heightCm, int x, int z)
        {
            int xm = x > 0 ? x - 1 : 0;
            int xp = x < ValleyWorld.SizeM ? x + 1 : ValleyWorld.SizeM;
            int zm = z > 0 ? z - 1 : 0;
            int zp = z < ValleyWorld.SizeM ? z + 1 : ValleyWorld.SizeM;

            int gx = heightCm[z * ValleyWorld.VertsPerSide + xp] - heightCm[z * ValleyWorld.VertsPerSide + xm];
            int gz = heightCm[zp * ValleyWorld.VertsPerSide + x] - heightCm[zm * ValleyWorld.VertsPerSide + x];
            return (int)Primitives.FloorDiv(Primitives.Isqrt((long)gx * gx + (long)gz * gz), 2L);
        }

        public static int WaterEdgeDistanceCm(ValleyWorld world, int xCm, int zCm)
        {
            int d1 = PassEdgeDistanceCm(xCm, zCm, world.Brook, world.SpringPool);
            int d2 = PassEdgeDistanceCm(xCm, zCm, world.Stream, world.PlungePool);
            return d1 < d2 ? d1 : d2;
        }

        public static int PassEdgeDistanceCm(int xCm, int zCm, WaterNode[] path, WaterPool pool)
        {
            long pdx = xCm - pool.XCm;
            long pdz = zCm - pool.ZCm;
            int bestE = (int)Primitives.Isqrt(pdx * pdx + pdz * pdz) - pool.RadiusCm;

            for (int i = 0; i < path.Length - 1; i++)
            {
                WaterNode a = path[i];
                WaterNode b = path[i + 1];
                int maxHw = a.HalfWidthCm > b.HalfWidthCm ? a.HalfWidthCm : b.HalfWidthCm;
                long bound = (long)bestE + maxHw;
                int minX = a.XCm < b.XCm ? a.XCm : b.XCm;
                int maxX = a.XCm > b.XCm ? a.XCm : b.XCm;
                int minZ = a.ZCm < b.ZCm ? a.ZCm : b.ZCm;
                int maxZ = a.ZCm > b.ZCm ? a.ZCm : b.ZCm;
                int dx = xCm < minX ? minX - xCm : (xCm > maxX ? xCm - maxX : 0);
                int dz = zCm < minZ ? minZ - zCm : (zCm > maxZ ? zCm - maxZ : 0);
                if (bound < 0 || (long)dx * dx + (long)dz * dz > bound * bound)
                {
                    continue;
                }

                int e = SegmentEdgeDistanceCm(xCm, zCm, a, b, out _);
                if (e < bestE)
                {
                    bestE = e;
                }
            }

            return bestE > 0 ? bestE : 0;
        }

        public static int SegmentEdgeDistanceCm(int xCm, int zCm, WaterNode a, WaterNode b, out int levelCm)
        {
            int abx = b.XCm - a.XCm;
            int abz = b.ZCm - a.ZCm;
            long den = (long)abx * abx + (long)abz * abz;
            if (den == 0)
            {
                levelCm = a.LevelCm;
                return int.MaxValue;
            }

            long dot = (long)(xCm - a.XCm) * abx + (long)(zCm - a.ZCm) * abz;
            int t = Primitives.Clamp01((int)Primitives.FloorDiv(dot * 1024L, den));
            int qx = a.XCm + (int)(((long)abx * t) >> 10);
            int qz = a.ZCm + (int)(((long)abz * t) >> 10);
            int delta = (int)Primitives.Isqrt((long)(xCm - qx) * (xCm - qx) + (long)(zCm - qz) * (zCm - qz));
            int hw = a.HalfWidthCm + (int)(((long)(b.HalfWidthCm - a.HalfWidthCm) * t) >> 10);
            levelCm = a.LevelCm + (int)(((long)(b.LevelCm - a.LevelCm) * t) >> 10);
            return delta - hw;
        }

        public static int Median(List<int> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        private static uint[] BuildSweepSeeds()
        {
            var seeds = new uint[66];
            for (int i = 0; i < 64; i++)
            {
                seeds[i] = (uint)i;
            }

            seeds[64] = 20261127u;
            seeds[65] = 4294967295u;
            return seeds;
        }
    }
}
