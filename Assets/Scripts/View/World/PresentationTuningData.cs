using System;

namespace Murim.View.World
{
    /// <summary>
    /// Plain JSON deserialization DTO for presentation.json with no field initializers per spec §2.
    /// </summary>
    [Serializable]
    public sealed class PresentationTuningData
    {
        public int schemaVersion;
        public float labelVisibleSeconds;
        public float labelFadeSeconds;
        public float markerVisibleSeconds;
        public float markerFadeSeconds;
        public float cutFadeSeconds;
        public float riverFlowSpeed;
        public float waterfallSpeed;
        public float bambooSwayAmplitude;
        public float bambooSwayPeriod;
        public float pineSwayAmplitude;
        public float pineSwayPeriod;
        public float grassSwayAmplitude;
        public float grassSwayPeriod;
        public float flowerSwayAmplitude;
        public float flowerSwayPeriod;
    }
}
