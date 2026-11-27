using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Murim.Editor.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.U2D;

namespace Murim.Tests.Art
{
    [TestFixture]
    [Category("Art")]
    public sealed class ArtDeliveryTests
    {
        // T-ART-01
        [Test]
        public void T_ART_01_TilesWaterAndBackdropTextures_ConformToSpec()
        {
            List<ArtAssetEntry> entries = ArtManifest.GetTexturesAndTiles();
            var issues = new List<ArtValidationIssue>();

            for (int i = 0; i < entries.Count; i++)
            {
                ArtAssetEntry entry = entries[i];
                if (!File.Exists(entry.AssetPath))
                {
                    issues.Add(new ArtValidationIssue(
                        entry.ArtId,
                        "FilePresent",
                        Path.GetFileName(entry.AssetPath),
                        $"Missing contracted texture file '{entry.AssetPath}'."));
                    continue;
                }

                bool needPixels = entry.RequiresAlpha || !string.IsNullOrEmpty(entry.ExactBottomRowHex);
                PngReadResult png = ArtRules.ValidatePngFile(entry, needPixels, issues);
                if (!png.Valid)
                {
                    continue;
                }

                CheckImporterForEntry(entry, issues);
            }

            string[] allAssetsFiles = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories);
            ArtRules.CheckNoModelFiles(allAssetsFiles, issues);

            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        // T-ART-02
        [Test]
        public void T_ART_02_WorldSprites_ConformToSpec()
        {
            List<ArtAssetEntry> entries = ArtManifest.GetWorldSprites();
            var issues = new List<ArtValidationIssue>();

            for (int i = 0; i < entries.Count; i++)
            {
                ArtAssetEntry entry = entries[i];
                if (!File.Exists(entry.AssetPath))
                {
                    issues.Add(new ArtValidationIssue(
                        entry.ArtId,
                        "FilePresent",
                        Path.GetFileName(entry.AssetPath),
                        $"Missing contracted sprite file '{entry.AssetPath}'."));
                    continue;
                }

                PngReadResult png = ArtRules.ValidatePngFile(entry, true, issues);
                if (!png.Valid)
                {
                    continue;
                }

                CheckImporterForEntry(entry, issues);
            }

            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        // T-ART-02
        [Test]
        public void T_ART_02_WorldPropsSpriteAtlas_ExistsAndPacksIntoOnePage()
        {
            var issues = new List<ArtValidationIssue>();
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ArtManifest.WorldPropsAtlasPath);
            SpriteAtlasSnapshot snapshot = BuildAtlasSnapshot(atlas);

            List<ArtAssetEntry> worldSprites = ArtManifest.GetWorldSprites();
            var expectedNames = new List<string>(worldSprites.Count);
            for (int i = 0; i < worldSprites.Count; i++)
            {
                expectedNames.Add(Path.GetFileNameWithoutExtension(worldSprites[i].AssetPath));
            }

            ArtRules.CheckSpriteAtlas(snapshot, expectedNames, issues);
            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        // T-ART-03
        [Test]
        public void T_ART_03_UiSpritesAndButtonSilhouettes_ConformToSpec()
        {
            List<ArtAssetEntry> entries = ArtManifest.GetUiSprites();
            var issues = new List<ArtValidationIssue>();
            var buffers = new Dictionary<string, ArtPixelBuffer>(StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                ArtAssetEntry entry = entries[i];
                if (!File.Exists(entry.AssetPath))
                {
                    issues.Add(new ArtValidationIssue(
                        entry.ArtId,
                        "FilePresent",
                        Path.GetFileName(entry.AssetPath),
                        $"Missing contracted UI sprite '{entry.AssetPath}'."));
                    continue;
                }

                PngReadResult png = ArtRules.ValidatePngFile(entry, true, issues);
                if (!png.Valid)
                {
                    continue;
                }

                buffers[Path.GetFileName(entry.AssetPath)] = png.Image;
                CheckImporterForEntry(entry, issues);
            }

            if (buffers.TryGetValue("overlay_button_normal.png", out ArtPixelBuffer normal)
                && buffers.TryGetValue("overlay_button_hover.png", out ArtPixelBuffer hover)
                && buffers.TryGetValue("overlay_button_pressed.png", out ArtPixelBuffer pressed))
            {
                ArtRules.CheckButtonSilhouettes(normal, hover, pressed, issues);
            }

            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        // T-ART-04
        [Test]
        public void T_ART_04_FontLicenseAndArtReadme_ConformToSpec()
        {
            var issues = new List<ArtValidationIssue>();
            var fontFiles = new List<string>();
            if (Directory.Exists(ArtManifest.FontsDirectory))
            {
                string[] allFiles = Directory.GetFiles(ArtManifest.FontsDirectory);
                for (int i = 0; i < allFiles.Length; i++)
                {
                    string ext = Path.GetExtension(allFiles[i]);
                    if (string.Equals(ext, ".ttf", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".otf", StringComparison.OrdinalIgnoreCase))
                    {
                        fontFiles.Add(Path.GetFileName(allFiles[i]));
                    }
                }
            }

            fontFiles.Sort(StringComparer.Ordinal);
            var fontSnap = new FontDirectorySnapshot { FontFileNames = fontFiles };
            string primaryFontName = string.Empty;
            if (fontFiles.Count == 1)
            {
                primaryFontName = fontFiles[0];
                string fullFontPath = Path.Combine(ArtManifest.FontsDirectory, primaryFontName);
                fontSnap.Font = FontReader.ReadFile(fullFontPath);
                string licPath = Path.Combine(
                    ArtManifest.FontsDirectory,
                    Path.GetFileNameWithoutExtension(primaryFontName) + "-LICENSE.txt");
                fontSnap.LicenseExists = File.Exists(licPath);
                fontSnap.LicenseNonEmpty = fontSnap.LicenseExists && new FileInfo(licPath).Length > 0;
            }

            ArtRules.CheckFont(fontSnap, issues);

            string readmeText = File.Exists(ArtManifest.ArtReadmePath)
                ? File.ReadAllText(ArtManifest.ArtReadmePath)
                : string.Empty;
            ArtRules.CheckArtReadme(readmeText, primaryFontName, issues);

            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        // T-ART-04
        [Test]
        public void T_ART_04_TmpFontAsset_ExistsWithRequiredSettings()
        {
            var issues = new List<ArtValidationIssue>();
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(ArtManifest.OverlaySerifAssetPath);
            TmpFontAssetSnapshot snapshot = BuildTmpSnapshot(asset);
            ArtRules.CheckTmpFontAsset(snapshot, issues);
            Assert.That(issues, Is.Empty, FormatIssues(issues));
        }

        private static void CheckImporterForEntry(ArtAssetEntry entry, IList<ArtValidationIssue> issues)
        {
            var importer = AssetImporter.GetAtPath(entry.AssetPath) as TextureImporter;
            if (importer == null)
            {
                issues.Add(new ArtValidationIssue(
                    entry.ArtId,
                    "TextureImporterPresent",
                    Path.GetFileName(entry.AssetPath),
                    $"Missing TextureImporter at '{entry.AssetPath}'."));
                return;
            }

            ArtImportSnapshot actual = ArtImportSnapshot.FromImporter(importer);
            ArtRules.CheckImportSettings(entry, actual, issues);
        }

        private static SpriteAtlasSnapshot BuildAtlasSnapshot(SpriteAtlas atlas)
        {
            if (atlas == null)
            {
                return new SpriteAtlasSnapshot { Exists = false };
            }

            SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            SpriteAtlasTextureSettings tex = atlas.GetTextureSettings();
            TextureImporterPlatformSettings plat = atlas.GetPlatformSettings("DefaultTexturePlatform");

            var so = new SerializedObject(atlas);
            SerializedProperty bindProp = so.FindProperty("m_EditorData.bindAsDefault");
            bool includeInBuild = bindProp != null && bindProp.boolValue;

            int pageCount = GetPackedPageCount(atlas);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sprites = new Sprite[atlas.spriteCount];
            int copied = atlas.GetSprites(sprites);
            for (int i = 0; i < copied; i++)
            {
                if (sprites[i] != null)
                {
                    string rawName = sprites[i].name;
                    const string cloneSuffix = "(Clone)";
                    if (rawName.EndsWith(cloneSuffix, StringComparison.Ordinal))
                    {
                        rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length);
                    }

                    names.Add(rawName);
                }
            }

            return new SpriteAtlasSnapshot
            {
                Exists = true,
                PageCount = pageCount,
                MaxTextureSize = plat.maxTextureSize,
                Padding = packing.padding,
                AllowRotation = packing.enableRotation,
                TightPacking = packing.enableTightPacking,
                GenerateMipMaps = tex.generateMipMaps,
                FilterMode = tex.filterMode,
                Srgb = tex.sRGB,
                Compression = plat.textureCompression,
                IncludeInBuild = includeInBuild,
                SpriteNames = names
            };
        }

        private static int GetPackedPageCount(SpriteAtlas atlas)
        {
            MethodInfo method = typeof(SpriteAtlasExtensions).GetMethod(
                "GetPreviewTextures",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                return atlas.spriteCount > 0 ? 1 : 0;
            }

            var textures = method.Invoke(null, new object[] { atlas }) as Texture2D[];
            return textures == null ? 0 : textures.Length;
        }

        private static TmpFontAssetSnapshot BuildTmpSnapshot(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return new TmpFontAssetSnapshot { Exists = false };
            }

            var so = new SerializedObject(asset);
            int atlasWidth = so.FindProperty("m_AtlasWidth")?.intValue ?? 0;
            int atlasHeight = so.FindProperty("m_AtlasHeight")?.intValue ?? 0;
            float facePointSize = so.FindProperty("m_FaceInfo.m_PointSize")?.floatValue
                ?? so.FindProperty("m_FaceInfo.pointSize")?.floatValue
                ?? 0f;
            int creationPointSize = so.FindProperty("m_CreationSettings.pointSize")?.intValue ?? 0;
            int pointSize = Mathf.RoundToInt(facePointSize > 0f ? facePointSize : creationPointSize);
            int atlasRenderMode = so.FindProperty("m_AtlasRenderMode")?.intValue ?? 0;
            int creationRenderMode = so.FindProperty("m_CreationSettings.renderMode")?.intValue ?? 0;
            int atlasPadding = so.FindProperty("m_AtlasPadding")?.intValue ?? 0;
            int creationPadding = so.FindProperty("m_CreationSettings.padding")?.intValue ?? 0;
            int padding = atlasPadding > 0 ? atlasPadding : creationPadding;

            bool isSdfAa = atlasRenderMode == (int)GlyphRenderMode.SDFAA
                || creationRenderMode == (int)GlyphRenderMode.SDFAA;

            bool coversAscii = CheckTmpAsciiCoverage(so);
            return new TmpFontAssetSnapshot
            {
                Exists = true,
                IsSdfAa = isSdfAa,
                AtlasWidth = atlasWidth,
                AtlasHeight = atlasHeight,
                Padding = padding,
                SamplingPointSize = pointSize,
                CoversAscii = coversAscii
            };
        }

        private static bool CheckTmpAsciiCoverage(SerializedObject so)
        {
            SerializedProperty charTable = so.FindProperty("m_CharacterTable");
            if (charTable == null || !charTable.isArray)
            {
                return false;
            }

            var present = new HashSet<int>();
            for (int i = 0; i < charTable.arraySize; i++)
            {
                SerializedProperty elem = charTable.GetArrayElementAtIndex(i);
                SerializedProperty unicode = elem.FindPropertyRelative("m_Unicode");
                if (unicode != null)
                {
                    present.Add(unicode.intValue);
                }
            }

            for (int cp = ArtManifest.FontMinCodepoint; cp <= ArtManifest.FontMaxCodepoint; cp++)
            {
                if (!present.Contains(cp))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatIssues(List<ArtValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("\n", issues);
        }
    }
}
