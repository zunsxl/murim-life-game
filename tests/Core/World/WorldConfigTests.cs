using System;
using System.IO;
using System.Text.Json;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldConfigTests
    {
        // T-CORE-14
        [Test]
        public void TryCreate_AcceptsDefaultsAndRejectsOutOfRangeByFieldName()
        {
            WorldConfigValues defaults = WorldConfig.Default.ToValues();
            Assert.That(WorldConfig.TryCreate(in defaults, out WorldConfig ok, out string err), Is.True);
            Assert.That(err, Is.Null);
            Assert.That(ok.MidTierHeightM, Is.EqualTo(12));
            Assert.That(ok.UpperTierHeightM, Is.EqualTo(26));
            Assert.That(ok.SideRidgeHeightM, Is.EqualTo(30));
            Assert.That(ok.NorthRidgeHeightM, Is.EqualTo(26));
            Assert.That(ok.TreeDensityPct, Is.EqualTo(100));
            Assert.That(ok.RockDensityPct, Is.EqualTo(100));
            Assert.That(ok.FloraDensityPct, Is.EqualTo(100));

            AssertFieldRejected("midTierHeightM", v => { v.MidTierHeightM = 7; return v; });
            AssertFieldRejected("midTierHeightM", v => { v.MidTierHeightM = 17; return v; });
            AssertFieldRejected("upperTierHeightM", v => { v.UpperTierHeightM = v.MidTierHeightM + 9; return v; });
            AssertFieldRejected("upperTierHeightM", v => { v.UpperTierHeightM = v.MidTierHeightM + 19; return v; });
            AssertFieldRejected("sideRidgeHeightM", v => { v.SideRidgeHeightM = 19; return v; });
            AssertFieldRejected("sideRidgeHeightM", v => { v.SideRidgeHeightM = 41; return v; });
            AssertFieldRejected("northRidgeHeightM", v => { v.NorthRidgeHeightM = 21; return v; });
            AssertFieldRejected("northRidgeHeightM", v => { v.NorthRidgeHeightM = 33; return v; });
            AssertFieldRejected("treeDensityPct", v => { v.TreeDensityPct = 24; return v; });
            AssertFieldRejected("treeDensityPct", v => { v.TreeDensityPct = 151; return v; });
            AssertFieldRejected("rockDensityPct", v => { v.RockDensityPct = 24; return v; });
            AssertFieldRejected("rockDensityPct", v => { v.RockDensityPct = 151; return v; });
            AssertFieldRejected("floraDensityPct", v => { v.FloraDensityPct = 24; return v; });
            AssertFieldRejected("floraDensityPct", v => { v.FloraDensityPct = 151; return v; });

            WorldConfigValues upperMin = defaults;
            upperMin.MidTierHeightM = 8;
            upperMin.UpperTierHeightM = 18;
            Assert.That(WorldConfig.TryCreate(in upperMin, out _, out _), Is.True);

            WorldConfigValues upperMax = defaults;
            upperMax.MidTierHeightM = 16;
            upperMax.UpperTierHeightM = 34;
            Assert.That(WorldConfig.TryCreate(in upperMax, out _, out _), Is.True);
        }

        // T-CORE-14
        [Test]
        public void ShippedWorldJson_MatchesWorldConfigDefault()
        {
            string repoRoot = FindRepoRoot();
            string jsonPath = Path.Combine(repoRoot, "Assets", "StreamingAssets", "Data", "world.json");
            Assert.That(File.Exists(jsonPath), Is.True, "Shipped world.json must exist.");

            string json = File.ReadAllText(jsonPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(WorldConfig.ExpectedSchemaVersion));
            WorldConfig d = WorldConfig.Default;
            Assert.That(root.GetProperty("midTierHeightM").GetInt32(), Is.EqualTo(d.MidTierHeightM));
            Assert.That(root.GetProperty("upperTierHeightM").GetInt32(), Is.EqualTo(d.UpperTierHeightM));
            Assert.That(root.GetProperty("sideRidgeHeightM").GetInt32(), Is.EqualTo(d.SideRidgeHeightM));
            Assert.That(root.GetProperty("northRidgeHeightM").GetInt32(), Is.EqualTo(d.NorthRidgeHeightM));
            Assert.That(root.GetProperty("treeDensityPct").GetInt32(), Is.EqualTo(d.TreeDensityPct));
            Assert.That(root.GetProperty("rockDensityPct").GetInt32(), Is.EqualTo(d.RockDensityPct));
            Assert.That(root.GetProperty("floraDensityPct").GetInt32(), Is.EqualTo(d.FloraDensityPct));
        }

        private static void AssertFieldRejected(string expectedField, Func<WorldConfigValues, WorldConfigValues> mutate)
        {
            WorldConfigValues v = mutate(WorldConfig.Default.ToValues());
            bool ok = WorldConfig.TryCreate(in v, out WorldConfig cfg, out string failedField);
            Assert.That(ok, Is.False, $"Expected rejection for {expectedField}");
            Assert.That(cfg, Is.Null);
            Assert.That(failedField, Is.EqualTo(expectedField));
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "global.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Could not locate repository root from test directory.");
        }
    }
}
