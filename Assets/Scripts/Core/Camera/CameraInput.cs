namespace Murim.Core.Camera
{
    /// <summary>
    /// Per-frame device inputs and viewport state consumed by <see cref="CameraRig.Tick"/>.
    /// </summary>
    public struct CameraInput
    {
        public bool PanNorth;
        public bool PanSouth;
        public bool PanWest;
        public bool PanEast;
        public bool DragHeld;
        public bool ResetPressed;
        public float ScrollNotches;
        public float PointerNdcX;
        public float PointerNdcY;
        public bool PointerInWindow;
        public float Aspect;
        public int ScreenHeightPx;
        public bool Blocked;
    }
}
