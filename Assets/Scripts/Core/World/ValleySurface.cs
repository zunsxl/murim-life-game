using System;

namespace Murim.Core.World
{
    /// <summary>
    /// Surface layer weights (§3.1.6) and screen-plane view frame (§3.1.7).
    /// </summary>
    internal static class ValleySurface
    {
        public static void ComputeSurfaceWeights(ValleyGenerationState state)
        {
            int[] w = new int[ValleyWorld.SurfaceLayerCount];
            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                int rowOffset = z * ValleyWorld.VertsPerSide;
                for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
                {
                    ComputeVertexSurface(state, x, z, rowOffset + x, w);
                }
            }
        }

        public static void ComputeViewFrame(ValleyGenerationState state)
        {
            int bottom = int.MinValue;
            int crest = int.MaxValue;
            int northRowOffset = ValleyWorld.SizeM * ValleyWorld.VertsPerSide;

            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                int vSouth = Projection.PlaneVCm(0, state.Height[x]);
                if (vSouth > bottom)
                {
                    bottom = vSouth;
                }

                int vNorth = Projection.PlaneVCm(51200, state.Height[northRowOffset + x]);
                if (vNorth < crest)
                {
                    crest = vNorth;
                }
            }

            state.PlaneBottomCm = bottom;
            state.PlaneCrestCm = crest;
            state.VistaUCm = state.Layout.WfX * 100;
            state.VistaVCm = Projection.PlaneVCm((state.Layout.FallZ + 3) * 100, state.BrookLevel) - 440;
        }

        private static void ComputeVertexSurface(ValleyGenerationState state, int x, int z, int idx, int[] w)
        {
            int g = ComputeGradePct(state.Height, x, z);
            state.Grade[idx] = g;

            int eb = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((Primitives.Noise(x, z, 56, state.Salts[9]) + 256) * 1024, 512)));
            state.Eb[idx] = (short)eb;

            int b = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((g - 45) * 1024, 45)));
            int so = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((Primitives.Noise(x, z, 44, state.Salts[11]) - 200) * 1024, 360)));
            int st = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((Primitives.Noise(x, z, 36, state.Salts[10]) - 380) * 1024, 260)));
            int hl = ComputeHillsideOverlay(state, x, idx, g);
            int r = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((state.Rt[idx] - 150) * 1024, 350)));
            int wt = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((350 - state.Wd[idx]) * 1024, 300)));

            w[0] = 1024;
            for (int k = 1; k < ValleyWorld.SurfaceLayerCount; k++)
            {
                w[k] = 0;
            }

            ApplyOverlay(w, 1, so);
            ApplyOverlay(w, 2, st);
            ApplyOverlay(w, 6, hl);
            ApplyOverlay(w, 7, r);
            ApplyOverlay(w, 4, (b * (1024 - eb)) >> 10);
            ApplyOverlay(w, 5, (b * eb) >> 10);
            ApplyOverlay(w, 3, wt);

            WriteNormalizedBytes(w, state.SurfaceWeights, idx * ValleyWorld.SurfaceLayerCount);
        }

        private static int ComputeGradePct(int[] height, int x, int z)
        {
            int xm = x > 0 ? x - 1 : 0;
            int xp = x < ValleyWorld.SizeM ? x + 1 : ValleyWorld.SizeM;
            int zm = z > 0 ? z - 1 : 0;
            int zp = z < ValleyWorld.SizeM ? z + 1 : ValleyWorld.SizeM;

            int rowZ = z * ValleyWorld.VertsPerSide;
            int gx = height[rowZ + xp] - height[rowZ + xm];
            int gz = height[zp * ValleyWorld.VertsPerSide + x] - height[zm * ValleyWorld.VertsPerSide + x];
            return (int)Primitives.FloorDiv(Primitives.Isqrt((long)gx * gx + (long)gz * gz), 2L);
        }

        private static int ComputeHillsideOverlay(ValleyGenerationState state, int x, int idx, int g)
        {
            int m = Primitives.S(Primitives.Clamp01(Primitives.FloorDiv((g - 15) * 1024, 30)));
            int s1 = state.S1[idx];
            int s2 = state.S2[idx];
            int site1 = (state.B1[x] * ((4 * s1 * (1024 - s1)) >> 10)) >> 10;
            int site2 = (state.B2[x] * ((4 * s2 * (1024 - s2)) >> 10)) >> 10;
            int slope = (m * 614) >> 10;
            return Math.Max(site1, Math.Max(site2, slope));
        }

        private static void ApplyOverlay(int[] w, int layer, int alpha)
        {
            int inv = 1024 - alpha;
            for (int k = 0; k < ValleyWorld.SurfaceLayerCount; k++)
            {
                w[k] = (w[k] * inv) >> 10;
            }

            w[layer] += alpha;
        }

        private static void WriteNormalizedBytes(int[] w, byte[] dst, int offset)
        {
            int sum = 0;
            int bestIdx = 0;
            for (int k = 0; k < ValleyWorld.SurfaceLayerCount; k++)
            {
                sum += w[k];
                if (w[k] > w[bestIdx])
                {
                    bestIdx = k;
                }
            }

            int totalBytes = 0;
            for (int k = 0; k < ValleyWorld.SurfaceLayerCount; k++)
            {
                int b = Primitives.FloorDiv(w[k] * 255, sum);
                dst[offset + k] = (byte)b;
                totalBytes += b;
            }

            dst[offset + bestIdx] = (byte)(dst[offset + bestIdx] + (255 - totalBytes));
        }
    }
}
