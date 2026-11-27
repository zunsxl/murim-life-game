using System.Collections.Generic;
using Murim.Editor.Art;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Murim.Tests.Art
{
    [TestFixture]
    [Category("Art")]
    public sealed class ArtRulesTests
    {
        // T-ART-05
        [Test]
        public void T_ART_05_Dimensions_ConformingAndViolating()
        {
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Terrain/terrain_moss_turf.png");

            var ok = new ArtPixelBuffer(2048, 2048, 8, false, null);
            var bad = new ArtPixelBuffer(1024, 2048, 8, false, null);

            AssertConforming(entry, ok, ArtRules.CheckDimensions);
            AssertViolating(entry, bad, "Dimensions", "ART-001", ArtRules.CheckDimensions);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_AlphaPresence_ConformingAndViolating()
        {
            ArtAssetEntry rgbEntry = RequireEntry("Assets/Resources/Art/Terrain/terrain_moss_turf.png");
            var rgbOk = new ArtPixelBuffer(2048, 2048, 8, false, null);
            var rgbBad = new ArtPixelBuffer(2048, 2048, 8, true, null);
            AssertConforming(rgbEntry, rgbOk, ArtRules.CheckAlphaPresence);
            AssertViolating(rgbEntry, rgbBad, "AlphaPresence", "ART-001", ArtRules.CheckAlphaPresence);

            ArtAssetEntry rgbaEntry = RequireEntry("Assets/Resources/Art/Water/water_shore.png");
            byte[] okPixels = CreateSolidRgba(16, 16, 255);
            okPixels[3] = 0;
            var rgbaOk = new ArtPixelBuffer(16, 16, 8, true, okPixels);
            var rgbaMissingAlpha = new ArtPixelBuffer(16, 16, 8, false, null);
            var rgbaAllOpaque = new ArtPixelBuffer(16, 16, 8, true, CreateSolidRgba(16, 16, 255));
            AssertConforming(rgbaEntry, rgbaOk, ArtRules.CheckAlphaPresence);
            AssertViolating(rgbaEntry, rgbaMissingAlpha, "AlphaPresence", "ART-003", ArtRules.CheckAlphaPresence);
            AssertViolating(rgbaEntry, rgbaAllOpaque, "AlphaPresence", "ART-003", ArtRules.CheckAlphaPresence);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_OuterMarginAndCanvasTransparency_ConformingAndViolating()
        {
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Props/Flora/flora_tussock.png");

            byte[] okPixels = CreateSolidRgba(128, 112, 0);
            FillRectAlpha(okPixels, 128, new RectInt(40, 20, 48, 60), 255);
            var ok = new ArtPixelBuffer(128, 112, 8, true, okPixels);

            byte[] badMarginPixels = (byte[])okPixels.Clone();
            badMarginPixels[(2 * 128 + 2) * 4 + 3] = 200;
            var badMargin = new ArtPixelBuffer(128, 112, 8, true, badMarginPixels);

            byte[] badRatioPixels = CreateSolidRgba(128, 112, 255);
            FillRectAlpha(badRatioPixels, 128, new RectInt(0, 0, 128, 4), 0);
            FillRectAlpha(badRatioPixels, 128, new RectInt(0, 108, 128, 4), 0);
            var badRatio = new ArtPixelBuffer(128, 112, 8, true, badRatioPixels);

            AssertConforming(entry, ok, ArtRules.CheckOuterMarginTransparent);
            AssertViolating(entry, badMargin, "OuterMarginTransparent", "ART-007", ArtRules.CheckOuterMarginTransparent);

            AssertConforming(entry, ok, ArtRules.CheckCanvasTransparentRatio);
            AssertViolating(entry, badRatio, "CanvasTransparencyRatio", "ART-007", ArtRules.CheckCanvasTransparentRatio);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_TopOpaqueRangeAndOpaqueNearPivot_ConformingAndViolating()
        {
            // flora_tussock: 128x112, pivot (64, 32), top range [32, 67] => highest y in [64, 99]
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Props/Flora/flora_tussock.png");

            byte[] okPixels = CreateSolidRgba(128, 112, 0);
            FillRectAlpha(okPixels, 128, new RectInt(56, 24, 16, 56), 255); // top y = 79 -> 47 above pivot
            var ok = new ArtPixelBuffer(128, 112, 8, true, okPixels);

            byte[] tooHighPixels = (byte[])okPixels.Clone();
            FillRectAlpha(tooHighPixels, 128, new RectInt(60, 105, 8, 2), 255); // 74 above pivot > 67
            var tooHigh = new ArtPixelBuffer(128, 112, 8, true, tooHighPixels);

            byte[] noPivotPixels = CreateSolidRgba(128, 112, 0);
            FillRectAlpha(noPivotPixels, 128, new RectInt(10, 70, 12, 12), 255); // far from pivot (64,32)
            var noPivot = new ArtPixelBuffer(128, 112, 8, true, noPivotPixels);

            AssertConforming(entry, ok, ArtRules.CheckTopOpaqueRange);
            AssertViolating(entry, tooHigh, "TopOpaqueRange", "ART-007", ArtRules.CheckTopOpaqueRange);

            AssertConforming(entry, ok, ArtRules.CheckOpaqueNearPivot);
            AssertViolating(entry, noPivot, "OpaqueNearPivot", "ART-007", ArtRules.CheckOpaqueNearPivot);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_BankLipEndFade_ConformingAndViolating()
        {
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Props/Banks/bank_lip.png");

            byte[] okPixels = CreateSolidRgba(384, 192, 0);
            for (int x = 38; x < 346; x++)
            {
                FillRectAlpha(okPixels, 384, new RectInt(x, 60, 1, 80), 255);
            }

            for (int i = 0; i < 38; i++)
            {
                byte a = i < 4 ? (byte)0 : (byte)(i * 255 / 38);
                FillRectAlpha(okPixels, 384, new RectInt(i, 60, 1, 80), a);
                FillRectAlpha(okPixels, 384, new RectInt(383 - i, 60, 1, 80), a);
            }

            byte[] badPixels = CreateSolidRgba(384, 192, 0);
            FillRectAlpha(badPixels, 384, new RectInt(4, 60, 376, 80), 255); // hard step at x=4, no 38px fade

            AssertConforming(entry, new ArtPixelBuffer(384, 192, 8, true, okPixels), ArtRules.CheckBankLipEndFade);
            AssertViolating(entry, new ArtPixelBuffer(384, 192, 8, true, badPixels), "BankLipEndFade", "ART-002", ArtRules.CheckBankLipEndFade);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_ShadowMaxAlpha_ConformingAndViolating()
        {
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Props/Shadows/shadow_soft.png");

            byte[] okPixels = CreateSolidRgba(512, 320, 0);
            FillRectAlpha(okPixels, 512, new RectInt(100, 100, 200, 100), 120);

            byte[] badPixels = (byte[])okPixels.Clone();
            badPixels[(150 * 512 + 150) * 4 + 3] = 200; // > 0.50 (127)

            AssertConforming(entry, new ArtPixelBuffer(512, 320, 8, true, okPixels), ArtRules.CheckMaxAlpha);
            AssertViolating(entry, new ArtPixelBuffer(512, 320, 8, true, badPixels), "ShadowMaxAlpha", "ART-008", ArtRules.CheckMaxAlpha);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_SkyBottomRowColor_ConformingAndViolating()
        {
            ArtAssetEntry entry = RequireEntry("Assets/Resources/Art/Backdrop/backdrop_sky.png");

            byte[] okPixels = new byte[16 * 1024 * 3];
            for (int x = 0; x < 16; x++)
            {
                int idx = x * 3;
                okPixels[idx] = 0xEF;
                okPixels[idx + 1] = 0xD3;
                okPixels[idx + 2] = 0xA0;
            }

            byte[] badPixels = (byte[])okPixels.Clone();
            badPixels[0] = 0x10;

            AssertConforming(entry, new ArtPixelBuffer(16, 1024, 8, false, okPixels), ArtRules.CheckBottomRowHex);
            AssertViolating(entry, new ArtPixelBuffer(16, 1024, 8, false, badPixels), "SkyBottomRowColor", "ART-010", ArtRules.CheckBottomRowHex);
        }

        // T-ART-05
        [Test]
        public void T_ART_05_ButtonIdenticalSilhouettes_ConformingAndViolating()
        {
            byte[] normalPx = CreateSolidRgba(32, 16, 0);
            FillRectAlpha(normalPx, 32, new RectInt(4, 4, 24, 8), 200);
            byte[] hoverPx = CreateSolidRgba(32, 16, 0);
            FillRectAlpha(hoverPx, 32, new RectInt(4, 4, 24, 8), 255);
            byte[] pressedPx = (byte[])hoverPx.Clone();

            var normal = new ArtPixelBuffer(32, 16, 8, true, normalPx);
            var hover = new ArtPixelBuffer(32, 16, 8, true, hoverPx);
            var pressed = new ArtPixelBuffer(32, 16, 8, true, pressedPx);

            var okIssues = new List<ArtValidationIssue>();
            ArtRules.CheckButtonSilhouettes(normal, hover, pressed, okIssues);
            Assert.That(okIssues, Is.Empty);

            byte[] badHoverPx = (byte[])hoverPx.Clone();
            badHoverPx[(8 * 32 + 1) * 4 + 3] = 255; // protrudes outside normal silhouette
            var badHover = new ArtPixelBuffer(32, 16, 8, true, badHoverPx);

            var badIssues = new List<ArtValidationIssue>();
            ArtRules.CheckButtonSilhouettes(normal, badHover, pressed, badIssues);
            AssertHasViolation(badIssues, "ButtonIdenticalSilhouettes", "ART-009");
        }

        // T-ART-05
        [Test]
        public void T_ART_05_ImportSettingsPivotsAndBorders_ConformingAndViolating()
        {
            ArtAssetEntry pine = RequireEntry("Assets/Resources/Art/Props/Pines/pine_leaning.png");
            ArtImportSnapshot pineOk = ArtImportSnapshot.FromSpec(pine.ImportSpec);
            var pineOkIssues = new List<ArtValidationIssue>();
            ArtRules.CheckImportSettings(pine, pineOk, pineOkIssues);
            Assert.That(pineOkIssues, Is.Empty);

            ArtImportSnapshot pineBadPivot = pineOk;
            pineBadPivot.Pivot = new Vector2(0.5f, 0.5f);
            var pineBadIssues = new List<ArtValidationIssue>();
            ArtRules.CheckImportSettings(pine, pineBadPivot, pineBadIssues);
            AssertHasViolation(pineBadIssues, "ImportSettings", "ART-004");

            ArtAssetEntry panel = RequireEntry("Assets/Resources/Art/UI/overlay_panel.png");
            ArtImportSnapshot panelOk = ArtImportSnapshot.FromSpec(panel.ImportSpec);
            var panelOkIssues = new List<ArtValidationIssue>();
            ArtRules.CheckImportSettings(panel, panelOk, panelOkIssues);
            Assert.That(panelOkIssues, Is.Empty);

            ArtImportSnapshot panelBadBorder = panelOk;
            panelBadBorder.SpriteBorder = new Vector4(32f, 32f, 32f, 32f);
            var panelBadIssues = new List<ArtValidationIssue>();
            ArtRules.CheckImportSettings(panel, panelBadBorder, panelBadIssues);
            AssertHasViolation(panelBadIssues, "ImportSettings", "ART-009");
        }

        // T-ART-05
        [Test]
        public void T_ART_05_FontAndReadmeAndModelRules_ConformingAndViolating()
        {
            var okFont = new FontDirectorySnapshot
            {
                FontFileNames = new[] { "TestSerif.ttf" },
                Font = new FontSnapshot(true, null, false, new[] { new CodepointRange(0x0020, 0x007E) }),
                LicenseExists = true,
                LicenseNonEmpty = true
            };
            var okFontIssues = new List<ArtValidationIssue>();
            ArtRules.CheckFont(okFont, okFontIssues);
            Assert.That(okFontIssues, Is.Empty);

            var badVarFont = okFont;
            badVarFont.Font = new FontSnapshot(true, null, true, new[] { new CodepointRange(0x0020, 0x007E) });
            var badFontIssues = new List<ArtValidationIssue>();
            ArtRules.CheckFont(badVarFont, badFontIssues);
            AssertHasViolation(badFontIssues, "FontNonVariable", "ART-011");

            string okReadme = BuildFullReadme("TestSerif.ttf");
            var okReadmeIssues = new List<ArtValidationIssue>();
            ArtRules.CheckArtReadme(okReadme, "TestSerif.ttf", okReadmeIssues);
            Assert.That(okReadmeIssues, Is.Empty);

            string missingRowReadme = okReadme.Replace("ART-005", "ART-XXX");
            var badReadmeIssues = new List<ArtValidationIssue>();
            ArtRules.CheckArtReadme(missingRowReadme, "TestSerif.ttf", badReadmeIssues);
            AssertHasViolation(badReadmeIssues, "ArtReadmeRow", "ART-005");

            var cleanPaths = new[] { "Assets/Resources/Art/Terrain/terrain_moss_turf.png", "Assets/Scripts/Core/World.cs" };
            var okModelIssues = new List<ArtValidationIssue>();
            ArtRules.CheckNoModelFiles(cleanPaths, okModelIssues);
            Assert.That(okModelIssues, Is.Empty);

            var badPaths = new[] { "Assets/Art/Bad/pine.fbx" };
            var badModelIssues = new List<ArtValidationIssue>();
            ArtRules.CheckNoModelFiles(badPaths, badModelIssues);
            AssertHasViolation(badModelIssues, "NoModelFiles", "ART-000");
        }

        // T-ART-05
        [Test]
        public void T_ART_05_SpriteAtlasAndTmpAsset_ConformingAndViolating()
        {
            var expectedSprites = new List<string> { "pine_ancient", "shadow_soft" };
            var okAtlas = new SpriteAtlasSnapshot
            {
                Exists = true,
                PageCount = 1,
                MaxTextureSize = 8192,
                Padding = 8,
                AllowRotation = false,
                TightPacking = false,
                GenerateMipMaps = true,
                FilterMode = FilterMode.Trilinear,
                Srgb = true,
                Compression = TextureImporterCompression.CompressedHQ,
                IncludeInBuild = true,
                SpriteNames = new HashSet<string>(expectedSprites)
            };
            var okAtlasIssues = new List<ArtValidationIssue>();
            ArtRules.CheckSpriteAtlas(okAtlas, expectedSprites, okAtlasIssues);
            Assert.That(okAtlasIssues, Is.Empty);

            var badAtlas = okAtlas;
            badAtlas.PageCount = 2;
            var badAtlasIssues = new List<ArtValidationIssue>();
            ArtRules.CheckSpriteAtlas(badAtlas, expectedSprites, badAtlasIssues);
            AssertHasViolation(badAtlasIssues, "SpriteAtlasSinglePage", "ART-004");

            var okTmp = new TmpFontAssetSnapshot
            {
                Exists = true,
                IsSdfAa = true,
                AtlasWidth = 2048,
                AtlasHeight = 2048,
                Padding = 9,
                SamplingPointSize = 96,
                CoversAscii = true
            };
            var okTmpIssues = new List<ArtValidationIssue>();
            ArtRules.CheckTmpFontAsset(okTmp, okTmpIssues);
            Assert.That(okTmpIssues, Is.Empty);

            var badTmp = okTmp;
            badTmp.Padding = 4;
            var badTmpIssues = new List<ArtValidationIssue>();
            ArtRules.CheckTmpFontAsset(badTmp, badTmpIssues);
            AssertHasViolation(badTmpIssues, "TmpFontAssetSettings", "ART-011");
        }

        private static ArtAssetEntry RequireEntry(string assetPath)
        {
            Assert.That(ArtManifest.TryGetByPath(assetPath, out ArtAssetEntry entry), Is.True, $"Missing manifest entry: {assetPath}");
            return entry;
        }

        private static void AssertConforming(
            ArtAssetEntry entry,
            ArtPixelBuffer buffer,
            System.Action<ArtAssetEntry, ArtPixelBuffer, IList<ArtValidationIssue>> check)
        {
            var issues = new List<ArtValidationIssue>();
            check(entry, buffer, issues);
            Assert.That(issues, Is.Empty);
        }

        private static void AssertViolating(
            ArtAssetEntry entry,
            ArtPixelBuffer buffer,
            string expectedRule,
            string expectedArtId,
            System.Action<ArtAssetEntry, ArtPixelBuffer, IList<ArtValidationIssue>> check)
        {
            var issues = new List<ArtValidationIssue>();
            check(entry, buffer, issues);
            AssertHasViolation(issues, expectedRule, expectedArtId);
        }

        private static void AssertHasViolation(
            IList<ArtValidationIssue> issues,
            string expectedRule,
            string expectedArtId)
        {
            Assert.That(issues.Count, Is.GreaterThan(0));
            bool found = false;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].RuleName == expectedRule && issues[i].ArtId == expectedArtId)
                {
                    found = true;
                    Assert.That(issues[i].ToString(), Does.Contain(expectedRule).And.Contain(expectedArtId));
                    break;
                }
            }

            Assert.That(found, Is.True, $"Expected ({expectedArtId}, {expectedRule}) in [{string.Join("; ", issues)}]");
        }

        private static byte[] CreateSolidRgba(int width, int height, byte alpha)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = alpha;
            }

            return pixels;
        }

        private static void FillRectAlpha(byte[] pixels, int width, RectInt rect, byte alpha)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    pixels[(y * width + x) * 4 + 3] = alpha;
                }
            }
        }

        private static string BuildFullReadme(string fontFileName)
        {
            var lines = new List<string> { $"Font: {fontFileName}", "| ID | Notes |", "|---|---|" };
            for (int i = 0; i < ArtManifest.RequiredArtIds.Length; i++)
            {
                lines.Add($"| {ArtManifest.RequiredArtIds[i]} | Test |");
            }

            return string.Join("\n", lines);
        }
    }
}
