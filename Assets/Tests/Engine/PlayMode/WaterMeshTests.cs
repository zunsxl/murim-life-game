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
    public sealed class WaterMeshTests
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

        // T-ENG-05
        [UnityTest]
        public IEnumerator T_ENG_05_WaterMeshes_MatchCoreGeometryPositionsAndSortOrders()
        {
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "1" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            ValleyWorld world = boot.World;
            WaterSceneObjects water = boot.WaterObjects;
            Assert.That(water, Is.Not.Null);

            Assert.That(water.Stream.sortingOrder, Is.EqualTo(-30000));
            Assert.That(water.Brook.sortingOrder, Is.EqualTo(-30000));
            Assert.That(water.PlungePool.sortingOrder, Is.EqualTo(-30000));
            Assert.That(water.SpringPool.sortingOrder, Is.EqualTo(-30000));
            Assert.That(water.Waterfall.sortingOrder, Is.EqualTo(-29999));
            Assert.That(water.WaterFoam.sortingOrder, Is.EqualTo(-29998));

            AssertRibbon(water.Stream.GetComponent<MeshFilter>().sharedMesh, world.Stream);
            AssertRibbon(water.Brook.GetComponent<MeshFilter>().sharedMesh, world.Brook);
            AssertPool(water.PlungePool.GetComponent<MeshFilter>().sharedMesh, world.PlungePool);
            AssertPool(water.SpringPool.GetComponent<MeshFilter>().sharedMesh, world.SpringPool);
            AssertWaterfall(water.Waterfall.GetComponent<MeshFilter>().sharedMesh, world.Waterfall);
            AssertFoam(water.WaterFoam.GetComponent<MeshFilter>().sharedMesh, world.PlungePool);
        }

        private static void AssertRibbon(Mesh mesh, WaterNode[] nodes)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(5 * nodes.Length));
            Vector3[] verts = mesh.vertices;
            Color32[] colors = mesh.colors32;
            Vector2[] uvs = mesh.uv;

            for (int i = 0; i < nodes.Length; i++)
            {
                int row = i * 5;
                for (int k = 0; k < 5; k++)
                {
                    Assert.That(verts[row + k].z, Is.EqualTo(0f));
                }

                Assert.That(colors[row + 0].a, Is.EqualTo(0));
                Assert.That(colors[row + 1].a, Is.EqualTo(255));
                Assert.That(colors[row + 2].a, Is.EqualTo(255));
                Assert.That(colors[row + 3].a, Is.EqualTo(255));
                Assert.That(colors[row + 4].a, Is.EqualTo(0));

                Assert.That(uvs[row + 2].x, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(uvs[row + 0].x, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(uvs[row + 4].x, Is.EqualTo(1f).Within(1e-5f));

                float expectedU = nodes[i].XCm / 100f;
                float expectedV = Projection.PlaneVCm(nodes[i].ZCm, nodes[i].LevelCm) / 100f;
                Assert.That(verts[row + 2].x, Is.EqualTo(expectedU).Within(1e-4f));
                Assert.That(verts[row + 2].y, Is.EqualTo(expectedV).Within(1e-4f));
            }
        }

        private static void AssertPool(Mesh mesh, WaterPool pool)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(1 + 3 * WaterMeshBuilder.PoolSegments));
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.That(verts[i].z, Is.EqualTo(0f));
            }

            float expectedU = pool.XCm / 100f;
            float expectedV = Projection.PlaneVCm(pool.ZCm, pool.LevelCm) / 100f;
            Assert.That(verts[0].x, Is.EqualTo(expectedU).Within(1e-4f));
            Assert.That(verts[0].y, Is.EqualTo(expectedV).Within(1e-4f));
        }

        private static void AssertWaterfall(Mesh mesh, FallNode[] nodes)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.That(verts[i].z, Is.EqualTo(0f));
            }

            FallNode top = nodes[0];
            FallNode bottom = nodes[nodes.Length - 1];
            float expectedTopV = Projection.PlaneVCm(top.ZCm, top.YCm) / 100f;
            float expectedBottomV = Projection.PlaneVCm(bottom.ZCm, bottom.YCm) / 100f;
            float expectedCenterU = top.XCm / 100f;
            float expectedWidth = ValleyWorld.WaterfallWidthCm / 100f;

            Assert.That(mesh.bounds.center.x, Is.EqualTo(expectedCenterU).Within(1e-4f));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(expectedWidth).Within(1e-4f));
            Assert.That(mesh.bounds.max.y, Is.EqualTo(expectedTopV).Within(1e-4f));
            Assert.That(mesh.bounds.min.y, Is.EqualTo(expectedBottomV).Within(1e-4f));
        }

        private static void AssertFoam(Mesh mesh, WaterPool plungePool)
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.That(verts[i].z, Is.EqualTo(0f));
            }

            float expectedU = plungePool.XCm / 100f;
            float expectedV = Projection.PlaneVCm(plungePool.ZCm, plungePool.LevelCm) / 100f;
            Assert.That(mesh.bounds.center.x, Is.EqualTo(expectedU).Within(1e-4f));
            Assert.That(mesh.bounds.center.y, Is.EqualTo(expectedV).Within(1e-4f));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(2.4f * plungePool.RadiusCm / 100f).Within(1e-4f));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(1.44f * plungePool.RadiusCm / 100f).Within(1e-4f));
        }
    }
}
