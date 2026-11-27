using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Murim.Tests.Shared;
using Murim.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class WorldBootTests
    {
        private Func<IReadOnlyList<string>> _originalArgs;
        private ConfigFileAccess _originalFileAccess;
        private readonly List<string> _murimLogs = new List<string>();
        private readonly List<string> _murimWarnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
            _originalFileAccess = JsonConfigLoader.FileAccess;
            _murimLogs.Clear();
            _murimWarnings.Clear();
            Application.logMessageReceived += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CaptureLog;
            SeedSource.GetCommandLineArgs = _originalArgs;
            JsonConfigLoader.FileAccess = _originalFileAccess;
        }

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.StartsWith(GameLog.Prefix, StringComparison.Ordinal))
            {
                _murimLogs.Add(condition);
                if (type == LogType.Warning)
                {
                    _murimWarnings.Add(condition);
                }
            }
        }

        // T-ENG-03
        [UnityTest]
        public IEnumerator T_ENG_03_BootWithSeedOneAndConfigOverride_LogsGoldenHashAndAppliesConfig()
        {
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "1" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            Assert.That(boot.World, Is.Not.Null);
            Assert.That(boot.World.Seed, Is.EqualTo(1u));
            Assert.That(boot.World.Hash, Is.EqualTo(GoldenHashes.Seed1));

            string expectedHash = GoldenHashes.Seed1.ToString("X16", CultureInfo.InvariantCulture);
            string readyLine = FindLastWorldReadyLog();
            Assert.That(readyLine, Is.Not.Null);
            Assert.That(readyLine, Does.Contain("seed=1 source=cli hash=" + expectedHash));
            AssertNoPathOrIdentityLeak();

            // Reload with world.json override via the seam
            const string overrideJson =
                "{\"schemaVersion\":1,\"midTierHeightM\":16,\"upperTierHeightM\":28," +
                "\"sideRidgeHeightM\":34,\"northRidgeHeightM\":29," +
                "\"treeDensityPct\":80,\"rockDensityPct\":90,\"floraDensityPct\":70}";
            byte[] overrideBytes = Encoding.UTF8.GetBytes(overrideJson);
            JsonConfigLoader.FileAccess = fileName =>
            {
                if (string.Equals(fileName, JsonConfigLoader.WorldConfigFileName, StringComparison.Ordinal))
                {
                    return new ConfigFileProbe(true, overrideBytes.Length, () => overrideBytes);
                }

                return new ConfigFileProbe(false, 0, null);
            };

            _murimLogs.Clear();
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot.Config.MidTierHeightM, Is.EqualTo(16));
            Assert.That(boot.Config.UpperTierHeightM, Is.EqualTo(28));
            Assert.That(boot.Config.SideRidgeHeightM, Is.EqualTo(34));
            Assert.That(boot.Config.NorthRidgeHeightM, Is.EqualTo(29));
            Assert.That(boot.Config.TreeDensityPct, Is.EqualTo(80));
            Assert.That(boot.Config.RockDensityPct, Is.EqualTo(90));
            Assert.That(boot.Config.FloraDensityPct, Is.EqualTo(70));
            Assert.That(boot.World.Hash, Is.Not.EqualTo(GoldenHashes.Seed1));
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-03
        [UnityTest]
        public IEnumerator T_ENG_03_BootWithoutSeedAndWithInvalidSeed_UsesRandomSeedsAndWarnsCleanly()
        {
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            uint seedA = ParseWorldReadySeed(FindLastWorldReadyLog(), "random");

            _murimLogs.Clear();
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            uint seedB = ParseWorldReadySeed(FindLastWorldReadyLog(), "random");
            Assert.That(seedB, Is.Not.EqualTo(seedA));

            // Invalid -seed should warn without echoing the raw value and boot with random seed
            _murimLogs.Clear();
            _murimWarnings.Clear();
            LogAssert.Expect(LogType.Warning, "[Murim] Ignoring invalid -seed value; using a random seed.");
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "bad_seed_val" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            Assert.That(_murimWarnings, Has.Member("[Murim] Ignoring invalid -seed value; using a random seed."));
            foreach (string line in _murimLogs)
            {
                Assert.That(line, Does.Not.Contain("bad_seed_val"));
            }

            ParseWorldReadySeed(FindLastWorldReadyLog(), "random");
            AssertNoPathOrIdentityLeak();
        }

        private string FindLastWorldReadyLog()
        {
            for (int i = _murimLogs.Count - 1; i >= 0; i--)
            {
                if (_murimLogs[i].StartsWith("[Murim] World ready ", StringComparison.Ordinal))
                {
                    return _murimLogs[i];
                }
            }

            return null;
        }

        private static uint ParseWorldReadySeed(string line, string expectedSource)
        {
            Assert.That(line, Is.Not.Null);
            Match m = Regex.Match(
                line,
                @"^\[Murim\] World ready seed=(\d+) source=([a-z]+) hash=([0-9A-F]{16}) props=(\d+) ms=(\d+)$");
            Assert.That(m.Success, Is.True, "Unexpected World ready log format: " + line);
            Assert.That(m.Groups[2].Value, Is.EqualTo(expectedSource));
            return uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private void AssertNoPathOrIdentityLeak()
        {
            string user = Environment.UserName;
            string machine = Environment.MachineName;
            foreach (string line in _murimLogs)
            {
                Assert.That(line, Does.Not.Contain("/"));
                Assert.That(line, Does.Not.Contain("\\"));
                if (!string.IsNullOrEmpty(user))
                {
                    Assert.That(line, Does.Not.Contain(user));
                }

                if (!string.IsNullOrEmpty(machine))
                {
                    Assert.That(line, Does.Not.Contain(machine));
                }
            }
        }
    }
}
