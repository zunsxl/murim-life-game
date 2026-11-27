namespace Murim.Core.Camera
{
    /// <summary>
    /// Published 2D camera pose on the oblique screen plane (<c>(Cu, Cv, H)</c> only).
    /// </summary>
    public readonly struct CameraPose
    {
        public CameraPose(float cu, float cv, float h)
        {
            Cu = cu;
            Cv = cv;
            H = h;
        }

        public float Cu { get; }
        public float Cv { get; }
        public float H { get; }

        public float ViewWidth(float aspect)
        {
            return H * aspect;
        }

        public (float U, float V) ScreenToPlane(float aspect, float sx, float sy)
        {
            float w = H * aspect;
            return (Cu + sx * (w * 0.5f), Cv + sy * (H * 0.5f));
        }
    }
}
