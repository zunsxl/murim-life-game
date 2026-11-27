using System;
using System.Collections.Generic;
using System.Globalization;

namespace Murim.Core.Common
{
    public enum SeedParseStatus
    {
        Absent = 0,
        Valid = 1,
        Invalid = 2,
    }

    /// <summary>
    /// Parses the optional <c>-seed &lt;uint&gt;</c> command-line argument.
    /// </summary>
    public static class SeedArgument
    {
        private const string Flag = "-seed";
        private const int MaxSeedDigits = 10;

        public static SeedParseStatus TryParse(IReadOnlyList<string> args, out uint seed)
        {
            seed = 0;
            if (args == null)
            {
                return SeedParseStatus.Absent;
            }

            for (int i = 0; i < args.Count; i++)
            {
                if (!string.Equals(args[i], Flag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (i + 1 >= args.Count)
                {
                    return SeedParseStatus.Invalid;
                }

                string raw = args[i + 1];
                if (!IsValidAsciiUint(raw, out seed))
                {
                    seed = 0;
                    return SeedParseStatus.Invalid;
                }

                return SeedParseStatus.Valid;
            }

            return SeedParseStatus.Absent;
        }

        private static bool IsValidAsciiUint(string value, out uint seed)
        {
            seed = 0;
            if (string.IsNullOrEmpty(value) || value.Length > MaxSeedDigits)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seed);
        }
    }
}
