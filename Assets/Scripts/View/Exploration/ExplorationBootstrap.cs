using Murim.Core.Exploration.Session;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using Murim.View.UI;
using Murim.View.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Scene entry point that loads tunables, builds WorldView and ExplorationSession,
    /// drives the fixed 60 Hz simulation loop with interpolation, and updates all views.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ExplorationBootstrap : MonoBehaviour
    {
        public const int MaxTicksPerFrame = 6;
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        [SerializeField] private CameraRig _cameraRig;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private FloorLabelView _floorLabelView;
        [SerializeField] private MenuController _menuController;
        [SerializeField] private EventSystem _eventSystem;
        [SerializeField] private InputSystemUIInputModule _uiInputModule;
        [SerializeField] private Material[] _preservedMaterials;
        [SerializeField] private Shader[] _preservedShaders;

        private ValleyLayout _layout;
        private ExplorationSession _session;
        private WorldView _worldView;
        private PlaceholderView _placeholderView;
        private DestinationMarkerView _destinationMarkerView;
        private ExplorationInput _input;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private DevOverlay _devOverlay;
#endif

        private Vec2 _prevPos;
        private Vec2 _currPos;
        private float _prevElev;
        private float _currElev;
        private float _tickAccumulator;
        private float _lastAlpha = 1f;
        private float _lastCutY = ValleyConstants.FullViewCutY;
        private bool _initialized;

        public ExplorationSession Session => _session;
        public WorldView WorldView => _worldView;
        public CameraRig CameraRig => _cameraRig;
        public PlaceholderView PlaceholderView => _placeholderView;
        public DestinationMarkerView DestinationMarkerView => _destinationMarkerView;
        public FloorLabelView FloorLabelView => _floorLabelView;
        public MenuController MenuController => _menuController;
        public Canvas UiCanvas => _canvas;
        public EventSystem EventSystem => _eventSystem;
        public InputSystemUIInputModule UiInputModule => _uiInputModule;

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            MenuController.ConfigureUiInputModule(_uiInputModule);
        }

        private void Update()
        {
            StepFrame(Time.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            HandleApplicationFocus(hasFocus);
        }

        private void OnDestroy()
        {
            if (_worldView != null)
            {
                Destroy(_worldView.gameObject);
                _worldView = null;
            }
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            TuningLoader.LoadFromStreamingAssets(
                out ExplorationTuning explorationTuning,
                out PresentationTuning presentationTuning);

            _layout = ValleyLayoutData.CreateLayout();
            EnsureCameraRig();

            _session = new ExplorationSession(_layout, explorationTuning, _cameraRig.CurrentAspect);
            _worldView = WorldView.Build(_layout, presentationTuning);

            EnsureUiHierarchy(_worldView.Catalog, presentationTuning);
            EnsureExplorationViews(_worldView.Catalog, presentationTuning);

            _input = new ExplorationInput();
            _prevPos = _session.Placeholder.Position;
            _currPos = _prevPos;
            _prevElev = _session.Placeholder.Elevation;
            _currElev = _prevElev;
            _tickAccumulator = 0f;
            _lastAlpha = 1f;
            _lastCutY = _session.FloorView.CutY;

            _worldView.Clock.AmbientRunning = _session.AmbientRunning;
            _cameraRig.Apply(_session.Camera);
            _initialized = true;
        }

        public void StepFrame(float unscaledDeltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            float clampedDt = Mathf.Clamp(unscaledDeltaTime, 0f, CameraModel.MaxFrameDeltaSeconds);
            _input.ProcessFrame(_session, _menuController, _cameraRig, clampedDt);
            StepSimulation(clampedDt);
            UpdateViews(clampedDt);
        }

        public void StepSimulation(float clampedDt)
        {
            if (!_session.CanSimulate)
            {
                return;
            }

            _tickAccumulator += clampedDt;
            int ticks = 0;
            while (_tickAccumulator >= ValleyConstants.TickSeconds && ticks < MaxTicksPerFrame)
            {
                _prevPos = _session.Placeholder.Position;
                _prevElev = _session.Placeholder.Elevation;
                _session.Tick();
                _currPos = _session.Placeholder.Position;
                _currElev = _session.Placeholder.Elevation;
                _tickAccumulator -= ValleyConstants.TickSeconds;
                ticks++;
            }

            _lastAlpha = Mathf.Clamp01(_tickAccumulator / ValleyConstants.TickSeconds);
        }

        public void UpdateViews(float clampedDt)
        {
            _worldView.Clock.AmbientRunning = _session.AmbientRunning;

            float cutY = _session.FloorView.CutY;
            if (!CutHeightsEqual(cutY, _lastCutY))
            {
                _lastCutY = cutY;
                _worldView.Cutaway.SetCut(cutY);
            }

            _cameraRig.Apply(_session.Camera);
            _placeholderView.UpdateView(
                _session,
                _layout,
                _currPos,
                _prevPos,
                _currElev,
                _prevElev,
                _lastAlpha,
                clampedDt);
            _destinationMarkerView.UpdateView(_session, clampedDt);
            _floorLabelView.UpdateView(_session, clampedDt);
            _menuController.SyncFromSession(_session);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_devOverlay != null)
            {
                _devOverlay.UpdateOverlay(_session, _layout, clampedDt);
            }
#endif
        }

        public void HandleApplicationFocus(bool hasFocus)
        {
            if (_session != null)
            {
                _session.SetFocused(hasFocus);
            }
        }

        private void EnsureCameraRig()
        {
            if (_cameraRig == null)
            {
                _cameraRig = FindFirstObjectByType<CameraRig>();
            }

            if (_cameraRig == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                _cameraRig = camGo.AddComponent<CameraRig>();
            }

            _cameraRig.EnsureConfigured();
        }

        private void EnsureUiHierarchy(ArtCatalog catalog, PresentationTuning tuning)
        {
            EnsureEventSystem();
            EnsureCanvas();
            EnsureFloorLabel(catalog, tuning);
            EnsureMenuController(catalog);
        }

        private void EnsureEventSystem()
        {
            if (_eventSystem == null)
            {
                _eventSystem = EventSystem.current != null
                    ? EventSystem.current
                    : FindFirstObjectByType<EventSystem>();
            }

            if (_eventSystem == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                _eventSystem = esGo.AddComponent<EventSystem>();
            }

            if (_uiInputModule == null)
            {
                _uiInputModule = _eventSystem.GetComponent<InputSystemUIInputModule>();
                if (_uiInputModule == null)
                {
                    _uiInputModule = _eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }

            MenuController.ConfigureUiInputModule(_uiInputModule);
        }

        private void EnsureCanvas()
        {
            if (_canvas == null)
            {
                _canvas = FindFirstObjectByType<Canvas>();
            }

            if (_canvas == null)
            {
                GameObject canvasGo = new GameObject("UiCanvas");
                canvasGo.layer = Layers.UiLayer;
                _canvas = canvasGo.AddComponent<Canvas>();
            }

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = _canvas.gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1.0f;

            if (_canvas.GetComponent<GraphicRaycaster>() == null)
            {
                _canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void EnsureFloorLabel(ArtCatalog catalog, PresentationTuning tuning)
        {
            if (_floorLabelView == null)
            {
                _floorLabelView = _canvas.GetComponentInChildren<FloorLabelView>(true);
            }

            if (_floorLabelView == null)
            {
                GameObject go = new GameObject("FloorLabel", typeof(RectTransform));
                go.layer = Layers.UiLayer;
                go.transform.SetParent(_canvas.transform, false);
                _floorLabelView = go.AddComponent<FloorLabelView>();
            }

            _floorLabelView.Initialize(catalog, tuning);
        }

        private void EnsureMenuController(ArtCatalog catalog)
        {
            if (_menuController == null)
            {
                _menuController = _canvas.GetComponentInChildren<MenuController>(true);
            }

            if (_menuController == null)
            {
                GameObject go = new GameObject("SystemMenu", typeof(RectTransform));
                go.layer = Layers.UiLayer;
                go.transform.SetParent(_canvas.transform, false);
                _menuController = go.AddComponent<MenuController>();
            }

            _menuController.Initialize(catalog, _session, _cameraRig);
        }

        private void EnsureExplorationViews(ArtCatalog catalog, PresentationTuning tuning)
        {
            GameObject phGo = new GameObject("PlaceholderView");
            phGo.layer = Layers.WorldLayer;
            phGo.transform.SetParent(transform, false);
            _placeholderView = phGo.AddComponent<PlaceholderView>();
            _placeholderView.Initialize(catalog, tuning, _session);

            GameObject markerGo = new GameObject("DestinationMarkerView");
            markerGo.layer = Layers.OverlayLayer;
            markerGo.transform.SetParent(transform, false);
            _destinationMarkerView = markerGo.AddComponent<DestinationMarkerView>();
            _destinationMarkerView.Initialize(catalog, tuning);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GameObject overlayGo = new GameObject("DevOverlay");
            overlayGo.layer = Layers.WorldLayer;
            overlayGo.transform.SetParent(transform, false);
            _devOverlay = overlayGo.AddComponent<DevOverlay>();
            _devOverlay.Initialize(_layout, _session);
#endif
        }

        private static bool CutHeightsEqual(float a, float b)
        {
            if (float.IsPositiveInfinity(a) && float.IsPositiveInfinity(b))
            {
                return true;
            }
            return Mathf.Approximately(a, b);
        }
    }
}
