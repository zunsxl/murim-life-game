using System;
using Murim.Editor.Art;
using UnityEditor;
using UnityEngine;

namespace Murim.Editor
{
    /// <summary>
    /// Enforces v0.1.0 §4 texture and sprite import settings exclusively under Assets/Resources/Art/.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith(ArtManifest.ArtRootPrefix, StringComparison.Ordinal))
            {
                return;
            }

            if (!ArtManifest.TryGetByPath(assetPath, out ArtAssetEntry entry))
            {
                return;
            }

            var importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            ApplyImportSpec(importer, entry.ImportSpec);
        }

        public static void ApplyImportSpec(TextureImporter importer, ArtImportSpec spec)
        {
            importer.textureType = spec.TextureType;
            importer.sRGBTexture = spec.Srgb;
            importer.mipmapEnabled = spec.Mipmaps;
            importer.filterMode = spec.FilterMode;
            importer.wrapModeU = spec.WrapU;
            importer.wrapModeV = spec.WrapV;
            importer.textureCompression = spec.Compression;
            importer.maxTextureSize = spec.MaxTextureSize;
            importer.alphaIsTransparency = spec.AlphaIsTransparency;
            importer.npotScale = TextureImporterNPOTScale.None;

            if (spec.TextureType == TextureImporterType.Sprite)
            {
                ApplySpriteSettings(importer, spec);
            }
        }

        private static void ApplySpriteSettings(TextureImporter importer, ArtImportSpec spec)
        {
            importer.spriteImportMode = spec.SpriteMode;
            importer.spritePixelsToUnits = spec.PixelsPerUnit;
            importer.spriteBorder = spec.SpriteBorder;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)spec.SpriteMode;
            settings.spriteMeshType = spec.MeshType;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = spec.Pivot;
            settings.spritePixelsPerUnit = spec.PixelsPerUnit;
            settings.spriteBorder = spec.SpriteBorder;
            settings.wrapModeU = spec.WrapU;
            settings.wrapModeV = spec.WrapV;
            settings.filterMode = spec.FilterMode;
            settings.mipmapEnabled = spec.Mipmaps;
            settings.sRGBTexture = spec.Srgb;
            settings.alphaIsTransparency = spec.AlphaIsTransparency;
            importer.SetTextureSettings(settings);
        }

        public static void ReimportAllArt()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (ArtManifest.TryGetByPath(path, out _))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            AssetDatabase.SaveAssets();
        }
    }
}
