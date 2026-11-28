using System;
using System.IO;
using UnityEngine;

namespace Murim.View
{
    /// <summary>
    /// Structured game logger that prefixes every line with [Murim] and strips local paths,
    /// user names, and machine names per spec §1 and §2.
    /// </summary>
    public static class GameLog
    {
        private const string Prefix = "[Murim]";

        public static void LogInfo(string eventName)
        {
            string safeEvent = SanitizeToken(eventName);
            Debug.Log($"{Prefix} {safeEvent}");
        }

        public static void LogWarning(string eventName, string fileName)
        {
            string safeEvent = SanitizeToken(eventName);
            string safeFile = SanitizeFileName(fileName);
            Debug.LogWarning($"{Prefix} {safeEvent}: {safeFile}");
        }

        public static void LogWarning(string eventName, string fileName, string fieldName)
        {
            string safeEvent = SanitizeToken(eventName);
            string safeFile = SanitizeFileName(fileName);
            string safeField = SanitizeToken(fieldName);
            Debug.LogWarning($"{Prefix} {safeEvent}: {safeFile} ({safeField})");
        }

        public static void LogError(string eventName, string fileName)
        {
            string safeEvent = SanitizeToken(eventName);
            string safeFile = SanitizeFileName(fileName);
            Debug.LogError($"{Prefix} {safeEvent}: {safeFile}");
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "unknown";
            }

            string normalized = fileName.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized) || normalized.Contains(":"))
            {
                normalized = Path.GetFileName(normalized);
            }

            return StripSensitiveIds(normalized);
        }

        private static string SanitizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "unknown";
            }

            string trimmed = token.Trim().Replace('\\', '_').Replace(':', '_');
            return StripSensitiveIds(trimmed);
        }

        private static string StripSensitiveIds(string value)
        {
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user))
            {
                value = value.Replace(user, "<redacted>", StringComparison.OrdinalIgnoreCase);
            }

            string machine = Environment.MachineName;
            if (!string.IsNullOrEmpty(machine))
            {
                value = value.Replace(machine, "<redacted>", StringComparison.OrdinalIgnoreCase);
            }

            return string.IsNullOrEmpty(value) ? "unknown" : value;
        }
    }
}
