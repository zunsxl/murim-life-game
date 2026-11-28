namespace Murim.Core.Tuning
{
    /// <summary>
    /// Sanitized exploration tuning values and spec §3.9 defaults/ranges.
    /// </summary>
    public sealed class ExplorationTuning
    {
        public const float DefaultWalkSpeed = 5.0f;
        public const float MinWalkSpeed = 1f;
        public const float MaxWalkSpeed = 12f;

        public const float DefaultAirSpeed = 8.0f;
        public const float MinAirSpeed = 1f;
        public const float MaxAirSpeed = 20f;

        public const float DefaultVerticalSpeed = 8.0f;
        public const float MinVerticalSpeed = 2f;
        public const float MaxVerticalSpeed = 30f;

        public const float DefaultDefaultOrthoSize = 11.25f;
        public const float MinDefaultOrthoSize = 4f;
        public const float MaxDefaultOrthoSize = 30f;

        public const float DefaultMinOrthoSize = 4.5f;
        public const float MinMinOrthoSize = 2f;
        public const float MaxMinOrthoSize = 10f;

        public const float DefaultMaxOrthoSize = 28.125f;
        public const float MinMaxOrthoSize = 12f;
        public const float MaxMaxOrthoSize = 34f;

        public const float DefaultZoomStep = 1.1f;
        public const float MinZoomStep = 1.02f;
        public const float MaxZoomStep = 1.5f;

        public const float DefaultPanSpeed = 1.2f;
        public const float MinPanSpeed = 0.2f;
        public const float MaxPanSpeed = 4f;

        public const float DefaultDefaultFocusX = 70f;
        public const float MinDefaultFocusX = 1f;
        public const float MaxDefaultFocusX = 159f;

        public const float DefaultDefaultFocusZ = 29f;
        public const float MinDefaultFocusZ = 1f;
        public const float MaxDefaultFocusZ = 95f;

        public static readonly ExplorationTuning Default = new ExplorationTuning(
            DefaultWalkSpeed,
            DefaultAirSpeed,
            DefaultVerticalSpeed,
            DefaultDefaultOrthoSize,
            DefaultMinOrthoSize,
            DefaultMaxOrthoSize,
            DefaultZoomStep,
            DefaultPanSpeed,
            DefaultDefaultFocusX,
            DefaultDefaultFocusZ);

        public float WalkSpeed { get; }
        public float AirSpeed { get; }
        public float VerticalSpeed { get; }
        public float DefaultOrthoSize { get; }
        public float MinOrthoSize { get; }
        public float MaxOrthoSize { get; }
        public float ZoomStep { get; }
        public float PanSpeed { get; }
        public float DefaultFocusX { get; }
        public float DefaultFocusZ { get; }

        public ExplorationTuning(
            float walkSpeed,
            float airSpeed,
            float verticalSpeed,
            float defaultOrthoSize,
            float minOrthoSize,
            float maxOrthoSize,
            float zoomStep,
            float panSpeed,
            float defaultFocusX,
            float defaultFocusZ)
        {
            WalkSpeed = walkSpeed;
            AirSpeed = airSpeed;
            VerticalSpeed = verticalSpeed;
            DefaultOrthoSize = defaultOrthoSize;
            MinOrthoSize = minOrthoSize;
            MaxOrthoSize = maxOrthoSize;
            ZoomStep = zoomStep;
            PanSpeed = panSpeed;
            DefaultFocusX = defaultFocusX;
            DefaultFocusZ = defaultFocusZ;
        }
    }
}
