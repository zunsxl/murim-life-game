using System;
using System.IO;
using System.Text.Json;
using Murim.Core.Camera;
using Murim.Core.Common;
using NUnit.Framework;

namespace Murim.Core.Tests.Camera
{
    [TestFixture]
    public class CameraConfigTests
    {
        // T-CORE-28
        [Test]
        public void T_CORE_28_Defaults_AreAccepted()
        {
            CameraConfigData data = CameraConfig.Default.ToData();

            bool ok = CameraConfig.TryCreate(in data, out CameraConfig config, out string failedField);

            Assert.That(ok, Is.True);
            Assert.That(failedField, Is.Null);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.SchemaVersion, Is.EqualTo(1));
            Assert.That(config.MinViewHeightM, Is.EqualTo(12f));
            Assert.That(config.MaxViewHeightM, Is.EqualTo(88f));
            Assert.That(config.DefaultViewHeightM, Is.EqualTo(44f));
            Assert.That(config.PanSpeedPerViewHeight, Is.EqualTo(0.9f));
            Assert.That(config.PanAccelTimeS, Is.EqualTo(0.10f));
            Assert.That(config.PanStopTimeS, Is.EqualTo(0.22f));
            Assert.That(config.ZoomStepPct, Is.EqualTo(15f));
            Assert.That(config.ZoomSmoothTimeS, Is.EqualTo(0.12f));
            Assert.That(config.ResetSmoothTimeS, Is.EqualTo(0.35f));
        }

        // T-CORE-28
        [Test]
        public void T_CORE_28_EachFieldOutsideRange_RejectedByName()
        {
            AssertRejected("schemaVersion", d => d.SchemaVersion = 0);
            AssertRejected("schemaVersion", d => d.SchemaVersion = 2);

            AssertRejected("minViewHeightM", d => d.MinViewHeightM = 7.99f);
            AssertRejected("minViewHeightM", d => d.MinViewHeightM = 24.01f);

            AssertRejected("maxViewHeightM", d => d.MaxViewHeightM = 69.99f);
            AssertRejected("maxViewHeightM", d => d.MaxViewHeightM = 100.01f);

            AssertRejected("defaultViewHeightM", d => d.DefaultViewHeightM = 11.99f);
            AssertRejected("defaultViewHeightM", d => d.DefaultViewHeightM = 88.01f);

            AssertRejected("panSpeedPerViewHeight", d => d.PanSpeedPerViewHeight = 0.29f);
            AssertRejected("panSpeedPerViewHeight", d => d.PanSpeedPerViewHeight = 3.01f);

            AssertRejected("panAccelTimeS", d => d.PanAccelTimeS = 0.009f);
            AssertRejected("panAccelTimeS", d => d.PanAccelTimeS = 1.01f);

            AssertRejected("panStopTimeS", d => d.PanStopTimeS = 0.009f);
            AssertRejected("panStopTimeS", d => d.PanStopTimeS = 1.01f);

            AssertRejected("zoomStepPct", d => d.ZoomStepPct = 4.99f);
            AssertRejected("zoomStepPct", d => d.ZoomStepPct = 50.01f);

            AssertRejected("zoomSmoothTimeS", d => d.ZoomSmoothTimeS = 0.009f);
            AssertRejected("zoomSmoothTimeS", d => d.ZoomSmoothTimeS = 1.01f);

            AssertRejected("resetSmoothTimeS", d => d.ResetSmoothTimeS = 0.049f);
            AssertRejected("resetSmoothTimeS", d => d.ResetSmoothTimeS = 2.01f);
        }

        // T-CORE-28
        [Test]
        public void T_CORE_28_DefaultViewHeightOutsideMinMax_RejectedByName()
        {
            AssertRejected("defaultViewHeightM", d =>
            {
                d.MinViewHeightM = 16f;
                d.MaxViewHeightM = 72f;
                d.DefaultViewHeightM = 15.9f;
            });

            AssertRejected("defaultViewHeightM", d =>
            {
                d.MinViewHeightM = 16f;
                d.MaxViewHeightM = 72f;
                d.DefaultViewHeightM = 72.1f;
            });
        }

        // T-CORE-28
        [Test]
        public void T_CORE_28_NonFiniteFloat_RejectedByName()
        {
            float[] badValues = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            foreach (float bad in badValues)
            {
                AssertRejected("minViewHeightM", d => d.MinViewHeightM = bad);
                AssertRejected("maxViewHeightM", d => d.MaxViewHeightM = bad);
                AssertRejected("defaultViewHeightM", d => d.DefaultViewHeightM = bad);
                AssertRejected("panSpeedPerViewHeight", d => d.PanSpeedPerViewHeight = bad);
                AssertRejected("panAccelTimeS", d => d.PanAccelTimeS = bad);
                AssertRejected("panStopTimeS", d => d.PanStopTimeS = bad);
                AssertRejected("zoomStepPct", d => d.ZoomStepPct = bad);
                AssertRejected("zoomSmoothTimeS", d => d.ZoomSmoothTimeS = bad);
                AssertRejected("resetSmoothTimeS", d => d.ResetSmoothTimeS = bad);
            }
        }

        // T-CORE-28
        [Test]
        public void T_CORE_28_ShippedCameraJson_EqualsDefault()
        {
            string jsonPath = FindShippedCameraJsonPath();
            Assert.That(File.Exists(jsonPath), Is.True, "Expected shipped Assets/StreamingAssets/Data/camera.json.");

            byte[] raw = File.ReadAllBytes(jsonPath);
            Assert.That(raw.Length, Is.LessThanOrEqualTo(65536));

            string text = File.ReadAllText(jsonPath);
            int depth = JsonGuard.MaxDepth(text);
            Assert.That(depth, Is.InRange(1, 4));

            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            var data = new CameraConfigData
            {
                SchemaVersion = root.GetProperty("schemaVersion").GetInt32(),
                MinViewHeightM = root.GetProperty("minViewHeightM").GetSingle(),
                MaxViewHeightM = root.GetProperty("maxViewHeightM").GetSingle(),
                DefaultViewHeightM = root.GetProperty("defaultViewHeightM").GetSingle(),
                PanSpeedPerViewHeight = root.GetProperty("panSpeedPerViewHeight").GetSingle(),
                PanAccelTimeS = root.GetProperty("panAccelTimeS").GetSingle(),
                PanStopTimeS = root.GetProperty("panStopTimeS").GetSingle(),
                ZoomStepPct = root.GetProperty("zoomStepPct").GetSingle(),
                ZoomSmoothTimeS = root.GetProperty("zoomSmoothTimeS").GetSingle(),
                ResetSmoothTimeS = root.GetProperty("resetSmoothTimeS").GetSingle(),
            };

            Assert.That(CameraConfig.TryCreate(in data, out CameraConfig parsed, out string failedField), Is.True);
            Assert.That(failedField, Is.Null);

            CameraConfig def = CameraConfig.Default;
            Assert.That(parsed.SchemaVersion, Is.EqualTo(def.SchemaVersion));
            Assert.That(parsed.MinViewHeightM, Is.EqualTo(def.MinViewHeightM));
            Assert.That(parsed.MaxViewHeightM, Is.EqualTo(def.MaxViewHeightM));
            Assert.That(parsed.DefaultViewHeightM, Is.EqualTo(def.DefaultViewHeightM));
            Assert.That(parsed.PanSpeedPerViewHeight, Is.EqualTo(def.PanSpeedPerViewHeight));
            Assert.That(parsed.PanAccelTimeS, Is.EqualTo(def.PanAccelTimeS));
            Assert.That(parsed.PanStopTimeS, Is.EqualTo(def.PanStopTimeS));
            Assert.That(parsed.ZoomStepPct, Is.EqualTo(def.ZoomStepPct));
            Assert.That(parsed.ZoomSmoothTimeS, Is.EqualTo(def.ZoomSmoothTimeS));
            Assert.That(parsed.ResetSmoothTimeS, Is.EqualTo(def.ResetSmoothTimeS));
        }

        private static void AssertRejected(string expectedField, Action<BoxedData> mutate)
        {
            var box = new BoxedData { Value = CameraConfig.Default.ToData() };
            mutate(box);
            CameraConfigData mutated = box.Value;

            bool ok = CameraConfig.TryCreate(in mutated, out CameraConfig config, out string failedField);

            Assert.That(ok, Is.False);
            Assert.That(config, Is.Null);
            Assert.That(failedField, Is.EqualTo(expectedField));
        }

        private sealed class BoxedData
        {
            public CameraConfigData Value;

            public int SchemaVersion { get => Value.SchemaVersion; set => Value.SchemaVersion = value; }
            public float MinViewHeightM { get => Value.MinViewHeightM; set => Value.MinViewHeightM = value; }
            public float MaxViewHeightM { get => Value.MaxViewHeightM; set => Value.MaxViewHeightM = value; }
            public float DefaultViewHeightM { get => Value.DefaultViewHeightM; set => Value.DefaultViewHeightM = value; }
            public float PanSpeedPerViewHeight { get => Value.PanSpeedPerViewHeight; set => Value.PanSpeedPerViewHeight = value; }
            public float PanAccelTimeS { get => Value.PanAccelTimeS; set => Value.PanAccelTimeS = value; }
            public float PanStopTimeS { get => Value.PanStopTimeS; set => Value.PanStopTimeS = value; }
            public float ZoomStepPct { get => Value.ZoomStepPct; set => Value.ZoomStepPct = value; }
            public float ZoomSmoothTimeS { get => Value.ZoomSmoothTimeS; set => Value.ZoomSmoothTimeS = value; }
            public float ResetSmoothTimeS { get => Value.ResetSmoothTimeS; set => Value.ResetSmoothTimeS = value; }
        }

        private static string FindShippedCameraJsonPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Data", "camera.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "Assets", "StreamingAssets", "Data", "camera.json");
        }
    }
}
