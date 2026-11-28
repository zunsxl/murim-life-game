using System;
using Murim.Core.Exploration.Session;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Applies Core CameraModel state to the fixed 45-degree orthographic camera per §3.5.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public const float CameraHeight = 84.85f;
        public const float CameraSouthOffset = 84.85f;
        public const float NearPlane = 1f;
        public const float FarPlane = 300f;
        public static readonly Vector3 FixedRotationEuler = new Vector3(45f, 0f, 0f);
        public static readonly int CameraCullingMask = (1 << Layers.WorldLayer) | (1 << Layers.OverlayLayer);

        [SerializeField] private Camera _camera;

        private UniversalAdditionalCameraData _urpCameraData;
        private OverlayRenderPass _overlayPass;
        private Action<ScriptableRenderContext, Camera> _beginCameraHandler;

        public Camera Camera => _camera;

        public float CurrentAspect
        {
            get
            {
                if (_camera != null && _camera.pixelHeight > 0)
                {
                    return (float)_camera.pixelWidth / _camera.pixelHeight;
                }

                if (Screen.height > 0)
                {
                    return (float)Screen.width / Screen.height;
                }

                return CameraModel.DefaultAspect;
            }
        }

        private void Awake()
        {
            EnsureConfigured();
        }

        private void OnEnable()
        {
            EnsureConfigured();
            RenderPipelineManager.beginCameraRendering += _beginCameraHandler;
        }

        private void OnDisable()
        {
            if (_beginCameraHandler != null)
            {
                RenderPipelineManager.beginCameraRendering -= _beginCameraHandler;
            }
        }

        public void EnsureConfigured()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
                if (_camera == null)
                {
                    _camera = gameObject.AddComponent<Camera>();
                }
            }

            _camera.orthographic = true;
            _camera.nearClipPlane = NearPlane;
            _camera.farClipPlane = FarPlane;
            _camera.cullingMask = CameraCullingMask;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.allowMSAA = true;

            transform.rotation = Quaternion.Euler(FixedRotationEuler);

            if (_urpCameraData == null)
            {
                _urpCameraData = _camera.GetComponent<UniversalAdditionalCameraData>();
                if (_urpCameraData == null)
                {
                    _urpCameraData = _camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }
            }
            _urpCameraData.renderPostProcessing = false;
            _urpCameraData.antialiasing = AntialiasingMode.None;

            if (_overlayPass == null)
            {
                _overlayPass = new OverlayRenderPass();
            }

            if (_beginCameraHandler == null)
            {
                _beginCameraHandler = OnBeginCameraRendering;
            }
        }

        public void Apply(CameraModel model)
        {
            if (model == null || _camera == null)
            {
                return;
            }

            _camera.orthographicSize = model.OrthoSize;
            transform.position = new Vector3(
                model.Focus.X,
                CameraHeight,
                model.Focus.Y - CameraSouthOffset);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (ReferenceEquals(cam, _camera) && _urpCameraData != null && _overlayPass != null)
            {
                _urpCameraData.scriptableRenderer?.EnqueuePass(_overlayPass);
            }
        }
    }
}
