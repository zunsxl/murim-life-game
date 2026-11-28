using System.Collections.Generic;

namespace Murim.Core.Tuning
{
    /// <summary>
    /// Validates and clamps tunable configuration files against spec §3.9 bounds.
    /// </summary>
    public static class TuningSanitizer
    {
        public const int SupportedSchemaVersion = 1;
        public const string ExplorationFileName = "exploration.json";

        public static bool CheckSchemaVersion(int schemaVersion, string fileName, ICollection<string> warnings)
        {
            if (schemaVersion == SupportedSchemaVersion)
            {
                return true;
            }
            warnings?.Add($"{fileName}: invalid or unsupported schemaVersion, using defaults.");
            return false;
        }

        public static float CheckRange(
            float value,
            float min,
            float max,
            float defaultValue,
            string fileName,
            string fieldName,
            ICollection<string> warnings)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            {
                warnings?.Add($"{fileName}: field '{fieldName}' is out of range, using default.");
                return defaultValue;
            }
            return value;
        }

        public static ExplorationTuning SanitizeExploration(ExplorationTuningData data, ICollection<string> warnings)
        {
            if (data == null || !CheckSchemaVersion(data.schemaVersion, ExplorationFileName, warnings))
            {
                if (data == null)
                {
                    warnings?.Add($"{ExplorationFileName}: missing or malformed data, using defaults.");
                }
                return ExplorationTuning.Default;
            }

            float walkSpeed = CheckRange(data.walkSpeed, ExplorationTuning.MinWalkSpeed, ExplorationTuning.MaxWalkSpeed, ExplorationTuning.DefaultWalkSpeed, ExplorationFileName, "walkSpeed", warnings);
            float airSpeed = CheckRange(data.airSpeed, ExplorationTuning.MinAirSpeed, ExplorationTuning.MaxAirSpeed, ExplorationTuning.DefaultAirSpeed, ExplorationFileName, "airSpeed", warnings);
            float verticalSpeed = CheckRange(data.verticalSpeed, ExplorationTuning.MinVerticalSpeed, ExplorationTuning.MaxVerticalSpeed, ExplorationTuning.DefaultVerticalSpeed, ExplorationFileName, "verticalSpeed", warnings);
            float defaultOrthoSize = CheckRange(data.defaultOrthoSize, ExplorationTuning.MinDefaultOrthoSize, ExplorationTuning.MaxDefaultOrthoSize, ExplorationTuning.DefaultDefaultOrthoSize, ExplorationFileName, "defaultOrthoSize", warnings);
            float minOrthoSize = CheckRange(data.minOrthoSize, ExplorationTuning.MinMinOrthoSize, ExplorationTuning.MaxMinOrthoSize, ExplorationTuning.DefaultMinOrthoSize, ExplorationFileName, "minOrthoSize", warnings);
            float maxOrthoSize = CheckRange(data.maxOrthoSize, ExplorationTuning.MinMaxOrthoSize, ExplorationTuning.MaxMaxOrthoSize, ExplorationTuning.DefaultMaxOrthoSize, ExplorationFileName, "maxOrthoSize", warnings);
            float zoomStep = CheckRange(data.zoomStep, ExplorationTuning.MinZoomStep, ExplorationTuning.MaxZoomStep, ExplorationTuning.DefaultZoomStep, ExplorationFileName, "zoomStep", warnings);
            float panSpeed = CheckRange(data.panSpeed, ExplorationTuning.MinPanSpeed, ExplorationTuning.MaxPanSpeed, ExplorationTuning.DefaultPanSpeed, ExplorationFileName, "panSpeed", warnings);
            float defaultFocusX = CheckRange(data.defaultFocusX, ExplorationTuning.MinDefaultFocusX, ExplorationTuning.MaxDefaultFocusX, ExplorationTuning.DefaultDefaultFocusX, ExplorationFileName, "defaultFocusX", warnings);
            float defaultFocusZ = CheckRange(data.defaultFocusZ, ExplorationTuning.MinDefaultFocusZ, ExplorationTuning.MaxDefaultFocusZ, ExplorationTuning.DefaultDefaultFocusZ, ExplorationFileName, "defaultFocusZ", warnings);

            if (!(minOrthoSize < defaultOrthoSize && defaultOrthoSize < maxOrthoSize))
            {
                warnings?.Add($"{ExplorationFileName}: minOrthoSize < defaultOrthoSize < maxOrthoSize violated, resetting zoom bounds to defaults.");
                minOrthoSize = ExplorationTuning.DefaultMinOrthoSize;
                defaultOrthoSize = ExplorationTuning.DefaultDefaultOrthoSize;
                maxOrthoSize = ExplorationTuning.DefaultMaxOrthoSize;
            }

            return new ExplorationTuning(
                walkSpeed, airSpeed, verticalSpeed,
                defaultOrthoSize, minOrthoSize, maxOrthoSize,
                zoomStep, panSpeed, defaultFocusX, defaultFocusZ);
        }
    }
}
