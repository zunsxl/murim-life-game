using System;
using System.IO;
using System.Text;
using Murim.Core.Common;
using UnityEngine;

namespace Murim.View
{
    internal readonly struct ConfigFileProbe
    {
        public ConfigFileProbe(bool exists, long lengthBytes, Func<byte[]> readBytes)
        {
            Exists = exists;
            LengthBytes = lengthBytes;
            ReadBytes = readBytes;
        }

        public bool Exists { get; }
        public long LengthBytes { get; }
        public Func<byte[]> ReadBytes { get; }
    }

    internal delegate ConfigFileProbe ConfigFileAccess(string fileName);

    /// <summary>
    /// Loads bounded, schema-versioned JSON configuration files from <c>StreamingAssets/Data/</c>.
    /// </summary>
    public static class JsonConfigLoader
    {
        public const int MaxFileBytes = 65536;
        public const int MaxJsonDepth = 4;
        public const int ExpectedSchemaVersion = 1;
        public const string WorldConfigFileName = "world.json";
        public const string CameraConfigFileName = "camera.json";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static ConfigFileAccess FileAccess { get; set; } = ReadFromStreamingAssets;

        [Serializable]
        private sealed class SchemaProbe
        {
            public int schemaVersion;
        }

        public static bool TryLoad<TDto>(string fileName, out TDto dto) where TDto : class
        {
            return TryLoad(fileName, out dto, null);
        }

        public static bool TryLoad<TDto>(string fileName, out TDto dto, Func<TDto, string> validate) where TDto : class
        {
            dto = null;
            if (!IsAllowedFileName(fileName))
            {
                GameLog.Warn("Config <disallowed> ignored (invalid); using defaults.");
                return false;
            }

            if (!TryReadText(fileName, out string text))
            {
                return false;
            }

            int depth = JsonGuard.MaxDepth(text);
            if (depth <= 0 || depth > MaxJsonDepth)
            {
                WarnIgnored(fileName, "malformed");
                return false;
            }

            if (!TryDeserialize(fileName, text, out dto))
            {
                return false;
            }

            if (validate != null)
            {
                string failedField = validate(dto);
                if (!string.IsNullOrEmpty(failedField))
                {
                    dto = null;
                    WarnIgnored(fileName, "range:" + failedField);
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadText(string fileName, out string text)
        {
            text = null;
            ConfigFileAccess reader = FileAccess ?? ReadFromStreamingAssets;
            ConfigFileProbe probe = reader(fileName);
            if (!probe.Exists)
            {
                WarnIgnored(fileName, "missing");
                return false;
            }

            if (probe.LengthBytes > MaxFileBytes)
            {
                WarnIgnored(fileName, "oversized");
                return false;
            }

            byte[] raw = probe.ReadBytes != null ? probe.ReadBytes() : null;
            if (raw == null)
            {
                WarnIgnored(fileName, "missing");
                return false;
            }

            if (raw.Length > MaxFileBytes)
            {
                WarnIgnored(fileName, "oversized");
                return false;
            }

            return TryDecodeUtf8(fileName, raw, out text);
        }

        private static bool TryDecodeUtf8(string fileName, byte[] raw, out string text)
        {
            text = null;
            int offset = HasUtf8Bom(raw) ? 3 : 0;
            try
            {
                text = StrictUtf8.GetString(raw, offset, raw.Length - offset);
                return true;
            }
            catch (DecoderFallbackException)
            {
                WarnIgnored(fileName, "malformed");
                return false;
            }
        }

        private static bool TryDeserialize<TDto>(string fileName, string text, out TDto dto) where TDto : class
        {
            dto = null;
            try
            {
                SchemaProbe probe = JsonUtility.FromJson<SchemaProbe>(text);
                dto = JsonUtility.FromJson<TDto>(text);
                if (dto == null)
                {
                    WarnIgnored(fileName, "malformed");
                    return false;
                }

                if (probe == null || probe.schemaVersion != ExpectedSchemaVersion)
                {
                    dto = null;
                    WarnIgnored(fileName, "schema");
                    return false;
                }

                return true;
            }
            catch (ArgumentException)
            {
                dto = null;
                WarnIgnored(fileName, "malformed");
                return false;
            }
        }

        private static bool IsAllowedFileName(string fileName)
        {
            return string.Equals(fileName, WorldConfigFileName, StringComparison.Ordinal)
                || string.Equals(fileName, CameraConfigFileName, StringComparison.Ordinal);
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
        }

        private static void WarnIgnored(string fileName, string reason)
        {
            GameLog.Warn("Config " + fileName + " ignored (" + reason + "); using defaults.");
        }

        private static ConfigFileProbe ReadFromStreamingAssets(string fileName)
        {
            string path = Application.streamingAssetsPath + "/Data/" + fileName;
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new ConfigFileProbe(false, 0, null);
            }

            return new ConfigFileProbe(true, info.Length, () => File.ReadAllBytes(path));
        }
    }
}
