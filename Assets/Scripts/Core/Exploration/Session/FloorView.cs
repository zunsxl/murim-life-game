using Murim.Core.Valley;

namespace Murim.Core.Exploration.Session
{
    /// <summary>
    /// Tracks the active ground floor slice (0 = Full View, 1-4 = Ground Floors 1-4)
    /// and visibility rules per spec §3.4.
    /// </summary>
    public sealed class FloorView
    {
        public int ViewedFloor { get; private set; }

        public float CutY => ValleyConstants.CutYForFloor(ViewedFloor);

        public FloorView()
        {
            ViewedFloor = 0;
        }

        public int StepDown()
        {
            ViewedFloor = ViewedFloor == 0
                ? ValleyConstants.GroundFloorCount
                : (ViewedFloor > 1 ? ViewedFloor - 1 : 1);
            return ViewedFloor;
        }

        public int StepUp()
        {
            if (ViewedFloor == 0)
            {
                return 0;
            }

            ViewedFloor = ViewedFloor < ValleyConstants.GroundFloorCount
                ? ViewedFloor + 1
                : 0;
            return ViewedFloor;
        }

        public bool IsPlaceholderVisible(int logicalBand)
        {
            return ViewedFloor == 0 || logicalBand <= ViewedFloor;
        }

        public bool EnsureVisible(int logicalBand)
        {
            if (IsPlaceholderVisible(logicalBand))
            {
                return false;
            }

            ViewedFloor = logicalBand <= ValleyConstants.GroundFloorCount ? logicalBand : 0;
            return true;
        }

        public void Reset()
        {
            ViewedFloor = 0;
        }
    }
}
