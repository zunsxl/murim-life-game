using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Murim.Core.Valley;
using Murim.View;
using Murim.View.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.Engine.World
{
    public sealed class WorldRenderingTests
    {
        private ValleyLayout _layout;
        private Scene _previousScene;
        private Scene _testScene;
        private bool _hasTestScene;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _layout = ValleyLayoutData.CreateLayout();
        }

        [SetUp]
        public void SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene($"WorldTest_{TestContext.CurrentContext.Test.Name}");
            SceneManager.SetActiveScene(_testScene);
            _hasTestScene = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hasTestScene && _testScene.IsValid())
            {
                GameObject[] roots = _testScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Object.DestroyImmediate(roots[i]);
                }

                if (_previousScene.IsValid() && _previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(_previousScene);
                }

                AsyncOperation unload = SceneManager.UnloadSceneAsync(_testScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
                _hasTestScene = false;
            }
        }

        // T-ENG-01: Terrain mesh vertex/triangle count, Core heights, bounds, and shared GPU textures.
        [Test]
        public void T_ENG_01_TerrainMeshMatchesCoreLayoutAndBounds()
        {
            TerrainBuildResult result = TerrainMeshBuilder.Build(_layout);
            try
            {
                Mesh mesh = result.TerrainMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(15617));
                Assert.That(mesh.triangles.Length, Is.EqualTo(30720 * 3));

                Vector3[] vertices = mesh.vertices;
                for (int vz = 0; vz < _layout.VertexDepth; vz++)
                {
                    for (int vx = 0; vx < _layout.VertexWidth; vx++)
                    {
                        int idx = vz * _layout.VertexWidth + vx;
                        Assert.That(vertices[idx].x, Is.EqualTo(vx).Within(1e-5f));
                        Assert.That(vertices[idx].y, Is.EqualTo(_layout.VertexHeight(vx, vz)).Within(1e-5f));
                        Assert.That(vertices[idx].z, Is.EqualTo(vz).Within(1e-5f));
                    }
                }

                Assert.That(mesh.bounds.min.x, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(mesh.bounds.min.y, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(mesh.bounds.min.z, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(mesh.bounds.max.x, Is.EqualTo(160f).Within(1e-4f));
                Assert.That(mesh.bounds.max.y, Is.EqualTo(13f).Within(1e-4f));
                Assert.That(mesh.bounds.max.z, Is.EqualTo(96f).Within(1e-4f));

                Assert.That(result.HeightTexture.width, Is.EqualTo(161));
                Assert.That(result.HeightTexture.height, Is.EqualTo(97));
                Assert.That(result.HeightTexture.format, Is.EqualTo(TextureFormat.RFloat));
                Assert.That(result.HeightTexture.filterMode, Is.EqualTo(FilterMode.Point));

                Assert.That(result.SurfaceTexture0.width, Is.EqualTo(160));
                Assert.That(result.SurfaceTexture0.height, Is.EqualTo(96));
                Assert.That(result.SurfaceTexture0.format, Is.EqualTo(TextureFormat.RGBA32));
                Assert.That(result.SurfaceTexture0.filterMode, Is.EqualTo(FilterMode.Bilinear));

                Assert.That(result.SurfaceTexture1.width, Is.EqualTo(160));
                Assert.That(result.SurfaceTexture1.height, Is.EqualTo(96));
                Assert.That(result.SurfaceTexture1.format, Is.EqualTo(TextureFormat.RGBA32));
                Assert.That(result.SurfaceTexture1.filterMode, Is.EqualTo(FilterMode.Bilinear));
            }
            finally
            {
                Object.DestroyImmediate(result.TerrainMesh);
                Object.DestroyImmediate(result.CapMesh);
                Object.DestroyImmediate(result.HeightTexture);
                Object.DestroyImmediate(result.SurfaceTexture0);
                Object.DestroyImmediate(result.SurfaceTexture1);
            }
        }

        // T-ENG-02: Stand-in render at Full View and Floor 1 verifies Floor 3 surface->cap, cliff-corner non-clear, and unchanged Floor 1.
        [UnityTest]
        public IEnumerator T_ENG_02_CutawayRendersFloor3CapCliffAndUnchangedFloor1WithStandInArt()
        {
            WorldView view = WorldView.Build(_layout, PresentationTuning.Default, forceStandInArt: true);
            Assert.That(view.Catalog.AnyStandInInUse, Is.True);
            Assert.That(view.Catalog.StandInCount, Is.EqualTo(view.Catalog.TotalAssetCount));

            Camera cam = CreateFixedGameCamera(new Vector2(60f, 48f), 22f, Color.magenta);
            RenderTexture rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            Vector2Int floor1Cell = FindOpenCell(66, 78, 24, 34, CellKind.Meadow, 0f, 8);
            Vector2Int floor3Cell = FindOpenCell(45, 75, 64, 72, CellKind.Meadow, 6f, 4);
            Vector2Int cliffCell = FindCliffCell(45, 75, 60, 64);

            Vector3 floor1Pt = new Vector3(floor1Cell.x + 0.5f, 0f, floor1Cell.y + 0.5f);
            Vector3 floor3FullPt = new Vector3(floor3Cell.x + 0.5f, 6f, floor3Cell.y + 0.5f);
            Vector3 floor3CapPt = new Vector3(floor3Cell.x + 0.5f, ValleyConstants.CutYForFloor(1), floor3Cell.y + 0.5f);
            Vector3 cliffPt = new Vector3(cliffCell.x + 0.5f, ValleyConstants.CutYForFloor(1), cliffCell.y + 0.5f);

            try
            {
                yield return null;
                Texture2D fullTex = RenderAndRead(cam, rt);

                view.Cutaway.SetCut(ValleyConstants.CutYForFloor(1));
                yield return AdvanceCutFadeToComplete(view.Cutaway);
                Texture2D floor1Tex = RenderAndRead(cam, rt);

                Color f3Full = SampleWorldPixel(cam, fullTex, floor3FullPt);
                Color f3Cut = SampleWorldPixel(cam, floor1Tex, floor3CapPt);
                Color cliffFull = SampleWorldPixel(cam, fullTex, cliffPt);
                Color cliffCut = SampleWorldPixel(cam, floor1Tex, cliffPt);
                Color f1Full = SampleWorldPixel(cam, fullTex, floor1Pt);
                Color f1Cut = SampleWorldPixel(cam, floor1Tex, floor1Pt);

                Assert.That(ColorDistance(f3Full, Color.magenta), Is.GreaterThan(0.15f), "Floor 3 full view must not be clear colour.");
                Assert.That(ColorDistance(f3Cut, Color.magenta), Is.GreaterThan(0.15f), "Floor 3 cut view must not be clear colour.");
                Assert.That(ColorDistance(f3Full, f3Cut), Is.GreaterThan(0.08f), "Floor 3 pixel must change from meadow surface to earth cap at floor 1.");

                Assert.That(ColorDistance(cliffFull, Color.magenta), Is.GreaterThan(0.15f), "Cliff-corner full view must not be clear colour.");
                Assert.That(ColorDistance(cliffCut, Color.magenta), Is.GreaterThan(0.15f), "Cliff-corner cut view must show terrain or cap, never clear colour.");

                Assert.That(ColorDistance(f1Full, Color.magenta), Is.GreaterThan(0.15f), "Floor 1 cell must render terrain, not clear colour.");
                Assert.That(ColorDistance(f1Full, f1Cut), Is.LessThan(0.01f), "Unshadowed Floor 1 pixel must remain unchanged at floor 1.");

                Object.DestroyImmediate(fullTex);
                Object.DestroyImmediate(floor1Tex);
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // T-ENG-03: Prop meshes quad count, block count <= 15, and -z facing.
        [Test]
        public void T_ENG_03_PropMeshesMatchExpectedQuadsBlockLimitAndFacing()
        {
            using ArtCatalog catalog = ArtCatalog.Load(true);
            List<Mesh> meshes = PropMeshBuilder.Build(_layout, catalog, PresentationTuning.Default);
            try
            {
                Assert.That(meshes.Count, Is.LessThanOrEqualTo(15));
                Assert.That(meshes.Count, Is.GreaterThan(0));

                int expectedQuads = CountExpectedPropAndCoverQuads(_layout);
                int actualTrisQuads = 0;
                int actualVertQuads = 0;

                for (int i = 0; i < meshes.Count; i++)
                {
                    Mesh mesh = meshes[i];
                    Assert.That(mesh.vertexCount % 4, Is.EqualTo(0));
                    Assert.That(mesh.triangles.Length % 6, Is.EqualTo(0));
                    actualVertQuads += mesh.vertexCount / 4;
                    actualTrisQuads += mesh.triangles.Length / 6;

                    Vector3[] normals = mesh.normals;
                    for (int v = 0; v < normals.Length; v++)
                    {
                        Assert.That(Vector3.Distance(normals[v], Vector3.back), Is.LessThan(1e-5f));
                    }

                    Vector3[] verts = mesh.vertices;
                    int[] tris = mesh.triangles;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 e1 = verts[tris[t + 1]] - verts[tris[t]];
                        Vector3 e2 = verts[tris[t + 2]] - verts[tris[t]];
                        Vector3 faceN = Vector3.Cross(e1, e2).normalized;
                        Assert.That(Vector3.Dot(faceN, Vector3.back), Is.GreaterThan(0.99f));
                    }
                }

                Assert.That(actualVertQuads, Is.EqualTo(expectedQuads));
                Assert.That(actualTrisQuads, Is.EqualTo(expectedQuads));
            }
            finally
            {
                for (int i = 0; i < meshes.Count; i++)
                {
                    Object.DestroyImmediate(meshes[i]);
                }
            }
        }

        // T-ENG-04: AmbientClock advances when true, holds exact value when false, and resumes from held value.
        [Test]
        public void T_ENG_04_AmbientClockAdvancesHoldsAndResumesExact()
        {
            GameObject go = new GameObject("AmbientClockTest");
            AmbientClock clock = go.AddComponent<AmbientClock>();
            clock.ResetClock(0f);

            clock.AmbientRunning = true;
            clock.Step(0.05f);
            clock.Step(0.25f); // Clamped to 0.1f
            float tAfterRun = Shader.GetGlobalFloat("_MurimAmbientTime");
            Assert.That(tAfterRun, Is.EqualTo(0.15f).Within(1e-6f));

            clock.AmbientRunning = false;
            clock.Step(0.08f);
            clock.Step(0.10f);
            float tWhilePaused = Shader.GetGlobalFloat("_MurimAmbientTime");
            Assert.That(tWhilePaused, Is.EqualTo(tAfterRun));

            clock.AmbientRunning = true;
            clock.Step(0.04f);
            float tAfterResume = Shader.GetGlobalFloat("_MurimAmbientTime");
            Assert.That(tAfterResume, Is.EqualTo(0.19f).Within(1e-6f));
        }

        // T-ENG-05: Rapid SetCut sets _MurimCutYFrom to first target, finishes within cutFadeSeconds while panning, unsliced ground pixel matches steady render.
        [UnityTest]
        public IEnumerator T_ENG_05_CutawayRetargetsMidFadeAndUnslicedGroundMatchesSteadyRender()
        {
            PresentationTuning tuning = PresentationTuning.Default;
            WorldView view = WorldView.Build(_layout, tuning, forceStandInArt: true);
            Vector2Int floor1Cell = FindOpenCell(66, 78, 24, 34, CellKind.Meadow, 0f, 8);
            Vector3 floor1Pt = new Vector3(floor1Cell.x + 0.5f, 0f, floor1Cell.y + 0.5f);

            Camera cam = CreateFixedGameCamera(new Vector2(floor1Pt.x, floor1Pt.z), 11.25f, Color.magenta);
            RenderTexture rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            try
            {
                yield return null;

                // Steady reference render at floor 1 (fade complete)
                view.Cutaway.SetCut(ValleyConstants.CutYForFloor(1));
                yield return AdvanceCutFadeToComplete(view.Cutaway);
                Texture2D steadyTex = RenderAndRead(cam, rt);
                Color steadyFloor1 = SampleWorldPixel(cam, steadyTex, floor1Pt);
                Object.DestroyImmediate(steadyTex);

                // Reset to floor 4 then trigger two cuts within cutFadeSeconds while panning camera
                view.Cutaway.SetCut(ValleyConstants.CutYForFloor(4));
                yield return AdvanceCutFadeToComplete(view.Cutaway);

                float firstTarget = ValleyConstants.CutYForFloor(3);
                float secondTarget = ValleyConstants.CutYForFloor(1);
                view.Cutaway.SetCut(firstTarget);
                yield return null;

                view.Cutaway.SetCut(secondTarget);
                Assert.That(view.Cutaway.CutYFrom, Is.EqualTo(firstTarget));
                Assert.That(Shader.GetGlobalFloat("_MurimCutYFrom"), Is.EqualTo(firstTarget).Within(1e-4f));
                Assert.That(Shader.GetGlobalFloat("_MurimCutY"), Is.EqualTo(secondTarget).Within(1e-4f));
                Assert.That(Shader.GetGlobalFloat("_MurimCutFade"), Is.EqualTo(0f).Within(1e-4f));

                bool sampledMidFade = false;
                float clampedElapsed = 0f;
                int guardFrames = 0;
                while (view.Cutaway.CutFade < 1f && guardFrames < 10000)
                {
                    // Pan camera throughout the fade
                    cam.transform.position += new Vector3(0.05f, 0f, 0f);
                    yield return null;
                    guardFrames++;
                    clampedElapsed += Mathf.Min(Time.unscaledDeltaTime, CutawayController.MaxFrameDeltaSeconds);

                    if (!sampledMidFade && view.Cutaway.CutFade > 0f && view.Cutaway.CutFade < 1f)
                    {
                        Texture2D midTex = RenderAndRead(cam, rt);
                        Color midFloor1 = SampleWorldPixel(cam, midTex, floor1Pt);
                        Object.DestroyImmediate(midTex);
                        Assert.That(ColorDistance(midFloor1, steadyFloor1), Is.LessThan(0.01f),
                            "Unsliced Floor 1 ground during fade must match steady non-fade render.");
                        sampledMidFade = true;
                    }
                }

                Assert.That(view.Cutaway.CutFade, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(Shader.GetGlobalFloat("_MurimCutFade"), Is.EqualTo(1f).Within(1e-5f));
                Assert.That(clampedElapsed, Is.LessThanOrEqualTo(tuning.CutFadeSeconds + CutawayController.MaxFrameDeltaSeconds + 1e-4f));
                Assert.That(sampledMidFade, Is.True, "Fade should span at least one intermediate frame so unsliced pixel is sampled mid-fade.");
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // T-ENG-06: Water and waterfall meshes have one quad per w/v cell and per f cell.
        [Test]
        public void T_ENG_06_WaterAndWaterfallMeshesHaveOneQuadPerCell()
        {
            WaterBuildResult water = WaterMeshBuilder.Build(_layout);
            try
            {
                int waterCells = 0;
                int waterfallCells = 0;
                for (int cz = 0; cz < _layout.Depth; cz++)
                {
                    for (int cx = 0; cx < _layout.Width; cx++)
                    {
                        CellKind kind = _layout.KindAt(cx, cz);
                        if (ValleyKindMetadata.IsWaterKind(kind))
                        {
                            waterCells++;
                        }
                        else if (kind == CellKind.Waterfall)
                        {
                            waterfallCells++;
                        }
                    }
                }

                Assert.That(water.WaterMesh.vertexCount, Is.EqualTo(waterCells * 4));
                Assert.That(water.WaterMesh.triangles.Length, Is.EqualTo(waterCells * 6));
                Assert.That(water.WaterfallMesh.vertexCount, Is.EqualTo(waterfallCells * 4));
                Assert.That(water.WaterfallMesh.triangles.Length, Is.EqualTo(waterfallCells * 6));
            }
            finally
            {
                Object.DestroyImmediate(water.WaterMesh);
                Object.DestroyImmediate(water.WaterfallMesh);
                Object.DestroyImmediate(water.RiverRocksMesh);
                Object.DestroyImmediate(water.FallFoamMesh);
            }
        }

        // T-ENG-07: WorldView.Build in empty scene sets §3.7 light & flat ambient, zero Volume, identical across 600 frames.
        [UnityTest]
        public IEnumerator T_ENG_07_WorldViewLightingAndAmbientStayConstantFor600Frames()
        {
            WorldView view = WorldView.Build(_layout, PresentationTuning.Default);

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Assert.That(lights.Length, Is.EqualTo(1));
            Assert.That(lights[0], Is.EqualTo(view.DirectionalLight));

            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            int volumeCount = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().FullName == "UnityEngine.Rendering.Volume")
                {
                    volumeCount++;
                }
            }
            Assert.That(volumeCount, Is.EqualTo(0));

            AssertLightMatchesSpec(view.DirectionalLight);
            Assert.That(RenderSettings.fog, Is.False);

            for (int frame = 0; frame < 600; frame++)
            {
                yield return null;
                AssertLightMatchesSpec(view.DirectionalLight);
            }
        }

        [Test]
        public void WorldView_ArtCatalogPacksAtlasAndPresentationTuningSanitizesRanges()
        {
            using ArtCatalog catalog = ArtCatalog.Load(false);
            Assert.That(catalog.AnyStandInInUse, Is.False);
            Assert.That(catalog.StandInCount, Is.EqualTo(0));
            Assert.That(catalog.TotalAssetCount, Is.EqualTo(45));
            Assert.That(catalog.PropAtlas, Is.Not.Null);
            Assert.That(catalog.PropAtlas.width, Is.LessThanOrEqualTo(ArtCatalog.MaxAtlasSize));
            Assert.That(catalog.PropAtlas.height, Is.LessThanOrEqualTo(ArtCatalog.MaxAtlasSize));
            Assert.That(catalog.WorldSpriteCount, Is.EqualTo(19));

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Murim\] TestWarn: file\.png \(cutFadeSeconds\)$"));
            GameLog.LogWarning("TestWarn", @"C:\Users\<user>\file.png", "cutFadeSeconds");

            List<string> warnings = new List<string>();
            PresentationTuningData badData = new PresentationTuningData
            {
                schemaVersion = 1,
                cutFadeSeconds = 0f,
                riverFlowSpeed = float.NaN,
                bambooSwayPeriod = 3.5f
            };
            PresentationTuning sanitized = PresentationTuning.Sanitize(badData, warnings);
            Assert.That(sanitized.CutFadeSeconds, Is.EqualTo(PresentationTuning.DefaultCutFadeSeconds));
            Assert.That(sanitized.RiverFlowSpeed, Is.EqualTo(PresentationTuning.DefaultRiverFlowSpeed));
            Assert.That(sanitized.BambooSwayPeriod, Is.EqualTo(3.5f));
            Assert.That(warnings.Count, Is.GreaterThanOrEqualTo(2));
        }

        private static void AssertLightMatchesSpec(Light light)
        {
            Assert.That(light.type, Is.EqualTo(LightType.Directional));
            Quaternion expectedRot = Quaternion.Euler(50f, 35f, 0f);
            Assert.That(Quaternion.Angle(light.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            Color expectedLightColor = new Color(1f, 248f / 255f, 238f / 255f, 1f);
            Assert.That(ColorDistance(light.color, expectedLightColor), Is.LessThan(1e-4f));
            Assert.That(light.intensity, Is.EqualTo(1.1f).Within(1e-5f));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(light.shadowStrength, Is.EqualTo(0.6f).Within(1e-5f));

            Color expectedAmbient = new Color(0x8E / 255f, 0x9A / 255f, 0xA6 / 255f, 1f);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
            Assert.That(ColorDistance(RenderSettings.ambientLight, expectedAmbient), Is.LessThan(1e-4f));
        }

        private static Camera CreateFixedGameCamera(Vector2 focusXZ, float orthoSize, Color clearColor)
        {
            GameObject camGo = new GameObject("TestCamera");
            camGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            camGo.transform.position = new Vector3(focusXZ.x, 84.85f, focusXZ.y - 84.85f);

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clearColor;
            cam.cullingMask = 1 << Layers.WorldLayer;
            return cam;
        }

        private static IEnumerator AdvanceCutFadeToComplete(CutawayController cutaway)
        {
            int guard = 0;
            while (cutaway.CutFade < 1f && guard < 10000)
            {
                yield return null;
                guard++;
            }
            Assert.That(cutaway.CutFade, Is.EqualTo(1f).Within(1e-5f));
        }

        private static Texture2D RenderAndRead(Camera cam, RenderTexture rt)
        {
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            return tex;
        }

        private static Color SampleWorldPixel(Camera cam, Texture2D tex, Vector3 worldPos)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            int px = Mathf.Clamp(Mathf.RoundToInt(screenPos.x), 0, tex.width - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(screenPos.y), 0, tex.height - 1);
            return tex.GetPixel(px, py);
        }

        private Vector2Int FindOpenCell(
            int minX,
            int maxX,
            int minZ,
            int maxZ,
            CellKind requiredKind,
            float requiredHeight,
            int propExclusionRadius)
        {
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (_layout.KindAt(cx, cz) != requiredKind)
                    {
                        continue;
                    }
                    if (_layout.CoverAt(cx, cz) != CoverKind.None)
                    {
                        continue;
                    }
                    if (Mathf.Abs(_layout.HeightAt(cx + 0.5f, cz + 0.5f) - requiredHeight) > 0.01f)
                    {
                        continue;
                    }
                    if (!HasNearbyProp(cx, cz, propExclusionRadius))
                    {
                        return new Vector2Int(cx, cz);
                    }
                }
            }
            Assert.Fail($"No open cell found matching {requiredKind} at {requiredHeight} m.");
            return default;
        }

        private Vector2Int FindCliffCell(int minX, int maxX, int minZ, int maxZ)
        {
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (_layout.KindAt(cx, cz) == CellKind.Cliff && _layout.MaxEdgeDelta(cx, cz) >= 1.5f)
                    {
                        return new Vector2Int(cx, cz);
                    }
                }
            }
            Assert.Fail("No cliff cell found in range.");
            return default;
        }

        private bool HasNearbyProp(int cx, int cz, int shadowRadius)
        {
            IReadOnlyList<PropPlacement> props = _layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                int dx = Mathf.Abs(p.Cx - cx);
                int dz = Mathf.Abs(p.Cz - cz);
                if (dx <= 1 && dz <= 1)
                {
                    return true;
                }
                if ((ValleyKindMetadata.IsPine(p.Kind) || ValleyKindMetadata.IsBoulder(p.Kind))
                    && dx <= shadowRadius && dz <= shadowRadius)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountExpectedPropAndCoverQuads(ValleyLayout layout)
        {
            int count = 0;
            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropKind kind = props[i].Kind;
                if (kind != PropKind.RiverRocks && kind != PropKind.FallFoam)
                {
                    count++;
                }
            }

            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.KindAt(cx, cz) == CellKind.Bamboo)
                    {
                        count++;
                    }
                    CoverKind cover = layout.CoverAt(cx, cz);
                    if (cover == CoverKind.ShortGrass)
                    {
                        count += 2;
                    }
                    else if (cover != CoverKind.None)
                    {
                        count += 1;
                    }
                }
            }
            return count;
        }

        private static float ColorDistance(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
