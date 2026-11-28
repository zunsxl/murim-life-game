using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Murim.Core.Tuning;
using Murim.View.Exploration;
using Murim.View.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.Engine.Exploration
{
    [TestFixture]
    [Category("ConfigLoading")]
    public sealed class ConfigLoadingTests
    {
        private readonly List<string> _tempDirs = new List<string>();
        private readonly List<(LogType type, string message)> _capturedLogs = new List<(LogType, string)>();
        private bool _capturingLogs;

        [TearDown]
        public void TearDown()
        {
            if (_capturingLogs)
            {
                Application.logMessageReceived -= OnLogMessage;
                _capturingLogs = false;
            }
            _capturedLogs.Clear();

            for (int i = 0; i < _tempDirs.Count; i++)
            {
                string dir = _tempDirs[i];
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            _tempDirs.Clear();
        }

        // T-ENG-16: Missing, malformed, 70 000-byte, schemaVersion 2, and one out-of-range field return defaults with 1 warning each.
        [Test]
        public void T_ENG_16_ConfigLoading_InvalidAndMissingFilesFallbackToDefaultsWithSingleWarning()
        {
            StartCapture();

            // 1. Real StreamingAssets/Data loads cleanly with zero warnings and §3.9 defaults.
            TuningLoader.LoadFromStreamingAssets(out ExplorationTuning realExp, out PresentationTuning realPres);
            Assert.That(_capturedLogs, Is.Empty);
            AssertExplorationDefaults(realExp);
            AssertPresentationDefaults(realPres);

            // 2. Missing files
            string missingDir = CreateTempDir();
            ExplorationTuning missingExp = TuningLoader.LoadExploration(missingDir);
            AssertExplorationDefaults(missingExp);
            AssertSingleWarning("exploration.json");

            PresentationTuning missingPres = TuningLoader.LoadPresentation(missingDir);
            AssertPresentationDefaults(missingPres);
            AssertSingleWarning("presentation.json");

            // 3. Malformed JSON
            string malformedDir = CreateTempDir();
            File.WriteAllText(Path.Combine(malformedDir, "exploration.json"), "{ broken json", Encoding.UTF8);
            File.WriteAllText(Path.Combine(malformedDir, "presentation.json"), "{ broken json", Encoding.UTF8);

            AssertExplorationDefaults(TuningLoader.LoadExploration(malformedDir));
            AssertSingleWarning("exploration.json");
            AssertPresentationDefaults(TuningLoader.LoadPresentation(malformedDir));
            AssertSingleWarning("presentation.json");

            // 4. Oversized 70 000-byte files
            string largeDir = CreateTempDir();
            string paddedExp = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", "exploration.json"), Encoding.UTF8)
                + new string(' ', 70000);
            string paddedPres = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", "presentation.json"), Encoding.UTF8)
                + new string(' ', 70000);
            File.WriteAllText(Path.Combine(largeDir, "exploration.json"), paddedExp, Encoding.UTF8);
            File.WriteAllText(Path.Combine(largeDir, "presentation.json"), paddedPres, Encoding.UTF8);

            AssertExplorationDefaults(TuningLoader.LoadExploration(largeDir));
            AssertSingleWarning("exploration.json");
            AssertPresentationDefaults(TuningLoader.LoadPresentation(largeDir));
            AssertSingleWarning("presentation.json");

            // 5. schemaVersion 2
            string schemaDir = CreateTempDir();
            File.WriteAllText(Path.Combine(schemaDir, "exploration.json"), "{\"schemaVersion\":2,\"walkSpeed\":7.0}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(schemaDir, "presentation.json"), "{\"schemaVersion\":2,\"cutFadeSeconds\":0.5}", Encoding.UTF8);

            AssertExplorationDefaults(TuningLoader.LoadExploration(schemaDir));
            AssertSingleWarning("exploration.json");
            AssertPresentationDefaults(TuningLoader.LoadPresentation(schemaDir));
            AssertSingleWarning("presentation.json");

            // 6. One out-of-range field while keeping valid custom fields
            string rangeDir = CreateTempDir();
            File.WriteAllText(
                Path.Combine(rangeDir, "exploration.json"),
                "{\"schemaVersion\":1,\"walkSpeed\":99.0,\"airSpeed\":10.0,\"verticalSpeed\":8.0,"
                + "\"defaultOrthoSize\":11.25,\"minOrthoSize\":4.5,\"maxOrthoSize\":28.125,"
                + "\"zoomStep\":1.1,\"panSpeed\":1.2,\"defaultFocusX\":70.0,\"defaultFocusZ\":29.0}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(rangeDir, "presentation.json"),
                "{\"schemaVersion\":1,\"labelVisibleSeconds\":1.5,\"labelFadeSeconds\":0.5,"
                + "\"markerVisibleSeconds\":0.8,\"markerFadeSeconds\":0.4,\"cutFadeSeconds\":9.0,"
                + "\"riverFlowSpeed\":2.5,\"waterfallSpeed\":3.0,\"bambooSwayAmplitude\":0.12,"
                + "\"bambooSwayPeriod\":3.5,\"pineSwayAmplitude\":0.05,\"pineSwayPeriod\":4.5,"
                + "\"grassSwayAmplitude\":0.04,\"grassSwayPeriod\":2.2,\"flowerSwayAmplitude\":0.03,"
                + "\"flowerSwayPeriod\":2.0}",
                Encoding.UTF8);

            ExplorationTuning rangeExp = TuningLoader.LoadExploration(rangeDir);
            Assert.That(rangeExp.WalkSpeed, Is.EqualTo(ExplorationTuning.DefaultWalkSpeed));
            Assert.That(rangeExp.AirSpeed, Is.EqualTo(10.0f));
            AssertSingleWarning("exploration.json", "walkSpeed");

            PresentationTuning rangePres = TuningLoader.LoadPresentation(rangeDir);
            Assert.That(rangePres.CutFadeSeconds, Is.EqualTo(PresentationTuning.DefaultCutFadeSeconds));
            Assert.That(rangePres.RiverFlowSpeed, Is.EqualTo(2.5f));
            AssertSingleWarning("presentation.json", "cutFadeSeconds");
        }

        // T-ENG-17: Every game log emitted during T-ENG-10 and T-ENG-16 starts with [Murim] and leaks no paths or user/machine names.
        [UnityTest]
        public IEnumerator T_ENG_17_GameLogsStartWithMurimAndContainNoPathsOrIdentity()
        {
            Scene previous = SceneManager.GetActiveScene();
            StartCapture();

            AsyncOperation loadOp = SceneManager.LoadSceneAsync("Assets/Scenes/Valley.unity", LoadSceneMode.Additive);
            while (!loadOp.isDone)
            {
                yield return null;
            }

            Scene valley = SceneManager.GetSceneByPath("Assets/Scenes/Valley.unity");
            SceneManager.SetActiveScene(valley);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            // Trigger config warnings as in T-ENG-16 while capturing logs.
            string dir = CreateTempDir();
            TuningLoader.LoadExploration(dir);
            TuningLoader.LoadPresentation(dir);
            File.WriteAllText(Path.Combine(dir, "exploration.json"), "{\"schemaVersion\":2}", Encoding.UTF8);
            TuningLoader.LoadExploration(dir);

            List<(LogType type, string message)> snapshot = new List<(LogType, string)>(_capturedLogs);
            StopCapture();

            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(valley);
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }

            int gameLineCount = 0;
            Regex drivePathRegex = new Regex(@"[A-Za-z]:[/\\]|/(?:Users|home)/", RegexOptions.Compiled);
            string user = Environment.UserName;
            string machine = Environment.MachineName;

            for (int i = 0; i < snapshot.Count; i++)
            {
                (LogType logType, string msg) = snapshot[i];
                if (msg.StartsWith("[Murim]", StringComparison.Ordinal))
                {
                    gameLineCount++;
                }
                else
                {
                    Assert.That(logType, Is.Not.EqualTo(LogType.Error).And.Not.EqualTo(LogType.Exception),
                        $"Unexpected non-[Murim] error log: {msg}");
                    continue;
                }

                Assert.That(drivePathRegex.IsMatch(msg), Is.False, $"Game log leaked local path: {msg}");
                if (!string.IsNullOrEmpty(user) && user.Length >= 2)
                {
                    Assert.That(msg.IndexOf(user, StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                        $"Game log leaked username: {msg}");
                }
                if (!string.IsNullOrEmpty(machine) && machine.Length >= 2)
                {
                    Assert.That(msg.IndexOf(machine, StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                        $"Game log leaked machine name: {msg}");
                }
            }

            Assert.That(gameLineCount, Is.GreaterThanOrEqualTo(3), "Expected [Murim] config warning logs to be captured.");
        }

        private void StartCapture()
        {
            _capturedLogs.Clear();
            if (!_capturingLogs)
            {
                Application.logMessageReceived += OnLogMessage;
                _capturingLogs = true;
            }
        }

        private void StopCapture()
        {
            if (_capturingLogs)
            {
                Application.logMessageReceived -= OnLogMessage;
                _capturingLogs = false;
            }
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            _capturedLogs.Add((type, condition));
        }

        private void AssertSingleWarning(string expectedFile, string expectedField = null)
        {
            Assert.That(_capturedLogs.Count, Is.EqualTo(1), $"Expected 1 warning for {expectedFile}, got {_capturedLogs.Count}.");
            Assert.That(_capturedLogs[0].type, Is.EqualTo(LogType.Warning));
            Assert.That(_capturedLogs[0].message, Does.StartWith("[Murim]"));
            Assert.That(_capturedLogs[0].message, Does.Contain(expectedFile));
            if (!string.IsNullOrEmpty(expectedField))
            {
                Assert.That(_capturedLogs[0].message, Does.Contain(expectedField));
            }
            _capturedLogs.Clear();
        }

        private string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "murim_cfg_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        private static void AssertExplorationDefaults(ExplorationTuning tuning)
        {
            Assert.That(tuning.WalkSpeed, Is.EqualTo(ExplorationTuning.DefaultWalkSpeed));
            Assert.That(tuning.AirSpeed, Is.EqualTo(ExplorationTuning.DefaultAirSpeed));
            Assert.That(tuning.VerticalSpeed, Is.EqualTo(ExplorationTuning.DefaultVerticalSpeed));
            Assert.That(tuning.DefaultOrthoSize, Is.EqualTo(ExplorationTuning.DefaultDefaultOrthoSize));
            Assert.That(tuning.MinOrthoSize, Is.EqualTo(ExplorationTuning.DefaultMinOrthoSize));
            Assert.That(tuning.MaxOrthoSize, Is.EqualTo(ExplorationTuning.DefaultMaxOrthoSize));
            Assert.That(tuning.ZoomStep, Is.EqualTo(ExplorationTuning.DefaultZoomStep));
            Assert.That(tuning.PanSpeed, Is.EqualTo(ExplorationTuning.DefaultPanSpeed));
            Assert.That(tuning.DefaultFocusX, Is.EqualTo(ExplorationTuning.DefaultDefaultFocusX));
            Assert.That(tuning.DefaultFocusZ, Is.EqualTo(ExplorationTuning.DefaultDefaultFocusZ));
        }

        private static void AssertPresentationDefaults(PresentationTuning tuning)
        {
            Assert.That(tuning.LabelVisibleSeconds, Is.EqualTo(PresentationTuning.DefaultLabelVisibleSeconds));
            Assert.That(tuning.LabelFadeSeconds, Is.EqualTo(PresentationTuning.DefaultLabelFadeSeconds));
            Assert.That(tuning.MarkerVisibleSeconds, Is.EqualTo(PresentationTuning.DefaultMarkerVisibleSeconds));
            Assert.That(tuning.MarkerFadeSeconds, Is.EqualTo(PresentationTuning.DefaultMarkerFadeSeconds));
            Assert.That(tuning.CutFadeSeconds, Is.EqualTo(PresentationTuning.DefaultCutFadeSeconds));
            Assert.That(tuning.RiverFlowSpeed, Is.EqualTo(PresentationTuning.DefaultRiverFlowSpeed));
            Assert.That(tuning.WaterfallSpeed, Is.EqualTo(PresentationTuning.DefaultWaterfallSpeed));
            Assert.That(tuning.BambooSwayAmplitude, Is.EqualTo(PresentationTuning.DefaultBambooSwayAmplitude));
            Assert.That(tuning.BambooSwayPeriod, Is.EqualTo(PresentationTuning.DefaultBambooSwayPeriod));
            Assert.That(tuning.PineSwayAmplitude, Is.EqualTo(PresentationTuning.DefaultPineSwayAmplitude));
            Assert.That(tuning.PineSwayPeriod, Is.EqualTo(PresentationTuning.DefaultPineSwayPeriod));
            Assert.That(tuning.GrassSwayAmplitude, Is.EqualTo(PresentationTuning.DefaultGrassSwayAmplitude));
            Assert.That(tuning.GrassSwayPeriod, Is.EqualTo(PresentationTuning.DefaultGrassSwayPeriod));
            Assert.That(tuning.FlowerSwayAmplitude, Is.EqualTo(PresentationTuning.DefaultFlowerSwayAmplitude));
            Assert.That(tuning.FlowerSwayPeriod, Is.EqualTo(PresentationTuning.DefaultFlowerSwayPeriod));
        }
    }
}
