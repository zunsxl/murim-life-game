using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Murim.Editor
{
    /// <summary>
    /// Builds the release Windows x64 player without debug or profiler options.
    /// </summary>
    public static class BuildScript
    {
        public const string ScenePath = "Assets/Scenes/Valley.unity";
        public const string OutputPath = "Builds/Windows/MurimLifeGame.exe";

        public static void BuildWindowsRelease()
        {
            string outputDir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
            BuildSummary summary = report.summary;
            Debug.Log(
                $"[Murim] Build result={summary.result} target={summary.platform} options={summary.options} errors={summary.totalErrors}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Windows release build failed with result: " + summary.result);
            }
        }
    }
}
