using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.View.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Renders the ART-017 destination marker on OverlayLayer with lifecycle fade per §3.6 and §3.8.
    /// </summary>
    public sealed class DestinationMarkerView : MonoBehaviour
    {
        public const float MarkerSizeMetres = 1.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private GameObject _quadGo;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _reachableMat;
        private Material _unreachableMat;
        private MaterialPropertyBlock _block;
        private readonly OverlaySlot _slot = new OverlaySlot();

        private float _visibleSeconds = PresentationTuning.DefaultMarkerVisibleSeconds;
        private float _fadeSeconds = PresentationTuning.DefaultMarkerFadeSeconds;
        private int _lastSequence;
        private float _elapsed;
        private float _alpha;
        private bool _active;
        private bool _initialized;

        public GameObject MarkerObject => _quadGo;
        public bool IsActive => _quadGo != null && _quadGo.activeSelf && _active;
        public float CurrentAlpha => _alpha;
        public Texture CurrentTexture => _renderer != null && _renderer.sharedMaterial != null
            ? _renderer.sharedMaterial.GetTexture("_BaseMap")
            : null;

        private void OnEnable()
        {
            OverlayRenderPass.RegisterSlot(_slot);
        }

        private void OnDisable()
        {
            OverlayRenderPass.UnregisterSlot(_slot);
        }

        private void OnDestroy()
        {
            OverlayRenderPass.UnregisterSlot(_slot);
            PlaceholderView.DestroyOwnedAsset(_mesh);
            PlaceholderView.DestroyOwnedAsset(_reachableMat);
            PlaceholderView.DestroyOwnedAsset(_unreachableMat);
        }

        public void Initialize(ArtCatalog catalog, PresentationTuning tuning)
        {
            if (_initialized)
            {
                return;
            }

            if (tuning != null)
            {
                _visibleSeconds = Mathf.Max(tuning.MarkerVisibleSeconds, PresentationTuning.MinMarkerVisibleSeconds);
                _fadeSeconds = Mathf.Max(tuning.MarkerFadeSeconds, PresentationTuning.MinMarkerFadeSeconds);
            }

            _quadGo = new GameObject("DestinationMarker");
            _quadGo.layer = Layers.OverlayLayer;
            _quadGo.transform.SetParent(transform, false);

            _mesh = PlaceholderView.BuildFlatSquareMesh(MarkerSizeMetres, "DestinationMarkerQuad");
            MeshFilter filter = _quadGo.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _reachableMat = PlaceholderView.CreateOverlayUnlitMaterial(catalog.GetTexture(ArtAssetId.MarkerReachable));
            _unreachableMat = PlaceholderView.CreateOverlayUnlitMaterial(catalog.GetTexture(ArtAssetId.MarkerUnreachable));
            _block = new MaterialPropertyBlock();

            _renderer = _quadGo.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _reachableMat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.forceRenderingOff = true;

            _slot.Mesh = _mesh;
            _slot.Material = _reachableMat;
            _slot.PropertyBlock = _block;

            _initialized = true;
            HideImmediate();
        }

        public void UpdateView(ExplorationSession session, float dt)
        {
            if (!_initialized || session == null)
            {
                return;
            }

            if (!session.MarkerVisible)
            {
                HideImmediate();
                return;
            }

            DestinationMarker marker = session.Marker;
            if (marker.Sequence != _lastSequence)
            {
                ActivateMarker(marker);
            }
            else if (_active && dt > 0f)
            {
                _elapsed += dt;
            }

            if (!_active)
            {
                return;
            }

            UpdateFade();
        }

        private void ActivateMarker(DestinationMarker marker)
        {
            _lastSequence = marker.Sequence;
            _elapsed = 0f;
            _alpha = 1f;
            _active = true;

            Material mat = marker.Kind == MarkerKind.Reachable ? _reachableMat : _unreachableMat;
            _renderer.sharedMaterial = mat;
            _slot.Material = mat;

            Vector3 worldPos = new Vector3(marker.Point.X, marker.Point.Y, marker.Point.Z);
            _quadGo.transform.position = worldPos;
            if (!_quadGo.activeSelf)
            {
                _quadGo.SetActive(true);
            }

            _slot.Active = true;
            _slot.Matrix = Matrix4x4.TRS(worldPos, Quaternion.identity, Vector3.one);
        }

        private void UpdateFade()
        {
            if (_elapsed < _visibleSeconds)
            {
                _alpha = 1f;
            }
            else
            {
                float fadeElapsed = _elapsed - _visibleSeconds;
                if (fadeElapsed >= _fadeSeconds)
                {
                    HideImmediate();
                    return;
                }
                _alpha = 1f - fadeElapsed / _fadeSeconds;
            }

            _block.SetColor(BaseColorId, new Color(1f, 1f, 1f, _alpha));
            _renderer.SetPropertyBlock(_block);
        }

        private void HideImmediate()
        {
            _active = false;
            _alpha = 0f;
            _slot.Active = false;
            if (_quadGo != null && _quadGo.activeSelf)
            {
                _quadGo.SetActive(false);
            }
        }
    }
}
