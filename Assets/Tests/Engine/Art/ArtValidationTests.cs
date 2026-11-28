using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Murim.View.World;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Murim.Tests.Engine.Art
{
    [TestFixture]
    [Category("Art")]
    public sealed class ArtValidationTests
    {
        private enum AssetCategory : byte
        {
            TerrainTile,
            TerrainMask,
            Water,
            Overlay,
            Prop,
            Ui
        }

        private readonly struct ContractEntry
        {
            public readonly ArtAssetId Id;
            public readonly string ArtFamily;
            public readonly string RelativePath;
            public readonly int Width;
            public readonly int Height;
            public readonly bool HasAlpha;
            public readonly AssetCategory Category;
            public readonly TextureWrapMode WrapU;
            public readonly TextureWrapMode WrapV;
            public readonly Vector2 PivotPx;
            public readonly Vector4 UiBorder;

            public ContractEntry(
                ArtAssetId id,
                string artFamily,
                string relativePath,
                int width,
                int height,
                bool hasAlpha,
                AssetCategory category,
                TextureWrapMode wrapU,
                TextureWrapMode wrapV,
                Vector2 pivotPx = default,
                Vector4 uiBorder = default)
            {
                Id = id;
                ArtFamily = artFamily;
                RelativePath = relativePath;
                Width = width;
                Height = height;
                HasAlpha = hasAlpha;
                Category = category;
                WrapU = wrapU;
                WrapV = wrapV;
                PivotPx = pivotPx;
                UiBorder = uiBorder;
            }
        }

        private static readonly ContractEntry[] ContractEntries =
        {
            // Terrain (ART-002, ART-003, ART-004)
            new ContractEntry(ArtAssetId.GroundMeadow, "ART-002", "Terrain/ground_meadow.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.GroundEarth, "ART-002", "Terrain/ground_earth.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.GroundGranite, "ART-002", "Terrain/ground_granite.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.GroundDamp, "ART-002", "Terrain/ground_damp.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.BlendMeadowEarth, "ART-002", "Terrain/blend_meadow_earth.png", 512, 512, false, AssetCategory.TerrainMask, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.BlendEarthGranite, "ART-002", "Terrain/blend_earth_granite.png", 512, 512, false, AssetCategory.TerrainMask, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.BlendMeadowGranite, "ART-002", "Terrain/blend_meadow_granite.png", 512, 512, false, AssetCategory.TerrainMask, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.BlendDampRiver, "ART-002", "Terrain/blend_damp_river.png", 512, 512, false, AssetCategory.TerrainMask, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.CliffFace, "ART-003", "Terrain/cliff_face.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.SlopeEarth, "ART-004", "Terrain/slope_earth.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.SlopeRock, "ART-004", "Terrain/slope_rock.png", 1024, 1024, false, AssetCategory.TerrainTile, TextureWrapMode.Repeat, TextureWrapMode.Repeat),

            // Water & Waterfall (ART-011, ART-012)
            new ContractEntry(ArtAssetId.RiverFlow, "ART-011", "Water/river_flow.png", 1024, 1024, false, AssetCategory.Water, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.RiverRiffle, "ART-011", "Water/river_riffle.png", 1024, 1024, true, AssetCategory.Water, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.RiverShore, "ART-011", "Water/river_shore.png", 1024, 128, true, AssetCategory.Water, TextureWrapMode.Repeat, TextureWrapMode.Clamp),
            new ContractEntry(ArtAssetId.RiverRocks, "ART-011", "Water/river_rocks.png", 512, 512, true, AssetCategory.Water, TextureWrapMode.Clamp, TextureWrapMode.Clamp),
            new ContractEntry(ArtAssetId.FallRibbon, "ART-012", "Water/fall_ribbon.png", 256, 1024, true, AssetCategory.Water, TextureWrapMode.Clamp, TextureWrapMode.Repeat),
            new ContractEntry(ArtAssetId.FallFoam, "ART-012", "Water/fall_foam.png", 512, 512, true, AssetCategory.Water, TextureWrapMode.Clamp, TextureWrapMode.Clamp),
            new ContractEntry(ArtAssetId.WetRock, "ART-012", "Water/wet_rock.png", 1024, 1024, false, AssetCategory.Water, TextureWrapMode.Repeat, TextureWrapMode.Repeat),

            // Overlay (ART-015, ART-017)
            new ContractEntry(ArtAssetId.CueRing, "ART-015", "Overlay/cue_ring.png", 128, 128, true, AssetCategory.Overlay, TextureWrapMode.Clamp, TextureWrapMode.Clamp),
            new ContractEntry(ArtAssetId.MarkerReachable, "ART-017", "Overlay/marker_reachable.png", 256, 256, true, AssetCategory.Overlay, TextureWrapMode.Clamp, TextureWrapMode.Clamp),
            new ContractEntry(ArtAssetId.MarkerUnreachable, "ART-017", "Overlay/marker_unreachable.png", 256, 256, true, AssetCategory.Overlay, TextureWrapMode.Clamp, TextureWrapMode.Clamp),

            // UI (ART-018, ART-019, ART-020)
            new ContractEntry(ArtAssetId.UiLabel, "ART-018", "UI/ui_label.png", 96, 48, true, AssetCategory.Ui, TextureWrapMode.Clamp, TextureWrapMode.Clamp, uiBorder: new Vector4(16f, 16f, 16f, 16f)),
            new ContractEntry(ArtAssetId.UiPanel, "ART-019", "UI/ui_panel.png", 128, 128, true, AssetCategory.Ui, TextureWrapMode.Clamp, TextureWrapMode.Clamp, uiBorder: new Vector4(24f, 24f, 24f, 24f)),
            new ContractEntry(ArtAssetId.UiButtonNormal, "ART-020", "UI/ui_button_normal.png", 96, 48, true, AssetCategory.Ui, TextureWrapMode.Clamp, TextureWrapMode.Clamp, uiBorder: new Vector4(12f, 12f, 12f, 12f)),
            new ContractEntry(ArtAssetId.UiButtonHover, "ART-020", "UI/ui_button_hover.png", 96, 48, true, AssetCategory.Ui, TextureWrapMode.Clamp, TextureWrapMode.Clamp, uiBorder: new Vector4(12f, 12f, 12f, 12f)),
            new ContractEntry(ArtAssetId.UiButtonPressed, "ART-020", "UI/ui_button_pressed.png", 96, 48, true, AssetCategory.Ui, TextureWrapMode.Clamp, TextureWrapMode.Clamp, uiBorder: new Vector4(12f, 12f, 12f, 12f)),

            // World sprites (ART-003, ART-005..ART-010, ART-014)
            new ContractEntry(ArtAssetId.CliffCrest, "ART-003", "Props/Cliffs/cliff_crest.png", 512, 384, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(256f, 40f)),
            new ContractEntry(ArtAssetId.CliffToe, "ART-003", "Props/Cliffs/cliff_toe.png", 512, 256, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(256f, 40f)),
            new ContractEntry(ArtAssetId.BoulderUpright, "ART-005", "Props/Boulders/boulder_upright.png", 320, 320, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(160f, 40f)),
            new ContractEntry(ArtAssetId.BoulderSquat, "ART-005", "Props/Boulders/boulder_squat.png", 448, 320, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(224f, 40f)),
            new ContractEntry(ArtAssetId.BoulderLow, "ART-005", "Props/Boulders/boulder_low.png", 320, 224, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(160f, 40f)),
            new ContractEntry(ArtAssetId.PineUpright, "ART-006", "Props/Pines/pine_upright.png", 768, 1024, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(384f, 40f)),
            new ContractEntry(ArtAssetId.PineLeaning, "ART-006", "Props/Pines/pine_leaning.png", 896, 960, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(384f, 40f)),
            new ContractEntry(ArtAssetId.PineSpreading, "ART-006", "Props/Pines/pine_spreading.png", 1024, 896, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(512f, 40f)),
            new ContractEntry(ArtAssetId.BambooUpright, "ART-007", "Props/Bamboo/bamboo_upright.png", 384, 768, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(192f, 40f)),
            new ContractEntry(ArtAssetId.BambooLeaning, "ART-007", "Props/Bamboo/bamboo_leaning.png", 512, 768, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(224f, 40f)),
            new ContractEntry(ArtAssetId.BambooEdge, "ART-007", "Props/Bamboo/bamboo_edge.png", 384, 512, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(192f, 40f)),
            new ContractEntry(ArtAssetId.ShrubRound, "ART-008", "Props/Shrubs/shrub_round.png", 192, 192, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(96f, 32f)),
            new ContractEntry(ArtAssetId.ShrubSpreading, "ART-008", "Props/Shrubs/shrub_spreading.png", 256, 160, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(128f, 32f)),
            new ContractEntry(ArtAssetId.GrassShort, "ART-009", "Props/Grass/grass_short.png", 128, 64, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 12f)),
            new ContractEntry(ArtAssetId.GrassTall, "ART-009", "Props/Grass/grass_tall.png", 128, 128, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 16f)),
            new ContractEntry(ArtAssetId.GrassSparse, "ART-009", "Props/Grass/grass_sparse.png", 128, 96, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 16f)),
            new ContractEntry(ArtAssetId.FlowersWhite, "ART-010", "Props/Flowers/flowers_white.png", 128, 96, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 16f)),
            new ContractEntry(ArtAssetId.FlowersYellow, "ART-010", "Props/Flowers/flowers_yellow.png", 128, 96, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 16f)),
            new ContractEntry(ArtAssetId.PlaceholderCapsule, "ART-014", "Props/Placeholder/placeholder_capsule.png", 128, 256, true, AssetCategory.Prop, TextureWrapMode.Clamp, TextureWrapMode.Clamp, new Vector2(64f, 32f))
        };

        private static readonly HashSet<string> ForbiddenModelExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".fbx", ".obj", ".dae", ".gltf", ".glb", ".blend", ".3ds",
                ".max", ".ma", ".mb", ".ply", ".stl", ".usd", ".usda", ".usdc", ".usdz"
            };

        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        // T-ART-01: Every §4 file exists at exact path with exact pixel size and channel layout,
        // and ArtCatalog reports zero stand-ins in use.
        [Test]
        public void T_ART_01_AllContractFilesExistWithExactSizeChannelsAndNoStandIns()
        {
            Assert.That(ContractEntries.Length, Is.EqualTo(Enum.GetValues(typeof(ArtAssetId)).Length));

            string artRoot = Path.Combine(Application.dataPath, "Resources", "Art");
            for (int i = 0; i < ContractEntries.Length; i++)
            {
                ContractEntry entry = ContractEntries[i];
                string filePath = Path.Combine(artRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.Exists(filePath), Is.True, $"Missing contracted art file: {entry.RelativePath}");

                ReadPngHeader(filePath, out int width, out int height, out byte bitDepth, out byte colorType);
                Assert.That(width, Is.EqualTo(entry.Width), $"Width mismatch on {entry.RelativePath}");
                Assert.That(height, Is.EqualTo(entry.Height), $"Height mismatch on {entry.RelativePath}");
                Assert.That(bitDepth, Is.EqualTo(8), $"Bit depth must be 8 on {entry.RelativePath}");

                byte expectedColorType = entry.HasAlpha ? (byte)6 : (byte)2;
                Assert.That(colorType, Is.EqualTo(expectedColorType), $"Channel layout mismatch on {entry.RelativePath}");
            }

            using ArtCatalog catalog = ArtCatalog.Load(false);
            Assert.That(catalog.TotalAssetCount, Is.EqualTo(ContractEntries.Length));
            Assert.That(catalog.StandInCount, Is.EqualTo(0), "Production art run must use zero stand-ins.");
            Assert.That(catalog.AnyStandInInUse, Is.False);

            for (int i = 0; i < ContractEntries.Length; i++)
            {
                ContractEntry entry = ContractEntries[i];
                Texture2D tex = catalog.GetTexture(entry.Id);
                Assert.That(tex, Is.Not.Null, $"ArtCatalog missing texture for {entry.Id}");
                Assert.That(tex.width, Is.EqualTo(entry.Width), $"Loaded texture width mismatch for {entry.Id}");
                Assert.That(tex.height, Is.EqualTo(entry.Height), $"Loaded texture height mismatch for {entry.Id}");
                Assert.That(catalog.IsStandIn(entry.Id), Is.False, $"Stand-in flag set for {entry.Id}");
            }
        }

        // T-ART-02: Every §4 file's Unity TextureImporter matches the §4 import settings for its class.
        [Test]
        public void T_ART_02_AllContractImportersMatchSection4Settings()
        {
#if !UNITY_EDITOR
            Assert.Fail("T-ART-02 requires UNITY_EDITOR to inspect TextureImporter settings.");
#else
            for (int i = 0; i < ContractEntries.Length; i++)
            {
                ContractEntry entry = ContractEntries[i];
                string assetPath = "Assets/Resources/Art/" + entry.RelativePath;
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, $"Expected TextureImporter at {assetPath}");

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                Assert.That(importer.wrapModeU, Is.EqualTo(entry.WrapU), $"wrapModeU mismatch on {assetPath}");
                Assert.That(importer.wrapModeV, Is.EqualTo(entry.WrapV), $"wrapModeV mismatch on {assetPath}");
                Assert.That(importer.maxTextureSize, Is.EqualTo(2048), $"maxTextureSize mismatch on {assetPath}");

                switch (entry.Category)
                {
                    case AssetCategory.TerrainTile:
                    case AssetCategory.TerrainMask:
                        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), assetPath);
                        Assert.That(importer.sRGBTexture, Is.EqualTo(entry.Category == AssetCategory.TerrainTile), assetPath);
                        Assert.That(importer.mipmapEnabled, Is.True, assetPath);
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), assetPath);
                        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), assetPath);
                        Assert.That(importer.alphaIsTransparency, Is.False, assetPath);
                        Assert.That(importer.isReadable, Is.False, assetPath);
                        break;

                    case AssetCategory.Water:
                        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), assetPath);
                        Assert.That(importer.sRGBTexture, Is.True, assetPath);
                        Assert.That(importer.mipmapEnabled, Is.True, assetPath);
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), assetPath);
                        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), assetPath);
                        Assert.That(importer.alphaIsTransparency, Is.EqualTo(entry.HasAlpha), assetPath);
                        Assert.That(importer.isReadable, Is.False, assetPath);
                        break;

                    case AssetCategory.Overlay:
                        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), assetPath);
                        Assert.That(importer.sRGBTexture, Is.True, assetPath);
                        Assert.That(importer.mipmapEnabled, Is.True, assetPath);
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), assetPath);
                        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), assetPath);
                        Assert.That(importer.alphaIsTransparency, Is.True, assetPath);
                        Assert.That(importer.isReadable, Is.False, assetPath);
                        break;

                    case AssetCategory.Prop:
                        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), assetPath);
                        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), assetPath);
                        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f).Within(1e-4f), assetPath);
                        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), assetPath);
                        Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom), assetPath);
                        Assert.That(importer.spritePivot.x, Is.EqualTo(entry.PivotPx.x / entry.Width).Within(1e-4f), assetPath);
                        Assert.That(importer.spritePivot.y, Is.EqualTo(entry.PivotPx.y / entry.Height).Within(1e-4f), assetPath);
                        Assert.That(importer.spriteBorder, Is.EqualTo(Vector4.zero), assetPath);
                        Assert.That(importer.isReadable, Is.True, assetPath);
                        Assert.That(importer.sRGBTexture, Is.True, assetPath);
                        Assert.That(importer.mipmapEnabled, Is.True, assetPath);
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), assetPath);
                        Assert.That(importer.alphaIsTransparency, Is.True, assetPath);
                        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ), assetPath);
                        break;

                    case AssetCategory.Ui:
                        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), assetPath);
                        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), assetPath);
                        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f).Within(1e-4f), assetPath);
                        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), assetPath);
                        Assert.That(importer.spriteBorder, Is.EqualTo(entry.UiBorder), assetPath);
                        Assert.That(importer.sRGBTexture, Is.True, assetPath);
                        Assert.That(importer.mipmapEnabled, Is.False, assetPath);
                        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), assetPath);
                        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), assetPath);
                        Assert.That(importer.alphaIsTransparency, Is.True, assetPath);
                        Assert.That(importer.isReadable, Is.False, assetPath);
                        break;
                }
            }
#endif
        }

        // T-ART-03: Production ArtCatalog builds a single-page runtime atlas <= 4096x4096 containing all 19 world sprites.
        [Test]
        public void T_ART_03_RuntimeAtlasIsSinglePageWithinMaxSizeWithAllWorldSprites()
        {
            using ArtCatalog catalog = ArtCatalog.Load(false);
            Assert.That(catalog.StandInCount, Is.EqualTo(0));
            Assert.That(catalog.WorldSpriteCount, Is.EqualTo(19));
            Assert.That(catalog.PropAtlas, Is.Not.Null);
            Assert.That(catalog.PropAtlas.width, Is.GreaterThan(0).And.LessThanOrEqualTo(ArtCatalog.MaxAtlasSize));
            Assert.That(catalog.PropAtlas.height, Is.GreaterThan(0).And.LessThanOrEqualTo(ArtCatalog.MaxAtlasSize));

            List<Rect> packedRects = new List<Rect>(19);
            int expectedWorldSprites = 0;

            for (int i = 0; i < ContractEntries.Length; i++)
            {
                ContractEntry entry = ContractEntries[i];
                if (entry.Category != AssetCategory.Prop)
                {
                    continue;
                }

                expectedWorldSprites++;
                WorldSpriteEntry ws = catalog.GetWorldSprite(entry.Id);
                Assert.That(ws.IsStandIn, Is.False, $"World sprite {entry.Id} must not be a stand-in.");
                Assert.That(ws.Texture, Is.Not.Null, $"World sprite {entry.Id} texture is null.");
                Assert.That(ws.Sprite, Is.Not.Null, $"World sprite {entry.Id} sprite is null.");
                Assert.That(ws.PixelWidth, Is.EqualTo(entry.Width));
                Assert.That(ws.PixelHeight, Is.EqualTo(entry.Height));

                Rect uv = ws.AtlasUvRect;
                Assert.That(uv.width, Is.GreaterThan(0f), $"Empty atlas UV width for {entry.Id}");
                Assert.That(uv.height, Is.GreaterThan(0f), $"Empty atlas UV height for {entry.Id}");
                Assert.That(uv.xMin, Is.GreaterThanOrEqualTo(0f), $"UV xMin < 0 for {entry.Id}");
                Assert.That(uv.yMin, Is.GreaterThanOrEqualTo(0f), $"UV yMin < 0 for {entry.Id}");
                Assert.That(uv.xMax, Is.LessThanOrEqualTo(1f), $"UV xMax > 1 for {entry.Id}");
                Assert.That(uv.yMax, Is.LessThanOrEqualTo(1f), $"UV yMax > 1 for {entry.Id}");

                for (int j = 0; j < packedRects.Count; j++)
                {
                    Assert.That(RectsOverlap(uv, packedRects[j]), Is.False, $"Atlas rect overlap for {entry.Id}");
                }
                packedRects.Add(uv);
            }

            Assert.That(expectedWorldSprites, Is.EqualTo(19));
            Assert.That(packedRects.Count, Is.EqualTo(19));
        }

        // T-ART-04: Assets/ contains no file matching any hash under docs/references/ and no 3D model files.
        [Test]
        public void T_ART_04_AssetsContainNoReferenceHashesAndNo3DModels()
        {
            string assetsDir = Application.dataPath;
            string repoRoot = Directory.GetParent(assetsDir).FullName;
            string referencesDir = Path.Combine(repoRoot, "docs", "references");
            Assert.That(Directory.Exists(referencesDir), Is.True, "docs/references/ directory must exist.");

            HashSet<string> referenceHashes = new HashSet<string>(StringComparer.Ordinal);
            using SHA256 sha = SHA256.Create();

            string[] refFiles = Directory.GetFiles(referencesDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < refFiles.Length; i++)
            {
                string file = refFiles[i];
                if (string.Equals(Path.GetFileName(file), ".gitkeep", StringComparison.Ordinal))
                {
                    continue;
                }
                referenceHashes.Add(ComputeSha256Hex(sha, file));
            }
            Assert.That(referenceHashes.Count, Is.GreaterThan(0), "Expected at least one reference file to hash.");

            string[] assetFiles = Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < assetFiles.Length; i++)
            {
                string file = assetFiles[i];
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLower(CultureInfo.InvariantCulture);

                Assert.That(
                    ForbiddenModelExtensions.Contains(ext),
                    Is.False,
                    $"Forbidden 3D model file found in Assets/: {fileName}");

                if (string.Equals(fileName, ".gitkeep", StringComparison.Ordinal))
                {
                    continue;
                }

                string hash = ComputeSha256Hex(sha, file);
                Assert.That(
                    referenceHashes.Contains(hash),
                    Is.False,
                    $"Asset matches a reference image hash from docs/references/: {fileName}");
            }
        }

        // T-ART-05: Assets/Art/ART_README.md covers every delivered ART-NNN family and the bundled LiberationSans SDF font.
        [Test]
        public void T_ART_05_ArtReadmeCoversAllDeliveredFamiliesAndBundledFont()
        {
            string assetsDir = Application.dataPath;
            string readmePath = Path.Combine(assetsDir, "Art", "ART_README.md");
            Assert.That(File.Exists(readmePath), Is.True, "Assets/Art/ART_README.md must exist.");

            string[] lines = File.ReadAllLines(readmePath);
            Dictionary<string, (string filesCol, string sourceCol)> artRows =
                new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            (string filesCol, string sourceCol)? fontRow = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || line.StartsWith("|---", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length < 4)
                {
                    continue;
                }

                string col0 = parts[1].Trim();
                string col1 = parts[2].Trim();
                string col2 = parts[3].Trim();

                if (col0.StartsWith("ART-", StringComparison.Ordinal))
                {
                    artRows[col0] = (col1, col2);
                }
                else if (col0.IndexOf("LiberationSans SDF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fontRow = (col1, col2);
                }
            }

            Dictionary<string, List<string>> expectedFilesByFamily =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < ContractEntries.Length; i++)
            {
                ContractEntry entry = ContractEntries[i];
                if (!expectedFilesByFamily.TryGetValue(entry.ArtFamily, out List<string> list))
                {
                    list = new List<string>();
                    expectedFilesByFamily[entry.ArtFamily] = list;
                }
                list.Add(Path.GetFileName(entry.RelativePath));
            }

            foreach (KeyValuePair<string, List<string>> kvp in expectedFilesByFamily)
            {
                Assert.That(artRows.TryGetValue(kvp.Key, out var row), Is.True, $"Missing ART_README row for {kvp.Key}");
                Assert.That(string.IsNullOrWhiteSpace(row.sourceCol), Is.False, $"Missing source/licence in ART_README for {kvp.Key}");
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    string fileName = kvp.Value[i];
                    Assert.That(
                        row.filesCol.IndexOf(fileName, StringComparison.OrdinalIgnoreCase),
                        Is.GreaterThanOrEqualTo(0),
                        $"ART_README row {kvp.Key} does not list {fileName}");
                }
            }

            Assert.That(fontRow.HasValue, Is.True, "Missing LiberationSans SDF row in Assets/Art/ART_README.md");
            Assert.That(fontRow.Value.filesCol, Does.Contain("LiberationSans.ttf"));
            Assert.That(fontRow.Value.filesCol, Does.Contain("LiberationSans - OFL.txt").Or.Contain("LiberationSans%20-%20OFL.txt"));
            Assert.That(fontRow.Value.sourceCol, Does.Contain("SIL Open Font License"));

            Assert.That(File.Exists(Path.Combine(assetsDir, "TextMesh Pro", "Fonts", "LiberationSans.ttf")), Is.True);
            Assert.That(File.Exists(Path.Combine(assetsDir, "TextMesh Pro", "Fonts", "LiberationSans - OFL.txt")), Is.True);
        }

        private static void ReadPngHeader(string filePath, out int width, out int height, out byte bitDepth, out byte colorType)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] header = new byte[26];
            int read = stream.Read(header, 0, header.Length);
            Assert.That(read, Is.EqualTo(header.Length), $"Truncated PNG header: {Path.GetFileName(filePath)}");

            for (int i = 0; i < PngSignature.Length; i++)
            {
                Assert.That(header[i], Is.EqualTo(PngSignature[i]), $"Invalid PNG signature: {Path.GetFileName(filePath)}");
            }

            Assert.That(header[12], Is.EqualTo((byte)'I'));
            Assert.That(header[13], Is.EqualTo((byte)'H'));
            Assert.That(header[14], Is.EqualTo((byte)'D'));
            Assert.That(header[15], Is.EqualTo((byte)'R'));

            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            bitDepth = header[24];
            colorType = header[25];
        }

        private static bool RectsOverlap(Rect a, Rect b)
        {
            const float eps = 1e-5f;
            return a.xMin < b.xMax - eps
                && a.xMax > b.xMin + eps
                && a.yMin < b.yMax - eps
                && a.yMax > b.yMin + eps;
        }

        private static string ComputeSha256Hex(SHA256 sha, string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }
}
