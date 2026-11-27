namespace Murim.Core.World
{
    /// <summary>
    /// Internal working arrays and salts for a single <see cref="ValleyGenerator.Generate"/> invocation.
    /// </summary>
    internal sealed class ValleyGenerationState
    {
        public readonly uint Seed;
        public readonly WorldConfig Config;
        public readonly ValleyLayout Layout = new ValleyLayout();
        public readonly int[] Off = new int[5];

        public readonly int[] Height = new int[ValleyWorld.VertexCount];
        public readonly byte[] TierLevelPct = new byte[ValleyWorld.VertexCount];
        public readonly byte[] RidgePct = new byte[ValleyWorld.VertexCount];
        public readonly byte[] SurfaceWeights = new byte[ValleyWorld.VertexCount * ValleyWorld.SurfaceLayerCount];

        public readonly short[] S1 = new short[ValleyWorld.VertexCount];
        public readonly short[] S2 = new short[ValleyWorld.VertexCount];
        public readonly short[] L = new short[ValleyWorld.VertexCount];
        public readonly short[] Rt = new short[ValleyWorld.VertexCount];
        public readonly int[] E1 = new int[ValleyWorld.VertexCount];
        public readonly int[] E2 = new int[ValleyWorld.VertexCount];

        public readonly short[] B1 = new short[ValleyWorld.VertsPerSide];
        public readonly short[] B2 = new short[ValleyWorld.VertsPerSide];
        public readonly short[] Hw1 = new short[ValleyWorld.VertsPerSide];
        public readonly short[] Hw2 = new short[ValleyWorld.VertsPerSide];

        public readonly int[] Wd = new int[ValleyWorld.VertexCount];
        public readonly int[] Grade = new int[ValleyWorld.VertexCount];
        public readonly short[] Eb = new short[ValleyWorld.VertexCount];

        public readonly uint[] Salts = new uint[14];

        public WaterNode[] Brook;
        public WaterNode[] Stream;
        public WaterPool SpringPool;
        public WaterPool PlungePool;
        public FallNode[] Waterfall;
        public SlopeSite[] Slopes;
        public PropInstance[] Props;

        public int BrookLevel;
        public int PlungeLevel;
        public int PlaneBottomCm;
        public int PlaneCrestCm;
        public int VistaUCm;
        public int VistaVCm;

        public ValleyGenerationState(uint seed, WorldConfig config)
        {
            Seed = seed;
            Config = config;
            for (int k = 1; k <= 13; k++)
            {
                Salts[k] = Primitives.Salt(k, seed);
            }
        }
    }
}
