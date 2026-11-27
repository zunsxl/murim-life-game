using System;
using Murim.Core.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Murim.View
{
    public sealed class WaterMaterials
    {
        public Material Water { get; set; }
        public Material Waterfall { get; set; }
        public Material Foam { get; set; }
    }

    public sealed class WaterSceneObjects
    {
        public MeshRenderer Stream { get; internal set; }
        public MeshRenderer Brook { get; internal set; }
        public MeshRenderer PlungePool { get; internal set; }
        public MeshRenderer SpringPool { get; internal set; }
        public MeshRenderer Waterfall { get; internal set; }
        public MeshRenderer WaterFoam { get; internal set; }
    }

    /// <summary>
    /// Builds static flat 2D water, waterfall, and foam meshes from <see cref="ValleyWorld"/> (§3.3.3).
    /// </summary>
    public static class WaterMeshBuilder
    {
        public const int PoolSegments = 24;
        private const int RibbonVerticesPerNode = 5;

        private sealed class MeshBuffers
        {
            public readonly Vector3[] Vertices;
            public readonly Vector2[] Uvs;
            public readonly Color32[] Colors;
            public readonly int[] Triangles;

            public MeshBuffers(int vertexCount, int indexCount)
            {
                Vertices = new Vector3[vertexCount];
                Uvs = new Vector2[vertexCount];
                Colors = new Color32[vertexCount];
                Triangles = new int[indexCount];
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = IndexFormat.UInt16,
                };
                mesh.vertices = Vertices;
                mesh.uv = Uvs;
                mesh.colors32 = Colors;
                mesh.triangles = Triangles;
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private readonly struct PoolRingSpec
        {
            public PoolRingSpec(int startIdx, float radiusCm, float shore, byte alpha)
            {
                StartIdx = startIdx;
                RadiusCm = radiusCm;
                Shore = shore;
                Alpha = alpha;
            }

            public int StartIdx { get; }
            public float RadiusCm { get; }
            public float Shore { get; }
            public byte Alpha { get; }
        }

        public static WaterSceneObjects Build(ValleyWorld world, WaterMaterials materials, Transform parent)
        {
            return new WaterSceneObjects
            {
                Stream = CreateMeshObject(
                    "Stream",
                    BuildRibbonMesh("StreamMesh", world.Stream),
                    materials.Water,
                    DrawOrder.Water,
                    parent),
                Brook = CreateMeshObject(
                    "Brook",
                    BuildRibbonMesh("BrookMesh", world.Brook),
                    materials.Water,
                    DrawOrder.Water,
                    parent),
                PlungePool = CreateMeshObject(
                    "PlungePool",
                    BuildPoolMesh("PlungePoolMesh", world.PlungePool),
                    materials.Water,
                    DrawOrder.Water,
                    parent),
                SpringPool = CreateMeshObject(
                    "SpringPool",
                    BuildPoolMesh("SpringPoolMesh", world.SpringPool),
                    materials.Water,
                    DrawOrder.Water,
                    parent),
                Waterfall = CreateMeshObject(
                    "Waterfall",
                    BuildWaterfallMesh("WaterfallMesh", world.Waterfall),
                    materials.Waterfall,
                    DrawOrder.Waterfall,
                    parent),
                WaterFoam = CreateMeshObject(
                    "WaterFoam",
                    BuildFoamMesh("WaterFoamMesh", world.PlungePool),
                    materials.Foam,
                    DrawOrder.WaterFoam,
                    parent),
            };
        }

        private static Mesh BuildRibbonMesh(string name, WaterNode[] nodes)
        {
            int nodeCount = nodes.Length;
            int quadRows = Math.Max(0, nodeCount - 1);
            var buffers = new MeshBuffers(
                nodeCount * RibbonVerticesPerNode,
                quadRows * (RibbonVerticesPerNode - 1) * 6);

            for (int i = 0; i < nodeCount; i++)
            {
                Vector2 perp = ComputeRibbonPerp(nodes, i);
                AddRibbonRow(nodes[i], i * RibbonVerticesPerNode, perp, buffers);
            }

            FillRibbonTriangles(quadRows, buffers.Triangles);
            return buffers.ToMesh(name);
        }

        private static Vector2 ComputeRibbonPerp(WaterNode[] nodes, int index)
        {
            int prev = index > 0 ? index - 1 : 0;
            int next = index < nodes.Length - 1 ? index + 1 : nodes.Length - 1;
            float dx = nodes[next].XCm - nodes[prev].XCm;
            float dz = nodes[next].ZCm - nodes[prev].ZCm;
            float len = Mathf.Sqrt(dx * dx + dz * dz);

            if (len < 1e-4f)
            {
                return new Vector2(1f, 0f);
            }

            float tx = dx / len;
            float tz = dz / len;
            return new Vector2(-tz, tx);
        }

        private static void AddRibbonRow(WaterNode node, int baseIdx, Vector2 perp, MeshBuffers buffers)
        {
            float hw = node.HalfWidthCm;
            float[] offsets = { -(hw + 120f), -(hw + 50f), 0f, hw + 50f, hw + 120f };
            byte[] alphas = { 0, 255, 255, 255, 0 };
            float[] shores = { 1f, 1f, 0f, 1f, 1f };

            for (int k = 0; k < RibbonVerticesPerNode; k++)
            {
                float off = offsets[k];
                float xCm = node.XCm + perp.x * off;
                int zCm = off == 0f ? node.ZCm : Mathf.RoundToInt(node.ZCm + perp.y * off);
                float v = Projection.PlaneVCm(zCm, node.LevelCm) / 100f;

                int idx = baseIdx + k;
                buffers.Vertices[idx] = new Vector3(xCm / 100f, v, 0f);
                buffers.Uvs[idx] = new Vector2(shores[k], 0f);
                buffers.Colors[idx] = new Color32(255, 255, 255, alphas[k]);
            }
        }

        private static void FillRibbonTriangles(int quadRows, int[] triangles)
        {
            int cursor = 0;
            for (int i = 0; i < quadRows; i++)
            {
                int rowA = i * RibbonVerticesPerNode;
                int rowB = (i + 1) * RibbonVerticesPerNode;
                for (int k = 0; k < RibbonVerticesPerNode - 1; k++)
                {
                    int a0 = rowA + k;
                    int b0 = rowB + k;
                    cursor = WriteQuad(triangles, cursor, a0, a0 + 1, b0 + 1, b0);
                }
            }
        }

        private static Mesh BuildPoolMesh(string name, WaterPool pool)
        {
            var buffers = new MeshBuffers(1 + 3 * PoolSegments, PoolSegments * 15);
            buffers.Vertices[0] = new Vector3(
                pool.XCm / 100f,
                Projection.PlaneVCm(pool.ZCm, pool.LevelCm) / 100f,
                0f);
            buffers.Uvs[0] = Vector2.zero;
            buffers.Colors[0] = new Color32(255, 255, 255, 255);

            FillPoolRing(pool, new PoolRingSpec(1, 0.6f * (pool.RadiusCm + 50f), 0f, 255), buffers);
            FillPoolRing(pool, new PoolRingSpec(1 + PoolSegments, pool.RadiusCm + 50f, 1f, 255), buffers);
            FillPoolRing(pool, new PoolRingSpec(1 + 2 * PoolSegments, pool.RadiusCm + 120f, 1f, 0), buffers);
            FillPoolTriangles(buffers.Triangles);
            return buffers.ToMesh(name);
        }

        private static void FillPoolRing(WaterPool pool, PoolRingSpec spec, MeshBuffers buffers)
        {
            float step = 2f * Mathf.PI / PoolSegments;
            var color = new Color32(255, 255, 255, spec.Alpha);

            for (int s = 0; s < PoolSegments; s++)
            {
                float angle = s * step;
                float xCm = pool.XCm + Mathf.Cos(angle) * spec.RadiusCm;
                int zCm = Mathf.RoundToInt(pool.ZCm - Mathf.Sin(angle) * spec.RadiusCm);
                float v = Projection.PlaneVCm(zCm, pool.LevelCm) / 100f;

                int idx = spec.StartIdx + s;
                buffers.Vertices[idx] = new Vector3(xCm / 100f, v, 0f);
                buffers.Uvs[idx] = new Vector2(spec.Shore, 0f);
                buffers.Colors[idx] = color;
            }
        }

        private static void FillPoolTriangles(int[] triangles)
        {
            int cursor = 0;
            for (int s = 0; s < PoolSegments; s++)
            {
                int next = (s + 1) % PoolSegments;
                int r1 = 1 + s;
                int r1Next = 1 + next;
                int r2 = 1 + PoolSegments + s;
                int r2Next = 1 + PoolSegments + next;
                int r3 = 1 + 2 * PoolSegments + s;
                int r3Next = 1 + 2 * PoolSegments + next;

                triangles[cursor++] = 0;
                triangles[cursor++] = r1;
                triangles[cursor++] = r1Next;
                cursor = WriteQuad(triangles, cursor, r1, r2, r2Next, r1Next);
                cursor = WriteQuad(triangles, cursor, r2, r3, r3Next, r2Next);
            }
        }

        private static int WriteQuad(int[] triangles, int cursor, int a, int b, int c, int d)
        {
            triangles[cursor++] = a;
            triangles[cursor++] = b;
            triangles[cursor++] = c;
            triangles[cursor++] = a;
            triangles[cursor++] = c;
            triangles[cursor++] = d;
            return cursor;
        }

        private static Mesh BuildWaterfallMesh(string name, FallNode[] nodes)
        {
            FallNode top = nodes[0];
            FallNode bottom = nodes[nodes.Length - 1];
            float centerU = top.XCm / 100f;
            float halfW = ValleyWorld.WaterfallWidthCm / 200f;
            float topV = Projection.PlaneVCm(top.ZCm, top.YCm) / 100f;
            float bottomV = Projection.PlaneVCm(bottom.ZCm, bottom.YCm) / 100f;
            return BuildQuadMesh(name, new Rect(centerU - halfW, bottomV, halfW * 2f, topV - bottomV));
        }

        private static Mesh BuildFoamMesh(string name, WaterPool pool)
        {
            float cu = pool.XCm / 100f;
            float cv = Projection.PlaneVCm(pool.ZCm, pool.LevelCm) / 100f;
            float width = 2.4f * pool.RadiusCm / 100f;
            float height = 1.44f * pool.RadiusCm / 100f;
            return BuildQuadMesh(name, new Rect(cu - width * 0.5f, cv - height * 0.5f, width, height));
        }

        private static Mesh BuildQuadMesh(string name, Rect bounds)
        {
            var buffers = new MeshBuffers(4, 6);
            buffers.Vertices[0] = new Vector3(bounds.xMin, bounds.yMin, 0f);
            buffers.Vertices[1] = new Vector3(bounds.xMin, bounds.yMax, 0f);
            buffers.Vertices[2] = new Vector3(bounds.xMax, bounds.yMax, 0f);
            buffers.Vertices[3] = new Vector3(bounds.xMax, bounds.yMin, 0f);
            buffers.Uvs[0] = new Vector2(0f, 0f);
            buffers.Uvs[1] = new Vector2(0f, 1f);
            buffers.Uvs[2] = new Vector2(1f, 1f);
            buffers.Uvs[3] = new Vector2(1f, 0f);
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < 4; i++)
            {
                buffers.Colors[i] = white;
            }

            WriteQuad(buffers.Triangles, 0, 0, 1, 2, 3);
            return buffers.ToMesh(name);
        }

        private static MeshRenderer CreateMeshObject(
            string name,
            Mesh mesh,
            Material material,
            int sortingOrder,
            Transform parent)
        {
            var go = new GameObject(name);
            go.layer = DrawOrder.WorldLayer;
            go.transform.SetParent(parent, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
    }
}
