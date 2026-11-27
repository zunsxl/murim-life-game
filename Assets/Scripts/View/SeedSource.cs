using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Murim.Core.Common;

namespace Murim.View
{
    /// <summary>
    /// Resolves the launch world seed from <c>-seed</c> CLI arguments or cryptographic randomness (§3.1.1).
    /// </summary>
    public static class SeedSource
    {
        public const string SourceCli = "cli";
        public const string SourceRandom = "random";

        internal static Func<IReadOnlyList<string>> GetCommandLineArgs { get; set; } = Environment.GetCommandLineArgs;

        public static uint Resolve(out string source)
        {
            IReadOnlyList<string> args = GetCommandLineArgs != null
                ? GetCommandLineArgs()
                : Environment.GetCommandLineArgs();

            SeedParseStatus status = SeedArgument.TryParse(args, out uint parsedSeed);
            if (status == SeedParseStatus.Valid)
            {
                source = SourceCli;
                return parsedSeed;
            }

            if (status == SeedParseStatus.Invalid)
            {
                GameLog.Warn("Ignoring invalid -seed value; using a random seed.");
            }

            source = SourceRandom;
            return GenerateCryptoSeed();
        }

        private static uint GenerateCryptoSeed()
        {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return (uint)bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }
    }
}
