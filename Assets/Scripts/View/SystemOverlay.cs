using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Murim.View
{
    /// <summary>
    /// Minimal Esc pause/quit system overlay blocking world camera inputs while open (§3.5.3).
    /// </summary>
    public sealed class SystemOverlay : MonoBehaviour
    {
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private Image scrimImage;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private EventSystem ownedEventSystem;

        private CameraRigDriver _cameraRigDriver;
        private bool _hasExternalEventSystem;

        public static Action QuitAction { get; set; } = Application.Quit;

        public bool IsOpen { get; private set; }
        public Canvas OverlayCanvas => overlayCanvas;
        public Image ScrimImage => scrimImage;
        public RectTransform PanelRect => panelRect;
        public TMP_Text TitleText => titleText;
        public Button ResumeButton => resumeButton;
        public Button QuitButton => quitButton;

        private void Awake()
        {
            ApplyVisualState(false);
        }

        private void OnEnable()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(HandleResumeClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(HandleQuitClicked);
            }
        }

        private void OnDisable()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(HandleResumeClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(HandleQuitClicked);
            }
        }

        public void Initialize(CameraRigDriver cameraRigDriver)
        {
            _cameraRigDriver = cameraRigDriver;
            EnsureSingleEventSystem();
            SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            ApplyVisualState(open);

            if (_cameraRigDriver != null)
            {
                _cameraRigDriver.Blocked = open;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(!IsOpen);
            }
        }

        private void ApplyVisualState(bool open)
        {
            EventSystem current = EventSystem.current;
            if (current != null)
            {
                current.SetSelectedGameObject(null);
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = open;
            }

            if (scrimImage != null)
            {
                scrimImage.gameObject.SetActive(open);
            }

            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(open);
            }

            if (!_hasExternalEventSystem && ownedEventSystem != null)
            {
                ownedEventSystem.gameObject.SetActive(open);
            }
        }

        private void EnsureSingleEventSystem()
        {
            EventSystem[] systems = FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i] != ownedEventSystem)
                {
                    _hasExternalEventSystem = true;
                    if (ownedEventSystem != null)
                    {
                        Destroy(ownedEventSystem.gameObject);
                        ownedEventSystem = null;
                    }

                    return;
                }
            }

            _hasExternalEventSystem = false;
        }

        private void HandleResumeClicked()
        {
            SetOpen(false);
        }

        private void HandleQuitClicked()
        {
            QuitAction?.Invoke();
        }
    }
}
