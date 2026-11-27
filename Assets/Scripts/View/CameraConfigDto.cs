using System;
using Murim.Core.Camera;

namespace Murim.View
{
    /// <summary>
    /// Flat JSON DTO for <c>camera.json</c> (§2.5, §3.4.8).
    /// </summary>
    [Serializable]
    public sealed class CameraConfigDto
    {
        public int schemaVersion;
        public float minViewHeightM;
        public float maxViewHeightM;
        public float defaultViewHeightM;
        public float panSpeedPerViewHeight;
        public float panAccelTimeS;
        public float panStopTimeS;
        public float zoomStepPct;
        public float zoomSmoothTimeS;
        public float resetSmoothTimeS;

        public CameraConfigData ToData()
        {
            return new CameraConfigData
            {
                SchemaVersion = schemaVersion,
                MinViewHeightM = minViewHeightM,
                MaxViewHeightM = maxViewHeightM,
                DefaultViewHeightM = defaultViewHeightM,
                PanSpeedPerViewHeight = panSpeedPerViewHeight,
                PanAccelTimeS = panAccelTimeS,
                PanStopTimeS = panStopTimeS,
                ZoomStepPct = zoomStepPct,
                ZoomSmoothTimeS = zoomSmoothTimeS,
                ResetSmoothTimeS = resetSmoothTimeS,
            };
        }
    }
}
