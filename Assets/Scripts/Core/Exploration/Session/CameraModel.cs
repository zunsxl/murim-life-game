using System;
using Murim.Core.Tuning;
using Murim.Core.Valley;

namespace Murim.Core.Exploration.Session
{
    /// <summary>
    /// Held pan-direction input for one frame.
    /// </summary>
    public readonly struct PanInput
    {
        public readonly int Left;
        public readonly int Right;
        public readonly int Up;
        public readonly int Down;

        public PanInput(int left, int right, int up, int down)
        {
            Left = left;
            Right = right;
            Up = up;
            Down = down;
        }

        public PanInput(bool left, bool right, bool up, bool down)
            : this(left ? 1 : 0, right ? 1 : 0, up ? 1 : 0, down ? 1 : 0)
        {
        }
    }

    /// <summary>
    /// World-space ray computed by <see cref="CameraModel"/> for a viewport coordinate.
    /// </summary>
    public readonly struct CameraRay : IEquatable<CameraRay>
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Direction;

        public CameraRay(Vec3 origin, Vec3 direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public bool Equals(CameraRay other)
        {
            return Origin.Equals(other.Origin) && Direction.Equals(other.Direction);
        }

        public override bool Equals(object obj) => obj is CameraRay other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Origin, Direction);
    }

    /// <summary>
    /// Fixed-angle 45-degree orthographic camera model and perimeter clamping per spec §3.5.
    /// </summary>
    public sealed class CameraModel
    {
        public const float CameraBackDistance = 120f;
        public const float MaxFrameDeltaSeconds = 0.1f;
        public const float DefaultAspect = 16f / 9f;

        private const float MaxFocusZSpan = ValleyConstants.GridDepth + ValleyConstants.NorthBackRowHeight;

        private readonly ExplorationTuning _tuning;

        public Vec2 Focus { get; private set; }
        public float OrthoSize { get; private set; }
        public bool IsDragging { get; private set; }

        public Vec3 CameraPosition => new Vec3(
            Focus.X,
            CameraBackDistance * ValleyConstants.Sin45,
            Focus.Y - CameraBackDistance * ValleyConstants.Cos45);

        public static Vec3 CameraForward => new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);

        public CameraModel()
            : this(ExplorationTuning.Default, DefaultAspect)
        {
        }

        public CameraModel(ExplorationTuning tuning, float aspect = DefaultAspect)
        {
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Reset(aspect);
        }

        public void Clamp(float aspect)
        {
            ValidateAspect(aspect);

            float maxByWidth = ValleyConstants.GridWidth / (2f * aspect);
            float maxByDepth = ValleyConstants.GridDepth / (2f * ValleyConstants.Sqrt2);
            float maxOrtho = MathF.Min(_tuning.MaxOrthoSize, MathF.Min(maxByWidth, maxByDepth));
            if (maxOrtho < _tuning.MinOrthoSize)
            {
                maxOrtho = _tuning.MinOrthoSize;
            }

            OrthoSize = MathF.Min(MathF.Max(OrthoSize, _tuning.MinOrthoSize), maxOrtho);

            float halfH = OrthoSize;
            float halfW = OrthoSize * aspect;
            float focusX = ClampAxis(Focus.X, halfW, ValleyConstants.GridWidth - halfW, ValleyConstants.GridWidth * 0.5f);

            float halfZ = halfH * ValleyConstants.Sqrt2;
            float focusZ = ClampAxis(Focus.Y, halfZ, MaxFocusZSpan - halfZ, MaxFocusZSpan * 0.5f);

            Focus = new Vec2(focusX, focusZ);
        }

        public void PanKeys(PanInput input, float dt, float aspect)
        {
            ValidateAspect(aspect);
            if (IsDragging || !(dt > 0f))
            {
                return;
            }

            int dx = (input.Right != 0 ? 1 : 0) - (input.Left != 0 ? 1 : 0);
            int dy = (input.Up != 0 ? 1 : 0) - (input.Down != 0 ? 1 : 0);
            if (dx == 0 && dy == 0)
            {
                return;
            }

            float dirX = dx;
            float dirY = dy;
            if (dx != 0 && dy != 0)
            {
                float invSqrt2 = 1f / ValleyConstants.Sqrt2;
                dirX *= invSqrt2;
                dirY *= invSqrt2;
            }

            float clampedDt = MathF.Min(dt, MaxFrameDeltaSeconds);
            float speed = _tuning.PanSpeed * 2f * OrthoSize;
            Focus = new Vec2(
                Focus.X + dirX * speed * clampedDt,
                Focus.Y + dirY * speed * clampedDt * ValleyConstants.Sqrt2);
            Clamp(aspect);
        }

        public void BeginDrag()
        {
            IsDragging = true;
        }

        public void Drag(float dxPx, float dyPx, float screenHeightPx, float aspect)
        {
            ValidateAspect(aspect);
            if (!(screenHeightPx > 0f) || float.IsNaN(dxPx) || float.IsNaN(dyPx))
            {
                return;
            }

            float mpp = 2f * OrthoSize / screenHeightPx;
            Focus = new Vec2(
                Focus.X - dxPx * mpp,
                Focus.Y - dyPx * mpp * ValleyConstants.Sqrt2);
            Clamp(aspect);
        }

        public void EndDrag()
        {
            IsDragging = false;
        }

        public void ZoomNotch(int direction, float aspect)
        {
            ValidateAspect(aspect);
            if (direction == 0)
            {
                return;
            }

            OrthoSize *= direction > 0 ? 1f / _tuning.ZoomStep : _tuning.ZoomStep;
            Clamp(aspect);
        }

        public void CenterOnPlaceholder(Vec3 placeholderPos, float aspect)
        {
            ValidateAspect(aspect);
            Focus = new Vec2(placeholderPos.X, placeholderPos.Z + placeholderPos.Y);
            Clamp(aspect);
        }

        public void Reset(float aspect)
        {
            ValidateAspect(aspect);
            Focus = new Vec2(_tuning.DefaultFocusX, _tuning.DefaultFocusZ);
            OrthoSize = _tuning.DefaultOrthoSize;
            Clamp(aspect);
        }

        public CameraRay ViewportPointToRay(float viewportX, float viewportY, float aspect)
        {
            ValidateAspect(aspect);

            float offsetX = (viewportX - 0.5f) * 2f * OrthoSize * aspect;
            float offsetY = (viewportY - 0.5f) * 2f * OrthoSize;
            Vec3 camPos = CameraPosition;
            Vec3 origin = new Vec3(
                camPos.X + offsetX,
                camPos.Y + offsetY * ValleyConstants.Cos45,
                camPos.Z + offsetY * ValleyConstants.Sin45);
            return new CameraRay(origin, CameraForward);
        }

        public Vec2 WorldToScreenOffset(Vec3 worldPoint)
        {
            float screenX = worldPoint.X - Focus.X;
            float screenY = (worldPoint.Z + worldPoint.Y - Focus.Y) * ValleyConstants.Cos45;
            return new Vec2(screenX, screenY);
        }

        private static float ClampAxis(float value, float min, float max, float fallbackCenter)
        {
            return min <= max
                ? MathF.Min(MathF.Max(value, min), max)
                : fallbackCenter;
        }

        private static void ValidateAspect(float aspect)
        {
            if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(aspect), "Aspect ratio must be positive and finite.");
            }
        }
    }
}
