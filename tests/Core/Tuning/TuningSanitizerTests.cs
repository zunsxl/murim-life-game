using System.Collections.Generic;
using Murim.Core.Tuning;
using NUnit.Framework;

namespace Murim.Core.Tests.Tuning
{
    [TestFixture]
    public sealed class TuningSanitizerTests
    {
        // T-CORE-32
        [Test]
        public void T_CORE_32_ValidValuesPassThrough()
        {
            ExplorationTuningData data = new ExplorationTuningData
            {
                schemaVersion = 1,
                walkSpeed = 6.5f,
                airSpeed = 10.0f,
                verticalSpeed = 12.0f,
                defaultOrthoSize = 12.0f,
                minOrthoSize = 3.5f,
                maxOrthoSize = 24.0f,
                zoomStep = 1.15f,
                panSpeed = 1.8f,
                defaultFocusX = 80f,
                defaultFocusZ = 40f
            };
            List<string> warnings = new List<string>();
            ExplorationTuning result = TuningSanitizer.SanitizeExploration(data, warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(result.WalkSpeed, Is.EqualTo(6.5f));
            Assert.That(result.AirSpeed, Is.EqualTo(10.0f));
            Assert.That(result.VerticalSpeed, Is.EqualTo(12.0f));
            Assert.That(result.DefaultOrthoSize, Is.EqualTo(12.0f));
            Assert.That(result.MinOrthoSize, Is.EqualTo(3.5f));
            Assert.That(result.MaxOrthoSize, Is.EqualTo(24.0f));
            Assert.That(result.ZoomStep, Is.EqualTo(1.15f));
            Assert.That(result.PanSpeed, Is.EqualTo(1.8f));
            Assert.That(result.DefaultFocusX, Is.EqualTo(80f));
            Assert.That(result.DefaultFocusZ, Is.EqualTo(40f));
        }

        [Test]
        public void T_CORE_32_OutOfRangeZeroAndNaNFieldsWarnAndFallback()
        {
            ExplorationTuningData valid = CreateValidData();

            // Zero field (simulates missing JSON field)
            valid.walkSpeed = 0f;
            AssertSingleFieldFallback(valid, "walkSpeed", t => t.WalkSpeed, ExplorationTuning.DefaultWalkSpeed);

            // Out-of-range high
            valid = CreateValidData();
            valid.airSpeed = 99f;
            AssertSingleFieldFallback(valid, "airSpeed", t => t.AirSpeed, ExplorationTuning.DefaultAirSpeed);

            // NaN
            valid = CreateValidData();
            valid.verticalSpeed = float.NaN;
            AssertSingleFieldFallback(valid, "verticalSpeed", t => t.VerticalSpeed, ExplorationTuning.DefaultVerticalSpeed);

            // Infinity
            valid = CreateValidData();
            valid.zoomStep = float.PositiveInfinity;
            AssertSingleFieldFallback(valid, "zoomStep", t => t.ZoomStep, ExplorationTuning.DefaultZoomStep);

            // Below minimum
            valid = CreateValidData();
            valid.panSpeed = 0.05f;
            AssertSingleFieldFallback(valid, "panSpeed", t => t.PanSpeed, ExplorationTuning.DefaultPanSpeed);

            valid = CreateValidData();
            valid.defaultFocusX = 200f;
            AssertSingleFieldFallback(valid, "defaultFocusX", t => t.DefaultFocusX, ExplorationTuning.DefaultDefaultFocusX);

            valid = CreateValidData();
            valid.defaultFocusZ = 0f;
            AssertSingleFieldFallback(valid, "defaultFocusZ", t => t.DefaultFocusZ, ExplorationTuning.DefaultDefaultFocusZ);
        }

        [Test]
        public void T_CORE_32_InvalidZoomOrderResetsAllThreeZoomFields()
        {
            ExplorationTuningData data = CreateValidData();
            data.minOrthoSize = 9.0f;
            data.defaultOrthoSize = 9.0f;
            data.maxOrthoSize = 20.0f;

            List<string> warnings = new List<string>();
            ExplorationTuning result = TuningSanitizer.SanitizeExploration(data, warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("exploration.json"));
            Assert.That(result.MinOrthoSize, Is.EqualTo(ExplorationTuning.DefaultMinOrthoSize));
            Assert.That(result.DefaultOrthoSize, Is.EqualTo(ExplorationTuning.DefaultDefaultOrthoSize));
            Assert.That(result.MaxOrthoSize, Is.EqualTo(ExplorationTuning.DefaultMaxOrthoSize));
        }

        [Test]
        public void T_CORE_32_WrongSchemaVersionOrNullReturnsAllDefaultsWithOneWarning()
        {
            ExplorationTuningData data = CreateValidData();
            data.schemaVersion = 2;
            data.walkSpeed = 9f;

            List<string> warnings = new List<string>();
            ExplorationTuning result = TuningSanitizer.SanitizeExploration(data, warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("exploration.json"));
            Assert.That(result.WalkSpeed, Is.EqualTo(ExplorationTuning.DefaultWalkSpeed));

            warnings.Clear();
            ExplorationTuning nullResult = TuningSanitizer.SanitizeExploration(null, warnings);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("exploration.json"));
            Assert.That(nullResult.WalkSpeed, Is.EqualTo(ExplorationTuning.DefaultWalkSpeed));
        }

        private static ExplorationTuningData CreateValidData()
        {
            return new ExplorationTuningData
            {
                schemaVersion = 1,
                walkSpeed = ExplorationTuning.DefaultWalkSpeed,
                airSpeed = ExplorationTuning.DefaultAirSpeed,
                verticalSpeed = ExplorationTuning.DefaultVerticalSpeed,
                defaultOrthoSize = ExplorationTuning.DefaultDefaultOrthoSize,
                minOrthoSize = ExplorationTuning.DefaultMinOrthoSize,
                maxOrthoSize = ExplorationTuning.DefaultMaxOrthoSize,
                zoomStep = ExplorationTuning.DefaultZoomStep,
                panSpeed = ExplorationTuning.DefaultPanSpeed,
                defaultFocusX = ExplorationTuning.DefaultDefaultFocusX,
                defaultFocusZ = ExplorationTuning.DefaultDefaultFocusZ
            };
        }

        private static void AssertSingleFieldFallback(
            ExplorationTuningData data,
            string fieldName,
            System.Func<ExplorationTuning, float> selector,
            float expectedDefault)
        {
            List<string> warnings = new List<string>();
            ExplorationTuning result = TuningSanitizer.SanitizeExploration(data, warnings);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("exploration.json"));
            Assert.That(warnings[0], Does.Contain(fieldName));
            Assert.That(selector(result), Is.EqualTo(expectedDefault));
        }
    }
}
