using UnityEngine;

namespace Murim.View.World
{
    /// <summary>
    /// Advances _MurimAmbientTime using clamped unscaled frame time while AmbientRunning is true per §3.7.
    /// </summary>
    public sealed class AmbientClock : MonoBehaviour
    {
        public const float MaxFrameDeltaSeconds = 0.1f;

        private static readonly int AmbientTimeId = Shader.PropertyToID("_MurimAmbientTime");

        public bool AmbientRunning { get; set; } = true;
        public float AmbientTime { get; private set; }

        private void Awake()
        {
            Shader.SetGlobalFloat(AmbientTimeId, AmbientTime);
        }

        private void Update()
        {
            Step(Time.unscaledDeltaTime);
        }

        public void Step(float unscaledDeltaTime)
        {
            if (!AmbientRunning)
            {
                return;
            }

            float dt = Mathf.Clamp(unscaledDeltaTime, 0f, MaxFrameDeltaSeconds);
            if (dt <= 0f)
            {
                return;
            }

            AmbientTime += dt;
            Shader.SetGlobalFloat(AmbientTimeId, AmbientTime);
        }

        public void ResetClock(float time = 0f)
        {
            AmbientTime = Mathf.Max(0f, time);
            Shader.SetGlobalFloat(AmbientTimeId, AmbientTime);
        }
    }
}
