using Murim.Core.Valley;
using UnityEngine;

namespace Murim.View.World
{
    public readonly struct TerrainBuildResult
    {
        public readonly Mesh TerrainMesh;
        public readonly Mesh CapMesh;
        public readonly Texture2D HeightTexture;
        public readonly Texture2D SurfaceTexture0;
        public readonly Texture2D SurfaceTexture1;

        public TerrainBuildResult(
            Mesh terrainMesh,
            Mesh capMesh,
            Texture2D heightTexture,
            Texture2D surfaceTexture0,
            Texture2D surfaceTexture1)
        {
            TerrainMesh = terrainMesh;
            CapMesh = capMesh;
            HeightTexture = heightTexture;
            SurfaceTexture0 = surfaceTexture0;
            SurfaceTexture1 = surfaceTexture1;
        }
    }

    /// <summary>
    /// Builds the valley terrain mesh, cutaway cap mesh, and shared height/surface-weight textures per §3.4 and §3.7.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        private static readonly int HeightTexId = Shader.PropertyToID("_MurimHeightTex");
        private static readonly int SurfaceTex0Id = Shader.PropertyToID("_MurimSurfaceTex0");
        private static readonly int SurfaceTex1Id = Shader.PropertyToID("_MurimSurfaceTex1");
        private static readonly int GridSizeId = Shader.PropertyToID("_MurimGridSize");

        public static TerrainBuildResult Build(ValleyLayout layout)
        {
            Mesh terrainMesh = BuildTerrainMesh(layout);
            Mesh capMesh = BuildCapMesh(layout);
            Texture2D heightTex = BuildHeightTexture(layout);
            BuildSurfaceTextures(layout, out Texture2D surfaceTex0, out Texture2D surfaceTex1);

            Shader.SetGlobalTexture(HeightTexId, heightTex);
            Shader.SetGlobalTexture(SurfaceTex0Id, surfaceTex0);
            Shader.SetGlobalTexture(SurfaceTex1Id, surfaceTex1);
            Shader.SetGlobalVector(GridSizeId, new Vector4(layout.Width, layout.Depth, layout.VertexWidth, layout.VertexDepth));

            return new TerrainBuildResult(terrainMesh, capMesh, heightTex, surfaceTex0, surfaceTex1);
        }

        private static Mesh BuildTerrainMesh(ValleyLayout layout)
        {
            int width = layout.Width;
            int depth = layout.Depth;
            int vw = layout.VertexWidth;
            int vd = layout.VertexDepth;

            Vector3[] vertices = new Vector3[vw * vd];
            Vector2[] uvs = new Vector2[vw * vd];
            for (int vz = 0; vz < vd; vz++)
            {
                int rowStart = vz * vw;
                float v = (float)vz / depth;
                for (int vx = 0; vx < vw; vx++)
                {
                    float h = layout.VertexHeight(vx, vz);
                    vertices[rowStart + vx] = new Vector3(vx, h, vz);
                    uvs[rowStart + vx] = new Vector2((float)vx / width, v);
                }
            }

            int[] triangles = new int[width * depth * 6];
            int ti = 0;
            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    int v00 = cz * vw + cx;
                    int v10 = v00 + 1;
                    int v01 = v00 + vw;
                    int v11 = v01 + 1;

                    // Diagonal (cx, cz)-(cx+1, cz+1) matching Core ValleyLayout.HeightAt
                    triangles[ti++] = v00;
                    triangles[ti++] = v11;
                    triangles[ti++] = v10;

                    triangles[ti++] = v00;
                    triangles[ti++] = v01;
                    triangles[ti++] = v11;
                }
            }

            Mesh mesh = new Mesh { name = "ValleyTerrain" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildCapMesh(ValleyLayout layout)
        {
            float maxX = layout.Width;
            float maxZ = Mathf.Min(layout.Depth, ValleyConstants.NorthUncutZ);

            // Quad 0 tracks _MurimCutY (uv2.x = 0); Quad 1 tracks _MurimCutYFrom during fade (uv2.x = 1).
            Vector3[] vertices =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(maxX, 0f, 0f),
                new Vector3(maxX, 0f, maxZ),
                new Vector3(0f, 0f, maxZ),
                new Vector3(0f, 0f, 0f),
                new Vector3(maxX, 0f, 0f),
                new Vector3(maxX, 0f, maxZ),
                new Vector3(0f, 0f, maxZ)
            };

            Vector3[] normals =
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };

            Vector2[] uvs =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };

            Vector2[] uv2 =
            {
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f)
            };

            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5, 4, 7, 6
            };

            Mesh mesh = new Mesh { name = "ValleyCutawayCap" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.uv2 = uv2;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(
                new Vector3(maxX * 0.5f, ValleyConstants.MaxAuthoredHeight * 0.5f, maxZ * 0.5f),
                new Vector3(maxX, ValleyConstants.MaxAuthoredHeight + 4f, maxZ));
            return mesh;
        }

        private static Texture2D BuildHeightTexture(ValleyLayout layout)
        {
            int vw = layout.VertexWidth;
            int vd = layout.VertexDepth;

            Texture2D tex = new Texture2D(vw, vd, TextureFormat.RFloat, false, true)
            {
                name = "_MurimHeightTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            float[] data = new float[vw * vd];
            for (int vz = 0; vz < vd; vz++)
            {
                int row = vz * vw;
                for (int vx = 0; vx < vw; vx++)
                {
                    data[row + vx] = layout.VertexHeight(vx, vz);
                }
            }

            tex.SetPixelData(data, 0);
            tex.Apply(false, false);
            return tex;
        }

        private static void BuildSurfaceTextures(
            ValleyLayout layout,
            out Texture2D surfaceTex0,
            out Texture2D surfaceTex1)
        {
            int width = layout.Width;
            int depth = layout.Depth;

            surfaceTex0 = new Texture2D(width, depth, TextureFormat.RGBA32, false, true)
            {
                name = "_MurimSurfaceTex0",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            surfaceTex1 = new Texture2D(width, depth, TextureFormat.RGBA32, false, true)
            {
                name = "_MurimSurfaceTex1",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels0 = new Color32[width * depth];
            Color32[] pixels1 = new Color32[width * depth];

            for (int cz = 0; cz < depth; cz++)
            {
                int row = cz * width;
                for (int cx = 0; cx < width; cx++)
                {
                    EncodeCellWeights(layout.KindAt(cx, cz), out pixels0[row + cx], out pixels1[row + cx]);
                }
            }

            surfaceTex0.SetPixels32(pixels0);
            surfaceTex0.Apply(false, false);
            surfaceTex1.SetPixels32(pixels1);
            surfaceTex1.Apply(false, false);
        }

        private static void EncodeCellWeights(CellKind kind, out Color32 w0, out Color32 w1)
        {
            w0 = default;
            w1 = default;

            switch (kind)
            {
                case CellKind.Meadow:
                case CellKind.Bamboo:
                    w0.r = 255;
                    break;
                case CellKind.Earth:
                    w0.g = 255;
                    break;
                case CellKind.Granite:
                    w0.b = 255;
                    break;
                case CellKind.Damp:
                case CellKind.RiverChannel:
                case CellKind.RiverRiffle:
                    w0.a = 255;
                    break;
                case CellKind.EarthenSlope:
                    w1.r = 255;
                    break;
                case CellKind.RockySlope:
                    w1.g = 255;
                    break;
                case CellKind.WetRock:
                case CellKind.Waterfall:
                    w1.b = 255;
                    break;
                case CellKind.Cliff:
                    w1.a = 255;
                    break;
            }
        }
    }
}
