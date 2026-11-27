using System;
using System.Collections;
using System.Collections.Generic;
using Murim.Core.World;
using Murim.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class TerrainMeshTests
    {
        private Func<IReadOnlyList<string>> _originalArgs;

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
        }

        [TearDown]
        public void TearDown()
        {
            SeedSource.GetCommandLineArgs = _originalArgs;
        }

        // T-ENG-04
        [UnityTest]
        public IEnumerator T_ENG_04_TerrainChunks_MatchCoreProjectionChannelsOrderAndSorting()
        {
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "1" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            Assert.That(boot.TerrainRenderers.Count, Is.EqualTo(64));
            Assert.That(UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None), Is.Empty);

            ValleyWorld world = boot.World;
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector4>();
            var uv2 = new List<Vector2>();

            for (int chunkRow = 0; chunkRow < TerrainMeshBuilder.ChunksPerSide; chunkRow++)
            {
                for (int chunkCol = 0; chunkCol < TerrainMeshBuilder.ChunksPerSide; chunkCol++)
                {
                    int chunkIndex = chunkRow * TerrainMeshBuilder.ChunksPerSide + chunkCol;
                    MeshRenderer renderer = boot.TerrainRenderers[chunkIndex];
                    Assert.That(renderer.sortingOrder, Is.EqualTo(-31000 + (7 - chunkRow)));

                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.vertexCount, Is.EqualTo(4225));

                    Vector3[] verts = mesh.vertices;
                    int[] tris = mesh.triangles;
                    Assert.That(tris.Length, Is.EqualTo(8192 * 3));

                    for (int i = 0; i < verts.Length; i++)
                    {
                        Assert.That(verts[i].z, Is.EqualTo(0f));
                    }

                    // First triangle in northern cell row (lz in {63, 64}); last triangle in southern cell row (lz in {0, 1})
                    AssertTriangleInRowRange(tris, 0, 63, 64);
                    AssertTriangleInRowRange(tris, tris.Length - 3, 0, 1);

                    mesh.GetUVs(0, uv0);
                    mesh.GetUVs(1, uv1);
                    mesh.GetUVs(2, uv2);
                    AssertSampleVertices(world, chunkRow, chunkCol, verts, uv0, uv1, uv2);
                }
            }
        }

        private static void AssertTriangleInRowRange(int[] tris, int baseIdx, int minLz, int maxLz)
        {
            for (int k = 0; k < 3; k++)
            {
                int lz = tris[baseIdx + k] / TerrainMeshBuilder.VertsPerChunkSide;
                Assert.That(lz, Is.InRange(minLz, maxLz));
            }
        }

        private static void AssertSampleVertices(
            ValleyWorld world,
            int chunkRow,
            int chunkCol,
            Vector3[] verts,
            List<Vector4> uv0,
            List<Vector4> uv1,
            List<Vector2> uv2)
        {
            int[] sampleCoords = { 0, 32, 64 };
            foreach (int lz in sampleCoords)
            {
                foreach (int lx in sampleCoords)
                {
                    int localIdx = lz * TerrainMeshBuilder.VertsPerChunkSide + lx;
                    int gx = chunkCol * TerrainMeshBuilder.CellsPerChunk + lx;
                    int gz = chunkRow * TerrainMeshBuilder.CellsPerChunk + lz;
                    int worldIdx = gz * ValleyWorld.VertsPerSide + gx;

                    float expectedV = Projection.PlaneVCm(gz * 100, world.HeightCm[worldIdx]) / 100f;
                    Assert.That(verts[localIdx].x, Is.EqualTo((float)gx).Within(1e-4f));
                    Assert.That(verts[localIdx].y, Is.EqualTo(expectedV).Within(1e-4f));

                    int wBase = worldIdx * ValleyWorld.SurfaceLayerCount;
                    byte[] w = world.SurfaceWeights;
                    Assert.That(uv0[localIdx].x, Is.EqualTo(w[wBase] / 255f).Within(1e-5f));
                    Assert.That(uv0[localIdx].y, Is.EqualTo(w[wBase + 1] / 255f).Within(1e-5f));
                    Assert.That(uv0[localIdx].z, Is.EqualTo(w[wBase + 2] / 255f).Within(1e-5f));
                    Assert.That(uv0[localIdx].w, Is.EqualTo(w[wBase + 3] / 255f).Within(1e-5f));
                    Assert.That(uv1[localIdx].x, Is.EqualTo(w[wBase + 4] / 255f).Within(1e-5f));
                    Assert.That(uv1[localIdx].y, Is.EqualTo(w[wBase + 5] / 255f).Within(1e-5f));
                    Assert.That(uv1[localIdx].z, Is.EqualTo(w[wBase + 6] / 255f).Within(1e-5f));
                    Assert.That(uv1[localIdx].w, Is.EqualTo(w[wBase + 7] / 255f).Within(1e-5f));

                    float expectedRelief = ExpectedRelief(world.HeightCm, gx, gz);
                    Assert.That(uv2[localIdx].x, Is.EqualTo(expectedRelief).Within(1e-5f));
                }
            }
        }

        private static float ExpectedRelief(short[] heights, int x, int z)
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
    }
}
