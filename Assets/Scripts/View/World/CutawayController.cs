using Murim.Core.Valley;
using UnityEngine;

namespace Murim.View.World
{
    /// <summary>
    /// Controls the global floor-cutaway clip heights and alpha-to-coverage fade globals per §3.4.
    /// </summary>
    public sealed class CutawayController : MonoBehaviour
    {
        public const float MaxFrameDeltaSeconds = 0.1f;
        private const float ShaderInfinityHeight = 10000f;

        private static readonly int CutYId = Shader.PropertyToID("_MurimCutY");
        private static readonly int CutYFromId = Shader.PropertyToID("_MurimCutYFrom");
        private static readonly int CutFadeId = Shader.PropertyToID("_MurimCutFade");

        private float _cutFadeSeconds = PresentationTuning.DefaultCutFadeSeconds;

        public float CutY { get; private set; } = ValleyConstants.FullViewCutY;
        public float CutYFrom { get; private set; } = ValleyConstants.FullViewCutY;
        public float CutFade { get; private set; } = 1f;
        public float CutFadeSeconds => _cutFadeSeconds;

        private void Awake()
        {
            PushShaderGlobals();
        }

        private void Update()
        {
            Step(Time.unscaledDeltaTime);
        }

        public void Initialize(float cutFadeSeconds)
        {
            _cutFadeSeconds = Mathf.Max(cutFadeSeconds, PresentationTuning.MinCutFadeSeconds);
            CutY = ValleyConstants.FullViewCutY;
            CutYFrom = ValleyConstants.FullViewCutY;
            CutFade = 1f;
            PushShaderGlobals();
        }

        public void SetCut(float cutY)
        {
            if (Mathf.Approximately(cutY, CutY) || (float.IsPositiveInfinity(cutY) && float.IsPositiveInfinity(CutY)))
            {
                if (CutFade >= 1f)
                {
                    return;
                }
            }

            if (CutFade < 1f)
            {
                CutYFrom = CutY;
                CutFade = 1f;
            }

            CutYFrom = CutY;
            CutY = cutY;
            CutFade = 0f;
            PushShaderGlobals();
        }

        public void Step(float unscaledDeltaTime)
        {
            if (CutFade >= 1f)
            {
                return;
            }

            float dt = Mathf.Clamp(unscaledDeltaTime, 0f, MaxFrameDeltaSeconds);
            if (dt <= 0f)
            {
                return;
            }

            CutFade = Mathf.Clamp01(CutFade + dt / _cutFadeSeconds);
            if (CutFade >= 1f)
            {
                CutFade = 1f;
                CutYFrom = CutY;
            }
            PushShaderGlobals();
        }

        private void PushShaderGlobals()
        {
            Shader.SetGlobalFloat(CutYId, ToShaderHeight(CutY));
            Shader.SetGlobalFloat(CutYFromId, ToShaderHeight(CutYFrom));
            Shader.SetGlobalFloat(CutFadeId, CutFade);
        }

        private static float ToShaderHeight(float height)
        {
            return float.IsPositiveInfinity(height) ? ShaderInfinityHeight : height;
        }
    }
}
