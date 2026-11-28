using System.Collections.Generic;
using Murim.Core.Valley;
using UnityEngine;

namespace Murim.View.World
{
    /// <summary>
    /// Builds up to 15 combined 32x32-cell block meshes for all upright props, bamboo, and ground cover per §3.7.
    /// </summary>
    public static class PropMeshBuilder
    {
        public const int BlockCells = 32;
        private const float PineTrunkCutoff = 0.35f;

        private static readonly Vector2[] CoverOffsets =
        {
            new Vector2(-0.22f, -0.18f),
            new Vector2(0.24f, -0.14f),
            new Vector2(-0.16f, 0.22f),
            new Vector2(0.20f, 0.18f),
            new Vector2(-0.28f, 0.04f),
            new Vector2(0.26f, -0.02f),
            new Vector2(0.04f, -0.26f),
            new Vector2(-0.04f, 0.26f)
        };

        private sealed class BlockBuffer
        {
            public readonly int BlockX;
            public readonly int BlockZ;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv0 = new List<Vector2>();
            public readonly List<Vector4> Uv1 = new List<Vector4>();
            public readonly List<Vector4> Uv2 = new List<Vector4>();
            public readonly List<int> Triangles = new List<int>();

            public BlockBuffer(int bx, int bz)
            {
                BlockX = bx;
                BlockZ = bz;
            }
        }

        private readonly struct SwayParams
        {
            public readonly float Amplitude;
            public readonly float Period;
            public readonly float Phase;
            public readonly float Cutoff;

            public SwayParams(float amplitude, float period, float phase, float cutoff)
            {
                Amplitude = amplitude;
                Period = period;
                Phase = phase;
                Cutoff = cutoff;
            }
        }

        public static List<Mesh> Build(ValleyLayout layout, ArtCatalog catalog, PresentationTuning tuning)
        {
            int blocksX = (layout.Width + BlockCells - 1) / BlockCells;
            int blocksZ = (layout.Depth + BlockCells - 1) / BlockCells;
            BlockBuffer[,] blocks = new BlockBuffer[blocksX, blocksZ];

            AddExplicitProps(layout, catalog, tuning, blocks);
            AddBambooAndCover(layout, catalog, tuning, blocks);

            List<Mesh> result = new List<Mesh>(blocksX * blocksZ);
            for (int bz = 0; bz < blocksZ; bz++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    BlockBuffer buffer = blocks[bx, bz];
                    if (buffer != null && buffer.Triangles.Count > 0)
                    {
                        result.Add(FinalizeBlockMesh(buffer));
                    }
                }
            }
            return result;
        }

        private static void AddExplicitProps(
            ValleyLayout layout,
            ArtCatalog catalog,
            PresentationTuning tuning,
            BlockBuffer[,] blocks)
        {
            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement prop = props[i];
                if (!TryMapPropAsset(prop.Kind, out ArtAssetId assetId))
                {
                    continue;
                }

                float x = prop.Cx + 0.5f;
                float z = prop.Cz + 0.5f;
                float y = ResolvePropGroundY(layout, prop.Kind, prop.Cx, prop.Cz);
                SwayParams sway = GetPropSway(prop.Kind, prop.Cx, prop.Cz, tuning);
                BlockBuffer buffer = GetOrCreateBlock(blocks, prop.Cx, prop.Cz);
                AppendQuad(buffer, catalog.GetWorldSprite(assetId), new Vector3(x, y, z), prop.FlipX, sway);
            }
        }

        private static void AddBambooAndCover(
            ValleyLayout layout,
            ArtCatalog catalog,
            PresentationTuning tuning,
            BlockBuffer[,] blocks)
        {
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.KindAt(cx, cz) == CellKind.Bamboo)
                    {
                        AddBambooCell(layout, catalog, tuning, blocks, cx, cz);
                    }

                    CoverKind cover = layout.CoverAt(cx, cz);
                    if (cover != CoverKind.None)
                    {
                        AddCoverCell(layout, catalog, tuning, blocks, cx, cz, cover);
                    }
                }
            }
        }

        private static void AddBambooCell(
            ValleyLayout layout,
            ArtCatalog catalog,
            PresentationTuning tuning,
            BlockBuffer[,] blocks,
            int cx,
            int cz)
        {
            bool southIsBamboo = cz > 0 && layout.KindAt(cx, cz - 1) == CellKind.Bamboo;
            ArtAssetId assetId;
            if (!southIsBamboo)
            {
                assetId = ArtAssetId.BambooEdge;
            }
            else
            {
                assetId = (((cx * 7 + cz * 13) & 1) == 0) ? ArtAssetId.BambooUpright : ArtAssetId.BambooLeaning;
            }

            float x = cx + 0.5f;
            float z = cz + 0.5f;
            float y = layout.HeightAt(x, z);
            SwayParams sway = new SwayParams(tuning.BambooSwayAmplitude, tuning.BambooSwayPeriod, CellPhase(cx, cz), 0f);
            BlockBuffer buffer = GetOrCreateBlock(blocks, cx, cz);
            AppendQuad(buffer, catalog.GetWorldSprite(assetId), new Vector3(x, y, z), false, sway);
        }

        private static void AddCoverCell(
            ValleyLayout layout,
            ArtCatalog catalog,
            PresentationTuning tuning,
            BlockBuffer[,] blocks,
            int cx,
            int cz,
            CoverKind cover)
        {
            ArtAssetId assetId = MapCoverAsset(cover);
            WorldSpriteEntry sprite = catalog.GetWorldSprite(assetId);
            BlockBuffer buffer = GetOrCreateBlock(blocks, cx, cz);
            SwayParams sway = GetCoverSway(cover, cx, cz, tuning);

            int baseOffsetIndex = ((cx * 7 + cz * 13) % CoverOffsets.Length + CoverOffsets.Length) % CoverOffsets.Length;
            int cardCount = cover == CoverKind.ShortGrass ? 2 : 1;

            for (int c = 0; c < cardCount; c++)
            {
                int idx = (baseOffsetIndex + c * 4) % CoverOffsets.Length;
                Vector2 offset = CoverOffsets[idx];
                float x = cx + 0.5f + offset.x;
                float z = cz + 0.5f + offset.y;
                float y = layout.HeightAt(x, z);
                bool flipX = ((cx + cz + c) & 1) == 1;
                SwayParams cardSway = new SwayParams(sway.Amplitude, sway.Period, CellPhase(cx + c * 17, cz + c * 31), 0f);
                AppendQuad(buffer, sprite, new Vector3(x, y, z), flipX, cardSway);
            }
        }

        private static void AppendQuad(
            BlockBuffer buffer,
            WorldSpriteEntry sprite,
            Vector3 pivotWorld,
            bool flipX,
            SwayParams sway)
        {
            float ppu = ArtCatalog.WorldPixelsPerMetre;
            float worldW = sprite.PixelWidth / ppu;
            float left = flipX ? -(sprite.PixelWidth - sprite.PivotPixels.x) / ppu : -sprite.PivotPixels.x / ppu;
            float right = left + worldW;

            float bottom = -(sprite.PivotPixels.y / ppu) * ValleyConstants.Sqrt2;
            float top = ((sprite.PixelHeight - sprite.PivotPixels.y) / ppu) * ValleyConstants.Sqrt2;
            float bottomNorm = top > 1e-5f ? bottom / top : 0f;

            Rect r = sprite.AtlasUvRect;
            float u0 = flipX ? r.xMax : r.xMin;
            float u1 = flipX ? r.xMin : r.xMax;
            float signedDuDx = (u1 - u0) / worldW;

            int vBase = buffer.Vertices.Count;
            buffer.Vertices.Add(new Vector3(pivotWorld.x + left, pivotWorld.y + bottom, pivotWorld.z));
            buffer.Vertices.Add(new Vector3(pivotWorld.x + right, pivotWorld.y + bottom, pivotWorld.z));
            buffer.Vertices.Add(new Vector3(pivotWorld.x + right, pivotWorld.y + top, pivotWorld.z));
            buffer.Vertices.Add(new Vector3(pivotWorld.x + left, pivotWorld.y + top, pivotWorld.z));

            for (int i = 0; i < 4; i++)
            {
                buffer.Normals.Add(Vector3.back);
            }

            buffer.Uv0.Add(new Vector2(u0, r.yMin));
            buffer.Uv0.Add(new Vector2(u1, r.yMin));
            buffer.Uv0.Add(new Vector2(u1, r.yMax));
            buffer.Uv0.Add(new Vector2(u0, r.yMax));

            buffer.Uv1.Add(new Vector4(sway.Amplitude, sway.Period, sway.Phase, bottomNorm));
            buffer.Uv1.Add(new Vector4(sway.Amplitude, sway.Period, sway.Phase, bottomNorm));
            buffer.Uv1.Add(new Vector4(sway.Amplitude, sway.Period, sway.Phase, 1f));
            buffer.Uv1.Add(new Vector4(sway.Amplitude, sway.Period, sway.Phase, 1f));

            Vector4 pineMeta = new Vector4(sway.Cutoff, signedDuDx, r.xMin, r.xMax);
            for (int i = 0; i < 4; i++)
            {
                buffer.Uv2.Add(pineMeta);
            }

            buffer.Triangles.Add(vBase + 0);
            buffer.Triangles.Add(vBase + 3);
            buffer.Triangles.Add(vBase + 2);
            buffer.Triangles.Add(vBase + 0);
            buffer.Triangles.Add(vBase + 2);
            buffer.Triangles.Add(vBase + 1);
        }

        private static Mesh FinalizeBlockMesh(BlockBuffer buffer)
        {
            Mesh mesh = new Mesh { name = $"PropBlock_{buffer.BlockX}_{buffer.BlockZ}" };
            mesh.SetVertices(buffer.Vertices);
            mesh.SetNormals(buffer.Normals);
            mesh.SetUVs(0, buffer.Uv0);
            mesh.SetUVs(1, buffer.Uv1);
            mesh.SetUVs(2, buffer.Uv2);
            mesh.SetTriangles(buffer.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BlockBuffer GetOrCreateBlock(BlockBuffer[,] blocks, int cx, int cz)
        {
            int bx = cx / BlockCells;
            int bz = cz / BlockCells;
            BlockBuffer buffer = blocks[bx, bz];
            if (buffer == null)
            {
                buffer = new BlockBuffer(bx, bz);
                blocks[bx, bz] = buffer;
            }
            return buffer;
        }

        private static float ResolvePropGroundY(ValleyLayout layout, PropKind kind, int cx, int cz)
        {
            if (kind != PropKind.CliffCrest && kind != PropKind.CliffToe)
            {
                return layout.HeightAt(cx + 0.5f, cz + 0.5f);
            }

            float h00 = layout.VertexHeight(cx, cz);
            float h10 = layout.VertexHeight(cx + 1, cz);
            float h01 = layout.VertexHeight(cx, cz + 1);
            float h11 = layout.VertexHeight(cx + 1, cz + 1);
            return kind == PropKind.CliffCrest
                ? Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11))
                : Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));
        }

        private static bool TryMapPropAsset(PropKind kind, out ArtAssetId assetId)
        {
            switch (kind)
            {
                case PropKind.BoulderUpright: assetId = ArtAssetId.BoulderUpright; return true;
                case PropKind.BoulderSquat: assetId = ArtAssetId.BoulderSquat; return true;
                case PropKind.BoulderLow: assetId = ArtAssetId.BoulderLow; return true;
                case PropKind.PineUpright: assetId = ArtAssetId.PineUpright; return true;
                case PropKind.PineLeaning: assetId = ArtAssetId.PineLeaning; return true;
                case PropKind.PineSpreading: assetId = ArtAssetId.PineSpreading; return true;
                case PropKind.ShrubRound: assetId = ArtAssetId.ShrubRound; return true;
                case PropKind.ShrubSpreading: assetId = ArtAssetId.ShrubSpreading; return true;
                case PropKind.CliffCrest: assetId = ArtAssetId.CliffCrest; return true;
                case PropKind.CliffToe: assetId = ArtAssetId.CliffToe; return true;
                default:
                    assetId = default;
                    return false;
            }
        }

        private static ArtAssetId MapCoverAsset(CoverKind cover)
        {
            return cover switch
            {
                CoverKind.ShortGrass => ArtAssetId.GrassShort,
                CoverKind.TallGrass => ArtAssetId.GrassTall,
                CoverKind.SparseGrass => ArtAssetId.GrassSparse,
                CoverKind.WhiteFlowers => ArtAssetId.FlowersWhite,
                CoverKind.YellowFlowers => ArtAssetId.FlowersYellow,
                _ => ArtAssetId.GrassShort
            };
        }

        private static SwayParams GetPropSway(PropKind kind, int cx, int cz, PresentationTuning tuning)
        {
            if (ValleyKindMetadata.IsPine(kind))
            {
                return new SwayParams(tuning.PineSwayAmplitude, tuning.PineSwayPeriod, CellPhase(cx, cz), PineTrunkCutoff);
            }
            return new SwayParams(0f, 1f, 0f, 0f);
        }

        private static SwayParams GetCoverSway(CoverKind cover, int cx, int cz, PresentationTuning tuning)
        {
            bool isFlower = cover == CoverKind.WhiteFlowers || cover == CoverKind.YellowFlowers;
            float amp = isFlower ? tuning.FlowerSwayAmplitude : tuning.GrassSwayAmplitude;
            float period = isFlower ? tuning.FlowerSwayPeriod : tuning.GrassSwayPeriod;
            return new SwayParams(amp, period, CellPhase(cx, cz), 0f);
        }

        private static float CellPhase(int cx, int cz)
        {
            unchecked
            {
                uint h = (uint)(cx * 73856093 ^ cz * 19349663);
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                return (h & 0xFFFFu) / 65536f;
            }
        }
    }
}
