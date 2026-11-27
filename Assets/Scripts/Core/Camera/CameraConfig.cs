namespace Murim.Core.Camera
{
    /// <summary>
    /// Raw tunable fields matching <c>Assets/StreamingAssets/Data/camera.json</c> (§3.4.8).
    /// </summary>
    public struct CameraConfigData
    {
        public int SchemaVersion;
        public float MinViewHeightM;
        public float MaxViewHeightM;
        public float DefaultViewHeightM;
        public float PanSpeedPerViewHeight;
        public float PanAccelTimeS;
        public float PanStopTimeS;
        public float ZoomStepPct;
        public float ZoomSmoothTimeS;
        public float ResetSmoothTimeS;
    }

    /// <summary>
    /// Validated camera tunables (§3.4.8).
    /// </summary>
    public sealed class CameraConfig
    {
        public const int ExpectedSchemaVersion = 1;

        public static CameraConfig Default { get; } = new CameraConfig(new CameraConfigData
        {
            SchemaVersion = ExpectedSchemaVersion,
            MinViewHeightM = 12f,
            MaxViewHeightM = 88f,
            DefaultViewHeightM = 44f,
            PanSpeedPerViewHeight = 0.9f,
            PanAccelTimeS = 0.10f,
            PanStopTimeS = 0.22f,
            ZoomStepPct = 15f,
            ZoomSmoothTimeS = 0.12f,
            ResetSmoothTimeS = 0.35f,
        });

        private CameraConfig(in CameraConfigData data)
        {
            SchemaVersion = data.SchemaVersion;
            MinViewHeightM = data.MinViewHeightM;
            MaxViewHeightM = data.MaxViewHeightM;
            DefaultViewHeightM = data.DefaultViewHeightM;
            PanSpeedPerViewHeight = data.PanSpeedPerViewHeight;
            PanAccelTimeS = data.PanAccelTimeS;
            PanStopTimeS = data.PanStopTimeS;
            ZoomStepPct = data.ZoomStepPct;
            ZoomSmoothTimeS = data.ZoomSmoothTimeS;
            ResetSmoothTimeS = data.ResetSmoothTimeS;
        }

        public int SchemaVersion { get; }
        public float MinViewHeightM { get; }
        public float MaxViewHeightM { get; }
        public float DefaultViewHeightM { get; }
        public float PanSpeedPerViewHeight { get; }
        public float PanAccelTimeS { get; }
        public float PanStopTimeS { get; }
        public float ZoomStepPct { get; }
        public float ZoomSmoothTimeS { get; }
        public float ResetSmoothTimeS { get; }

        public CameraConfigData ToData()
        {
            return new CameraConfigData
            {
                SchemaVersion = SchemaVersion,
                MinViewHeightM = MinViewHeightM,
                MaxViewHeightM = MaxViewHeightM,
                DefaultViewHeightM = DefaultViewHeightM,
                PanSpeedPerViewHeight = PanSpeedPerViewHeight,
                PanAccelTimeS = PanAccelTimeS,
                PanStopTimeS = PanStopTimeS,
                ZoomStepPct = ZoomStepPct,
                ZoomSmoothTimeS = ZoomSmoothTimeS,
                ResetSmoothTimeS = ResetSmoothTimeS,
            };
        }

        public static bool TryCreate(in CameraConfigData data, out CameraConfig config, out string failedField)
        {
            config = null;
            if (!TryValidate(in data, out failedField))
            {
                return false;
            }

            config = new CameraConfig(in data);
            return true;
        }

        private static bool TryValidate(in CameraConfigData data, out string failedField)
        {
            if (data.SchemaVersion != ExpectedSchemaVersion)
            {
                failedField = "schemaVersion";
                return false;
            }

            if (!CheckRange(data.MinViewHeightM, 8f, 24f, "minViewHeightM", out failedField)
                || !CheckRange(data.MaxViewHeightM, 70f, 100f, "maxViewHeightM", out failedField)
                || !CheckRange(
                    data.DefaultViewHeightM,
                    data.MinViewHeightM,
                    data.MaxViewHeightM,
                    "defaultViewHeightM",
                    out failedField))
            {
                return false;
            }

            return TryValidateMotionFields(in data, out failedField);
        }

        private static bool TryValidateMotionFields(in CameraConfigData data, out string failedField)
        {
            if (!CheckRange(data.PanSpeedPerViewHeight, 0.3f, 3.0f, "panSpeedPerViewHeight", out failedField)
                || !CheckRange(data.PanAccelTimeS, 0.01f, 1.0f, "panAccelTimeS", out failedField)
                || !CheckRange(data.PanStopTimeS, 0.01f, 1.0f, "panStopTimeS", out failedField)
                || !CheckRange(data.ZoomStepPct, 5f, 50f, "zoomStepPct", out failedField)
                || !CheckRange(data.ZoomSmoothTimeS, 0.01f, 1.0f, "zoomSmoothTimeS", out failedField)
                || !CheckRange(data.ResetSmoothTimeS, 0.05f, 2.0f, "resetSmoothTimeS", out failedField))
            {
                return false;
            }

            failedField = null;
            return true;
        }

        private static bool CheckRange(float value, float min, float max, string fieldName, out string failedField)
        {
            if (!float.IsFinite(value) || value < min || value > max)
            {
                failedField = fieldName;
                return false;
            }

            failedField = null;
            return true;
        }
    }
}
