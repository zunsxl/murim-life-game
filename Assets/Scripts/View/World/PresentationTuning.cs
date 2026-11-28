using System.Collections.Generic;
using Murim.Core.Tuning;

namespace Murim.View.World
{
    /// <summary>
    /// Sanitized presentation tuning values and spec §3.9 defaults/ranges.
    /// </summary>
    public sealed class PresentationTuning
    {
        public const string PresentationFileName = "presentation.json";

        public const float DefaultLabelVisibleSeconds = 1.5f;
        public const float MinLabelVisibleSeconds = 0.2f;
        public const float MaxLabelVisibleSeconds = 5f;

        public const float DefaultLabelFadeSeconds = 0.5f;
        public const float MinLabelFadeSeconds = 0.1f;
        public const float MaxLabelFadeSeconds = 2f;

        public const float DefaultMarkerVisibleSeconds = 0.8f;
        public const float MinMarkerVisibleSeconds = 0.2f;
        public const float MaxMarkerVisibleSeconds = 5f;

        public const float DefaultMarkerFadeSeconds = 0.4f;
        public const float MinMarkerFadeSeconds = 0.1f;
        public const float MaxMarkerFadeSeconds = 2f;

        public const float DefaultCutFadeSeconds = 0.25f;
        public const float MinCutFadeSeconds = 0.05f;
        public const float MaxCutFadeSeconds = 1f;

        public const float DefaultRiverFlowSpeed = 1.2f;
        public const float DefaultWaterfallSpeed = 3.0f;
        public const float MinWaterSpeed = 0.1f;
        public const float MaxWaterSpeed = 6f;

        public const float DefaultBambooSwayAmplitude = 0.12f;
        public const float DefaultBambooSwayPeriod = 3.5f;
        public const float DefaultPineSwayAmplitude = 0.05f;
        public const float DefaultPineSwayPeriod = 4.5f;
        public const float DefaultGrassSwayAmplitude = 0.04f;
        public const float DefaultGrassSwayPeriod = 2.2f;
        public const float DefaultFlowerSwayAmplitude = 0.03f;
        public const float DefaultFlowerSwayPeriod = 2.0f;

        public const float MinSwayAmplitude = 0.01f;
        public const float MaxSwayAmplitude = 0.4f;
        public const float MinSwayPeriod = 1f;
        public const float MaxSwayPeriod = 10f;

        public static readonly PresentationTuning Default = new PresentationTuning(CreateDefaultData());

        public float LabelVisibleSeconds { get; }
        public float LabelFadeSeconds { get; }
        public float MarkerVisibleSeconds { get; }
        public float MarkerFadeSeconds { get; }
        public float CutFadeSeconds { get; }
        public float RiverFlowSpeed { get; }
        public float WaterfallSpeed { get; }
        public float BambooSwayAmplitude { get; }
        public float BambooSwayPeriod { get; }
        public float PineSwayAmplitude { get; }
        public float PineSwayPeriod { get; }
        public float GrassSwayAmplitude { get; }
        public float GrassSwayPeriod { get; }
        public float FlowerSwayAmplitude { get; }
        public float FlowerSwayPeriod { get; }

        private PresentationTuning(PresentationTuningData sanitized)
        {
            LabelVisibleSeconds = sanitized.labelVisibleSeconds;
            LabelFadeSeconds = sanitized.labelFadeSeconds;
            MarkerVisibleSeconds = sanitized.markerVisibleSeconds;
            MarkerFadeSeconds = sanitized.markerFadeSeconds;
            CutFadeSeconds = sanitized.cutFadeSeconds;
            RiverFlowSpeed = sanitized.riverFlowSpeed;
            WaterfallSpeed = sanitized.waterfallSpeed;
            BambooSwayAmplitude = sanitized.bambooSwayAmplitude;
            BambooSwayPeriod = sanitized.bambooSwayPeriod;
            PineSwayAmplitude = sanitized.pineSwayAmplitude;
            PineSwayPeriod = sanitized.pineSwayPeriod;
            GrassSwayAmplitude = sanitized.grassSwayAmplitude;
            GrassSwayPeriod = sanitized.grassSwayPeriod;
            FlowerSwayAmplitude = sanitized.flowerSwayAmplitude;
            FlowerSwayPeriod = sanitized.flowerSwayPeriod;
        }

        public static PresentationTuning Sanitize(PresentationTuningData data, ICollection<string> warnings)
        {
            if (data == null)
            {
                warnings?.Add($"{PresentationFileName}: missing or malformed data, using defaults.");
                return Default;
            }

            if (!TuningSanitizer.CheckSchemaVersion(data.schemaVersion, PresentationFileName, warnings))
            {
                return Default;
            }

            PresentationTuningData clean = new PresentationTuningData();
            clean.schemaVersion = TuningSanitizer.SupportedSchemaVersion;
            SanitizeTimers(data, clean, warnings);
            SanitizeWater(data, clean, warnings);
            SanitizeSway(data, clean, warnings);
            return new PresentationTuning(clean);
        }

        private static void SanitizeTimers(
            PresentationTuningData source,
            PresentationTuningData target,
            ICollection<string> warnings)
        {
            target.labelVisibleSeconds = Check("labelVisibleSeconds", source.labelVisibleSeconds, MinLabelVisibleSeconds, MaxLabelVisibleSeconds, DefaultLabelVisibleSeconds, warnings);
            target.labelFadeSeconds = Check("labelFadeSeconds", source.labelFadeSeconds, MinLabelFadeSeconds, MaxLabelFadeSeconds, DefaultLabelFadeSeconds, warnings);
            target.markerVisibleSeconds = Check("markerVisibleSeconds", source.markerVisibleSeconds, MinMarkerVisibleSeconds, MaxMarkerVisibleSeconds, DefaultMarkerVisibleSeconds, warnings);
            target.markerFadeSeconds = Check("markerFadeSeconds", source.markerFadeSeconds, MinMarkerFadeSeconds, MaxMarkerFadeSeconds, DefaultMarkerFadeSeconds, warnings);
            target.cutFadeSeconds = Check("cutFadeSeconds", source.cutFadeSeconds, MinCutFadeSeconds, MaxCutFadeSeconds, DefaultCutFadeSeconds, warnings);
        }

        private static void SanitizeWater(
            PresentationTuningData source,
            PresentationTuningData target,
            ICollection<string> warnings)
        {
            target.riverFlowSpeed = Check("riverFlowSpeed", source.riverFlowSpeed, MinWaterSpeed, MaxWaterSpeed, DefaultRiverFlowSpeed, warnings);
            target.waterfallSpeed = Check("waterfallSpeed", source.waterfallSpeed, MinWaterSpeed, MaxWaterSpeed, DefaultWaterfallSpeed, warnings);
        }

        private static void SanitizeSway(
            PresentationTuningData source,
            PresentationTuningData target,
            ICollection<string> warnings)
        {
            target.bambooSwayAmplitude = Check("bambooSwayAmplitude", source.bambooSwayAmplitude, MinSwayAmplitude, MaxSwayAmplitude, DefaultBambooSwayAmplitude, warnings);
            target.bambooSwayPeriod = Check("bambooSwayPeriod", source.bambooSwayPeriod, MinSwayPeriod, MaxSwayPeriod, DefaultBambooSwayPeriod, warnings);
            target.pineSwayAmplitude = Check("pineSwayAmplitude", source.pineSwayAmplitude, MinSwayAmplitude, MaxSwayAmplitude, DefaultPineSwayAmplitude, warnings);
            target.pineSwayPeriod = Check("pineSwayPeriod", source.pineSwayPeriod, MinSwayPeriod, MaxSwayPeriod, DefaultPineSwayPeriod, warnings);
            target.grassSwayAmplitude = Check("grassSwayAmplitude", source.grassSwayAmplitude, MinSwayAmplitude, MaxSwayAmplitude, DefaultGrassSwayAmplitude, warnings);
            target.grassSwayPeriod = Check("grassSwayPeriod", source.grassSwayPeriod, MinSwayPeriod, MaxSwayPeriod, DefaultGrassSwayPeriod, warnings);
            target.flowerSwayAmplitude = Check("flowerSwayAmplitude", source.flowerSwayAmplitude, MinSwayAmplitude, MaxSwayAmplitude, DefaultFlowerSwayAmplitude, warnings);
            target.flowerSwayPeriod = Check("flowerSwayPeriod", source.flowerSwayPeriod, MinSwayPeriod, MaxSwayPeriod, DefaultFlowerSwayPeriod, warnings);
        }

        private static float Check(
            string fieldName,
            float value,
            float min,
            float max,
            float defaultValue,
            ICollection<string> warnings)
        {
            return TuningSanitizer.CheckRange(value, min, max, defaultValue, PresentationFileName, fieldName, warnings);
        }

        private static PresentationTuningData CreateDefaultData()
        {
            return new PresentationTuningData
            {
                schemaVersion = TuningSanitizer.SupportedSchemaVersion,
                labelVisibleSeconds = DefaultLabelVisibleSeconds,
                labelFadeSeconds = DefaultLabelFadeSeconds,
                markerVisibleSeconds = DefaultMarkerVisibleSeconds,
                markerFadeSeconds = DefaultMarkerFadeSeconds,
                cutFadeSeconds = DefaultCutFadeSeconds,
                riverFlowSpeed = DefaultRiverFlowSpeed,
                waterfallSpeed = DefaultWaterfallSpeed,
                bambooSwayAmplitude = DefaultBambooSwayAmplitude,
                bambooSwayPeriod = DefaultBambooSwayPeriod,
                pineSwayAmplitude = DefaultPineSwayAmplitude,
                pineSwayPeriod = DefaultPineSwayPeriod,
                grassSwayAmplitude = DefaultGrassSwayAmplitude,
                grassSwayPeriod = DefaultGrassSwayPeriod,
                flowerSwayAmplitude = DefaultFlowerSwayAmplitude,
                flowerSwayPeriod = DefaultFlowerSwayPeriod
            };
        }
    }
}
