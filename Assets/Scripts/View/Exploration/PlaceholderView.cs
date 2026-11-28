using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.Core.Valley;
using Murim.View.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Renders the ART-014 placeholder capsule and ART-015 airborne height/surface cue per §3.4 and §3.8.
    /// </summary>
    public sealed class PlaceholderView : MonoBehaviour
    {
        public const float DepthBiasMetres = 0.7f;
        public const float CueLineWidthMetres = 0.04f;
        public const float CueLineBaseAlpha = 0.9f;
        public const float CueRingSizeMetres = 0.8f;

        private static readonly Color CueLineColor = new Color(0xF4 / 255f, 0xF1 / 255f, 0xE4 / 255f, CueLineBaseAlpha);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int SpecularHighlightsId = Shader.PropertyToID("_SpecularHighlights");

        private GameObject _capsuleGo;
        private MeshRenderer _capsuleRenderer;
        private Mesh _capsuleMesh;
        private Material _capsuleMat;
        private MaterialPropertyBlock _capsuleBlock;
        private bool _capsuleInFadeMode;

        private GameObject _cueLineGo;
        private MeshRenderer _cueLineRenderer;
        private Mesh _cueLineMesh;
        private Material _cueLineMat;
        private MaterialPropertyBlock _cueLineBlock;
        private readonly OverlaySlot _cueLineSlot = new OverlaySlot();

        private GameObject _cueRingGo;
        private MeshRenderer _cueRingRenderer;
        private Mesh _cueRingMesh;
        private Material _cueRingMat;
        private MaterialPropertyBlock _cueRingBlock;
        private readonly OverlaySlot _cueRingSlot = new OverlaySlot();

        private float _cutFadeSeconds = PresentationTuning.DefaultCutFadeSeconds;
        private float _fadeAlpha = 1f;
        private bool _initialized;

        public GameObject CapsuleObject => _capsuleGo;
        public GameObject CueLineObject => _cueLineGo;
        public GameObject CueRingObject => _cueRingGo;
        public bool CapsuleVisible => _capsuleGo != null && _capsuleGo.activeSelf && _fadeAlpha > 0.001f;
        public bool CueVisible => _cueRingGo != null && _cueRingGo.activeSelf && _fadeAlpha > 0.001f;
        public float FadeAlpha => _fadeAlpha;
        public Vector3 CueRingPosition => _cueRingGo != null ? _cueRingGo.transform.position : Vector3.zero;

        private void OnEnable()
        {
            OverlayRenderPass.RegisterSlot(_cueLineSlot);
            OverlayRenderPass.RegisterSlot(_cueRingSlot);
        }

        private void OnDisable()
        {
            OverlayRenderPass.UnregisterSlot(_cueLineSlot);
            OverlayRenderPass.UnregisterSlot(_cueRingSlot);
        }

        private void OnDestroy()
        {
            OverlayRenderPass.UnregisterSlot(_cueLineSlot);
            OverlayRenderPass.UnregisterSlot(_cueRingSlot);
            DestroyOwnedAsset(_capsuleMesh);
            DestroyOwnedAsset(_capsuleMat);
            DestroyOwnedAsset(_cueLineMesh);
            DestroyOwnedAsset(_cueLineMat);
            DestroyOwnedAsset(_cueRingMesh);
            DestroyOwnedAsset(_cueRingMat);
        }

        public void Initialize(ArtCatalog catalog, PresentationTuning tuning, ExplorationSession session)
        {
            if (_initialized)
            {
                return;
            }

            _cutFadeSeconds = Mathf.Max(
                tuning != null ? tuning.CutFadeSeconds : PresentationTuning.DefaultCutFadeSeconds,
                PresentationTuning.MinCutFadeSeconds);

            BuildCapsuleVisual(catalog);
            BuildCueLineVisual();
            BuildCueRingVisual(catalog);

            bool visible = session == null || session.FloorView.IsPlaceholderVisible(session.Placeholder.LogicalBand);
            _fadeAlpha = visible ? 1f : 0f;
            _initialized = true;

            if (session != null)
            {
                PlaceholderState state = session.Placeholder;
                UpdateView(session, session.Layout, state.Position, state.Position, state.Elevation, state.Elevation, 1f, 0f);
            }
        }

        public void UpdateView(
            ExplorationSession session,
            ValleyLayout layout,
            Vec2 currPos,
            Vec2 prevPos,
            float currElev,
            float prevElev,
            float interpAlpha,
            float dt)
        {
            if (!_initialized || session == null || layout == null)
            {
                return;
            }

            PlaceholderState state = session.Placeholder;
            bool logicalVisible = session.FloorView.IsPlaceholderVisible(state.LogicalBand);
            float targetAlpha = logicalVisible ? 1f : 0f;
            if (dt > 0f)
            {
                _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, targetAlpha, dt / _cutFadeSeconds);
            }

            float t = Mathf.Clamp01(interpAlpha);
            float rx = Mathf.Lerp(prevPos.X, currPos.X, t);
            float rz = Mathf.Lerp(prevPos.Y, currPos.Y, t);
            float ry = Mathf.Lerp(prevElev, currElev, t);

            transform.position = new Vector3(rx, ry, rz);
            UpdateCapsuleVisual();
            UpdateHeightCue(state, layout, rx, ry, rz);
        }

        private void BuildCapsuleVisual(ArtCatalog catalog)
        {
            WorldSpriteEntry sprite = catalog.GetWorldSprite(ArtAssetId.PlaceholderCapsule);
            Texture2D tex = catalog.GetTexture(ArtAssetId.PlaceholderCapsule);

            _capsuleGo = new GameObject("PlaceholderCapsule");
            _capsuleGo.layer = Layers.WorldLayer;
            _capsuleGo.transform.SetParent(transform, false);
            _capsuleGo.transform.localPosition = new Vector3(
                0f,
                ValleyConstants.Sin45 * DepthBiasMetres,
                -ValleyConstants.Cos45 * DepthBiasMetres);

            _capsuleMesh = BuildCapsuleQuadMesh(sprite);
            MeshFilter filter = _capsuleGo.AddComponent<MeshFilter>();
            filter.sharedMesh = _capsuleMesh;

            _capsuleMat = CreateCapsuleMaterial(tex);
            _capsuleBlock = new MaterialPropertyBlock();
            _capsuleRenderer = _capsuleGo.AddComponent<MeshRenderer>();
            _capsuleRenderer.sharedMaterial = _capsuleMat;
            _capsuleRenderer.shadowCastingMode = ShadowCastingMode.On;
            _capsuleRenderer.receiveShadows = true;
            SetCapsuleOpaqueMode();
        }

        private void BuildCueLineVisual()
        {
            _cueLineGo = new GameObject("HeightCueLine");
            _cueLineGo.layer = Layers.OverlayLayer;
            _cueLineGo.transform.SetParent(transform, false);

            _cueLineMesh = BuildVerticalUnitQuadMesh(CueLineWidthMetres);
            MeshFilter filter = _cueLineGo.AddComponent<MeshFilter>();
            filter.sharedMesh = _cueLineMesh;

            _cueLineMat = CreateOverlayUnlitMaterial(null);
            _cueLineBlock = new MaterialPropertyBlock();
            _cueLineRenderer = _cueLineGo.AddComponent<MeshRenderer>();
            _cueLineRenderer.sharedMaterial = _cueLineMat;
            _cueLineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _cueLineRenderer.receiveShadows = false;
            _cueLineRenderer.forceRenderingOff = true;

            _cueLineSlot.Mesh = _cueLineMesh;
            _cueLineSlot.Material = _cueLineMat;
            _cueLineSlot.PropertyBlock = _cueLineBlock;
        }

        private void BuildCueRingVisual(ArtCatalog catalog)
        {
            Texture2D tex = catalog.GetTexture(ArtAssetId.CueRing);

            _cueRingGo = new GameObject("HeightCueRing");
            _cueRingGo.layer = Layers.OverlayLayer;
            _cueRingGo.transform.SetParent(transform, false);

            _cueRingMesh = BuildFlatSquareMesh(CueRingSizeMetres, "CueRingQuad");
            MeshFilter filter = _cueRingGo.AddComponent<MeshFilter>();
            filter.sharedMesh = _cueRingMesh;

            _cueRingMat = CreateOverlayUnlitMaterial(tex);
            _cueRingBlock = new MaterialPropertyBlock();
            _cueRingRenderer = _cueRingGo.AddComponent<MeshRenderer>();
            _cueRingRenderer.sharedMaterial = _cueRingMat;
            _cueRingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _cueRingRenderer.receiveShadows = false;
            _cueRingRenderer.forceRenderingOff = true;

            _cueRingSlot.Mesh = _cueRingMesh;
            _cueRingSlot.Material = _cueRingMat;
            _cueRingSlot.PropertyBlock = _cueRingBlock;
        }

        private void UpdateCapsuleVisual()
        {
            if (_fadeAlpha <= 0.001f)
            {
                if (_capsuleGo.activeSelf)
                {
                    _capsuleGo.SetActive(false);
                }
                return;
            }

            if (!_capsuleGo.activeSelf)
            {
                _capsuleGo.SetActive(true);
            }

            if (_fadeAlpha >= 0.999f)
            {
                if (_capsuleInFadeMode)
                {
                    SetCapsuleOpaqueMode();
                }
                return;
            }

            if (!_capsuleInFadeMode)
            {
                SetCapsuleFadeMode();
            }

            _capsuleRenderer.shadowCastingMode = _fadeAlpha >= 0.5f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _capsuleBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, _fadeAlpha));
            _capsuleRenderer.SetPropertyBlock(_capsuleBlock);
        }

        private void UpdateHeightCue(PlaceholderState state, ValleyLayout layout, float rx, float ry, float rz)
        {
            float surfaceY = layout.SurfaceAt(rx, rz);
            bool landing = state.AirLayer == 0
                && state.Motion == PlaceholderMotion.Descending
                && ry > surfaceY + 0.001f;
            bool showCue = (state.AirLayer > 0 || landing) && _fadeAlpha > 0.001f;

            if (!showCue)
            {
                HideCue();
                return;
            }

            if (!_cueRingGo.activeSelf)
            {
                _cueRingGo.SetActive(true);
            }

            Vector3 ringWorld = new Vector3(rx, surfaceY, rz);
            _cueRingGo.transform.position = ringWorld;
            _cueRingBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, _fadeAlpha));
            _cueRingRenderer.SetPropertyBlock(_cueRingBlock);
            _cueRingSlot.Active = true;
            _cueRingSlot.Matrix = Matrix4x4.TRS(ringWorld, Quaternion.identity, Vector3.one);

            float lineHeight = Mathf.Max(0f, ry - surfaceY);
            if (lineHeight > 0.001f)
            {
                if (!_cueLineGo.activeSelf)
                {
                    _cueLineGo.SetActive(true);
                }
                _cueLineGo.transform.position = ringWorld;
                _cueLineGo.transform.localScale = new Vector3(1f, lineHeight, 1f);
                _cueLineBlock.SetColor(BaseColorId, new Color(CueLineColor.r, CueLineColor.g, CueLineColor.b, CueLineBaseAlpha * _fadeAlpha));
                _cueLineRenderer.SetPropertyBlock(_cueLineBlock);
                _cueLineSlot.Active = true;
                _cueLineSlot.Matrix = Matrix4x4.TRS(ringWorld, Quaternion.identity, new Vector3(1f, lineHeight, 1f));
            }
            else
            {
                if (_cueLineGo.activeSelf)
                {
                    _cueLineGo.SetActive(false);
                }
                _cueLineSlot.Active = false;
            }
        }

        private void HideCue()
        {
            if (_cueLineGo != null && _cueLineGo.activeSelf)
            {
                _cueLineGo.SetActive(false);
            }
            if (_cueRingGo != null && _cueRingGo.activeSelf)
            {
                _cueRingGo.SetActive(false);
            }
            _cueLineSlot.Active = false;
            _cueRingSlot.Active = false;
        }

        private void SetCapsuleOpaqueMode()
        {
            _capsuleInFadeMode = false;
            _capsuleMat.SetFloat(SurfaceId, 0f);
            _capsuleMat.SetFloat(AlphaClipId, 1f);
            _capsuleMat.SetFloat(CutoffId, 0.5f);
            _capsuleMat.SetFloat(SrcBlendId, (float)BlendMode.One);
            _capsuleMat.SetFloat(DstBlendId, (float)BlendMode.Zero);
            _capsuleMat.SetFloat(ZWriteId, 1f);
            _capsuleMat.EnableKeyword("_ALPHATEST_ON");
            _capsuleMat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _capsuleMat.renderQueue = (int)RenderQueue.AlphaTest;
            _capsuleRenderer.shadowCastingMode = ShadowCastingMode.On;
            _capsuleBlock.SetColor(BaseColorId, Color.white);
            _capsuleRenderer.SetPropertyBlock(_capsuleBlock);
        }

        private void SetCapsuleFadeMode()
        {
            _capsuleInFadeMode = true;
            _capsuleMat.SetFloat(SurfaceId, 1f);
            _capsuleMat.SetFloat(AlphaClipId, 0f);
            _capsuleMat.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            _capsuleMat.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            _capsuleMat.SetFloat(ZWriteId, 0f);
            _capsuleMat.DisableKeyword("_ALPHATEST_ON");
            _capsuleMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _capsuleMat.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Material CreateCapsuleMaterial(Texture2D tex)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                GameLog.LogError("ShaderMissing", "SimpleLit");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            Material mat = new Material(shader);
            mat.SetTexture(BaseMapId, tex);
            mat.SetColor(BaseColorId, Color.white);
            mat.SetFloat(SmoothnessId, 0f);
            mat.SetFloat(SpecularHighlightsId, 0f);
            mat.SetFloat(CullId, (float)CullMode.Off);
            return mat;
        }

        internal static Material CreateOverlayUnlitMaterial(Texture2D tex)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                GameLog.LogError("ShaderMissing", "Unlit");
            }

            Material mat = new Material(shader);
            if (tex != null)
            {
                mat.SetTexture(BaseMapId, tex);
            }
            mat.SetColor(BaseColorId, Color.white);
            mat.SetFloat(SurfaceId, 1f);
            mat.SetFloat(AlphaClipId, 0f);
            mat.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            mat.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(ZWriteId, 0f);
            mat.SetFloat(CullId, (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Overlay;
            return mat;
        }

        private static Mesh BuildCapsuleQuadMesh(WorldSpriteEntry sprite)
        {
            float ppu = ArtCatalog.WorldPixelsPerMetre;
            float worldW = sprite.PixelWidth / ppu;
            float left = -sprite.PivotPixels.x / ppu;
            float right = left + worldW;
            float bottom = -(sprite.PivotPixels.y / ppu) * ValleyConstants.Sqrt2;
            float top = ((sprite.PixelHeight - sprite.PivotPixels.y) / ppu) * ValleyConstants.Sqrt2;

            Mesh mesh = new Mesh { name = "PlaceholderCapsuleQuad" };
            mesh.vertices = new[]
            {
                new Vector3(left, bottom, 0f),
                new Vector3(right, bottom, 0f),
                new Vector3(right, top, 0f),
                new Vector3(left, top, 0f)
            };
            // Upward normal aligns NdotL with fixed flat-ground lighting on MurimProp.
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildVerticalUnitQuadMesh(float width)
        {
            float halfW = width * 0.5f;
            Mesh mesh = new Mesh { name = "HeightCueLineQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-halfW, 0f, 0f),
                new Vector3(halfW, 0f, 0f),
                new Vector3(halfW, 1f, 0f),
                new Vector3(-halfW, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh BuildFlatSquareMesh(float size, string meshName)
        {
            float half = size * 0.5f;
            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = new[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3(half, 0f, -half),
                new Vector3(half, 0f, half),
                new Vector3(-half, 0f, half)
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static void DestroyOwnedAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

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
