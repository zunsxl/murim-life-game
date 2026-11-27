using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Murim.Core.Camera;
using Murim.Core.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace Murim.View
{
    /// <summary>
    /// Bootstraps the generated valley world, 2D layers, atmosphere globals, and fixed vista camera (§3.5.1).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const float HazeSpanM = 110f;
        public const float HazeMax = 0.45f;
        public const float CameraZ = -10f;
        public const float CameraNear = 0.3f;
        public const float CameraFar = 20f;
        public const float BackdropLocalZ = 10f;

        public static readonly Color WarmTintColor = new Color(1f, 242f / 255f, 217f / 255f, 1f);
        public static readonly Color HazeColor = new Color(239f / 255f, 211f / 255f, 160f / 255f, 1f);

        private static readonly int WarmTintId = Shader.PropertyToID("_MurimWarmTint");
        private static readonly int HazeColorId = Shader.PropertyToID("_MurimHazeColor");
        private static readonly int HazeMaxId = Shader.PropertyToID("_MurimHazeMax");
        private static readonly int HazeEndVId = Shader.PropertyToID("_MurimHazeEndV");
        private static readonly int HazeStartVId = Shader.PropertyToID("_MurimHazeStartV");
        private static readonly int CoarseBlendId = Shader.PropertyToID("_MurimCoarseBlend");

        [SerializeField] private Camera targetCamera;
        [SerializeField] private MeshRenderer backdropRenderer;
        [SerializeField] private CameraRigDriver cameraRigDriver;

        public ValleyWorld World { get; private set; }
        public WorldConfig Config { get; private set; }
        public CameraConfig CameraConfig { get; private set; }
        public uint Seed { get; private set; }
        public string SeedSourceName { get; private set; }
        public Camera WorldCamera => targetCamera;
        public MeshRenderer BackdropRenderer => backdropRenderer;
        public CameraRigDriver CameraDriver => cameraRigDriver;
        public IReadOnlyList<MeshRenderer> TerrainRenderers { get; private set; }
        public WaterSceneObjects WaterObjects { get; private set; }
        public PropView PropView { get; private set; }
        public SystemOverlay SystemOverlay { get; private set; }

        private void Awake()
        {
            EnsureWindowedMode();

            var stopwatch = Stopwatch.StartNew();
            Seed = SeedSource.Resolve(out string seedSource);
            SeedSourceName = seedSource;
            Config = LoadWorldConfig();
            World = ValleyGenerator.Generate(Seed, Config);

            ApplyAtmosphereGlobals(World);
            BuildWorldVisuals(World);
            SetupCameraAndBackdrop(World);
            CameraConfig = LoadCameraConfig();
            SetupCameraDriver(World, CameraConfig);
            SetupSystemOverlay();

            stopwatch.Stop();
            LogWorldReady(World, seedSource, (int)stopwatch.ElapsedMilliseconds);
        }

        private static void EnsureWindowedMode()
        {
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
            }
        }

        private static WorldConfig LoadWorldConfig()
        {
            WorldConfig validated = null;
            bool ok = JsonConfigLoader.TryLoad<WorldConfigDto>(
                JsonConfigLoader.WorldConfigFileName,
                out WorldConfigDto _,
                parsed =>
                {
                    if (WorldConfig.TryCreate(parsed.ToValues(), out WorldConfig created, out string failedField))
                    {
                        validated = created;
                        return null;
                    }

                    return failedField;
                });

            return ok && validated != null ? validated : WorldConfig.Default;
        }

        private static CameraConfig LoadCameraConfig()
        {
            CameraConfig validated = null;
            bool ok = JsonConfigLoader.TryLoad<CameraConfigDto>(
                JsonConfigLoader.CameraConfigFileName,
                out CameraConfigDto _,
                parsed =>
                {
                    if (Murim.Core.Camera.CameraConfig.TryCreate(
                        parsed.ToData(),
                        out CameraConfig created,
                        out string failedField))
                    {
                        validated = created;
                        return null;
                    }

                    return failedField;
                });

            return ok && validated != null ? validated : Murim.Core.Camera.CameraConfig.Default;
        }

        private void SetupCameraDriver(ValleyWorld world, CameraConfig cameraConfig)
        {
            if (targetCamera == null)
            {
                return;
            }

            if (cameraRigDriver == null)
            {
                cameraRigDriver = targetCamera.GetComponent<CameraRigDriver>();
                if (cameraRigDriver == null)
                {
                    cameraRigDriver = targetCamera.gameObject.AddComponent<CameraRigDriver>();
                }
            }

            var frame = new ViewFrame(world.PlaneBottomCm, world.PlaneCrestCm, world.VistaUCm, world.VistaVCm);
            float initialAspect = CameraRigDriver.ResolveAspect(targetCamera);
            int initialScreenHeight = CameraRigDriver.ResolveScreenHeightPx();

            var rig = new CameraRig(frame, cameraConfig, initialAspect, initialScreenHeight);
            Transform backdropTransform = backdropRenderer != null ? backdropRenderer.transform : null;
            cameraRigDriver.Initialize(targetCamera, backdropTransform, rig, PropView);
        }

        private void SetupSystemOverlay()
        {
            GameObject prefab = Resources.Load<GameObject>("Prefabs/SystemOverlay");
            if (prefab == null)
            {
                return;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = "SystemOverlay";
            SystemOverlay = instance.GetComponent<SystemOverlay>();
            if (SystemOverlay != null)
            {
                SystemOverlay.Initialize(cameraRigDriver);
            }
        }

        private static void ApplyAtmosphereGlobals(ValleyWorld world)
        {
            float crestV = world.PlaneCrestCm / 100f;
            Shader.SetGlobalColor(WarmTintId, WarmTintColor);
            Shader.SetGlobalColor(HazeColorId, HazeColor);
            Shader.SetGlobalFloat(HazeMaxId, HazeMax);
            Shader.SetGlobalFloat(HazeEndVId, crestV);
            Shader.SetGlobalFloat(HazeStartVId, crestV - HazeSpanM);
            Shader.SetGlobalFloat(CoarseBlendId, 0f);
        }

        private void BuildWorldVisuals(ValleyWorld world)
        {
            var worldRoot = new GameObject("World");
            worldRoot.layer = DrawOrder.WorldLayer;

            var terrainRoot = new GameObject("Terrain");
            terrainRoot.layer = DrawOrder.WorldLayer;
            terrainRoot.transform.SetParent(worldRoot.transform, false);
            Material terrainMat = Resources.Load<Material>("Materials/Terrain");
            TerrainRenderers = TerrainMeshBuilder.Build(world, terrainMat, terrainRoot.transform);

            var waterRoot = new GameObject("Water");
            waterRoot.layer = DrawOrder.WorldLayer;
            waterRoot.transform.SetParent(worldRoot.transform, false);
            var waterMaterials = new WaterMaterials
            {
                Water = Resources.Load<Material>("Materials/Water"),
                Waterfall = Resources.Load<Material>("Materials/Waterfall"),
                Foam = Resources.Load<Material>("Materials/WaterFoam"),
            };
            WaterObjects = WaterMeshBuilder.Build(world, waterMaterials, waterRoot.transform);

            var propsRoot = new GameObject("Props");
            propsRoot.layer = DrawOrder.WorldLayer;
            propsRoot.transform.SetParent(worldRoot.transform, false);
            var worldPropsAtlas = Resources.Load<SpriteAtlas>("Art/Props/WorldProps");
            Material worldSpriteMat = Resources.Load<Material>("Materials/WorldSprite");
            Material worldFloraMat = Resources.Load<Material>("Materials/WorldFlora");
            PropView = PropView.Build(world, worldPropsAtlas, worldSpriteMat, worldFloraMat, propsRoot.transform);
        }

        private void SetupCameraAndBackdrop(ValleyWorld world)
        {
            if (targetCamera == null)
            {
                targetCamera = FindFirstObjectByType<Camera>();
            }

            if (targetCamera != null)
            {
                targetCamera.gameObject.layer = DrawOrder.WorldLayer;
                targetCamera.orthographic = true;
                targetCamera.orthographicSize = Murim.Core.Camera.CameraConfig.Default.DefaultViewHeightM * 0.5f;
                targetCamera.nearClipPlane = CameraNear;
                targetCamera.farClipPlane = CameraFar;
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = HazeColor;
                targetCamera.transform.position = new Vector3(
                    world.VistaUCm / 100f,
                    world.VistaVCm / 100f,
                    CameraZ);
                targetCamera.transform.rotation = Quaternion.identity;
                EnsureBackdropQuad(targetCamera.transform);
            }
        }

        private void EnsureBackdropQuad(Transform cameraTransform)
        {
            if (backdropRenderer == null)
            {
                var go = new GameObject("Backdrop");
                go.transform.SetParent(cameraTransform, false);
                go.AddComponent<MeshFilter>();
                backdropRenderer = go.AddComponent<MeshRenderer>();
            }

            GameObject backdropGo = backdropRenderer.gameObject;
            backdropGo.layer = DrawOrder.WorldLayer;
            backdropGo.transform.SetParent(cameraTransform, false);
            backdropGo.transform.localPosition = new Vector3(0f, 0f, BackdropLocalZ);
            backdropGo.transform.localRotation = Quaternion.identity;

            MeshFilter filter = backdropGo.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = backdropGo.AddComponent<MeshFilter>();
            }

            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = CreateUnitQuadMesh();
            }

            backdropRenderer.sharedMaterial = Resources.Load<Material>("Materials/Backdrop");
            backdropRenderer.sortingOrder = DrawOrder.Backdrop;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;
        }

        private static Mesh CreateUnitQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "BackdropUnitQuad",
                indexFormat = IndexFormat.UInt16,
            };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };
            var white = new Color32(255, 255, 255, 255);
            mesh.colors32 = new[] { white, white, white, white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void LogWorldReady(ValleyWorld world, string seedSource, int elapsedMs)
        {
            string hashHex = world.Hash.ToString("X16", CultureInfo.InvariantCulture);
            GameLog.Info(string.Format(
                CultureInfo.InvariantCulture,
                "World ready seed={0} source={1} hash={2} props={3} ms={4}",
                world.Seed,
                seedSource,
                hashHex,
                world.Props.Count,
                elapsedMs));
        }
    }
}
