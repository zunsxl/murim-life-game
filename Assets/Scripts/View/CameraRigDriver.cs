using Murim.Core.Camera;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Murim.View
{
    /// <summary>
    /// Samples Input System devices in <c>LateUpdate</c>, advances <see cref="CameraRig"/>,
    /// and applies pose, backdrop scale, and terrain coarse blend (§3.4.2, §3.4.4).
    /// </summary>
    public sealed class CameraRigDriver : MonoBehaviour
    {
        private static readonly int CoarseBlendId = Shader.PropertyToID("_MurimCoarseBlend");

        private Camera _camera;
        private Transform _cameraTransform;
        private Transform _backdropTransform;
        private float _lastAppliedH = -1f;
        private float _lastBackdropH = -1f;
        private float _lastBackdropAspect = -1f;

        public CameraRig Rig { get; private set; }
        public bool Blocked { get; set; }

        public void Initialize(Camera targetCamera, Transform backdropTransform, CameraRig rig)
        {
            _camera = targetCamera;
            _cameraTransform = targetCamera != null ? targetCamera.transform : null;
            _backdropTransform = backdropTransform;
            Rig = rig;
            _lastAppliedH = -1f;
            _lastBackdropH = -1f;
            _lastBackdropAspect = -1f;

            if (InputSystem.settings != null
                && InputSystem.settings.scrollDeltaBehavior
                    != InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms)
            {
                InputSystem.settings.scrollDeltaBehavior =
                    InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            }

            if (Rig != null && _camera != null)
            {
                ApplyPose(Rig.Pose, ResolveAspect());
            }
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Rig != null)
            {
                Rig.OnFocusLost();
            }
        }

        public void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && Rig != null)
            {
                Rig.OnApplicationPaused();
            }
        }

        private void LateUpdate()
        {
            if (Rig == null || _camera == null)
            {
                return;
            }

            SampleInput(out CameraInput input);
            Rig.Tick(in input, Time.unscaledDeltaTime);
            ApplyPose(Rig.Pose, input.Aspect);
        }

        private void SampleInput(out CameraInput input)
        {
            input = new CameraInput
            {
                Aspect = ResolveAspect(),
                ScreenHeightPx = ResolveScreenHeightPx(),
                Blocked = Blocked,
            };

            SampleKeyboard(ref input);
            SampleMouse(ref input);
        }

        private static void SampleKeyboard(ref CameraInput input)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            input.PanNorth = kb.wKey.isPressed || kb.upArrowKey.isPressed;
            input.PanSouth = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            input.PanWest = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            input.PanEast = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
            input.ResetPressed = kb.homeKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame;
        }

        private static void SampleMouse(ref CameraInput input)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            input.DragHeld = mouse.rightButton.isPressed;
            input.ScrollNotches = mouse.scroll.y.ReadValue();

            Vector2 pos = mouse.position.ReadValue();
            int widthPx = Screen.width;
            int heightPx = Screen.height;
            if (widthPx > 0 && heightPx > 0)
            {
                input.PointerInWindow = pos.x >= 0f
                    && pos.x <= widthPx
                    && pos.y >= 0f
                    && pos.y <= heightPx;
                input.PointerNdcX = (pos.x / widthPx) * 2f - 1f;
                input.PointerNdcY = (pos.y / heightPx) * 2f - 1f;
            }
        }

        private float ResolveAspect()
        {
            if (_camera != null && float.IsFinite(_camera.aspect) && _camera.aspect > 0f)
            {
                return _camera.aspect;
            }

            int h = Screen.height;
            return h > 0 ? Screen.width / (float)h : 16f / 9f;
        }

        private static int ResolveScreenHeightPx()
        {
            int h = Screen.height;
            return h > 0 ? h : 1080;
        }

        private void ApplyPose(in CameraPose pose, float aspect)
        {
            if (_cameraTransform != null)
            {
                _cameraTransform.position = new Vector3(pose.Cu, pose.Cv, GameBootstrap.CameraZ);
            }

            _camera.orthographicSize = pose.H * 0.5f;

            if (_backdropTransform != null
                && (!Mathf.Approximately(_lastBackdropH, pose.H)
                    || !Mathf.Approximately(_lastBackdropAspect, aspect)))
            {
                _backdropTransform.localScale = new Vector3(pose.H * aspect, pose.H, 1f);
                _lastBackdropH = pose.H;
                _lastBackdropAspect = aspect;
            }

            if (!Mathf.Approximately(_lastAppliedH, pose.H))
            {
                float coarseBlend = Mathf.Clamp01((pose.H - 30f) / 40f) * 0.5f;
                Shader.SetGlobalFloat(CoarseBlendId, coarseBlend);
                _lastAppliedH = pose.H;
            }
        }
    }
}
