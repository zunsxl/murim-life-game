using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Murim.Editor
{
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Valley.unity";
        private const string OutputExePath = "Builds/Windows/MurimLifeGame.exe";
        private const string ReportPath = "artifacts/build-report.json";

        [Serializable]
        private sealed class ReleaseBuildReport
        {
            public string result;
            public string target;
            public string outputPath;
            public string buildOptions;
            public bool development;
            public string bundleVersion;
            public string[] scenes;
            public int totalErrors;
            public int totalWarnings;
            public ulong totalSizeBytes;
        }

        public static void BuildWindowsRelease()
        {
            if (!File.Exists(ScenePath))
            {
                throw new BuildFailedException($"Required scene not found: {ScenePath}");
            }

            string outputDir = Path.GetDirectoryName(OutputExePath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string reportDir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(reportDir))
            {
                Directory.CreateDirectory(reportDir);
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputExePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            var record = new ReleaseBuildReport
            {
                result = summary.result.ToString(),
                target = summary.platform.ToString(),
                outputPath = OutputExePath,
                buildOptions = "BuildOptions.None",
                development = false,
                bundleVersion = PlayerSettings.bundleVersion,
                scenes = options.scenes,
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                totalSizeBytes = summary.totalSize
            };

            File.WriteAllText(ReportPath, JsonUtility.ToJson(record, true) + "\n");

            if (summary.result != BuildResult.Succeeded || !File.Exists(OutputExePath))
            {
                throw new BuildFailedException(
                    $"Windows release build failed with result {summary.result} ({summary.totalErrors} error(s)).");
            }

            Debug.Log($"[Murim] BuildWindowsRelease succeeded: {OutputExePath} (v{PlayerSettings.bundleVersion})");
        }
    }
}
