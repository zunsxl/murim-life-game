using System;

namespace Murim.Core.World
{
    /// <summary>
    /// Constructive integer-only mountain valley generator (§3.1–§3.2).
    /// </summary>
    public static class ValleyGenerator
    {
        public static ValleyWorld Generate(uint seed, WorldConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var state = new ValleyGenerationState(seed, config);
            DrawLayout(state);
            ComputeTierAndHeights(state);
            ComputeSlopeSites(state);
            ValleyWater.BuildAndCarve(state);
            ValleySurface.ComputeSurfaceWeights(state);
            ValleySurface.ComputeViewFrame(state);
            ValleyPlacement.PlaceProps(state);

            return AssembleWorld(state);
        }

        private static void DrawLayout(ValleyGenerationState state)
        {
            var rng = new Pcg32(state.Seed, 1UL);
            ValleyLayout l = state.Layout;
            l.AxisX = 256 + rng.R(-40, 40);
            l.HeadZ = rng.R(196, 228);
            l.MidDepth = rng.R(96, 116);
            l.BowK = rng.R(150, 190);
            l.WfX = l.AxisX + rng.R(-12, 12);
            int side = rng.R(0, 1) * 2 - 1;
            l.Slope1X = Primitives.Clamp(l.AxisX + side * rng.R(72, 124), 112, 400);
            l.Slope2X = Primitives.Clamp(l.AxisX - side * rng.R(72, 132), 112, 400);
            l.ExitX = l.AxisX + rng.R(-60, 60);
            for (int k = 0; k < 5; k++)
            {
                state.Off[k] = rng.R(-22, 22);
            }

            l.SpringX = l.WfX + rng.R(-8, 8);
        }

        private sealed class ColumnBuffers
        {
            public readonly int[] P = new int[ValleyWorld.VertsPerSide];
            public readonly long[] G = new long[ValleyWorld.VertsPerSide];
            public readonly int[] Hwv1 = new int[ValleyWorld.VertsPerSide];
            public readonly int[] Hwv2 = new int[ValleyWorld.VertsPerSide];
            public readonly int[] TW = new int[ValleyWorld.VertsPerSide];
            public readonly int[] TE = new int[ValleyWorld.VertsPerSide];
        }

        private static void ComputeTierAndHeights(ValleyGenerationState state)
        {
            var cols = new ColumnBuffers();
            PrecomputeColumns(state, cols);
            PrecomputeEastWestT(cols);

            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                ComputeRow(state, cols, z);
            }
        }

        private static void PrecomputeColumns(ValleyGenerationState state, ColumnBuffers cols)
        {
            ValleyLayout l = state.Layout;
            int mid = state.Config.MidTierHeightM * 100;
            int upper = state.Config.UpperTierHeightM * 100;
            int midHalfWidthTarget = Primitives.FloorDiv(mid * 5, 2);
            int upperHalfWidthTarget = Primitives.FloorDiv((upper - mid) * 5, 2);

            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                int dx = x - l.AxisX;
                int absDx = dx < 0 ? -dx : dx;
                cols.P[x] = (int)Primitives.FloorDiv((long)l.BowK * 100L * dx * dx, 30976L);
                long slopeQ = Primitives.FloorDiv(2L * l.BowK * absDx * 1024L, 30976L);
                long g = Primitives.Isqrt(1048576L + slopeQ * slopeQ);
                cols.G[x] = g;

                int b1 = Primitives.Bump(Math.Abs(x - l.Slope1X), 22, 48);
                int b2 = Primitives.Bump(Math.Abs(x - l.Slope2X), 22, 48);
                int wf = Primitives.Bump(Math.Abs(x - l.WfX), 4, 12);
                state.B1[x] = (short)b1;
                state.B2[x] = (short)b2;

                int hw1 = 450 + (((midHalfWidthTarget - 450) * b1) >> 10);
                hw1 = hw1 + (((250 - hw1) * wf) >> 10);
                int hw2 = 500 + (((upperHalfWidthTarget - 500) * b2) >> 10);
                state.Hw1[x] = (short)hw1;
                state.Hw2[x] = (short)hw2;

                cols.Hwv1[x] = (int)((hw1 * g) >> 10);
                cols.Hwv2[x] = (int)((hw2 * g) >> 10);
            }
        }

        private static void PrecomputeEastWestT(ColumnBuffers cols)
        {
            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                cols.TW[x] = Primitives.Clamp01(Primitives.FloorDiv((72 - x) * 1024, 72));
                cols.TE[x] = Primitives.Clamp01(Primitives.FloorDiv((x - 440) * 1024, 72));
            }
        }

        private readonly struct RowContext
        {
            public RowContext(ValleyGenerationState state, int z)
            {
                Z = z;
                RowOffset = z * ValleyWorld.VertsPerSide;
                FS = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((z - 8) * 1024, 48)));
                Tilt = 200 + Primitives.FloorDiv(150 * Math.Min(z, state.Layout.HeadZ), state.Layout.HeadZ);
                TN = Primitives.Clamp01(Primitives.FloorDiv((z - 432) * 1024, 80));
                PN = RidgeCurve(TN);
            }

            public int Z { get; }
            public int RowOffset { get; }
            public int FS { get; }
            public int Tilt { get; }
            public int TN { get; }
            public int PN { get; }
        }

        private static void ComputeRow(ValleyGenerationState state, ColumnBuffers cols, int z)
        {
            var row = new RowContext(state, z);
            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                ComputeVertex(state, cols, in row, x);
            }
        }

        private static void ComputeVertex(
            ValleyGenerationState state,
            ColumnBuffers cols,
            in RowContext row,
            int x)
        {
            ValleyLayout l = state.Layout;
            int idx = row.RowOffset + x;
            int wob = ((Primitives.Noise(x, row.Z, 64, state.Salts[1]) * 1200) >> 10)
                + ((Primitives.Noise(x, row.Z, 24, state.Salts[2]) * 250) >> 10);
            int v = 100 * row.Z + cols.P[x] + wob;
            int v1 = l.HeadZ * 100;
            int v2 = (l.HeadZ + l.MidDepth) * 100;
            long g = cols.G[x];

            int s1 = Primitives.S(Primitives.Clamp01((int)Primitives.FloorDiv((long)(v - v1 + cols.Hwv1[x]) * 1024L, 2L * cols.Hwv1[x])));
            int s2 = Primitives.S(Primitives.Clamp01((int)Primitives.FloorDiv((long)(v - v2 + cols.Hwv2[x]) * 1024L, 2L * cols.Hwv2[x])));
            state.S1[idx] = (short)s1;
            state.S2[idx] = (short)s2;
            state.E1[idx] = (int)Primitives.FloorDiv((long)(v - v1) * 1024L, g);
            state.E2[idx] = (int)Primitives.FloorDiv((long)(v - v2) * 1024L, g);

            int lVal = s1 + s2;
            state.L[idx] = (short)lVal;
            state.TierLevelPct[idx] = (byte)Primitives.FloorDiv(lVal * 100, 1024);

            int mid = state.Config.MidTierHeightM * 100;
            int upper = state.Config.UpperTierHeightM * 100;
            int ht = ((s1 * mid) >> 10) + ((s2 * (upper - mid)) >> 10);
            int ridge = ComputeRidge(state, cols, in row, x, idx);

            int basin = row.Tilt + ((Primitives.Noise(x, row.Z, 48, state.Salts[3]) * 60) >> 10)
                + ((Primitives.Noise(x, row.Z, 16, state.Salts[4]) * 25) >> 10);
            int und = ((Primitives.Noise(x, row.Z, 40, state.Salts[5]) * 70) >> 10)
                + ((Primitives.Noise(x, row.Z, 8, state.Salts[6]) * 15) >> 10);
            int minL = lVal < 1024 ? lVal : 1024;
            state.Height[idx] = basin + ((row.FS * (ht + ridge + ((und * minL) >> 10))) >> 10);
        }

        private static int ComputeRidge(
            ValleyGenerationState state,
            ColumnBuffers cols,
            in RowContext row,
            int x,
            int idx)
        {
            int tW = cols.TW[x];
            int tE = cols.TE[x];
            int rt = Math.Max(tW, Math.Max(tE, row.TN));
            state.Rt[idx] = (short)rt;
            state.RidgePct[idx] = (byte)Primitives.FloorDiv(rt * 100, 1024);

            int sideRidge = state.Config.SideRidgeHeightM * 100;
            int northRidge = state.Config.NorthRidgeHeightM * 100;
            int ridgeBase = Math.Max(sideRidge * RidgeCurve(tW), Math.Max(sideRidge * RidgeCurve(tE), northRidge * row.PN));
            int rough = ((Primitives.Noise(x, row.Z, 32, state.Salts[7]) * 250) >> 10)
                + ((Primitives.Noise(x, row.Z, 12, state.Salts[8]) * 60) >> 10);
            return Primitives.FloorDiv(ridgeBase, 1024) + ((rough * RidgeCurve(rt)) >> 10);
        }

        private static int RidgeCurve(int t)
        {
            return Primitives.FloorDiv(t * (t + 1024), 2048);
        }

        private static void ComputeSlopeSites(ValleyGenerationState state)
        {
            ValleyLayout l = state.Layout;
            int zBot1 = Primitives.FindFirstZAtLeast(state.S1, l.Slope1X, 16);
            int zTop1 = Primitives.FindFirstZAtLeast(state.S1, l.Slope1X, 1008);
            int zBot2 = Primitives.FindFirstZAtLeast(state.S2, l.Slope2X, 16);
            int zTop2 = Primitives.FindFirstZAtLeast(state.S2, l.Slope2X, 1008);

            state.Slopes = new[]
            {
                new SlopeSite(l.Slope1X * 100, zBot1 * 100, l.Slope1X * 100, zTop1 * 100),
                new SlopeSite(l.Slope2X * 100, zBot2 * 100, l.Slope2X * 100, zTop2 * 100),
            };
        }

        private static ValleyWorld AssembleWorld(ValleyGenerationState state)
        {
            var heightCm = new short[ValleyWorld.VertexCount];
            for (int i = 0; i < heightCm.Length; i++)
            {
                heightCm[i] = (short)state.Height[i];
            }

            var world = new ValleyWorld
            {
                Seed = state.Seed,
                Config = state.Config,
                HeightCm = Array.AsReadOnly(heightCm),
                TierLevelPct = Array.AsReadOnly(state.TierLevelPct),
                RidgePct = Array.AsReadOnly(state.RidgePct),
                SurfaceWeights = Array.AsReadOnly(state.SurfaceWeights),
                Stream = Array.AsReadOnly(state.Stream),
                Brook = Array.AsReadOnly(state.Brook),
                PlungePool = state.PlungePool,
                SpringPool = state.SpringPool,
                Waterfall = Array.AsReadOnly(state.Waterfall),
                Slopes = Array.AsReadOnly(state.Slopes),
                Layout = state.Layout,
                Props = Array.AsReadOnly(state.Props),
                PlaneBottomCm = state.PlaneBottomCm,
                PlaneCrestCm = state.PlaneCrestCm,
                VistaUCm = state.VistaUCm,
                VistaVCm = state.VistaVCm,
            };

            world.Hash = ValleyHash.Compute(world);
            return world;
        }
    }
}
