using Murim.Core.Exploration.Session;
using Murim.View.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Murim.View.UI
{
    /// <summary>
    /// Transient top-centre uGUI floor/altitude label on ART-018 backing per §3.6 and §3.8.
    /// </summary>
    public sealed class FloorLabelView : MonoBehaviour
    {
        public const float LabelWidth = 360f;
        public const float LabelHeight = 56f;
        public const float TopOffset = 96f;
        public const float FontSize = 30f;
        public const string DefaultFontResourcePath = "Fonts & Materials/LiberationSans SDF";

        private static readonly Color TextColor = new Color(0xF2 / 255f, 0xEE / 255f, 0xE6 / 255f, 1f);
        private static readonly string FullViewText = "Full View";
        private static readonly string[] GroundFloorTexts =
        {
            "Ground Floor 1",
            "Ground Floor 2",
            "Ground Floor 3",
            "Ground Floor 4"
        };
        private static readonly string[] AirLayerTexts =
        {
            "Air Layer 1",
            "Air Layer 2",
            "Air Layer 3"
        };

        [SerializeField] private Image _backgroundImage;
        [SerializeField] private TextMeshProUGUI _labelText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_FontAsset _fontAsset;

        private float _visibleSeconds = PresentationTuning.DefaultLabelVisibleSeconds;
        private float _fadeSeconds = PresentationTuning.DefaultLabelFadeSeconds;
        private int _lastSequence;
        private float _elapsed;
        private bool _active;
        private bool _initialized;

        public bool IsActive => gameObject.activeSelf && _active;
        public float CurrentAlpha => _canvasGroup != null ? _canvasGroup.alpha : 0f;
        public string CurrentText => _labelText != null ? _labelText.text : string.Empty;
        public RectTransform RectTransform => transform as RectTransform;
        public TextMeshProUGUI LabelText => _labelText;
        public Image BackgroundImage => _backgroundImage;

        public void Initialize(ArtCatalog catalog, PresentationTuning tuning)
        {
            if (_initialized)
            {
                return;
            }

            if (tuning != null)
            {
                _visibleSeconds = Mathf.Max(tuning.LabelVisibleSeconds, PresentationTuning.MinLabelVisibleSeconds);
                _fadeSeconds = Mathf.Max(tuning.LabelFadeSeconds, PresentationTuning.MinLabelFadeSeconds);
            }

            EnsureHierarchy(catalog);
            _initialized = true;
            HideImmediate();
        }

        public void UpdateView(ExplorationSession session, float dt)
        {
            if (!_initialized || session == null)
            {
                return;
            }

            if (!session.LabelVisible)
            {
                HideImmediate();
                return;
            }

            FloorLabel label = session.Label;
            if (label.Sequence != _lastSequence)
            {
                ShowLabel(label);
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

        private void EnsureHierarchy(ArtCatalog catalog)
        {
            gameObject.layer = Layers.UiLayer;

            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null)
            {
                rt = gameObject.AddComponent<RectTransform>();
            }
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            rt.anchoredPosition = new Vector2(0f, -TopOffset);
            rt.localScale = Vector3.one;

            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            if (_backgroundImage == null)
            {
                _backgroundImage = GetComponent<Image>();
                if (_backgroundImage == null)
                {
                    _backgroundImage = gameObject.AddComponent<Image>();
                }
            }
            _backgroundImage.sprite = catalog != null ? catalog.GetSprite(ArtAssetId.UiLabel) : null;
            _backgroundImage.type = Image.Type.Sliced;
            _backgroundImage.color = Color.white;
            _backgroundImage.raycastTarget = false;

            EnsureLabelText(rt);
        }

        private void EnsureLabelText(RectTransform parentRt)
        {
            if (_labelText == null)
            {
                _labelText = GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (_labelText == null)
            {
                GameObject textGo = new GameObject("Text", typeof(RectTransform));
                textGo.layer = Layers.UiLayer;
                textGo.transform.SetParent(parentRt, false);
                _labelText = textGo.AddComponent<TextMeshProUGUI>();
            }

            RectTransform textRt = _labelText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = new Vector2(-12f, 0f);
            textRt.localScale = Vector3.one;

            if (_fontAsset == null)
            {
                _fontAsset = Resources.Load<TMP_FontAsset>(DefaultFontResourcePath);
            }

            if (_fontAsset != null)
            {
                _labelText.font = _fontAsset;
            }
            _labelText.fontSize = FontSize;
            _labelText.color = TextColor;
            _labelText.alignment = TextAlignmentOptions.Center;
            _labelText.textWrappingMode = TextWrappingModes.NoWrap;
            _labelText.overflowMode = TextOverflowModes.Overflow;
            _labelText.raycastTarget = false;
        }

        private void ShowLabel(FloorLabel label)
        {
            _lastSequence = label.Sequence;
            _elapsed = 0f;
            _active = true;
            _labelText.text = ResolveText(label);
            _canvasGroup.alpha = 1f;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
        }

        private void UpdateFade()
        {
            if (_elapsed < _visibleSeconds)
            {
                _canvasGroup.alpha = 1f;
                return;
            }

            float fadeElapsed = _elapsed - _visibleSeconds;
            if (fadeElapsed >= _fadeSeconds)
            {
                HideImmediate();
                return;
            }

            _canvasGroup.alpha = 1f - fadeElapsed / _fadeSeconds;
        }

        private void HideImmediate()
        {
            _active = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private static string ResolveText(FloorLabel label)
        {
            switch (label.Kind)
            {
                case FloorLabelKind.GroundFloor:
                    int groundIdx = label.Index - 1;
                    return groundIdx >= 0 && groundIdx < GroundFloorTexts.Length
                        ? GroundFloorTexts[groundIdx]
                        : FullViewText;
                case FloorLabelKind.AirLayer:
                    int airIdx = label.Index - 1;
                    return airIdx >= 0 && airIdx < AirLayerTexts.Length
                        ? AirLayerTexts[airIdx]
                        : FullViewText;
                default:
                    return FullViewText;
            }
        }
    }
}
