using System.Collections.Generic;
using Murim.Core.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Murim.View
{
    /// <summary>
    /// Builds the 64 flat projected terrain chunk meshes from <see cref="ValleyWorld"/> (§3.3.2).
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public const int ChunksPerSide = 8;
        public const int CellsPerChunk = 64;
        public const int VertsPerChunkSide = CellsPerChunk + 1;
        public const int VerticesPerChunk = VertsPerChunkSide * VertsPerChunkSide;
        public const int TrianglesPerChunk = CellsPerChunk * CellsPerChunk * 2;

        private sealed class ChunkScratch
        {
            public readonly Vector3[] Positions = new Vector3[VerticesPerChunk];
            public readonly Vector4[] Uv0 = new Vector4[VerticesPerChunk];
            public readonly Vector4[] Uv1 = new Vector4[VerticesPerChunk];
            public readonly Vector2[] Uv2 = new Vector2[VerticesPerChunk];
            public readonly Color32[] Colors = CreateWhiteColors();
            public readonly int[] Indices = BuildChunkIndices();

            private static Color32[] CreateWhiteColors()
            {
                var colors = new Color32[VerticesPerChunk];
                var white = new Color32(255, 255, 255, 255);
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = white;
                }

                return colors;
            }
        }

        public static List<MeshRenderer> Build(ValleyWorld world, Material terrainMaterial, Transform parent)
        {
            var renderers = new List<MeshRenderer>(ChunksPerSide * ChunksPerSide);
            var scratch = new ChunkScratch();

            for (int chunkRow = 0; chunkRow < ChunksPerSide; chunkRow++)
            {
                for (int chunkCol = 0; chunkCol < ChunksPerSide; chunkCol++)
                {
                    Mesh mesh = BuildChunkMesh(world, chunkRow, chunkCol, scratch);
                    MeshRenderer renderer = CreateChunkObject(
                        mesh,
                        terrainMaterial,
                        chunkRow,
                        chunkCol,
                        parent);
                    renderers.Add(renderer);
                }
            }

            return renderers;
        }

        private static Mesh BuildChunkMesh(ValleyWorld world, int chunkRow, int chunkCol, ChunkScratch scratch)
        {
            int xStart = chunkCol * CellsPerChunk;
            int zStart = chunkRow * CellsPerChunk;

            for (int lz = 0; lz < VertsPerChunkSide; lz++)
            {
                int gz = zStart + lz;
                int localRowOffset = lz * VertsPerChunkSide;
                for (int lx = 0; lx < VertsPerChunkSide; lx++)
                {
                    FillVertex(world, xStart + lx, gz, localRowOffset + lx, scratch);
                }
            }

            var mesh = new Mesh
            {
                name = $"TerrainChunk_{chunkRow}_{chunkCol}",
                indexFormat = IndexFormat.UInt16,
            };
            mesh.vertices = scratch.Positions;
            mesh.SetUVs(0, scratch.Uv0);
            mesh.SetUVs(1, scratch.Uv1);
            mesh.SetUVs(2, scratch.Uv2);
            mesh.colors32 = scratch.Colors;
            mesh.triangles = scratch.Indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void FillVertex(ValleyWorld world, int gx, int gz, int dstIdx, ChunkScratch scratch)
        {
            int worldIdx = gz * ValleyWorld.VertsPerSide + gx;
            int heightCm = world.HeightCm[worldIdx];
            scratch.Positions[dstIdx] = new Vector3(gx, Projection.PlaneVCm(gz * 100, heightCm) / 100f, 0f);

            byte[] weights = world.SurfaceWeights;
            int wBase = worldIdx * ValleyWorld.SurfaceLayerCount;
            scratch.Uv0[dstIdx] = new Vector4(
                weights[wBase] / 255f,
                weights[wBase + 1] / 255f,
                weights[wBase + 2] / 255f,
                weights[wBase + 3] / 255f);
            scratch.Uv1[dstIdx] = new Vector4(
                weights[wBase + 4] / 255f,
                weights[wBase + 5] / 255f,
                weights[wBase + 6] / 255f,
                weights[wBase + 7] / 255f);

            float relief = ComputeReliefFactor(world.HeightCm, gx, gz);
            scratch.Uv2[dstIdx] = new Vector2(relief, 0f);
        }

        private static float ComputeReliefFactor(short[] heights, int x, int z)
        {
            int xm = x > 0 ? x - 1 : 0;
            int xp = x < ValleyWorld.SizeM ? x + 1 : ValleyWorld.SizeM;
            int zm = z > 0 ? z - 1 : 0;
            int zp = z < ValleyWorld.SizeM ? z + 1 : ValleyWorld.SizeM;

            int rowZ = z * ValleyWorld.VertsPerSide;
            int gx = heights[rowZ + xp] - heights[rowZ + xm];
            int gz = heights[zp * ValleyWorld.VertsPerSide + x] - heights[zm * ValleyWorld.VertsPerSide + x];
            return Projection.ReliefQ(gx, gz) / 1024f;
        }

        private static int[] BuildChunkIndices()
        {
            var indices = new int[TrianglesPerChunk * 3];
            int cursor = 0;

            for (int cz = CellsPerChunk - 1; cz >= 0; cz--)
            {
                for (int cx = 0; cx < CellsPerChunk; cx++)
                {
                    int v00 = cz * VertsPerChunkSide + cx;
                    int v10 = v00 + 1;
                    int v01 = v00 + VertsPerChunkSide;
                    int v11 = v01 + 1;

                    indices[cursor++] = v00;
                    indices[cursor++] = v01;
                    indices[cursor++] = v11;
                    indices[cursor++] = v00;
                    indices[cursor++] = v11;
                    indices[cursor++] = v10;
                }
            }

            return indices;
        }

        private static MeshRenderer CreateChunkObject(
            Mesh mesh,
            Material material,
            int chunkRow,
            int chunkCol,
            Transform parent)
        {
            var go = new GameObject($"Terrain_{chunkRow}_{chunkCol}");
            go.layer = DrawOrder.WorldLayer;
            go.transform.SetParent(parent, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = DrawOrder.TerrainChunk(chunkRow);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
    }
}
