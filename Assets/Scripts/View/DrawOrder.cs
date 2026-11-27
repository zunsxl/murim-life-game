namespace Murim.View
{
    /// <summary>
    /// Fixed 2D sorting order and layer index constants on the default sorting layer (§2.4).
    /// </summary>
    public static class DrawOrder
    {
        public const int WorldLayer = 0;
        public const int UiLayer = 5;

        public const int Backdrop = -32000;
        public const int TerrainBase = -31000;
        public const int Water = -30000;
        public const int Waterfall = -29999;
        public const int WaterFoam = -29998;
        public const int Lips = -29000;
        public const int Shadows = -28000;
        public const int Props = -16000;

        public static int TerrainChunk(int chunkRow)
        {
            return TerrainBase + (7 - chunkRow);
        }
    }
}
