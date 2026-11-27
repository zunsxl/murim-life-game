using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Murim.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Murim.Tests.EditMode
{
    [TestFixture]
    public class ProjectBootstrapTests
    {
        private static readonly HashSet<string> AllowedSpecPackages = new HashSet<string>
        {
            "com.unity.render-pipelines.universal",
            "com.unity.2d.sprite",
            "com.unity.inputsystem",
            "com.unity.ugui",
            "com.unity.test-framework",
        };

        // T-ENG-02
        [Test]
        public void PlayerSettings_AndWindowDefaults_MatchSpec()
        {
            Assert.That(PlayerSettings.fullScreenMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(PlayerSettings.resizableWindow, Is.True);
            Assert.That(PlayerSettings.defaultScreenWidth, Is.EqualTo(1600));
            Assert.That(PlayerSettings.defaultScreenHeight, Is.EqualTo(900));
            Assert.That(PlayerSettings.allowFullscreenSwitch, Is.False);
            Assert.That(PlayerSettings.runInBackground, Is.False);
            Assert.That(PlayerSettings.SplashScreen.show, Is.False);
            Assert.That(PlayerSettings.SplashScreen.showUnityLogo, Is.False);

            Assert.That(
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone),
                Is.EqualTo(ScriptingImplementation.Mono2x));
            Assert.That(
                PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone),
                Is.EqualTo(ApiCompatibilityLevel.NET_Standard));
            Assert.That(PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone), Is.EqualTo(1));

            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("0.1.0"));
            Assert.That(PlayerSettings.productName, Is.EqualTo("Murim Life Game"));
            Assert.That(PlayerSettings.companyName, Is.EqualTo("Murim Life Game"));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(PlayerSettings.enableCrashReportAPI, Is.False);
        }

        // T-ENG-02
        [Test]
        public void ProjectSettingsAssets_AnalyticsInputAndBuildScenes_MatchSpec()
        {
            var psObj = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            Assert.That(psObj.FindProperty("activeInputHandler").intValue, Is.EqualTo(1));
            Assert.That(psObj.FindProperty("submitAnalytics").boolValue, Is.False);

            var ucObj = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/UnityConnectSettings.asset")[0]);
            Assert.That(ucObj.FindProperty("m_Enabled").boolValue, Is.False);
            Assert.That(ucObj.FindProperty("UnityAnalyticsSettings.m_Enabled").boolValue, Is.False);
            Assert.That(
                ucObj.FindProperty("CrashReportingSettings.m_EnableCloudDiagnosticsReporting").boolValue,
                Is.False);

            Assert.That(
                EditorBuildSettings.TryGetConfigObject("com.unity.input.settings", out InputSettings configuredInputSettings),
                Is.True,
                "Expected Input System settings registered under 'com.unity.input.settings'.");
            Assert.That(configuredInputSettings, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(configuredInputSettings),
                Is.EqualTo("Assets/Settings/InputSystem.inputsettings.asset"));
            Assert.That(
                configuredInputSettings.scrollDeltaBehavior,
                Is.EqualTo(InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms));
            Assert.That(InputSystem.settings, Is.Not.Null);
            Assert.That(
                InputSystem.settings.scrollDeltaBehavior,
                Is.EqualTo(InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms));

            string versionText = File.ReadAllText("ProjectSettings/ProjectVersion.txt");
            Assert.That(versionText, Does.StartWith("m_EditorVersion: 6000.3."));

            Assert.That(EditorBuildSettings.scenes.Length, Is.GreaterThanOrEqualTo(1));
            bool hasValley = false;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == BuildScript.ScenePath && scene.enabled)
                {
                    hasValley = true;
                }
            }

            Assert.That(hasValley, Is.True);
        }

        // T-ENG-02
        [Test]
        public void PackagesManifest_HasOnlyPinnedSpecPackagesAndLockfile()
        {
            Assert.That(File.Exists("Packages/packages-lock.json"), Is.True);
            string manifest = File.ReadAllText("Packages/manifest.json");
            Match block = Regex.Match(manifest, "\"dependencies\"\\s*:\\s*\\{([^}]*)\\}");
            Assert.That(block.Success, Is.True);

            var foundSpec = new HashSet<string>();
            foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\""))
            {
                string pkg = m.Groups[1].Value;
                string ver = m.Groups[2].Value;
                Assert.That(ver, Is.Not.Empty, $"Package {pkg} has empty version");
                Assert.That(
                    Regex.IsMatch(ver, @"^\d+\.\d+\.\d+$"),
                    Is.True,
                    $"Package {pkg} must be pinned to an exact x.y.z version, found '{ver}'");

                if (pkg.StartsWith("com.unity.modules."))
                {
                    continue;
                }

                Assert.That(AllowedSpecPackages.Contains(pkg), Is.True, $"Unexpected package: {pkg}");
                foundSpec.Add(pkg);
            }

            Assert.That(foundSpec, Is.EquivalentTo(AllowedSpecPackages));
        }

        // T-ENG-02
        [Test]
        public void RenderPipelineAndQuality_Strictly2DRendererWithoutHdrOrMsaa()
        {
            var urpAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(urpAsset, Is.Not.Null);
            Assert.That(QualitySettings.renderPipeline, Is.EqualTo(urpAsset));
            Assert.That(urpAsset.supportsHDR, Is.False);
            Assert.That(urpAsset.msaaSampleCount, Is.EqualTo(1));

            var urpObj = new SerializedObject(urpAsset);
            SerializedProperty renderers = urpObj.FindProperty("m_RendererDataList");
            Assert.That(renderers, Is.Not.Null);
            Assert.That(renderers.arraySize, Is.EqualTo(1));
            Assert.That(
                renderers.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.InstanceOf<Renderer2DData>());

            Assert.That(QualitySettings.names, Is.EqualTo(new[] { "Default" }));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
        }

        // T-ENG-02
        [Test]
        public void ScriptsFolder_ContainsNoWantsToQuitHandler()
        {
            string[] scriptFiles = Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories);
            foreach (string file in scriptFiles)
            {
                string content = File.ReadAllText(file);
                Assert.That(content, Does.Not.Contain("wantsToQuit"), $"Found wantsToQuit in {file}");
            }
        }

        // T-ENG-02
        [Test]
        public void ScriptsFolder_DoesNotAssignInputSystemSettings()
        {
            string[] scriptFiles = Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories);
            foreach (string file in scriptFiles)
            {
                string content = File.ReadAllText(file);
                bool assignsSettings = Regex.IsMatch(
                    content,
                    @"InputSystem\s*\.\s*settings(?:\s*\.\s*[A-Za-z0-9_]+)?\s*=(?!=)");
                Assert.That(assignsSettings, Is.False, $"Found InputSystem.settings assignment in {file}");
            }
        }

        // T-BLD-01
        [Test]
        public void BuildScript_TargetsWindowsReleaseScene()
        {
            Assert.That(BuildScript.ScenePath, Is.EqualTo("Assets/Scenes/Valley.unity"));
            Assert.That(BuildScript.OutputPath, Is.EqualTo("Builds/Windows/MurimLifeGame.exe"));
            Assert.That(File.Exists(BuildScript.ScenePath), Is.True);
        }
    }
}
