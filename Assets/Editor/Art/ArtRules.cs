using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Murim.Editor.Art
{
    public readonly struct ArtValidationIssue
    {
        public string ArtId { get; }
        public string RuleName { get; }
        public string FileName { get; }
        public string Message { get; }

        public ArtValidationIssue(string artId, string ruleName, string fileName, string message)
        {
            ArtId = artId;
            RuleName = ruleName;
            FileName = fileName;
            Message = message;
        }

        public override string ToString()
        {
            return $"[{ArtId}] {RuleName} ({FileName}): {Message}";
        }
    }

    public struct ArtImportSnapshot
    {
        public TextureImporterType TextureType;
        public SpriteImportMode SpriteMode;
        public int PixelsPerUnit;
        public SpriteMeshType MeshType;
        public bool Srgb;
        public bool Mipmaps;
        public FilterMode FilterMode;
        public TextureWrapMode WrapU;
        public TextureWrapMode WrapV;
        public TextureImporterCompression Compression;
        public int MaxTextureSize;
        public bool AlphaIsTransparency;
        public Vector2 Pivot;
        public Vector4 SpriteBorder;

        public static ArtImportSnapshot FromSpec(ArtImportSpec spec)
        {
            return new ArtImportSnapshot
            {
                TextureType = spec.TextureType,
                SpriteMode = spec.SpriteMode,
                PixelsPerUnit = spec.PixelsPerUnit,
                MeshType = spec.MeshType,
                Srgb = spec.Srgb,
                Mipmaps = spec.Mipmaps,
                FilterMode = spec.FilterMode,
                WrapU = spec.WrapU,
                WrapV = spec.WrapV,
                Compression = spec.Compression,
                MaxTextureSize = spec.MaxTextureSize,
                AlphaIsTransparency = spec.AlphaIsTransparency,
                Pivot = spec.Pivot,
                SpriteBorder = spec.SpriteBorder
            };
        }

        public static ArtImportSnapshot FromImporter(TextureImporter importer)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            return new ArtImportSnapshot
            {
                TextureType = importer.textureType,
                SpriteMode = importer.spriteImportMode,
                PixelsPerUnit = Mathf.RoundToInt(importer.spritePixelsToUnits),
                MeshType = settings.spriteMeshType,
                Srgb = importer.sRGBTexture,
                Mipmaps = importer.mipmapEnabled,
                FilterMode = importer.filterMode,
                WrapU = importer.wrapModeU,
                WrapV = importer.wrapModeV,
                Compression = importer.textureCompression,
                MaxTextureSize = importer.maxTextureSize,
                AlphaIsTransparency = importer.alphaIsTransparency,
                Pivot = importer.spritePivot,
                SpriteBorder = importer.spriteBorder
            };
        }
    }

    public struct FontDirectorySnapshot
    {
        public IReadOnlyList<string> FontFileNames;
        public FontSnapshot Font;
        public bool LicenseExists;
        public bool LicenseNonEmpty;
    }

    public struct SpriteAtlasSnapshot
    {
        public bool Exists;
        public int PageCount;
        public int MaxTextureSize;
        public int Padding;
        public bool AllowRotation;
        public bool TightPacking;
        public bool GenerateMipMaps;
        public FilterMode FilterMode;
        public bool Srgb;
        public TextureImporterCompression Compression;
        public bool IncludeInBuild;
        public HashSet<string> SpriteNames;
    }

    public struct TmpFontAssetSnapshot
    {
        public bool Exists;
        public bool IsSdfAa;
        public int AtlasWidth;
        public int AtlasHeight;
        public int Padding;
        public int SamplingPointSize;
        public bool CoversAscii;
    }

    public static class ArtRules
    {
        private const byte OpaqueThreshold = 128;

        public static void ValidateImage(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            CheckDimensions(entry, image, issues);
            CheckAlphaPresence(entry, image, issues);

            if (image.Pixels == null || image.Width <= 0 || image.Height <= 0)
            {
                return;
            }

            CheckOuterMarginTransparent(entry, image, issues);
            CheckCanvasTransparentRatio(entry, image, issues);
            CheckTopOpaqueRange(entry, image, issues);
            CheckOpaqueNearPivot(entry, image, issues);
            CheckBankLipEndFade(entry, image, issues);
            CheckMaxAlpha(entry, image, issues);
            CheckBottomRowHex(entry, image, issues);
        }

        public static void CheckDimensions(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (image.Width == entry.Width && image.Height == entry.Height)
            {
                return;
            }

            AddIssue(
                issues,
                entry,
                "Dimensions",
                $"Expected {entry.Width}x{entry.Height}, got {image.Width}x{image.Height}.");
        }

        public static void CheckAlphaPresence(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (!entry.RequiresAlpha)
            {
                if (image.HasAlpha)
                {
                    AddIssue(issues, entry, "AlphaPresence", "Expected RGB without alpha channel, but alpha is present.");
                }

                return;
            }

            if (!image.HasAlpha)
            {
                AddIssue(issues, entry, "AlphaPresence", "Expected RGBA with alpha channel, but alpha is absent.");
                return;
            }

            if (image.Pixels != null)
            {
                CheckAlphaRange(entry, image, issues);
            }
        }

        private static void CheckAlphaRange(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            byte minA = 255;
            byte maxA = 0;
            for (int i = 3; i < image.Pixels.Length; i += 4)
            {
                byte a = image.Pixels[i];
                if (a < minA)
                {
                    minA = a;
                }

                if (a > maxA)
                {
                    maxA = a;
                }
            }

            if (maxA == 0)
            {
                AddIssue(issues, entry, "AlphaPresence", "Image is completely transparent (all alpha == 0).");
            }
            else if (minA == 255)
            {
                AddIssue(issues, entry, "AlphaPresence", "RGBA image has no transparency (all alpha == 255).");
            }
        }

        public static void CheckOuterMarginTransparent(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (!entry.RequireOuter4Transparent || !image.HasAlpha)
            {
                return;
            }

            int margin = ArtManifest.OuterTransparentMarginPx;
            int w = image.Width;
            int h = image.Height;
            for (int y = 0; y < h; y++)
            {
                bool edgeY = y < margin || y >= h - margin;
                for (int x = 0; x < w; x++)
                {
                    if ((edgeY || x < margin || x >= w - margin) && image.GetAlpha(x, y) != 0)
                    {
                        AddIssue(issues, entry, "OuterMarginTransparent", $"Non-zero alpha in outer {margin}px margin at ({x},{y}).");
                        return;
                    }
                }
            }
        }

        public static void CheckCanvasTransparentRatio(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (!entry.Require20PctTransparent || !image.HasAlpha)
            {
                return;
            }

            int total = image.Width * image.Height;
            if (total <= 0)
            {
                return;
            }

            int transparentCount = 0;
            for (int i = 3; i < image.Pixels.Length; i += 4)
            {
                if (image.Pixels[i] == 0)
                {
                    transparentCount++;
                }
            }

            float ratio = (float)transparentCount / total;
            if (ratio < ArtManifest.MinTransparentCanvasFraction)
            {
                AddIssue(
                    issues,
                    entry,
                    "CanvasTransparencyRatio",
                    $"Transparent fraction {ratio:P1} is below required {ArtManifest.MinTransparentCanvasFraction:P0}.");
            }
        }

        public static void CheckTopOpaqueRange(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (!entry.HasTopRange || !image.HasAlpha)
            {
                return;
            }

            int highestY = FindHighestOpaqueRow(image);
            if (highestY < 0)
            {
                AddIssue(issues, entry, "TopOpaqueRange", "No opaque pixels found.");
                return;
            }

            int topAbovePivot = highestY - entry.PivotPxY;
            if (topAbovePivot < entry.TopMinPx || topAbovePivot > entry.TopMaxPx)
            {
                AddIssue(
                    issues,
                    entry,
                    "TopOpaqueRange",
                    $"Top opaque pixel at {topAbovePivot}px above pivot is outside [{entry.TopMinPx}, {entry.TopMaxPx}].");
            }
        }

        private static int FindHighestOpaqueRow(ArtPixelBuffer image)
        {
            for (int y = image.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.GetAlpha(x, y) >= OpaqueThreshold)
                    {
                        return y;
                    }
                }
            }

            return -1;
        }

        public static void CheckOpaqueNearPivot(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (!entry.RequireOpaqueNearPivot || !image.HasAlpha)
            {
                return;
            }

            int r = ArtManifest.PivotOpaqueRadiusPx;
            int minX = Math.Max(0, entry.PivotPxX - r);
            int maxX = Math.Min(image.Width - 1, entry.PivotPxX + r);
            int minY = Math.Max(0, entry.PivotPxY - r);
            int maxY = Math.Min(image.Height - 1, entry.PivotPxY + r);
            int rSq = r * r;

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - entry.PivotPxY;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - entry.PivotPxX;
                    if (dx * dx + dy * dy <= rSq && image.GetAlpha(x, y) >= OpaqueThreshold)
                    {
                        return;
                    }
                }
            }

            AddIssue(issues, entry, "OpaqueNearPivot", $"No opaque pixel within {r}px of pivot ({entry.PivotPxX},{entry.PivotPxY}).");
        }

        public static void CheckBankLipEndFade(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            int fade = entry.EndFadePx;
            if (fade <= 0 || !image.HasAlpha || image.Width < fade * 2 + 1)
            {
                return;
            }

            byte[] colMax = ComputeColumnMaxAlpha(image);
            if (colMax[0] != 0 || colMax[image.Width - 1] != 0)
            {
                AddIssue(issues, entry, "BankLipEndFade", "Leftmost or rightmost column is not alpha 0.");
                return;
            }

            if (!IsSideFading(colMax, 0, 1, fade) || !IsSideFading(colMax, image.Width - 1, -1, fade))
            {
                AddIssue(issues, entry, "BankLipEndFade", $"Outer {fade}px ends do not fade smoothly to alpha 0.");
            }
        }

        private static byte[] ComputeColumnMaxAlpha(ArtPixelBuffer image)
        {
            byte[] colMax = new byte[image.Width];
            for (int x = 0; x < image.Width; x++)
            {
                byte max = 0;
                for (int y = 0; y < image.Height; y++)
                {
                    byte a = image.GetAlpha(x, y);
                    if (a > max)
                    {
                        max = a;
                    }
                }

                colMax[x] = max;
            }

            return colMax;
        }

        private static bool IsSideFading(byte[] colMax, int edgeIdx, int step, int fadePx)
        {
            byte outerHalfMax = 0;
            for (int i = 0; i < fadePx - 1; i++)
            {
                byte cur = colMax[edgeIdx + i * step];
                byte next = colMax[edgeIdx + (i + 1) * step];
                if (cur > next + 2)
                {
                    return false;
                }

                if (i < fadePx / 2 && cur > outerHalfMax)
                {
                    outerHalfMax = cur;
                }
            }

            return outerHalfMax < 200;
        }

        public static void CheckMaxAlpha(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (entry.MaxAlphaNormalized >= 1.0f || !image.HasAlpha)
            {
                return;
            }

            int maxAllowedByte = Mathf.FloorToInt(entry.MaxAlphaNormalized * 255.0f + 1e-4f);
            for (int i = 3; i < image.Pixels.Length; i += 4)
            {
                if (image.Pixels[i] > maxAllowedByte)
                {
                    AddIssue(
                        issues,
                        entry,
                        "ShadowMaxAlpha",
                        $"Alpha byte {image.Pixels[i]} exceeds max {entry.MaxAlphaNormalized:0.00} ({maxAllowedByte}).");
                    return;
                }
            }
        }

        public static void CheckBottomRowHex(
            ArtAssetEntry entry,
            ArtPixelBuffer image,
            IList<ArtValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(entry.ExactBottomRowHex))
            {
                return;
            }

            int rgb = int.Parse(entry.ExactBottomRowHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte expR = (byte)((rgb >> 16) & 0xFF);
            byte expG = (byte)((rgb >> 8) & 0xFF);
            byte expB = (byte)(rgb & 0xFF);

            for (int x = 0; x < image.Width; x++)
            {
                image.GetRgb(x, 0, out byte r, out byte g, out byte b);
                if (r != expR || g != expG || b != expB)
                {
                    AddIssue(
                        issues,
                        entry,
                        "SkyBottomRowColor",
                        $"Bottom row pixel ({x},0) is #{r:X2}{g:X2}{b:X2}, expected #{entry.ExactBottomRowHex}.");
                    return;
                }
            }
        }

        public static void CheckButtonSilhouettes(
            ArtPixelBuffer normal,
            ArtPixelBuffer hover,
            ArtPixelBuffer pressed,
            IList<ArtValidationIssue> issues)
        {
            CheckButtonPair(normal, hover, "overlay_button_hover.png", issues);
            CheckButtonPair(normal, pressed, "overlay_button_pressed.png", issues);
        }

        private static void CheckButtonPair(
            ArtPixelBuffer normal,
            ArtPixelBuffer other,
            string otherName,
            IList<ArtValidationIssue> issues)
        {
            if (normal.Width != other.Width || normal.Height != other.Height)
            {
                issues.Add(new ArtValidationIssue("ART-009", "ButtonIdenticalSilhouettes", otherName, "Dimensions differ from overlay_button_normal.png."));
                return;
            }

            for (int y = 0; y < normal.Height; y++)
            {
                for (int x = 0; x < normal.Width; x++)
                {
                    bool nSolid = normal.GetAlpha(x, y) >= OpaqueThreshold;
                    bool oSolid = other.GetAlpha(x, y) >= OpaqueThreshold;
                    if (nSolid != oSolid)
                    {
                        issues.Add(new ArtValidationIssue("ART-009", "ButtonIdenticalSilhouettes", otherName, $"Alpha silhouette differs at ({x},{y}) for alpha >= 0.5."));
                        return;
                    }
                }
            }
        }

        public static void CheckImportSettings(
            ArtAssetEntry entry,
            ArtImportSnapshot actual,
            IList<ArtValidationIssue> issues)
        {
            CheckCommonImportSettings(entry, actual, issues);
            if (entry.ImportSpec.TextureType == TextureImporterType.Sprite)
            {
                CheckSpriteImportSettings(entry, actual, issues);
            }
        }

        private static void CheckCommonImportSettings(
            ArtAssetEntry entry,
            ArtImportSnapshot actual,
            IList<ArtValidationIssue> issues)
        {
            ArtImportSpec exp = entry.ImportSpec;
            if (actual.TextureType != exp.TextureType)
            {
                AddIssue(issues, entry, "ImportSettings", $"TextureType is {actual.TextureType}, expected {exp.TextureType}.");
            }

            if (actual.Srgb != exp.Srgb || actual.Mipmaps != exp.Mipmaps)
            {
                AddIssue(issues, entry, "ImportSettings", $"sRGB/Mipmaps mismatch (got {actual.Srgb}/{actual.Mipmaps}, expected {exp.Srgb}/{exp.Mipmaps}).");
            }

            if (actual.FilterMode != exp.FilterMode || actual.WrapU != exp.WrapU || actual.WrapV != exp.WrapV)
            {
                AddIssue(issues, entry, "ImportSettings", $"Filter/Wrap mismatch (got {actual.FilterMode} {actual.WrapU}/{actual.WrapV}, expected {exp.FilterMode} {exp.WrapU}/{exp.WrapV}).");
            }

            if (actual.Compression != exp.Compression || actual.MaxTextureSize != exp.MaxTextureSize)
            {
                AddIssue(issues, entry, "ImportSettings", $"Compression/MaxSize mismatch (got {actual.Compression}/{actual.MaxTextureSize}, expected {exp.Compression}/{exp.MaxTextureSize}).");
            }
        }

        private static void CheckSpriteImportSettings(
            ArtAssetEntry entry,
            ArtImportSnapshot actual,
            IList<ArtValidationIssue> issues)
        {
            ArtImportSpec exp = entry.ImportSpec;
            if (actual.SpriteMode != exp.SpriteMode || actual.PixelsPerUnit != exp.PixelsPerUnit || actual.MeshType != exp.MeshType)
            {
                AddIssue(issues, entry, "ImportSettings", $"Sprite mode/PPU/mesh mismatch (got {actual.SpriteMode}/{actual.PixelsPerUnit}/{actual.MeshType}).");
            }

            if (actual.AlphaIsTransparency != exp.AlphaIsTransparency)
            {
                AddIssue(issues, entry, "ImportSettings", $"alphaIsTransparency is {actual.AlphaIsTransparency}, expected {exp.AlphaIsTransparency}.");
            }

            if (Vector4.Distance(actual.SpriteBorder, exp.SpriteBorder) > 0.01f)
            {
                AddIssue(issues, entry, "ImportSettings", $"Sprite border is {actual.SpriteBorder}, expected {exp.SpriteBorder}.");
            }

            if (entry.HasCustomPivot && Vector2.Distance(actual.Pivot, exp.Pivot) > 1e-4f)
            {
                AddIssue(issues, entry, "ImportSettings", $"Sprite pivot is {actual.Pivot}, expected {exp.Pivot}.");
            }
        }

        public static void CheckFont(FontDirectorySnapshot snapshot, IList<ArtValidationIssue> issues)
        {
            int count = snapshot.FontFileNames == null ? 0 : snapshot.FontFileNames.Count;
            if (count != 1)
            {
                issues.Add(new ArtValidationIssue("ART-011", "FontFileCount", "Fonts/", $"Expected exactly 1 .ttf/.otf file, found {count}."));
                return;
            }

            string fileName = snapshot.FontFileNames[0];
            if (!snapshot.Font.Valid)
            {
                issues.Add(new ArtValidationIssue("ART-011", "FontValid", fileName, snapshot.Font.Error ?? "Invalid SFNT font."));
                return;
            }

            if (snapshot.Font.IsVariable)
            {
                issues.Add(new ArtValidationIssue("ART-011", "FontNonVariable", fileName, "Variable font (fvar table present) is not allowed."));
            }

            if (!snapshot.Font.CoversRange(ArtManifest.FontMinCodepoint, ArtManifest.FontMaxCodepoint))
            {
                issues.Add(new ArtValidationIssue("ART-011", "FontAsciiCoverage", fileName, "Font does not cover full U+0020-U+007E range."));
            }

            if (!snapshot.LicenseExists || !snapshot.LicenseNonEmpty)
            {
                string licName = Path.GetFileNameWithoutExtension(fileName) + "-LICENSE.txt";
                issues.Add(new ArtValidationIssue("ART-011", "FontLicenseFile", licName, "Missing or empty font -LICENSE.txt sidecar."));
            }
        }

        public static void CheckArtReadme(
            string readmeText,
            string fontFileName,
            IList<ArtValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(readmeText))
            {
                issues.Add(new ArtValidationIssue("ART-011", "ArtReadme", "ART_README.md", "ART_README.md is missing or empty."));
                return;
            }

            if (!string.IsNullOrEmpty(fontFileName) && readmeText.IndexOf(fontFileName, StringComparison.Ordinal) < 0)
            {
                issues.Add(new ArtValidationIssue("ART-011", "ArtReadmeFontCredit", "ART_README.md", $"ART_README.md does not name font file '{fontFileName}'."));
            }

            for (int i = 0; i < ArtManifest.RequiredArtIds.Length; i++)
            {
                string artId = ArtManifest.RequiredArtIds[i];
                if (!HasReadmeRow(readmeText, artId))
                {
                    issues.Add(new ArtValidationIssue(artId, "ArtReadmeRow", "ART_README.md", $"ART_README.md is missing table row for {artId}."));
                }
            }
        }

        private static bool HasReadmeRow(string readmeText, string artId)
        {
            string[] lines = readmeText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.IndexOf(artId, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static void CheckNoModelFiles(IReadOnlyList<string> assetPaths, IList<ArtValidationIssue> issues)
        {
            if (assetPaths == null)
            {
                return;
            }

            for (int i = 0; i < assetPaths.Count; i++)
            {
                string path = assetPaths[i];
                string ext = Path.GetExtension(path);
                if (IsForbiddenModelExtension(ext))
                {
                    issues.Add(new ArtValidationIssue(
                        "ART-000",
                        "NoModelFiles",
                        Path.GetFileName(path),
                        $"Forbidden 3D model file '{path}' under Assets/ (strictly 2D project)."));
                }
            }
        }

        private static bool IsForbiddenModelExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }

            for (int i = 0; i < ArtManifest.ForbiddenModelExtensions.Length; i++)
            {
                if (string.Equals(ext, ArtManifest.ForbiddenModelExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void CheckSpriteAtlas(
            SpriteAtlasSnapshot atlas,
            IReadOnlyList<string> expectedSpriteNames,
            IList<ArtValidationIssue> issues)
        {
            const string file = "WorldProps.spriteatlas";
            if (!atlas.Exists)
            {
                issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasPresent", file, "WorldProps.spriteatlas is missing (delivered by ARCH-007)."));
                return;
            }

            if (atlas.PageCount != 1)
            {
                issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasSinglePage", file, $"Expected 1 packed page, got {atlas.PageCount}."));
            }

            CheckAtlasSettings(atlas, file, issues);
            CheckAtlasSprites(atlas, expectedSpriteNames, file, issues);
        }

        private static void CheckAtlasSettings(
            SpriteAtlasSnapshot atlas,
            string file,
            IList<ArtValidationIssue> issues)
        {
            if (atlas.MaxTextureSize != ArtManifest.AtlasMaxTextureSize || atlas.Padding != ArtManifest.AtlasPadding)
            {
                issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasSettings", file, $"MaxSize/Padding mismatch ({atlas.MaxTextureSize}/{atlas.Padding})."));
            }

            if (atlas.AllowRotation || atlas.TightPacking || !atlas.IncludeInBuild)
            {
                issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasSettings", file, "Rotation off, tight packing off, and Include in Build on are required."));
            }

            if (!atlas.GenerateMipMaps || atlas.FilterMode != FilterMode.Trilinear || !atlas.Srgb || atlas.Compression != TextureImporterCompression.CompressedHQ)
            {
                issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasSettings", file, "Mipmaps on, Trilinear, sRGB, and High Quality compression are required."));
            }
        }

        private static void CheckAtlasSprites(
            SpriteAtlasSnapshot atlas,
            IReadOnlyList<string> expectedSpriteNames,
            string file,
            IList<ArtValidationIssue> issues)
        {
            if (expectedSpriteNames == null || atlas.SpriteNames == null)
            {
                return;
            }

            for (int i = 0; i < expectedSpriteNames.Count; i++)
            {
                string name = expectedSpriteNames[i];
                if (!atlas.SpriteNames.Contains(name))
                {
                    issues.Add(new ArtValidationIssue("ART-004", "SpriteAtlasMissingSprite", file, $"Atlas does not contain sprite '{name}'."));
                }
            }
        }

        public static void CheckTmpFontAsset(TmpFontAssetSnapshot tmp, IList<ArtValidationIssue> issues)
        {
            const string file = "OverlaySerif SDF.asset";
            if (!tmp.Exists)
            {
                issues.Add(new ArtValidationIssue("ART-011", "TmpFontAssetPresent", file, "OverlaySerif SDF.asset is missing (delivered by ARCH-008)."));
                return;
            }

            if (!tmp.IsSdfAa || tmp.AtlasWidth != ArtManifest.TmpAtlasSize || tmp.AtlasHeight != ArtManifest.TmpAtlasSize)
            {
                issues.Add(new ArtValidationIssue("ART-011", "TmpFontAssetSettings", file, "SDFAA and 2048x2048 atlas size are required."));
            }

            if (tmp.Padding != ArtManifest.TmpPadding || tmp.SamplingPointSize < ArtManifest.TmpMinSamplingPointSize || !tmp.CoversAscii)
            {
                issues.Add(new ArtValidationIssue("ART-011", "TmpFontAssetSettings", file, "Padding 9, sampling point size >= 90, and ASCII charset are required."));
            }
        }

        private static void AddIssue(
            IList<ArtValidationIssue> issues,
            ArtAssetEntry entry,
            string ruleName,
            string message)
        {
            issues.Add(new ArtValidationIssue(
                entry.ArtId,
                ruleName,
                Path.GetFileName(entry.AssetPath),
                message));
        }
    }
}
