using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Murim.Core.Tuning;
using Murim.View.World;
using UnityEngine;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Loads and sanitizes exploration.json and presentation.json from StreamingAssets/Data per §2 and §3.9.
    /// </summary>
    public static class TuningLoader
    {
        public const int MaxConfigFileBytes = 65536;

        public static void LoadFromStreamingAssets(out ExplorationTuning exploration, out PresentationTuning presentation)
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "Data");
            LoadFromDirectory(dataDir, out exploration, out presentation);
        }

        public static void LoadFromDirectory(
            string dataDirectory,
            out ExplorationTuning exploration,
            out PresentationTuning presentation)
        {
            exploration = LoadExploration(dataDirectory);
            presentation = LoadPresentation(dataDirectory);
        }

        public static ExplorationTuning LoadExploration(string dataDirectory)
        {
            const string fileName = TuningSanitizer.ExplorationFileName;
            if (!TryReadJsonText(dataDirectory, fileName, out string json))
            {
                return ExplorationTuning.Default;
            }

            ExplorationTuningData data = ParseJson<ExplorationTuningData>(json, fileName);
            if (data == null)
            {
                return ExplorationTuning.Default;
            }

            List<string> warnings = new List<string>();
            ExplorationTuning tuning = TuningSanitizer.SanitizeExploration(data, warnings);
            EmitSanitizerWarnings(warnings, fileName);
            return tuning;
        }

        public static PresentationTuning LoadPresentation(string dataDirectory)
        {
            const string fileName = PresentationTuning.PresentationFileName;
            if (!TryReadJsonText(dataDirectory, fileName, out string json))
            {
                return PresentationTuning.Default;
            }

            PresentationTuningData data = ParseJson<PresentationTuningData>(json, fileName);
            if (data == null)
            {
                return PresentationTuning.Default;
            }

            List<string> warnings = new List<string>();
            PresentationTuning tuning = PresentationTuning.Sanitize(data, warnings);
            EmitSanitizerWarnings(warnings, fileName);
            return tuning;
        }

        private static bool TryReadJsonText(string dataDirectory, string fileName, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(dataDirectory))
            {
                GameLog.LogWarning("ConfigMissing", fileName);
                return false;
            }

            string path = Path.Combine(dataDirectory, fileName);
            if (!File.Exists(path))
            {
                GameLog.LogWarning("ConfigMissing", fileName);
                return false;
            }

            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length > MaxConfigFileBytes)
                {
                    GameLog.LogWarning("ConfigTooLarge", fileName);
                    return false;
                }

                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception)
            {
                GameLog.LogWarning("ConfigReadFailed", fileName);
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                GameLog.LogWarning("ConfigMalformed", fileName);
                return false;
            }

            return true;
        }

        private static T ParseJson<T>(string json, string fileName) where T : class
        {
            try
            {
                T data = JsonUtility.FromJson<T>(json);
                if (data == null)
                {
                    GameLog.LogWarning("ConfigMalformed", fileName);
                }
                return data;
            }
            catch (Exception)
            {
                GameLog.LogWarning("ConfigMalformed", fileName);
                return null;
            }
        }

        private static void EmitSanitizerWarnings(List<string> warnings, string fileName)
        {
            for (int i = 0; i < warnings.Count; i++)
            {
                string field = ExtractFieldName(warnings[i]);
                if (field != null)
                {
                    GameLog.LogWarning("ConfigInvalid", fileName, field);
                }
                else
                {
                    GameLog.LogWarning("ConfigInvalid", fileName);
                }
            }
        }

        private static string ExtractFieldName(string warning)
        {
            const string marker = "field '";
            int idx = warning.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                int start = idx + marker.Length;
                int end = warning.IndexOf('\'', start);
                if (end > start)
                {
                    return warning.Substring(start, end - start);
                }
            }

            if (warning.IndexOf("schemaVersion", StringComparison.Ordinal) >= 0)
            {
                return "schemaVersion";
            }

            if (warning.IndexOf("minOrthoSize", StringComparison.Ordinal) >= 0)
            {
                return "minOrthoSize";
            }

            return null;
        }
    }
}
