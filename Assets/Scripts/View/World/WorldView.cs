using System.Collections.Generic;
using Murim.Core.Valley;
using UnityEngine;
using UnityEngine.Rendering;

namespace Murim.View.World
{
    /// <summary>
    /// Builds and owns the valley's rendered geometry, materials, directional daylight, cutaway, and ambient clock.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public static readonly Vector3 DirectionalLightEuler = new Vector3(50f, 35f, 0f);
        public static readonly Color DirectionalLightColor = new Color(1f, 248f / 255f, 238f / 255f, 1f);
        public const float DirectionalLightIntensity = 1.1f;
        public const float DirectionalShadowStrength = 0.6f;
        public static readonly Color FlatAmbientColor = new Color(0x8E / 255f, 0x9A / 255f, 0xA6 / 255f, 1f);

        private readonly List<Object> _ownedAssets = new List<Object>();
        private bool _ownsCatalog;

        public CutawayController Cutaway { get; private set; }
        public AmbientClock Clock { get; private set; }
        public ArtCatalog Catalog { get; private set; }
        public Light DirectionalLight { get; private set; }
        public Mesh TerrainMesh { get; private set; }
        public Mesh CapMesh { get; private set; }
        public IReadOnlyList<Mesh> PropMeshes { get; private set; }
        public Mesh WaterMesh { get; private set; }
        public Mesh WaterfallMesh { get; private set; }
        public Mesh RiverRocksMesh { get; private set; }
        public Mesh FallFoamMesh { get; private set; }

        public static WorldView Build(ValleyLayout layout, PresentationTuning tuning = null)
        {
            ArtCatalog catalog = ArtCatalog.Load(false);
            return BuildInternal(layout, tuning, catalog, true);
        }

        public static WorldView Build(ValleyLayout layout, PresentationTuning tuning, bool forceStandInArt)
        {
            ArtCatalog catalog = ArtCatalog.Load(forceStandInArt);
            return BuildInternal(layout, tuning, catalog, true);
        }

        public static WorldView Build(ValleyLayout layout, PresentationTuning tuning, ArtCatalog catalog)
        {
            bool ownsCatalog = catalog == null;
            ArtCatalog resolvedCatalog = catalog ?? ArtCatalog.Load(false);
            return BuildInternal(layout, tuning, resolvedCatalog, ownsCatalog);
        }

        private static WorldView BuildInternal(
            ValleyLayout layout,
            PresentationTuning tuning,
            ArtCatalog catalog,
            bool ownsCatalog)
        {
            PresentationTuning effectiveTuning = tuning ?? PresentationTuning.Default;

            GameObject root = new GameObject("WorldView");
            root.layer = Layers.WorldLayer;

            WorldView view = root.AddComponent<WorldView>();
            view.Catalog = catalog;
            view._ownsCatalog = ownsCatalog;
            view.Cutaway = root.AddComponent<CutawayController>();
            view.Cutaway.Initialize(effectiveTuning.CutFadeSeconds);
            view.Clock = root.AddComponent<AmbientClock>();

            view.BuildLighting();
            view.BuildWorldGeometry(layout, effectiveTuning);
            return view;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _ownedAssets.Count; i++)
            {
                Object asset = _ownedAssets[i];
                if (asset != null)
                {
                    DestroyAsset(asset);
                }
            }
            _ownedAssets.Clear();

            if (_ownsCatalog && Catalog != null)
            {
                Catalog.Dispose();
                Catalog = null;
            }
        }

        private void BuildLighting()
        {
            GameObject lightGo = new GameObject("DirectionalLight");
            lightGo.layer = Layers.WorldLayer;
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(DirectionalLightEuler);

            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = DirectionalLightColor;
            light.intensity = DirectionalLightIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = DirectionalShadowStrength;
            DirectionalLight = light;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = FlatAmbientColor;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();
        }

        private void BuildWorldGeometry(ValleyLayout layout, PresentationTuning tuning)
        {
            TerrainBuildResult terrain = TerrainMeshBuilder.Build(layout);
            TerrainMesh = terrain.TerrainMesh;
            CapMesh = terrain.CapMesh;
            TrackTerrainAssets(terrain);

            Material terrainMat = CreateTerrainMaterial();
            CreateMeshObject("Terrain", terrain.TerrainMesh, terrainMat, ShadowCastingMode.On);

            Material capMat = CreateCapMaterial();
            CreateMeshObject("Cap", terrain.CapMesh, capMat, ShadowCastingMode.Off);

            BuildProps(layout, tuning);
            BuildWaterAndWaterfall(layout, tuning);
        }

        private void BuildProps(ValleyLayout layout, PresentationTuning tuning)
        {
            List<Mesh> propMeshes = PropMeshBuilder.Build(layout, Catalog, tuning);
            PropMeshes = propMeshes;

            Material propMat = CreatePropMaterial();
            GameObject propsParent = new GameObject("Props");
            propsParent.layer = Layers.WorldLayer;
            propsParent.transform.SetParent(transform, false);

            for (int i = 0; i < propMeshes.Count; i++)
            {
                Mesh mesh = propMeshes[i];
                _ownedAssets.Add(mesh);
                CreateMeshObject(mesh.name, mesh, propMat, ShadowCastingMode.On, propsParent.transform);
            }
        }

        private void BuildWaterAndWaterfall(ValleyLayout layout, PresentationTuning tuning)
        {
            WaterBuildResult water = WaterMeshBuilder.Build(layout);
            WaterMesh = water.WaterMesh;
            WaterfallMesh = water.WaterfallMesh;
            RiverRocksMesh = water.RiverRocksMesh;
            FallFoamMesh = water.FallFoamMesh;

            _ownedAssets.Add(water.WaterMesh);
            _ownedAssets.Add(water.WaterfallMesh);
            _ownedAssets.Add(water.RiverRocksMesh);
            _ownedAssets.Add(water.FallFoamMesh);

            Material waterMat = CreateWaterSurfaceMaterial(tuning.RiverFlowSpeed);
            CreateMeshObject("Water", water.WaterMesh, waterMat, ShadowCastingMode.Off);

            if (water.RiverRocksMesh.vertexCount > 0)
            {
                Material rocksMat = CreateWaterDecalMaterial(Catalog.GetTexture(ArtAssetId.RiverRocks));
                CreateMeshObject("RiverRocks", water.RiverRocksMesh, rocksMat, ShadowCastingMode.Off);
            }

            if (water.FallFoamMesh.vertexCount > 0)
            {
                Material foamMat = CreateWaterDecalMaterial(Catalog.GetTexture(ArtAssetId.FallFoam));
                CreateMeshObject("FallFoam", water.FallFoamMesh, foamMat, ShadowCastingMode.Off);
            }

            if (water.WaterfallMesh.vertexCount > 0)
            {
                Material waterfallMat = CreateWaterfallMaterial(tuning.WaterfallSpeed);
                CreateMeshObject("Waterfall", water.WaterfallMesh, waterfallMat, ShadowCastingMode.Off);
            }
        }

        private void CreateMeshObject(
            string objectName,
            Mesh mesh,
            Material material,
            ShadowCastingMode shadowCasting,
            Transform parent = null)
        {
            GameObject go = new GameObject(objectName);
            go.layer = Layers.WorldLayer;
            go.transform.SetParent(parent != null ? parent : transform, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadowCasting;
            renderer.receiveShadows = true;
        }

        private Material CreateTerrainMaterial()
        {
            Material mat = CreateMaterial("Murim/Terrain");
            mat.SetTexture("_GroundMeadow", Catalog.GetTexture(ArtAssetId.GroundMeadow));
            mat.SetTexture("_GroundEarth", Catalog.GetTexture(ArtAssetId.GroundEarth));
            mat.SetTexture("_GroundGranite", Catalog.GetTexture(ArtAssetId.GroundGranite));
            mat.SetTexture("_GroundDamp", Catalog.GetTexture(ArtAssetId.GroundDamp));
            mat.SetTexture("_SlopeEarth", Catalog.GetTexture(ArtAssetId.SlopeEarth));
            mat.SetTexture("_SlopeRock", Catalog.GetTexture(ArtAssetId.SlopeRock));
            mat.SetTexture("_CliffFace", Catalog.GetTexture(ArtAssetId.CliffFace));
            mat.SetTexture("_WetRock", Catalog.GetTexture(ArtAssetId.WetRock));
            mat.SetTexture("_BlendMeadowEarth", Catalog.GetTexture(ArtAssetId.BlendMeadowEarth));
            mat.SetTexture("_BlendEarthGranite", Catalog.GetTexture(ArtAssetId.BlendEarthGranite));
            mat.SetTexture("_BlendMeadowGranite", Catalog.GetTexture(ArtAssetId.BlendMeadowGranite));
            return mat;
        }

        private Material CreateCapMaterial()
        {
            Material mat = CreateMaterial("Murim/Cap");
            mat.SetTexture("_GroundEarth", Catalog.GetTexture(ArtAssetId.GroundEarth));
            mat.SetTexture("_CliffFace", Catalog.GetTexture(ArtAssetId.CliffFace));
            return mat;
        }

        private Material CreatePropMaterial()
        {
            Material mat = CreateMaterial("Murim/Prop");
            mat.SetTexture("_PropAtlas", Catalog.PropAtlas);
            return mat;
        }

        private Material CreateWaterSurfaceMaterial(float riverFlowSpeed)
        {
            Material mat = CreateMaterial("Murim/Water");
            mat.SetFloat("_RiverFlowSpeed", riverFlowSpeed);
            mat.SetTexture("_RiverFlow", Catalog.GetTexture(ArtAssetId.RiverFlow));
            mat.SetTexture("_RiverRiffle", Catalog.GetTexture(ArtAssetId.RiverRiffle));
            mat.SetTexture("_RiverShore", Catalog.GetTexture(ArtAssetId.RiverShore));
            mat.SetTexture("_BlendDampRiver", Catalog.GetTexture(ArtAssetId.BlendDampRiver));
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_AlphaToMask", 1f);
            mat.renderQueue = (int)RenderQueue.Geometry;
            mat.DisableKeyword("_WATER_DECAL");
            return mat;
        }

        private Material CreateWaterDecalMaterial(Texture2D decalTex)
        {
            Material mat = CreateMaterial("Murim/Water");
            mat.SetTexture("_DecalTex", decalTex);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaToMask", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_WATER_DECAL");
            return mat;
        }

        private Material CreateWaterfallMaterial(float waterfallSpeed)
        {
            Material mat = CreateMaterial("Murim/Waterfall");
            mat.SetFloat("_WaterfallSpeed", waterfallSpeed);
            mat.SetTexture("_FallRibbon", Catalog.GetTexture(ArtAssetId.FallRibbon));
            return mat;
        }

        private Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                GameLog.LogError("ShaderMissing", shaderName);
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            Material mat = new Material(shader);
            _ownedAssets.Add(mat);
            return mat;
        }

        private void TrackTerrainAssets(TerrainBuildResult terrain)
        {
            _ownedAssets.Add(terrain.TerrainMesh);
            _ownedAssets.Add(terrain.CapMesh);
            _ownedAssets.Add(terrain.HeightTexture);
            _ownedAssets.Add(terrain.SurfaceTexture0);
            _ownedAssets.Add(terrain.SurfaceTexture1);
        }

        private static void DestroyAsset(Object asset)
        {
            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }
    }
}
