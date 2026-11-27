namespace Murim.Core.World
{
    /// <summary>
    /// Fixed 3-4-5 oblique screen-plane projection and baked relief shade (§2.1, §3.1.7).
    /// </summary>
    public static class Projection
    {
        public const int PixelsPerMetre = 128;

        public static int PlaneVCm(int zCm, int yCm)
        {
            return (int)Primitives.FloorDiv(3L * zCm + 4L * yCm, 5L);
        }

        public static int ReliefQ(int gx, int gz)
        {
            int raw = Primitives.Clamp(3 * gx - 2 * gz, -180, 180);
            return 1024 + raw;
        }
    }
}
