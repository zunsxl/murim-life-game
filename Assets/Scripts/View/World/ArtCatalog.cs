using System;
using System.Collections.Generic;
using UnityEngine;

namespace Murim.View.World
{
    /// <summary>
    /// Identifiers for all 45 §4 art deliverables.
    /// </summary>
    public enum ArtAssetId
    {
        GroundMeadow,
        GroundEarth,
        GroundGranite,
        GroundDamp,
        BlendMeadowEarth,
        BlendEarthGranite,
        BlendMeadowGranite,
        BlendDampRiver,
        CliffFace,
        SlopeEarth,
        SlopeRock,

        RiverFlow,
        RiverRiffle,
        RiverShore,
        RiverRocks,
        FallRibbon,
        FallFoam,
        WetRock,

        CueRing,
        MarkerReachable,
        MarkerUnreachable,

        UiLabel,
        UiPanel,
        UiButtonNormal,
        UiButtonHover,
        UiButtonPressed,

        CliffCrest,
        CliffToe,
        BoulderUpright,
        BoulderSquat,
        BoulderLow,
        PineUpright,
        PineLeaning,
        PineSpreading,
        BambooUpright,
        BambooLeaning,
        BambooEdge,
        ShrubRound,
        ShrubSpreading,
        GrassShort,
        GrassTall,
        GrassSparse,
        FlowersWhite,
        FlowersYellow,
        PlaceholderCapsule
    }

    public readonly struct WorldSpriteEntry
    {
        public readonly ArtAssetId Id;
        public readonly Texture2D Texture;
        public readonly Sprite Sprite;
        public readonly Rect AtlasUvRect;
        public readonly int PixelWidth;
        public readonly int PixelHeight;
        public readonly Vector2 PivotPixels;
        public readonly bool IsStandIn;

        public WorldSpriteEntry(
            ArtAssetId id,
            Texture2D texture,
            Sprite sprite,
            Rect atlasUvRect,
            int pixelWidth,
            int pixelHeight,
            Vector2 pivotPixels,
            bool isStandIn)
        {
            Id = id;
            Texture = texture;
            Sprite = sprite;
            AtlasUvRect = atlasUvRect;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            PivotPixels = pivotPixels;
            IsStandIn = isStandIn;
        }
    }

    /// <summary>
    /// Loads every §4 art file from Resources/Art/, generates contracted flat-colour stand-ins when
    /// missing (or when forceStandIn is requested), and packs all world sprites into one atlas.
    /// </summary>
    public sealed class ArtCatalog : IDisposable
    {
        public const int MaxAtlasSize = 4096;
        public const int AtlasPadding = 8;
        public const float WorldPixelsPerMetre = 128f;
        public const float UiPixelsPerUnit = 100f;

        private enum AssetClass : byte
        {
            Tile,
            Mask,
            WaterChannel,
            WaterDecal,
            Overlay,
            UiSprite,
            WorldSprite
        }

        private readonly struct AssetSpec
        {
            public readonly ArtAssetId Id;
            public readonly string ResourcePath;
            public readonly string FileName;
            public readonly int Width;
            public readonly int Height;
            public readonly AssetClass Class;
            public readonly Vector2 PivotPixels;
            public readonly Vector4 UiBorder;
            public readonly Color32 StandInColor;

            public AssetSpec(
                ArtAssetId id,
                string resourcePath,
                int width,
                int height,
                AssetClass assetClass,
                Color32 standInColor,
                Vector2 pivotPixels = default,
                Vector4 uiBorder = default)
            {
                Id = id;
                ResourcePath = resourcePath;
                int slash = resourcePath.LastIndexOf('/');
                FileName = (slash >= 0 ? resourcePath.Substring(slash + 1) : resourcePath) + ".png";
                Width = width;
                Height = height;
                Class = assetClass;
                StandInColor = standInColor;
                PivotPixels = pivotPixels;
                UiBorder = uiBorder;
            }
        }

        private static readonly AssetSpec[] Specs =
        {
            // Terrain (ART-002, ART-003, ART-004)
            new AssetSpec(ArtAssetId.GroundMeadow, "Art/Terrain/ground_meadow", 1024, 1024, AssetClass.Tile, new Color32(82, 134, 72, 255)),
            new AssetSpec(ArtAssetId.GroundEarth, "Art/Terrain/ground_earth", 1024, 1024, AssetClass.Tile, new Color32(140, 92, 56, 255)),
            new AssetSpec(ArtAssetId.GroundGranite, "Art/Terrain/ground_granite", 1024, 1024, AssetClass.Tile, new Color32(148, 153, 158, 255)),
            new AssetSpec(ArtAssetId.GroundDamp, "Art/Terrain/ground_damp", 1024, 1024, AssetClass.Tile, new Color32(76, 97, 66, 255)),
            new AssetSpec(ArtAssetId.BlendMeadowEarth, "Art/Terrain/blend_meadow_earth", 512, 512, AssetClass.Mask, new Color32(128, 128, 128, 255)),
            new AssetSpec(ArtAssetId.BlendEarthGranite, "Art/Terrain/blend_earth_granite", 512, 512, AssetClass.Mask, new Color32(128, 128, 128, 255)),
            new AssetSpec(ArtAssetId.BlendMeadowGranite, "Art/Terrain/blend_meadow_granite", 512, 512, AssetClass.Mask, new Color32(128, 128, 128, 255)),
            new AssetSpec(ArtAssetId.BlendDampRiver, "Art/Terrain/blend_damp_river", 512, 512, AssetClass.Mask, new Color32(128, 128, 128, 255)),
            new AssetSpec(ArtAssetId.CliffFace, "Art/Terrain/cliff_face", 1024, 1024, AssetClass.Tile, new Color32(66, 74, 87, 255)),
            new AssetSpec(ArtAssetId.SlopeEarth, "Art/Terrain/slope_earth", 1024, 1024, AssetClass.Tile, new Color32(126, 88, 54, 255)),
            new AssetSpec(ArtAssetId.SlopeRock, "Art/Terrain/slope_rock", 1024, 1024, AssetClass.Tile, new Color32(118, 122, 128, 255)),

            // Water & Waterfall (ART-011, ART-012)
            new AssetSpec(ArtAssetId.RiverFlow, "Art/Water/river_flow", 1024, 1024, AssetClass.Tile, new Color32(46, 122, 107, 255)),
            new AssetSpec(ArtAssetId.RiverRiffle, "Art/Water/river_riffle", 1024, 1024, AssetClass.Tile, new Color32(180, 214, 206, 160)),
            new AssetSpec(ArtAssetId.RiverShore, "Art/Water/river_shore", 1024, 128, AssetClass.WaterChannel, new Color32(62, 106, 90, 180)),
            new AssetSpec(ArtAssetId.RiverRocks, "Art/Water/river_rocks", 512, 512, AssetClass.WaterDecal, new Color32(96, 102, 108, 220)),
            new AssetSpec(ArtAssetId.FallRibbon, "Art/Water/fall_ribbon", 256, 1024, AssetClass.WaterChannel, new Color32(198, 228, 224, 220)),
            new AssetSpec(ArtAssetId.FallFoam, "Art/Water/fall_foam", 512, 512, AssetClass.WaterDecal, new Color32(236, 242, 240, 220)),
            new AssetSpec(ArtAssetId.WetRock, "Art/Water/wet_rock", 1024, 1024, AssetClass.Tile, new Color32(58, 66, 74, 255)),

            // Overlay (ART-015, ART-017)
            new AssetSpec(ArtAssetId.CueRing, "Art/Overlay/cue_ring", 128, 128, AssetClass.Overlay, new Color32(244, 241, 228, 220)),
            new AssetSpec(ArtAssetId.MarkerReachable, "Art/Overlay/marker_reachable", 256, 256, AssetClass.Overlay, new Color32(244, 241, 228, 220)),
            new AssetSpec(ArtAssetId.MarkerUnreachable, "Art/Overlay/marker_unreachable", 256, 256, AssetClass.Overlay, new Color32(224, 160, 48, 220)),

            // UI (ART-018, ART-019, ART-020)
            new AssetSpec(ArtAssetId.UiLabel, "Art/UI/ui_label", 96, 48, AssetClass.UiSprite, new Color32(38, 44, 52, 230), new Vector2(48f, 24f), new Vector4(16f, 16f, 16f, 16f)),
            new AssetSpec(ArtAssetId.UiPanel, "Art/UI/ui_panel", 128, 128, AssetClass.UiSprite, new Color32(38, 44, 52, 255), new Vector2(64f, 64f), new Vector4(24f, 24f, 24f, 24f)),
            new AssetSpec(ArtAssetId.UiButtonNormal, "Art/UI/ui_button_normal", 96, 48, AssetClass.UiSprite, new Color32(48, 54, 62, 255), new Vector2(48f, 24f), new Vector4(12f, 12f, 12f, 12f)),
            new AssetSpec(ArtAssetId.UiButtonHover, "Art/UI/ui_button_hover", 96, 48, AssetClass.UiSprite, new Color32(66, 70, 74, 255), new Vector2(48f, 24f), new Vector4(12f, 12f, 12f, 12f)),
            new AssetSpec(ArtAssetId.UiButtonPressed, "Art/UI/ui_button_pressed", 96, 48, AssetClass.UiSprite, new Color32(84, 72, 52, 255), new Vector2(48f, 24f), new Vector4(12f, 12f, 12f, 12f)),

            // World sprites (ART-003, ART-005..ART-010, ART-014)
            new AssetSpec(ArtAssetId.CliffCrest, "Art/Props/Cliffs/cliff_crest", 512, 384, AssetClass.WorldSprite, new Color32(104, 110, 118, 255), new Vector2(256f, 40f)),
            new AssetSpec(ArtAssetId.CliffToe, "Art/Props/Cliffs/cliff_toe", 512, 256, AssetClass.WorldSprite, new Color32(94, 100, 108, 255), new Vector2(256f, 40f)),
            new AssetSpec(ArtAssetId.BoulderUpright, "Art/Props/Boulders/boulder_upright", 320, 320, AssetClass.WorldSprite, new Color32(114, 118, 124, 255), new Vector2(160f, 40f)),
            new AssetSpec(ArtAssetId.BoulderSquat, "Art/Props/Boulders/boulder_squat", 448, 320, AssetClass.WorldSprite, new Color32(110, 114, 120, 255), new Vector2(224f, 40f)),
            new AssetSpec(ArtAssetId.BoulderLow, "Art/Props/Boulders/boulder_low", 320, 224, AssetClass.WorldSprite, new Color32(106, 110, 116, 255), new Vector2(160f, 40f)),
            new AssetSpec(ArtAssetId.PineUpright, "Art/Props/Pines/pine_upright", 768, 1024, AssetClass.WorldSprite, new Color32(52, 88, 54, 255), new Vector2(384f, 40f)),
            new AssetSpec(ArtAssetId.PineLeaning, "Art/Props/Pines/pine_leaning", 896, 960, AssetClass.WorldSprite, new Color32(56, 92, 58, 255), new Vector2(384f, 40f)),
            new AssetSpec(ArtAssetId.PineSpreading, "Art/Props/Pines/pine_spreading", 1024, 896, AssetClass.WorldSprite, new Color32(48, 84, 52, 255), new Vector2(512f, 40f)),
            new AssetSpec(ArtAssetId.BambooUpright, "Art/Props/Bamboo/bamboo_upright", 384, 768, AssetClass.WorldSprite, new Color32(68, 118, 62, 255), new Vector2(192f, 40f)),
            new AssetSpec(ArtAssetId.BambooLeaning, "Art/Props/Bamboo/bamboo_leaning", 512, 768, AssetClass.WorldSprite, new Color32(72, 122, 66, 255), new Vector2(224f, 40f)),
            new AssetSpec(ArtAssetId.BambooEdge, "Art/Props/Bamboo/bamboo_edge", 384, 512, AssetClass.WorldSprite, new Color32(64, 112, 58, 255), new Vector2(192f, 40f)),
            new AssetSpec(ArtAssetId.ShrubRound, "Art/Props/Shrubs/shrub_round", 192, 192, AssetClass.WorldSprite, new Color32(74, 110, 66, 255), new Vector2(96f, 32f)),
            new AssetSpec(ArtAssetId.ShrubSpreading, "Art/Props/Shrubs/shrub_spreading", 256, 160, AssetClass.WorldSprite, new Color32(78, 114, 70, 255), new Vector2(128f, 32f)),
            new AssetSpec(ArtAssetId.GrassShort, "Art/Props/Grass/grass_short", 128, 64, AssetClass.WorldSprite, new Color32(86, 136, 74, 255), new Vector2(64f, 12f)),
            new AssetSpec(ArtAssetId.GrassTall, "Art/Props/Grass/grass_tall", 128, 128, AssetClass.WorldSprite, new Color32(82, 130, 70, 255), new Vector2(64f, 16f)),
            new AssetSpec(ArtAssetId.GrassSparse, "Art/Props/Grass/grass_sparse", 128, 96, AssetClass.WorldSprite, new Color32(94, 128, 78, 255), new Vector2(64f, 16f)),
            new AssetSpec(ArtAssetId.FlowersWhite, "Art/Props/Flowers/flowers_white", 128, 96, AssetClass.WorldSprite, new Color32(232, 232, 224, 255), new Vector2(64f, 16f)),
            new AssetSpec(ArtAssetId.FlowersYellow, "Art/Props/Flowers/flowers_yellow", 128, 96, AssetClass.WorldSprite, new Color32(226, 196, 68, 255), new Vector2(64f, 16f)),
            new AssetSpec(ArtAssetId.PlaceholderCapsule, "Art/Props/Placeholder/placeholder_capsule", 128, 256, AssetClass.WorldSprite, new Color32(239, 232, 216, 255), new Vector2(64f, 32f))
        };

        private readonly Dictionary<ArtAssetId, Texture2D> _textures = new Dictionary<ArtAssetId, Texture2D>();
        private readonly Dictionary<ArtAssetId, Sprite> _sprites = new Dictionary<ArtAssetId, Sprite>();
        private readonly Dictionary<ArtAssetId, WorldSpriteEntry> _worldSprites = new Dictionary<ArtAssetId, WorldSpriteEntry>();
        private readonly HashSet<ArtAssetId> _standInIds = new HashSet<ArtAssetId>();
        private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>();

        public Texture2D PropAtlas { get; private set; }
        public bool AnyStandInInUse => _standInIds.Count > 0;
        public int StandInCount => _standInIds.Count;
        public int TotalAssetCount => Specs.Length;
        public int WorldSpriteCount => _worldSprites.Count;

        private ArtCatalog()
        {
        }

        public static ArtCatalog Load(bool forceStandIn = false)
        {
            ArtCatalog catalog = new ArtCatalog();
            catalog.LoadAll(forceStandIn);
            return catalog;
        }

        public Texture2D GetTexture(ArtAssetId id)
        {
            return _textures.TryGetValue(id, out Texture2D tex) ? tex : null;
        }

        public Sprite GetSprite(ArtAssetId id)
        {
            return _sprites.TryGetValue(id, out Sprite sprite) ? sprite : null;
        }

        public WorldSpriteEntry GetWorldSprite(ArtAssetId id)
        {
            if (_worldSprites.TryGetValue(id, out WorldSpriteEntry entry))
            {
                return entry;
            }
            throw new KeyNotFoundException($"World sprite '{id}' was not loaded.");
        }

        public bool IsStandIn(ArtAssetId id)
        {
            return _standInIds.Contains(id);
        }

        public void Dispose()
        {
            for (int i = 0; i < _ownedObjects.Count; i++)
            {
                UnityEngine.Object obj = _ownedObjects[i];
                if (obj != null)
                {
                    DestroyObject(obj);
                }
            }
            _ownedObjects.Clear();
            PropAtlas = null;
        }

        private void LoadAll(bool forceStandIn)
        {
            List<AssetSpec> worldSpecs = new List<AssetSpec>();
            List<Texture2D> worldTextures = new List<Texture2D>();
            List<Sprite> worldSpriteRefs = new List<Sprite>();

            for (int i = 0; i < Specs.Length; i++)
            {
                AssetSpec spec = Specs[i];
                LoadEntry(spec, forceStandIn, worldSpecs, worldTextures, worldSpriteRefs);
            }

            PackWorldAtlas(worldSpecs, worldTextures, worldSpriteRefs);
        }

        private void LoadEntry(
            AssetSpec spec,
            bool forceStandIn,
            List<AssetSpec> worldSpecs,
            List<Texture2D> worldTextures,
            List<Sprite> worldSpriteRefs)
        {
            Texture2D tex = forceStandIn ? null : Resources.Load<Texture2D>(spec.ResourcePath);
            Sprite sprite = (!forceStandIn && IsSpriteClass(spec.Class))
                ? Resources.Load<Sprite>(spec.ResourcePath)
                : null;

            bool isStandIn = tex == null || (IsSpriteClass(spec.Class) && sprite == null);
            if (isStandIn)
            {
                if (!forceStandIn)
                {
                    GameLog.LogWarning("ArtMissing", spec.FileName);
                }
                _standInIds.Add(spec.Id);
                tex = CreateStandInTexture(spec);
                if (IsSpriteClass(spec.Class))
                {
                    sprite = CreateStandInSprite(spec, tex);
                }
            }

            _textures[spec.Id] = tex;
            if (sprite != null)
            {
                _sprites[spec.Id] = sprite;
            }

            if (spec.Class == AssetClass.WorldSprite)
            {
                worldSpecs.Add(spec);
                worldTextures.Add(tex);
                worldSpriteRefs.Add(sprite);
            }
        }

        private void PackWorldAtlas(
            List<AssetSpec> worldSpecs,
            List<Texture2D> worldTextures,
            List<Sprite> worldSpriteRefs)
        {
            Texture2D[] packInputs = new Texture2D[worldTextures.Count];
            List<Texture2D> tempUncompressed = new List<Texture2D>();

            for (int i = 0; i < worldTextures.Count; i++)
            {
                packInputs[i] = EnsurePackableTexture(worldTextures[i], tempUncompressed);
            }

            Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Trilinear;
            Rect[] rects = atlas.PackTextures(packInputs, AtlasPadding, MaxAtlasSize, false);
            PropAtlas = atlas;
            _ownedObjects.Add(atlas);

            for (int i = 0; i < tempUncompressed.Count; i++)
            {
                DestroyObject(tempUncompressed[i]);
            }

            for (int i = 0; i < worldSpecs.Count; i++)
            {
                AssetSpec spec = worldSpecs[i];
                bool isStandIn = _standInIds.Contains(spec.Id);
                _worldSprites[spec.Id] = new WorldSpriteEntry(
                    spec.Id,
                    worldTextures[i],
                    worldSpriteRefs[i],
                    rects[i],
                    spec.Width,
                    spec.Height,
                    spec.PivotPixels,
                    isStandIn);
            }
        }

        private Texture2D CreateStandInTexture(AssetSpec spec)
        {
            bool linear = spec.Class == AssetClass.Mask;
            bool mipChain = spec.Class != AssetClass.UiSprite;
            TextureFormat format = IsAlphaStandIn(spec) ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            Texture2D tex = new Texture2D(spec.Width, spec.Height, format, mipChain, linear);

            ApplyStandInSampling(spec, tex);
            FillStandInPixels(spec, tex);
            tex.Apply(mipChain, false);
            _ownedObjects.Add(tex);
            return tex;
        }

        private Sprite CreateStandInSprite(AssetSpec spec, Texture2D tex)
        {
            float ppu = spec.Class == AssetClass.UiSprite ? UiPixelsPerUnit : WorldPixelsPerMetre;
            Vector2 pivotNorm = new Vector2(spec.PivotPixels.x / spec.Width, spec.PivotPixels.y / spec.Height);
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, spec.Width, spec.Height),
                pivotNorm,
                ppu,
                0,
                SpriteMeshType.FullRect,
                spec.UiBorder);
            _ownedObjects.Add(sprite);
            return sprite;
        }

        private static void ApplyStandInSampling(AssetSpec spec, Texture2D tex)
        {
            if (spec.Class == AssetClass.Tile || spec.Class == AssetClass.Mask)
            {
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
            }
            else if (spec.Id == ArtAssetId.RiverShore)
            {
                tex.wrapModeU = TextureWrapMode.Repeat;
                tex.wrapModeV = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
            }
            else if (spec.Id == ArtAssetId.FallRibbon)
            {
                tex.wrapModeU = TextureWrapMode.Clamp;
                tex.wrapModeV = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
            }
            else if (spec.Class == AssetClass.Overlay || spec.Class == AssetClass.UiSprite)
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
            }
            else
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
            }
        }

        private static void FillStandInPixels(AssetSpec spec, Texture2D tex)
        {
            int w = spec.Width;
            int h = spec.Height;
            Color32[] pixels = new Color32[w * h];
            Color32 fill = spec.StandInColor;
            Color32 clear = new Color32(0, 0, 0, 0);
            bool borderTransparent = spec.Class == AssetClass.WorldSprite
                || spec.Class == AssetClass.WaterDecal
                || spec.Id == ArtAssetId.MarkerUnreachable;

            for (int y = 0; y < h; y++)
            {
                bool rowEdge = borderTransparent && (y < 4 || y >= h - 4);
                int rowStart = y * w;
                for (int x = 0; x < w; x++)
                {
                    bool colEdge = borderTransparent && (x < 4 || x >= w - 4);
                    pixels[rowStart + x] = (rowEdge || colEdge) ? clear : fill;
                }
            }

            tex.SetPixels32(pixels);
        }

        private static Texture2D EnsurePackableTexture(Texture2D source, List<Texture2D> tempList)
        {
            if (source.format == TextureFormat.RGBA32 || source.format == TextureFormat.ARGB32)
            {
                return source;
            }

            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            if (source.isReadable)
            {
                copy.SetPixels32(source.GetPixels32());
                copy.Apply(false, false);
            }
            else
            {
                CopyViaRenderTexture(source, copy);
            }
            tempList.Add(copy);
            return copy;
        }

        private static void CopyViaRenderTexture(Texture2D source, Texture2D destination)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            destination.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            destination.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        private static bool IsSpriteClass(AssetClass assetClass)
        {
            return assetClass == AssetClass.WorldSprite || assetClass == AssetClass.UiSprite;
        }

        private static bool IsAlphaStandIn(AssetSpec spec)
        {
            return spec.Class != AssetClass.Tile && spec.Class != AssetClass.Mask;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
