using System;
using Murim.Core.Exploration.Session;
using Murim.View.Exploration;
using Murim.View.World;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Murim.View.UI
{
    /// <summary>
    /// Controls the centred Esc system menu panel (ART-019) and buttons (ART-020) per §3.6 and §3.8.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        public const float PanelWidth = 420f;
        public const float PanelHeight = 340f;
        public const float ButtonWidth = 320f;
        public const float ButtonHeight = 64f;
        public const float ButtonGap = 16f;
        public const float ButtonFontSize = 28f;

        private static readonly Color ButtonTextColor = new Color(0xF2 / 255f, 0xEE / 255f, 0xE6 / 255f, 1f);
        private static readonly Action DefaultQuit = Application.Quit;

        [SerializeField] private Image _panelImage;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _resetViewButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private TMP_FontAsset _fontAsset;

        private ExplorationSession _session;
        private CameraRig _cameraRig;
        private UnityAction _resumeAction;
        private UnityAction _resetViewAction;
        private UnityAction _quitAction;
        private bool _listenersBound;
        private bool _initialized;

        public Action QuitRequested { get; set; } = DefaultQuit;
        public bool IsOpen => gameObject.activeSelf;
        public RectTransform PanelRectTransform => transform as RectTransform;
        public Button ResumeButton => _resumeButton;
        public Button ResetViewButton => _resetViewButton;
        public Button QuitButton => _quitButton;

        private void OnDestroy()
        {
            UnbindListeners();
        }

        public void Initialize(ArtCatalog catalog, ExplorationSession session, CameraRig cameraRig)
        {
            _session = session;
            _cameraRig = cameraRig;

            if (!_initialized)
            {
                EnsureHierarchy(catalog);
                EnsureDelegates();
                BindListeners();
                _initialized = true;
            }
            else
            {
                ApplyArt(catalog);
            }

            CloseImmediate();
        }

        public static void ConfigureUiInputModule(InputSystemUIInputModule uiModule)
        {
            if (uiModule == null)
            {
                return;
            }

            if (uiModule.actionsAsset == null && uiModule.leftClick == null)
            {
                uiModule.AssignDefaultActions();
            }

            uiModule.move = null;
            uiModule.submit = null;
            uiModule.cancel = null;
        }

        public void Open()
        {
            if (_session != null && !_session.Paused)
            {
                _session.OpenMenu();
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            ClearEventSystemSelection();
        }

        public void Close()
        {
            if (_session != null && _session.Paused)
            {
                _session.Resume();
            }

            CloseImmediate();
        }

        public void Toggle()
        {
            if (_session != null)
            {
                if (_session.Paused)
                {
                    Close();
                }
                else
                {
                    Open();
                }
                return;
            }

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void SyncFromSession(ExplorationSession session)
        {
            if (session == null)
            {
                return;
            }

            if (session.Paused && !gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                ClearEventSystemSelection();
            }
            else if (!session.Paused && gameObject.activeSelf)
            {
                CloseImmediate();
            }
        }

        private void EnsureHierarchy(ArtCatalog catalog)
        {
            gameObject.layer = Layers.UiLayer;

            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null)
            {
                rt = gameObject.AddComponent<RectTransform>();
            }
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;

            if (_panelImage == null)
            {
                _panelImage = GetComponent<Image>();
                if (_panelImage == null)
                {
                    _panelImage = gameObject.AddComponent<Image>();
                }
            }
            _panelImage.type = Image.Type.Sliced;
            _panelImage.color = Color.white;
            _panelImage.raycastTarget = true;

            if (_fontAsset == null)
            {
                _fontAsset = Resources.Load<TMP_FontAsset>(FloorLabelView.DefaultFontResourcePath);
            }

            float stepY = ButtonHeight + ButtonGap;
            _resumeButton = EnsureButton(_resumeButton, "ResumeButton", "Resume", stepY, rt);
            _resetViewButton = EnsureButton(_resetViewButton, "ResetViewButton", "Reset View", 0f, rt);
            _quitButton = EnsureButton(_quitButton, "QuitButton", "Quit", -stepY, rt);

            ApplyArt(catalog);
        }

        private Button EnsureButton(
            Button existing,
            string objectName,
            string labelText,
            float anchoredY,
            RectTransform parentRt)
        {
            Button btn = existing;
            if (btn == null)
            {
                Transform child = parentRt.Find(objectName);
                btn = child != null ? child.GetComponent<Button>() : null;
            }

            if (btn == null)
            {
                GameObject go = new GameObject(objectName, typeof(RectTransform));
                go.layer = Layers.UiLayer;
                go.transform.SetParent(parentRt, false);
                go.AddComponent<Image>();
                btn = go.AddComponent<Button>();
            }

            ConfigureButtonTransformAndNavigation(btn, anchoredY);
            EnsureButtonLabel(btn, labelText);
            return btn;
        }

        private static void ConfigureButtonTransformAndNavigation(Button btn, float anchoredY)
        {
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rt.anchoredPosition = new Vector2(0f, anchoredY);
            rt.localScale = Vector3.one;

            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.transition = Selectable.Transition.SpriteSwap;
        }

        private void EnsureButtonLabel(Button btn, string labelText)
        {
            TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null)
            {
                GameObject textGo = new GameObject("Label", typeof(RectTransform));
                textGo.layer = Layers.UiLayer;
                textGo.transform.SetParent(btn.transform, false);
                tmp = textGo.AddComponent<TextMeshProUGUI>();
            }

            RectTransform textRt = tmp.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = new Vector2(-12f, 0f);
            textRt.localScale = Vector3.one;

            if (_fontAsset != null)
            {
                tmp.font = _fontAsset;
            }
            tmp.text = labelText;
            tmp.fontSize = ButtonFontSize;
            tmp.color = ButtonTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
        }

        private void ApplyArt(ArtCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            if (_panelImage != null)
            {
                _panelImage.sprite = catalog.GetSprite(ArtAssetId.UiPanel);
            }

            Sprite normal = catalog.GetSprite(ArtAssetId.UiButtonNormal);
            Sprite hover = catalog.GetSprite(ArtAssetId.UiButtonHover);
            Sprite pressed = catalog.GetSprite(ArtAssetId.UiButtonPressed);

            ApplyButtonSprites(_resumeButton, normal, hover, pressed);
            ApplyButtonSprites(_resetViewButton, normal, hover, pressed);
            ApplyButtonSprites(_quitButton, normal, hover, pressed);
        }

        private static void ApplyButtonSprites(Button btn, Sprite normal, Sprite hover, Sprite pressed)
        {
            if (btn == null)
            {
                return;
            }

            Image img = btn.targetGraphic as Image ?? btn.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = normal;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                btn.targetGraphic = img;
            }

            SpriteState states = btn.spriteState;
            states.highlightedSprite = hover;
            states.pressedSprite = pressed;
            states.selectedSprite = normal;
            states.disabledSprite = normal;
            btn.spriteState = states;
        }

        private void EnsureDelegates()
        {
            if (_resumeAction == null)
            {
                _resumeAction = OnResumeClicked;
                _resetViewAction = OnResetViewClicked;
                _quitAction = OnQuitClicked;
            }
        }

        private void BindListeners()
        {
            if (_listenersBound)
            {
                return;
            }

            if (_resumeButton != null)
            {
                _resumeButton.onClick.AddListener(_resumeAction);
            }
            if (_resetViewButton != null)
            {
                _resetViewButton.onClick.AddListener(_resetViewAction);
            }
            if (_quitButton != null)
            {
                _quitButton.onClick.AddListener(_quitAction);
            }
            _listenersBound = true;
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            if (_resumeButton != null && _resumeAction != null)
            {
                _resumeButton.onClick.RemoveListener(_resumeAction);
            }
            if (_resetViewButton != null && _resetViewAction != null)
            {
                _resetViewButton.onClick.RemoveListener(_resetViewAction);
            }
            if (_quitButton != null && _quitAction != null)
            {
                _quitButton.onClick.RemoveListener(_quitAction);
            }
            _listenersBound = false;
        }

        private void OnResumeClicked()
        {
            Close();
        }

        private void OnResetViewClicked()
        {
            if (_session != null)
            {
                float aspect = _cameraRig != null ? _cameraRig.CurrentAspect : CameraModel.DefaultAspect;
                _session.ResetViewFromMenu(aspect);
            }

            CloseImmediate();
        }

        private void OnQuitClicked()
        {
            ClearEventSystemSelection();
            QuitRequested?.Invoke();
        }

        private void CloseImmediate()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
            ClearEventSystemSelection();
        }

        private static void ClearEventSystemSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
