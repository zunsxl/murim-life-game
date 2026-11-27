using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Murim.Editor.Art
{
    public enum ArtCategory
    {
        SeamlessTerrainTile,
        WaterTexture,
        BackdropTexture,
        WorldPropSprite,
        BankLipSprite,
        ShadowSprite,
        UiSprite,
        Font
    }

    public readonly struct ArtImportSpec
    {
        public TextureImporterType TextureType { get; }
        public SpriteImportMode SpriteMode { get; }
        public int PixelsPerUnit { get; }
        public SpriteMeshType MeshType { get; }
        public bool Srgb { get; }
        public bool Mipmaps { get; }
        public FilterMode FilterMode { get; }
        public TextureWrapMode WrapU { get; }
        public TextureWrapMode WrapV { get; }
        public TextureImporterCompression Compression { get; }
        public int MaxTextureSize { get; }
        public bool AlphaIsTransparency { get; }
        public Vector2 Pivot { get; }
        public Vector4 SpriteBorder { get; }

        private ArtImportSpec(TextureImportArgs args)
        {
            TextureType = args.TextureType;
            SpriteMode = args.SpriteMode;
            PixelsPerUnit = args.PixelsPerUnit;
            MeshType = args.MeshType;
            Srgb = args.Srgb;
            Mipmaps = args.Mipmaps;
            FilterMode = args.FilterMode;
            WrapU = args.WrapU;
            WrapV = args.WrapV;
            Compression = args.Compression;
            MaxTextureSize = args.MaxTextureSize;
            AlphaIsTransparency = args.AlphaIsTransparency;
            Pivot = args.Pivot;
            SpriteBorder = args.SpriteBorder;
        }

        private struct TextureImportArgs
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
        }

        public static ArtImportSpec DefaultTexture(
            TextureWrapMode wrapU,
            TextureWrapMode wrapV,
            int maxTextureSize,
            bool alphaIsTransparency)
        {
            return new ArtImportSpec(new TextureImportArgs
            {
                TextureType = TextureImporterType.Default,
                SpriteMode = SpriteImportMode.None,
                PixelsPerUnit = 100,
                MeshType = SpriteMeshType.FullRect,
                Srgb = true,
                Mipmaps = true,
                FilterMode = FilterMode.Trilinear,
                WrapU = wrapU,
                WrapV = wrapV,
                Compression = TextureImporterCompression.CompressedHQ,
                MaxTextureSize = maxTextureSize,
                AlphaIsTransparency = alphaIsTransparency,
                Pivot = new Vector2(0.5f, 0.5f),
                SpriteBorder = Vector4.zero
            });
        }

        public static ArtImportSpec WorldSprite(Vector2Int size, Vector2Int pivotPx)
        {
            var pivot = new Vector2((float)pivotPx.x / size.x, (float)pivotPx.y / size.y);
            return new ArtImportSpec(new TextureImportArgs
            {
                TextureType = TextureImporterType.Sprite,
                SpriteMode = SpriteImportMode.Single,
                PixelsPerUnit = 128,
                MeshType = SpriteMeshType.Tight,
                Srgb = true,
                Mipmaps = true,
                FilterMode = FilterMode.Trilinear,
                WrapU = TextureWrapMode.Clamp,
                WrapV = TextureWrapMode.Clamp,
                Compression = TextureImporterCompression.CompressedHQ,
                MaxTextureSize = 4096,
                AlphaIsTransparency = true,
                Pivot = pivot,
                SpriteBorder = Vector4.zero
            });
        }

        public static ArtImportSpec UiSprite(Vector4 spriteBorder)
        {
            return new ArtImportSpec(new TextureImportArgs
            {
                TextureType = TextureImporterType.Sprite,
                SpriteMode = SpriteImportMode.Single,
                PixelsPerUnit = 100,
                MeshType = SpriteMeshType.FullRect,
                Srgb = true,
                Mipmaps = false,
                FilterMode = FilterMode.Bilinear,
                WrapU = TextureWrapMode.Clamp,
                WrapV = TextureWrapMode.Clamp,
                Compression = TextureImporterCompression.Uncompressed,
                MaxTextureSize = 2048,
                AlphaIsTransparency = true,
                Pivot = new Vector2(0.5f, 0.5f),
                SpriteBorder = spriteBorder
            });
        }
    }

    public sealed class ArtAssetEntry
    {
        public string ArtId { get; }
        public string AssetPath { get; }
        public ArtCategory Category { get; }
        public int Width { get; }
        public int Height { get; }
        public bool RequiresAlpha { get; }
        public bool HasCustomPivot { get; private set; }
        public int PivotPxX { get; private set; }
        public int PivotPxY { get; private set; }
        public bool HasTopRange { get; private set; }
        public int TopMinPx { get; private set; }
        public int TopMaxPx { get; private set; }
        public bool RequireOuter4Transparent { get; private set; }
        public bool Require20PctTransparent { get; private set; }
        public bool RequireOpaqueNearPivot { get; private set; }
        public int EndFadePx { get; private set; }
        public float MaxAlphaNormalized { get; private set; }
        public string ExactBottomRowHex { get; private set; }
        public bool IsButtonStateSprite { get; private set; }
        public ArtImportSpec ImportSpec { get; }

        public ArtAssetEntry(
            string artId,
            string assetPath,
            ArtCategory category,
            Vector2Int size,
            ArtImportSpec importSpec)
        {
            ArtId = artId;
            AssetPath = assetPath;
            Category = category;
            Width = size.x;
            Height = size.y;
            RequiresAlpha = importSpec.AlphaIsTransparency;
            ImportSpec = importSpec;
            MaxAlphaNormalized = 1.0f;
        }

        public ArtAssetEntry WithSpriteRules(Vector2Int pivotPx, Vector2Int topRangePx)
        {
            HasCustomPivot = true;
            PivotPxX = pivotPx.x;
            PivotPxY = pivotPx.y;
            HasTopRange = true;
            TopMinPx = topRangePx.x;
            TopMaxPx = topRangePx.y;
            RequireOuter4Transparent = true;
            Require20PctTransparent = true;
            RequireOpaqueNearPivot = true;
            return this;
        }

        public ArtAssetEntry WithBankLipRules(Vector2Int pivotPx, int topMaxPx, int endFadePx)
        {
            HasCustomPivot = true;
            PivotPxX = pivotPx.x;
            PivotPxY = pivotPx.y;
            HasTopRange = true;
            TopMinPx = 0;
            TopMaxPx = topMaxPx;
            RequireOuter4Transparent = true;
            Require20PctTransparent = true;
            RequireOpaqueNearPivot = true;
            EndFadePx = endFadePx;
            return this;
        }

        public ArtAssetEntry WithShadowRules(Vector2Int pivotPx, float maxAlpha)
        {
            HasCustomPivot = true;
            PivotPxX = pivotPx.x;
            PivotPxY = pivotPx.y;
            RequireOuter4Transparent = true;
            Require20PctTransparent = true;
            MaxAlphaNormalized = maxAlpha;
            return this;
        }

        public ArtAssetEntry WithBottomRowHex(string hex)
        {
            ExactBottomRowHex = hex;
            return this;
        }

        public ArtAssetEntry WithButtonRule()
        {
            IsButtonStateSprite = true;
            return this;
        }
    }

    public static class ArtManifest
    {
        public const string ArtRootPrefix = "Assets/Resources/Art/";
        public const string PropsRootPrefix = "Assets/Resources/Art/Props/";
        public const string WorldPropsAtlasPath = "Assets/Resources/Art/Props/WorldProps.spriteatlas";
        public const string FontsDirectory = "Assets/Resources/Art/UI/Fonts";
        public const string OverlaySerifAssetPath = "Assets/Resources/Art/UI/Fonts/OverlaySerif SDF.asset";
        public const string ArtReadmePath = "Assets/Art/ART_README.md";
        public const int WorldSpritePpu = 128;
        public const int UiSpritePpu = 100;
        public const int OuterTransparentMarginPx = 4;
        public const float MinTransparentCanvasFraction = 0.20f;
        public const int PivotOpaqueRadiusPx = 16;
        public const int AtlasMaxTextureSize = 8192;
        public const int AtlasPadding = 8;
        public const int TmpAtlasSize = 2048;
        public const int TmpPadding = 9;
        public const int TmpMinSamplingPointSize = 90;
        public const int FontMinCodepoint = 0x0020;
        public const int FontMaxCodepoint = 0x007E;

        public static readonly string[] RequiredArtIds =
        {
            "ART-001", "ART-002", "ART-003", "ART-004", "ART-005", "ART-006",
            "ART-007", "ART-008", "ART-009", "ART-010", "ART-011"
        };

        public static readonly string[] ForbiddenModelExtensions =
        {
            ".fbx", ".obj", ".blend", ".dae", ".gltf", ".glb", ".3ds"
        };

        private static readonly List<ArtAssetEntry> Entries = BuildManifest();
        private static readonly Dictionary<string, ArtAssetEntry> ByPath = BuildLookup(Entries);

        public static IReadOnlyList<ArtAssetEntry> AllEntries => Entries;

        public static bool TryGetByPath(string assetPath, out ArtAssetEntry entry)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                entry = null;
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            return ByPath.TryGetValue(normalized, out entry);
        }

        public static List<ArtAssetEntry> GetTexturesAndTiles()
        {
            var list = new List<ArtAssetEntry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                ArtCategory cat = Entries[i].Category;
                if (cat == ArtCategory.SeamlessTerrainTile
                    || cat == ArtCategory.WaterTexture
                    || cat == ArtCategory.BackdropTexture)
                {
                    list.Add(Entries[i]);
                }
            }

            return list;
        }

        public static List<ArtAssetEntry> GetWorldSprites()
        {
            var list = new List<ArtAssetEntry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                ArtCategory cat = Entries[i].Category;
                if (cat == ArtCategory.WorldPropSprite
                    || cat == ArtCategory.BankLipSprite
                    || cat == ArtCategory.ShadowSprite)
                {
                    list.Add(Entries[i]);
                }
            }

            return list;
        }

        public static List<ArtAssetEntry> GetUiSprites()
        {
            var list = new List<ArtAssetEntry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Category == ArtCategory.UiSprite)
                {
                    list.Add(Entries[i]);
                }
            }

            return list;
        }

        private static List<ArtAssetEntry> BuildManifest()
        {
            var list = new List<ArtAssetEntry>(38);
            AddTerrainTiles(list);
            AddWaterTextures(list);
            AddBackdropTextures(list);
            AddBankAndShadowSprites(list);
            AddPineSprites(list);
            AddBambooSprites(list);
            AddRockSprites(list);
            AddFloraSprites(list);
            AddUiSprites(list);
            return list;
        }

        private static Dictionary<string, ArtAssetEntry> BuildLookup(List<ArtAssetEntry> entries)
        {
            var map = new Dictionary<string, ArtAssetEntry>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                map[entries[i].AssetPath] = entries[i];
            }

            return map;
        }

        private static void AddTerrainTiles(List<ArtAssetEntry> list)
        {
            ArtImportSpec spec = ArtImportSpec.DefaultTexture(
                TextureWrapMode.Repeat,
                TextureWrapMode.Repeat,
                2048,
                false);

            list.Add(Tile("ART-001", "Terrain/terrain_moss_turf.png", spec));
            list.Add(Tile("ART-001", "Terrain/terrain_slate_soil.png", spec));
            list.Add(Tile("ART-001", "Terrain/terrain_mossy_stone.png", spec));
            list.Add(Tile("ART-001", "Terrain/terrain_pebble_bank.png", spec));
            list.Add(Tile("ART-002", "Terrain/terrain_bank_grassy.png", spec));
            list.Add(Tile("ART-002", "Terrain/terrain_bank_earth_stone.png", spec));
            list.Add(Tile("ART-002", "Terrain/terrain_hillside_slope.png", spec));
            list.Add(Tile("ART-008", "Terrain/terrain_ridge_ground.png", spec));
        }

        private static ArtAssetEntry Tile(string artId, string relPath, ArtImportSpec spec)
        {
            return new ArtAssetEntry(
                artId,
                ArtRootPrefix + relPath,
                ArtCategory.SeamlessTerrainTile,
                new Vector2Int(2048, 2048),
                spec);
        }

        private static void AddWaterTextures(List<ArtAssetEntry> list)
        {
            list.Add(new ArtAssetEntry(
                "ART-003",
                ArtRootPrefix + "Water/water_stream.png",
                ArtCategory.WaterTexture,
                new Vector2Int(1024, 1024),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1024, false)));
            list.Add(new ArtAssetEntry(
                "ART-003",
                ArtRootPrefix + "Water/water_shore.png",
                ArtCategory.WaterTexture,
                new Vector2Int(1024, 1024),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1024, true)));
            list.Add(new ArtAssetEntry(
                "ART-003",
                ArtRootPrefix + "Water/water_fall.png",
                ArtCategory.WaterTexture,
                new Vector2Int(512, 2048),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2048, true)));
            list.Add(new ArtAssetEntry(
                "ART-003",
                ArtRootPrefix + "Water/water_foam.png",
                ArtCategory.WaterTexture,
                new Vector2Int(1280, 768),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1280, true)));
        }

        private static void AddBackdropTextures(List<ArtAssetEntry> list)
        {
            list.Add(new ArtAssetEntry(
                "ART-010",
                ArtRootPrefix + "Backdrop/backdrop_sky.png",
                ArtCategory.BackdropTexture,
                new Vector2Int(16, 1024),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1024, false))
                .WithBottomRowHex("EFD3A0"));
            list.Add(new ArtAssetEntry(
                "ART-010",
                ArtRootPrefix + "Backdrop/backdrop_peaks_far.png",
                ArtCategory.BackdropTexture,
                new Vector2Int(4096, 1024),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Repeat, TextureWrapMode.Clamp, 4096, true)));
            list.Add(new ArtAssetEntry(
                "ART-010",
                ArtRootPrefix + "Backdrop/backdrop_peaks_near.png",
                ArtCategory.BackdropTexture,
                new Vector2Int(4096, 1024),
                ArtImportSpec.DefaultTexture(TextureWrapMode.Repeat, TextureWrapMode.Clamp, 4096, true)));
        }

        private static void AddBankAndShadowSprites(List<ArtAssetEntry> list)
        {
            var lipSize = new Vector2Int(384, 192);
            var lipPivot = new Vector2Int(192, 112);
            list.Add(new ArtAssetEntry(
                "ART-002",
                ArtRootPrefix + "Props/Banks/bank_lip.png",
                ArtCategory.BankLipSprite,
                lipSize,
                ArtImportSpec.WorldSprite(lipSize, lipPivot))
                .WithBankLipRules(lipPivot, 76, 38));

            var shadowSize = new Vector2Int(512, 320);
            var shadowPivot = new Vector2Int(154, 192);
            list.Add(new ArtAssetEntry(
                "ART-008",
                ArtRootPrefix + "Props/Shadows/shadow_soft.png",
                ArtCategory.ShadowSprite,
                shadowSize,
                ArtImportSpec.WorldSprite(shadowSize, shadowPivot))
                .WithShadowRules(shadowPivot, 0.50f));
        }

        private static void AddPineSprites(List<ArtAssetEntry> list)
        {
            list.Add(Prop("ART-004", "Props/Pines/pine_ancient.png", new Vector2Int(1536, 2048), new Vector2Int(768, 160), new Vector2Int(1474, 1802)));
            list.Add(Prop("ART-004", "Props/Pines/pine_tall.png", new Vector2Int(1152, 1728), new Vector2Int(576, 128), new Vector2Int(1291, 1577)));
            list.Add(Prop("ART-004", "Props/Pines/pine_leaning.png", new Vector2Int(1408, 1152), new Vector2Int(448, 128), new Vector2Int(830, 1014)));
            list.Add(Prop("ART-004", "Props/Pines/pine_young.png", new Vector2Int(640, 768), new Vector2Int(320, 96), new Vector2Int(507, 619)));
        }

        private static void AddBambooSprites(List<ArtAssetEntry> list)
        {
            list.Add(Prop("ART-005", "Props/Bamboo/bamboo_dense.png", new Vector2Int(768, 1600), new Vector2Int(384, 240), new Vector2Int(1106, 1352)));
            list.Add(Prop("ART-005", "Props/Bamboo/bamboo_edge.png", new Vector2Int(640, 1216), new Vector2Int(320, 192), new Vector2Int(830, 1014)));
            list.Add(Prop("ART-005", "Props/Bamboo/bamboo_young.png", new Vector2Int(384, 704), new Vector2Int(192, 128), new Vector2Int(461, 563)));
        }

        private static void AddRockSprites(List<ArtAssetEntry> list)
        {
            list.Add(Prop("ART-006", "Props/Rocks/rock_outcrop.png", new Vector2Int(1024, 1088), new Vector2Int(512, 320), new Vector2Int(415, 760)));
            list.Add(Prop("ART-006", "Props/Rocks/rock_boulder.png", new Vector2Int(384, 448), new Vector2Int(192, 128), new Vector2Int(166, 300)));
            list.Add(Prop("ART-006", "Props/Rocks/rock_riverstone.png", new Vector2Int(256, 192), new Vector2Int(128, 76), new Vector2Int(37, 110)));
            list.Add(Prop("ART-006", "Props/Rocks/rock_ridged.png", new Vector2Int(512, 512), new Vector2Int(256, 144), new Vector2Int(203, 364)));
            list.Add(Prop("ART-006", "Props/Rocks/rock_cluster.png", new Vector2Int(256, 192), new Vector2Int(128, 80), new Vector2Int(32, 108)));
        }

        private static void AddFloraSprites(List<ArtAssetEntry> list)
        {
            list.Add(Prop("ART-007", "Props/Flora/flora_wildflower.png", new Vector2Int(160, 128), new Vector2Int(80, 48), new Vector2Int(28, 76)));
            list.Add(Prop("ART-007", "Props/Flora/flora_shrub.png", new Vector2Int(224, 224), new Vector2Int(112, 64), new Vector2Int(83, 154)));
            list.Add(Prop("ART-007", "Props/Flora/flora_fern.png", new Vector2Int(128, 144), new Vector2Int(64, 40), new Vector2Int(55, 100)));
            list.Add(Prop("ART-007", "Props/Flora/flora_tussock.png", new Vector2Int(128, 112), new Vector2Int(64, 32), new Vector2Int(32, 67)));
        }

        private static ArtAssetEntry Prop(
            string artId,
            string relPath,
            Vector2Int size,
            Vector2Int pivotPx,
            Vector2Int topRangePx)
        {
            return new ArtAssetEntry(
                artId,
                ArtRootPrefix + relPath,
                ArtCategory.WorldPropSprite,
                size,
                ArtImportSpec.WorldSprite(size, pivotPx))
                .WithSpriteRules(pivotPx, topRangePx);
        }

        private static void AddUiSprites(List<ArtAssetEntry> list)
        {
            var panelBorder = new Vector4(96f, 96f, 96f, 96f);
            var buttonBorder = new Vector4(32f, 32f, 32f, 32f);

            list.Add(new ArtAssetEntry(
                "ART-009",
                ArtRootPrefix + "UI/overlay_panel.png",
                ArtCategory.UiSprite,
                new Vector2Int(512, 512),
                ArtImportSpec.UiSprite(panelBorder)));
            list.Add(new ArtAssetEntry(
                "ART-009",
                ArtRootPrefix + "UI/overlay_divider.png",
                ArtCategory.UiSprite,
                new Vector2Int(512, 8),
                ArtImportSpec.UiSprite(Vector4.zero)));
            list.Add(new ArtAssetEntry(
                "ART-009",
                ArtRootPrefix + "UI/overlay_button_normal.png",
                ArtCategory.UiSprite,
                new Vector2Int(320, 96),
                ArtImportSpec.UiSprite(buttonBorder))
                .WithButtonRule());
            list.Add(new ArtAssetEntry(
                "ART-009",
                ArtRootPrefix + "UI/overlay_button_hover.png",
                ArtCategory.UiSprite,
                new Vector2Int(320, 96),
                ArtImportSpec.UiSprite(buttonBorder))
                .WithButtonRule());
            list.Add(new ArtAssetEntry(
                "ART-009",
                ArtRootPrefix + "UI/overlay_button_pressed.png",
                ArtCategory.UiSprite,
                new Vector2Int(320, 96),
                ArtImportSpec.UiSprite(buttonBorder))
                .WithButtonRule());
        }
    }
}
