using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Murim.Editor
{
    /// <summary>
    /// Enforces the §4 texture and sprite import contract for all assets under Assets/Resources/Art/.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        private const string ArtRootPrefix = "Assets/Resources/Art/";
        private const int MaxTextureSize = 2048;
        private const float WorldSpritePixelsPerUnit = 128f;
        private const float UiSpritePixelsPerUnit = 100f;

        private readonly struct DefaultTextureRule
        {
            public readonly bool Srgb;
            public readonly bool MipmapEnabled;
            public readonly FilterMode FilterMode;
            public readonly TextureWrapMode WrapU;
            public readonly TextureWrapMode WrapV;
            public readonly TextureImporterCompression Compression;
            public readonly bool AlphaIsTransparency;

            public DefaultTextureRule(
                bool srgb,
                bool mipmapEnabled,
                FilterMode filterMode,
                TextureWrapMode wrapU,
                TextureWrapMode wrapV,
                TextureImporterCompression compression,
                bool alphaIsTransparency)
            {
                Srgb = srgb;
                MipmapEnabled = mipmapEnabled;
                FilterMode = filterMode;
                WrapU = wrapU;
                WrapV = wrapV;
                Compression = compression;
                AlphaIsTransparency = alphaIsTransparency;
            }
        }

        private static readonly Dictionary<string, Vector2> PropPivots =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase)
            {
                { "cliff_crest.png", new Vector2(256f / 512f, 40f / 384f) },
                { "cliff_toe.png", new Vector2(256f / 512f, 40f / 256f) },
                { "boulder_upright.png", new Vector2(160f / 320f, 40f / 320f) },
                { "boulder_squat.png", new Vector2(224f / 448f, 40f / 320f) },
                { "boulder_low.png", new Vector2(160f / 320f, 40f / 224f) },
                { "pine_upright.png", new Vector2(384f / 768f, 40f / 1024f) },
                { "pine_leaning.png", new Vector2(384f / 896f, 40f / 960f) },
                { "pine_spreading.png", new Vector2(512f / 1024f, 40f / 896f) },
                { "bamboo_upright.png", new Vector2(192f / 384f, 40f / 768f) },
                { "bamboo_leaning.png", new Vector2(224f / 512f, 40f / 768f) },
                { "bamboo_edge.png", new Vector2(192f / 384f, 40f / 512f) },
                { "shrub_round.png", new Vector2(96f / 192f, 32f / 192f) },
                { "shrub_spreading.png", new Vector2(128f / 256f, 32f / 160f) },
                { "grass_short.png", new Vector2(64f / 128f, 12f / 64f) },
                { "grass_tall.png", new Vector2(64f / 128f, 16f / 128f) },
                { "grass_sparse.png", new Vector2(64f / 128f, 16f / 96f) },
                { "flowers_white.png", new Vector2(64f / 128f, 16f / 96f) },
                { "flowers_yellow.png", new Vector2(64f / 128f, 16f / 96f) },
                { "placeholder_capsule.png", new Vector2(64f / 128f, 32f / 256f) }
            };

        private static readonly Dictionary<string, (TextureWrapMode wrapU, TextureWrapMode wrapV, bool alpha)> WaterRules =
            new Dictionary<string, (TextureWrapMode, TextureWrapMode, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                { "river_flow.png", (TextureWrapMode.Repeat, TextureWrapMode.Repeat, false) },
                { "river_riffle.png", (TextureWrapMode.Repeat, TextureWrapMode.Repeat, true) },
                { "wet_rock.png", (TextureWrapMode.Repeat, TextureWrapMode.Repeat, false) },
                { "river_shore.png", (TextureWrapMode.Repeat, TextureWrapMode.Clamp, true) },
                { "fall_ribbon.png", (TextureWrapMode.Clamp, TextureWrapMode.Repeat, true) },
                { "river_rocks.png", (TextureWrapMode.Clamp, TextureWrapMode.Clamp, true) },
                { "fall_foam.png", (TextureWrapMode.Clamp, TextureWrapMode.Clamp, true) }
            };

        private void OnPreprocessTexture()
        {
            string normalizedPath = assetPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(ArtRootPrefix, StringComparison.Ordinal))
            {
                return;
            }

            TextureImporter importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            string relativePath = normalizedPath.Substring(ArtRootPrefix.Length);
            string fileName = Path.GetFileName(relativePath);

            if (relativePath.StartsWith("Terrain/", StringComparison.Ordinal))
            {
                ApplyTerrainSettings(importer, fileName);
            }
            else if (relativePath.StartsWith("Water/", StringComparison.Ordinal))
            {
                ApplyWaterSettings(importer, fileName);
            }
            else if (relativePath.StartsWith("Overlay/", StringComparison.Ordinal))
            {
                ApplyOverlaySettings(importer);
            }
            else if (relativePath.StartsWith("Props/", StringComparison.Ordinal))
            {
                ApplyPropSettings(importer, fileName);
            }
            else if (relativePath.StartsWith("UI/", StringComparison.Ordinal))
            {
                ApplyUiSettings(importer, fileName);
            }
        }

        private static void ApplyTerrainSettings(TextureImporter importer, string fileName)
        {
            bool srgb = !fileName.StartsWith("blend_", StringComparison.OrdinalIgnoreCase);
            DefaultTextureRule rule = new DefaultTextureRule(
                srgb,
                mipmapEnabled: true,
                FilterMode.Trilinear,
                TextureWrapMode.Repeat,
                TextureWrapMode.Repeat,
                TextureImporterCompression.CompressedHQ,
                alphaIsTransparency: false);
            ApplyDefaultTextureSettings(importer, rule);
        }

        private static void ApplyWaterSettings(TextureImporter importer, string fileName)
        {
            if (!WaterRules.TryGetValue(fileName, out var water))
            {
                water = (TextureWrapMode.Clamp, TextureWrapMode.Clamp, importer.DoesSourceTextureHaveAlpha());
            }

            DefaultTextureRule rule = new DefaultTextureRule(
                srgb: true,
                mipmapEnabled: true,
                FilterMode.Trilinear,
                water.wrapU,
                water.wrapV,
                TextureImporterCompression.CompressedHQ,
                water.alpha);
            ApplyDefaultTextureSettings(importer, rule);
        }

        private static void ApplyOverlaySettings(TextureImporter importer)
        {
            DefaultTextureRule rule = new DefaultTextureRule(
                srgb: true,
                mipmapEnabled: true,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                TextureWrapMode.Clamp,
                TextureImporterCompression.Uncompressed,
                alphaIsTransparency: true);
            ApplyDefaultTextureSettings(importer, rule);
        }

        private static void ApplyDefaultTextureSettings(TextureImporter importer, DefaultTextureRule rule)
        {
            importer.textureType = TextureImporterType.Default;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.sRGBTexture = rule.Srgb;
            settings.mipmapEnabled = rule.MipmapEnabled;
            settings.filterMode = rule.FilterMode;
            settings.wrapModeU = rule.WrapU;
            settings.wrapModeV = rule.WrapV;
            settings.readable = false;
            settings.alphaIsTransparency = rule.AlphaIsTransparency;
            importer.SetTextureSettings(settings);

            importer.textureCompression = rule.Compression;
            importer.maxTextureSize = MaxTextureSize;
        }

        private static void ApplyPropSettings(TextureImporter importer, string fileName)
        {
            importer.textureType = TextureImporterType.Sprite;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spritePixelsPerUnit = WorldSpritePixelsPerUnit;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteBorder = Vector4.zero;
            settings.sRGBTexture = true;
            settings.mipmapEnabled = true;
            settings.filterMode = FilterMode.Trilinear;
            settings.wrapModeU = TextureWrapMode.Clamp;
            settings.wrapModeV = TextureWrapMode.Clamp;
            settings.readable = true;
            settings.alphaIsTransparency = true;

            if (PropPivots.TryGetValue(fileName, out Vector2 pivot))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
            }
            else
            {
                Debug.LogWarning($"[Murim] ArtImportPostprocessor: unrecognized prop filename '{fileName}'.");
            }

            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = MaxTextureSize;
        }

        private static void ApplyUiSettings(TextureImporter importer, string fileName)
        {
            importer.textureType = TextureImporterType.Sprite;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spritePixelsPerUnit = UiSpritePixelsPerUnit;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.spriteBorder = ResolveUiBorder(fileName);
            settings.sRGBTexture = true;
            settings.mipmapEnabled = false;
            settings.filterMode = FilterMode.Bilinear;
            settings.wrapModeU = TextureWrapMode.Clamp;
            settings.wrapModeV = TextureWrapMode.Clamp;
            settings.readable = false;
            settings.alphaIsTransparency = true;
            importer.SetTextureSettings(settings);

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = MaxTextureSize;
        }

        private static Vector4 ResolveUiBorder(string fileName)
        {
            if (string.Equals(fileName, "ui_label.png", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(16f, 16f, 16f, 16f);
            }
            if (string.Equals(fileName, "ui_panel.png", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(24f, 24f, 24f, 24f);
            }
            if (fileName.StartsWith("ui_button_", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(12f, 12f, 12f, 12f);
            }

            Debug.LogWarning($"[Murim] ArtImportPostprocessor: unrecognized UI filename '{fileName}'.");
            return Vector4.zero;
        }
    }
}
