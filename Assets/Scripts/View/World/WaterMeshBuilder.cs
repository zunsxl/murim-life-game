using System.Collections.Generic;
using Murim.Core.Valley;
using UnityEngine;

namespace Murim.View.World
{
    public readonly struct WaterBuildResult
    {
        public readonly Mesh WaterMesh;
        public readonly Mesh WaterfallMesh;
        public readonly Mesh RiverRocksMesh;
        public readonly Mesh FallFoamMesh;

        public WaterBuildResult(Mesh waterMesh, Mesh waterfallMesh, Mesh riverRocksMesh, Mesh fallFoamMesh)
        {
            WaterMesh = waterMesh;
            WaterfallMesh = waterfallMesh;
            RiverRocksMesh = riverRocksMesh;
            FallFoamMesh = fallFoamMesh;
        }
    }

    /// <summary>
    /// Builds water surface quads, waterfall cascade ribbon quads, and RiverRocks/FallFoam decal quads per §3.7.
    /// </summary>
    public static class WaterMeshBuilder
    {
        public const float WaterHeightOffset = 0.02f;
        public const float DecalHeightOffset = 0.03f;
        public const float WaterfallNormalOffset = 0.05f;
        public const float RiverRocksDecalSize = 2.2f;
        public const float FallFoamDecalSize = 1.8f;

        private sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv0 = new List<Vector2>();
            public readonly List<Vector4> Uv1 = new List<Vector4>();
            public readonly List<Vector2> Uv2 = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();

            public Mesh ToMesh(string meshName, bool includeExtras)
            {
                Mesh mesh = new Mesh { name = meshName };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uv0);
                if (includeExtras && Uv1.Count > 0)
                {
                    mesh.SetUVs(1, Uv1);
                }
                if (includeExtras && Uv2.Count > 0)
                {
                    mesh.SetUVs(2, Uv2);
                }
                if (includeExtras && Colors.Count > 0)
                {
                    mesh.SetColors(Colors);
                }
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static WaterBuildResult Build(ValleyLayout layout)
        {
            Mesh waterMesh = BuildWaterMesh(layout);
            Mesh waterfallMesh = BuildWaterfallMesh(layout);
            BuildDecalMeshes(layout, out Mesh rocksMesh, out Mesh foamMesh);
            return new WaterBuildResult(waterMesh, waterfallMesh, rocksMesh, foamMesh);
        }

        private static Mesh BuildWaterMesh(ValleyLayout layout)
        {
            MeshData data = new MeshData();
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    CellKind kind = layout.KindAt(cx, cz);
                    if (!ValleyKindMetadata.IsWaterKind(kind))
                    {
                        continue;
                    }
                    AppendWaterCell(layout, data, cx, cz, kind == CellKind.RiverRiffle);
                }
            }
            return data.ToMesh("ValleyWater", true);
        }

        private static void AppendWaterCell(ValleyLayout layout, MeshData data, int cx, int cz, bool isRiffle)
        {
            int vBase = data.Vertices.Count;
            float h00 = layout.VertexHeight(cx, cz) + WaterHeightOffset;
            float h10 = layout.VertexHeight(cx + 1, cz) + WaterHeightOffset;
            float h01 = layout.VertexHeight(cx, cz + 1) + WaterHeightOffset;
            float h11 = layout.VertexHeight(cx + 1, cz + 1) + WaterHeightOffset;

            data.Vertices.Add(new Vector3(cx, h00, cz));
            data.Vertices.Add(new Vector3(cx + 1f, h10, cz));
            data.Vertices.Add(new Vector3(cx + 1f, h11, cz + 1f));
            data.Vertices.Add(new Vector3(cx, h01, cz + 1f));

            Vector4 cardinalShore = new Vector4(
                IsLandShore(layout, cx - 1, cz),
                IsLandShore(layout, cx + 1, cz),
                IsLandShore(layout, cx, cz - 1),
                IsLandShore(layout, cx, cz + 1));

            Color diagShore = new Color(
                IsLandShore(layout, cx - 1, cz - 1),
                IsLandShore(layout, cx + 1, cz - 1),
                IsLandShore(layout, cx - 1, cz + 1),
                IsLandShore(layout, cx + 1, cz + 1));

            Vector2 riffle = new Vector2(isRiffle ? 1f : 0f, 0f);

            for (int i = 0; i < 4; i++)
            {
                data.Normals.Add(Vector3.up);
                data.Uv1.Add(cardinalShore);
                data.Uv2.Add(riffle);
                data.Colors.Add(diagShore);
            }

            data.Uv0.Add(new Vector2(0f, 0f));
            data.Uv0.Add(new Vector2(1f, 0f));
            data.Uv0.Add(new Vector2(1f, 1f));
            data.Uv0.Add(new Vector2(0f, 1f));

            AppendUpwardQuadTriangles(data.Triangles, vBase);
        }

        private static Mesh BuildWaterfallMesh(ValleyLayout layout)
        {
            MeshData data = new MeshData();
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                int cx = 0;
                while (cx < layout.Width)
                {
                    if (layout.KindAt(cx, cz) != CellKind.Waterfall)
                    {
                        cx++;
                        continue;
                    }

                    int runStart = cx;
                    while (cx < layout.Width && layout.KindAt(cx, cz) == CellKind.Waterfall)
                    {
                        cx++;
                    }
                    int runWidth = cx - runStart;
                    for (int x = runStart; x < cx; x++)
                    {
                        float u0 = (float)(x - runStart) / runWidth;
                        float u1 = (float)(x + 1 - runStart) / runWidth;
                        AppendWaterfallCell(layout, data, x, cz, u0, u1);
                    }
                }
            }
            return data.ToMesh("ValleyWaterfall", false);
        }

        private static void AppendWaterfallCell(
            ValleyLayout layout,
            MeshData data,
            int cx,
            int cz,
            float u0,
            float u1)
        {
            Vector3 p00 = new Vector3(cx, layout.VertexHeight(cx, cz), cz);
            Vector3 p10 = new Vector3(cx + 1f, layout.VertexHeight(cx + 1, cz), cz);
            Vector3 p01 = new Vector3(cx, layout.VertexHeight(cx, cz + 1), cz + 1f);
            Vector3 p11 = new Vector3(cx + 1f, layout.VertexHeight(cx + 1, cz + 1), cz + 1f);

            Vector3 normal = Vector3.Cross(p01 - p00, p10 - p00).normalized;
            Vector3 offset = normal * WaterfallNormalOffset;

            int vBase = data.Vertices.Count;
            data.Vertices.Add(p00 + offset);
            data.Vertices.Add(p10 + offset);
            data.Vertices.Add(p11 + offset);
            data.Vertices.Add(p01 + offset);

            for (int i = 0; i < 4; i++)
            {
                data.Normals.Add(normal);
            }

            data.Uv0.Add(new Vector2(u0, 0f));
            data.Uv0.Add(new Vector2(u1, 0f));
            data.Uv0.Add(new Vector2(u1, 1f));
            data.Uv0.Add(new Vector2(u0, 1f));

            AppendUpwardQuadTriangles(data.Triangles, vBase);
        }

        private static void BuildDecalMeshes(ValleyLayout layout, out Mesh rocksMesh, out Mesh foamMesh)
        {
            MeshData rocks = new MeshData();
            MeshData foam = new MeshData();
            IReadOnlyList<PropPlacement> props = layout.Props;

            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement prop = props[i];
                if (prop.Kind == PropKind.RiverRocks)
                {
                    AppendDecalQuad(layout, rocks, prop, RiverRocksDecalSize, false);
                }
                else if (prop.Kind == PropKind.FallFoam)
                {
                    AppendDecalQuad(layout, foam, prop, FallFoamDecalSize, true);
                }
            }

            rocksMesh = rocks.ToMesh("ValleyRiverRocks", true);
            foamMesh = foam.ToMesh("ValleyFallFoam", true);
        }

        private static void AppendDecalQuad(
            ValleyLayout layout,
            MeshData data,
            PropPlacement prop,
            float size,
            bool isFoam)
        {
            float cx = prop.Cx + 0.5f;
            float cz = prop.Cz + 0.5f;
            float y = layout.HeightAt(cx, cz) + DecalHeightOffset;
            float half = size * 0.5f;

            int vBase = data.Vertices.Count;
            data.Vertices.Add(new Vector3(cx - half, y, cz - half));
            data.Vertices.Add(new Vector3(cx + half, y, cz - half));
            data.Vertices.Add(new Vector3(cx + half, y, cz + half));
            data.Vertices.Add(new Vector3(cx - half, y, cz + half));

            float u0 = prop.FlipX ? 1f : 0f;
            float u1 = prop.FlipX ? 0f : 1f;
            data.Uv0.Add(new Vector2(u0, 0f));
            data.Uv0.Add(new Vector2(u1, 0f));
            data.Uv0.Add(new Vector2(u1, 1f));
            data.Uv0.Add(new Vector2(u0, 1f));

            Vector4 meta = new Vector4(cx, cz, isFoam ? 1f : 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                data.Normals.Add(Vector3.up);
                data.Uv1.Add(meta);
            }

            AppendUpwardQuadTriangles(data.Triangles, vBase);
        }

        private static void AppendUpwardQuadTriangles(List<int> triangles, int vBase)
        {
            triangles.Add(vBase + 0);
            triangles.Add(vBase + 2);
            triangles.Add(vBase + 1);
            triangles.Add(vBase + 0);
            triangles.Add(vBase + 3);
            triangles.Add(vBase + 2);
        }

        private static float IsLandShore(ValleyLayout layout, int cx, int cz)
        {
            if (!layout.InCellBounds(cx, cz))
            {
                return 0f;
            }
            CellKind kind = layout.KindAt(cx, cz);
            return ValleyKindMetadata.IsWaterKind(kind) || kind == CellKind.Waterfall ? 0f : 1f;
        }
    }
}
