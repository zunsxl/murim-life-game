using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using Murim.View;
using NUnit.Framework;
using UnityEngine;

namespace Murim.Tests.EditMode
{
    [TestFixture]
    public class ConfigLoading
    {
        [Serializable]
        private sealed class TestConfigDto
        {
            public int schemaVersion;
            public int testValue;
            public float testRatio;
        }

        [Serializable]
        private sealed class MissingSchemaDto
        {
            public int testValue;
        }

        private string _tempDir;
        private ConfigFileAccess _originalAccess;
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _allMurimLogs = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "murim-cfg-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAccess = JsonConfigLoader.FileAccess;
            _warnings.Clear();
            _allMurimLogs.Clear();
            Application.logMessageReceived += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CaptureLog;
            JsonConfigLoader.FileAccess = _originalAccess;
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.StartsWith(GameLog.Prefix, StringComparison.Ordinal))
            {
                _allMurimLogs.Add(condition);
                if (type == LogType.Warning)
                {
                    _warnings.Add(condition);
                }
            }
        }

        private void RouteToTempDir(string overrideFilePath, out Func<int> readCountGetter)
        {
            int readCount = 0;
            readCountGetter = () => readCount;
            JsonConfigLoader.FileAccess = requestedName =>
            {
                string path = overrideFilePath ?? Path.Combine(_tempDir, requestedName);
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return new ConfigFileProbe(false, 0, null);
                }

                return new ConfigFileProbe(true, info.Length, () =>
                {
                    readCount++;
                    return File.ReadAllBytes(path);
                });
            };
        }

        // T-ENG-01
        [Test]
        public void TryLoad_MissingFile_WarnsOnceAndReturnsFalse()
        {
            RouteToTempDir(null, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "missing");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_OversizedFile_DoesNotReadAndWarnsOnce()
        {
            string path = Path.Combine(_tempDir, "world.json");
            File.WriteAllBytes(path, new byte[65537]);
            RouteToTempDir(path, out Func<int> reads);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            Assert.That(reads(), Is.EqualTo(0));
            AssertSingleWarningWithReason("world.json", "oversized");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_MalformedJson_WarnsOnceAndReturnsFalse()
        {
            string path = Path.Combine(_tempDir, "world.json");
            File.WriteAllText(path, "{ invalid json", Encoding.UTF8);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "malformed");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_DepthFive_RejectedAsMalformed()
        {
            string path = Path.Combine(_tempDir, "world.json");
            File.WriteAllText(path, "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":1}}}}}", Encoding.UTF8);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "malformed");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_SchemaVersionTwo_RejectedWithSchemaReason()
        {
            string path = Path.Combine(_tempDir, "camera.json");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"testValue\":42}", Encoding.UTF8);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.CameraConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("camera.json", "schema");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_RangeFailure_LogsRangeFieldReason()
        {
            string path = Path.Combine(_tempDir, "world.json");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"testValue\":999}", Encoding.UTF8);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(
                JsonConfigLoader.WorldConfigFileName,
                out TestConfigDto dto,
                parsed => parsed.testValue > 100 ? "testValue" : null);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "range:testValue");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_ValidWithAndWithoutBom_ReturnsDtoWithoutWarnings()
        {
            string path = Path.Combine(_tempDir, "camera.json");
            byte[] utf8 = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"testValue\":42,\"testRatio\":1.5}");
            byte[] withBom = new byte[3 + utf8.Length];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(utf8, 0, withBom, 3, utf8.Length);
            File.WriteAllBytes(path, withBom);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.CameraConfigFileName, out TestConfigDto dto);

            Assert.That(ok, Is.True);
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto.schemaVersion, Is.EqualTo(1));
            Assert.That(dto.testValue, Is.EqualTo(42));
            Assert.That(dto.testRatio, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(_warnings, Is.Empty);
        }

        // T-ENG-01
        [Test]
        public void TryLoad_DisallowedFileName_RefusedWithoutLeakingPath()
        {
            RouteToTempDir(null, out _);

            Assert.That(JsonConfigLoader.TryLoad("../world.json", out TestConfigDto d1), Is.False);
            Assert.That(d1, Is.Null);
            Assert.That(JsonConfigLoader.TryLoad("other\\config.json", out TestConfigDto d2), Is.False);
            Assert.That(d2, Is.Null);
            Assert.That(JsonConfigLoader.TryLoad("custom.json", out TestConfigDto d3), Is.False);
            Assert.That(d3, Is.Null);

            Assert.That(_warnings.Count, Is.EqualTo(3));
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_DtoLackingSchemaVersion_RejectedWithSchemaReason()
        {
            string path = Path.Combine(_tempDir, "world.json");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"testValue\":42}", Encoding.UTF8);
            RouteToTempDir(path, out _);

            bool ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out MissingSchemaDto dto);

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "schema");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_ProbeThrowsIOException_WarnsUnreadableAndReturnsFalse()
        {
            string secretMessage = $"I/O error at {Path.Combine("mock-drive", "private-dir", Environment.UserName, Environment.MachineName)}\\world.json /sub";
            JsonConfigLoader.FileAccess = _ => throw new IOException(secretMessage);

            bool ok = false;
            TestConfigDto dto = null;
            Assert.DoesNotThrow(() =>
                ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out dto));

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "unreadable");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_ReadBytesThrowsIOException_WarnsUnreadableAndReturnsFalse()
        {
            string secretMessage = $"Locked {Path.Combine("mock-drive", "private-dir", Environment.UserName, Environment.MachineName)}\\camera.json /sub";
            JsonConfigLoader.FileAccess = _ => new ConfigFileProbe(
                true,
                128,
                () => throw new IOException(secretMessage));

            bool ok = false;
            TestConfigDto dto = null;
            Assert.DoesNotThrow(() =>
                ok = JsonConfigLoader.TryLoad(JsonConfigLoader.CameraConfigFileName, out dto));

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("camera.json", "unreadable");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_ReadBytesThrowsUnauthorizedAccessException_WarnsUnreadableAndReturnsFalse()
        {
            string secretMessage = $"Denied {Path.Combine("mock-drive", "private-dir", Environment.UserName, Environment.MachineName)}\\world.json /sub";
            JsonConfigLoader.FileAccess = _ => new ConfigFileProbe(
                true,
                128,
                () => throw new UnauthorizedAccessException(secretMessage));

            bool ok = false;
            TestConfigDto dto = null;
            Assert.DoesNotThrow(() =>
                ok = JsonConfigLoader.TryLoad(JsonConfigLoader.WorldConfigFileName, out dto));

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("world.json", "unreadable");
            AssertNoPathOrIdentityLeak();
        }

        // T-ENG-01
        [Test]
        public void TryLoad_ReadBytesThrowsSecurityException_WarnsUnreadableAndReturnsFalse()
        {
            string secretMessage = $"Security {Path.Combine("mock-drive", "private-dir", Environment.UserName, Environment.MachineName)}\\camera.json /sub";
            JsonConfigLoader.FileAccess = _ => new ConfigFileProbe(
                true,
                128,
                () => throw new SecurityException(secretMessage));

            bool ok = false;
            TestConfigDto dto = null;
            Assert.DoesNotThrow(() =>
                ok = JsonConfigLoader.TryLoad(JsonConfigLoader.CameraConfigFileName, out dto));

            Assert.That(ok, Is.False);
            Assert.That(dto, Is.Null);
            AssertSingleWarningWithReason("camera.json", "unreadable");
            AssertNoPathOrIdentityLeak();
        }

        private void AssertSingleWarningWithReason(string fileName, string reason)
        {
            Assert.That(_warnings.Count, Is.EqualTo(1));
            string expected = $"[Murim] Config {fileName} ignored ({reason}); using defaults.";
            Assert.That(_warnings[0], Is.EqualTo(expected));
        }

        private void AssertNoPathOrIdentityLeak()
        {
            string user = Environment.UserName;
            string machine = Environment.MachineName;
            foreach (string line in _allMurimLogs)
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
