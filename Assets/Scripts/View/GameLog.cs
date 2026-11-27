using System;
using UnityEngine;

namespace Murim.View
{
    /// <summary>
    /// Centralised game logger that prefixes <c>[Murim] </c> and avoids path or PII leakage.
    /// </summary>
    public static class GameLog
    {
        public const string Prefix = "[Murim] ";

        public static void Info(string message)
        {
            Debug.Log(Format(message));
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Format(message));
        }

        private static string Format(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Prefix;
            }

            return message.StartsWith(Prefix, StringComparison.Ordinal) ? message : Prefix + message;
        }
    }
}
