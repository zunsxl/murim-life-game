namespace Murim.Core.Camera
{
    /// <summary>
    /// Screen-plane valley bounds and vista coordinates needed by <see cref="CameraRig"/>.
    /// </summary>
    public readonly struct ViewFrame
    {
        public const float WorldWidthM = 512f;
        public const float SkyFraction = 0.30f;
        public const float MaxPixelsPerMetre = 160f;

        public ViewFrame(int planeBottomCm, int planeCrestCm, int vistaUCm, int vistaVCm)
        {
            BottomV = planeBottomCm / 100f;
            CrestV = planeCrestCm / 100f;
            VistaU = vistaUCm / 100f;
            VistaV = vistaVCm / 100f;
        }

        public float BottomV { get; }
        public float CrestV { get; }
        public float VistaU { get; }
        public float VistaV { get; }
    }
}
