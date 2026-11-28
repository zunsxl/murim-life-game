using System;

namespace Murim.Core.Tuning
{
    /// <summary>
    /// Plain JSON deserialization DTO for exploration.json with no field initializers.
    /// </summary>
    [Serializable]
    public sealed class ExplorationTuningData
    {
        public int schemaVersion;
        public float walkSpeed;
        public float airSpeed;
        public float verticalSpeed;
        public float defaultOrthoSize;
        public float minOrthoSize;
        public float maxOrthoSize;
        public float zoomStep;
        public float panSpeed;
        public float defaultFocusX;
        public float defaultFocusZ;
    }
}
