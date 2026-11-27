using System;

namespace Murim.Core.Camera
{
    public enum CameraMode
    {
        Free = 0,
        Resetting = 1,
    }

    /// <summary>
    /// Pure frame-stepped camera rig state machine operating in oblique screen-plane coordinates (§3.4).
    /// </summary>
    public sealed class CameraRig
    {
        private const float MaxDeltaTimeS = 0.1f;
        private const float MaxScrollNotches = 4f;
        private const float DefaultAspect = 16f / 9f;
        private const int DefaultScreenHeightPx = 1080;
        private const float DragVelocitySmoothTimeS = 0.06f;
        private const float DragReleaseSpeedMultiplier = 2f;
        private const float ResetSnapFraction = 0.002f;
        private const float ZoomSnapFraction = 0.001f;
        private const float PanStopSnapFraction = 0.001f;

        [Flags]
        private enum HeldInputMask
        {
            None = 0,
            North = 1 << 0,
            South = 1 << 1,
            West = 1 << 2,
            East = 1 << 3,
            Drag = 1 << 4,
            PanMask = North | South | West | East,
        }

        private readonly ViewFrame _frame;
        private readonly CameraConfig _config;

        private float _cu;
        private float _cv;
        private float _h;
        private float _ht;
        private float _vu;
        private float _vv;
        private CameraMode _mode;

        private bool _dragActive;
        private float _grabU;
        private float _grabV;
        private float _dragVu;
        private float _dragVv;

        private bool _anchorSet;
        private float _anchorU;
        private float _anchorV;
        private float _anchorSx;
        private float _anchorSy;

        private HeldInputMask _suppressed;
        private HeldInputMask _prevEffective;
        private HeldInputMask _lastHeld;
        private bool _prevBlocked;
        private bool _hasTicked;
        private bool _prevPointerInWindow;

        public CameraRig(ViewFrame frame, CameraConfig config)
            : this(frame, config, DefaultAspect, DefaultScreenHeightPx)
        {
        }

        public CameraRig(ViewFrame frame, CameraConfig config, float initialAspect, int initialScreenHeightPx)
        {
            _frame = frame;
            _config = config ?? throw new ArgumentNullException(nameof(config));

            float aspect = SanitizeAspect(initialAspect);
            float hMinEff = ComputeHMinEff(initialScreenHeightPx);
            float hMaxEff = ComputeHMaxEff(aspect, hMinEff);

            _cu = _frame.VistaU;
            _cv = _frame.VistaV;
            _h = Clamp(_config.DefaultViewHeightM, hMinEff, hMaxEff);
            _ht = _h;
            _mode = CameraMode.Free;

            ApplyBounds(aspect, hMinEff, hMaxEff);
            Pose = new CameraPose(_cu, _cv, _h);
        }

        public CameraPose Pose { get; private set; }
        public float Cu => Pose.Cu;
        public float Cv => Pose.Cv;
        public float H => Pose.H;
        public float TargetViewHeight => _ht;
        public float Vu => _vu;
        public float Vv => _vv;
        public CameraMode Mode => _mode;
        public bool IsDragging => _dragActive;
        public bool HasZoomAnchor => _anchorSet;

        public float ComputeHMinEff(int screenHeightPx)
        {
            return MathF.Max(_config.MinViewHeightM, screenHeightPx / ViewFrame.MaxPixelsPerMetre);
        }

        public float ComputeHMaxEff(float aspect, float hMinEff)
        {
            float safeAspect = SanitizeAspect(aspect);
            float vSpanCap = (_frame.CrestV - _frame.BottomV) / (1f - ViewFrame.SkyFraction);
            float hCap = MathF.Min(ViewFrame.WorldWidthM / safeAspect, vSpanCap);
            return MathF.Max(hMinEff, MathF.Min(_config.MaxViewHeightM, hCap));
        }

        public void OnFocusLost()
        {
            CancelAll();
        }

        public void OnApplicationPaused()
        {
            CancelAll();
        }

        public void CancelAll()
        {
            ResetMotionState();
            _suppressed |= _lastHeld;
            _prevEffective = HeldInputMask.None;
        }

        public void Tick(in CameraInput input, float dt)
        {
            float clampedDt = float.IsFinite(dt) ? Clamp(dt, 0f, MaxDeltaTimeS) : 0f;
            float aspect = SanitizeAspect(input.Aspect);
            float hMinEff = ComputeHMinEff(input.ScreenHeightPx);
            float hMaxEff = ComputeHMaxEff(aspect, hMinEff);

            HeldInputMask currentHeld = ExtractHeldMask(in input);
            _suppressed &= currentHeld;

            if (input.Blocked)
            {
                HandleBlockedTick(currentHeld, input.PointerInWindow, aspect, hMinEff, hMaxEff);
                return;
            }

            _prevBlocked = false;
            HeldInputMask effective = currentHeld & ~_suppressed;
            HeldInputMask newInputs = effective & ~_prevEffective;
            ApplyPointerExitRule(currentHeld, input.PointerInWindow, ref effective, ref newInputs);

            float scrollNotches = SanitizeScroll(input.ScrollNotches);
            HandleResetLifecycle(in input, currentHeld, scrollNotches, ref effective, ref newInputs);
            ApplyScroll(scrollNotches, in input, aspect, hMinEff, hMaxEff);
            UpdateDragTransitions(effective, newInputs, in input, aspect);
            StepMotion(effective, clampedDt, hMinEff, hMaxEff);
            ApplyPointerConstraints(in input, aspect, clampedDt);
            ApplyBounds(aspect, hMinEff, hMaxEff);
            RecordTickTailAndPublish(currentHeld, effective, input.PointerInWindow);
        }

        private void HandleBlockedTick(
            HeldInputMask currentHeld,
            bool pointerInWindow,
            float aspect,
            float hMinEff,
            float hMaxEff)
        {
            if (!_prevBlocked)
            {
                CancelFromTick(currentHeld);
                _prevBlocked = true;
            }

            ApplyBounds(aspect, hMinEff, hMaxEff);
            RecordTickTailAndPublish(currentHeld, HeldInputMask.None, pointerInWindow);
        }

        private void ApplyPointerExitRule(
            HeldInputMask currentHeld,
            bool pointerInWindow,
            ref HeldInputMask effective,
            ref HeldInputMask newInputs)
        {
            bool pointerLeft = _hasTicked && _prevPointerInWindow && !pointerInWindow;
            bool hasActiveMotionInput = ((effective & HeldInputMask.PanMask) != 0) || _dragActive;
            if (pointerLeft && hasActiveMotionInput)
            {
                CancelFromTick(currentHeld);
                effective = HeldInputMask.None;
                newInputs = HeldInputMask.None;
            }
        }

        private void HandleResetLifecycle(
            in CameraInput input,
            HeldInputMask currentHeld,
            float scrollNotches,
            ref HeldInputMask effective,
            ref HeldInputMask newInputs)
        {
            if (input.ResetPressed)
            {
                ResetMotionState();
                _mode = CameraMode.Resetting;
                _suppressed |= currentHeld;
                effective = HeldInputMask.None;
                newInputs = HeldInputMask.None;
            }

            if (_mode != CameraMode.Resetting)
            {
                return;
            }

            bool hasNewPan = (newInputs & HeldInputMask.PanMask) != 0;
            bool hasNewDrag = (newInputs & HeldInputMask.Drag) != 0;
            if (hasNewPan || hasNewDrag || scrollNotches != 0f)
            {
                _mode = CameraMode.Free;
                _ht = _h;
            }
        }

        private void ApplyScroll(
            float scrollNotches,
            in CameraInput input,
            float aspect,
            float hMinEff,
            float hMaxEff)
        {
            if (scrollNotches == 0f)
            {
                return;
            }

            float step = _config.ZoomStepPct / 100f;
            float factor = scrollNotches > 0f
                ? 1f / (1f + step * scrollNotches)
                : 1f + step * (-scrollNotches);
            _ht = Clamp(_ht * factor, hMinEff, hMaxEff);

            if (input.PointerInWindow)
            {
                float sx = SanitizeFinite(input.PointerNdcX);
                float sy = SanitizeFinite(input.PointerNdcY);
                (float u, float v) = new CameraPose(_cu, _cv, _h).ScreenToPlane(aspect, sx, sy);
                _anchorSet = true;
                _anchorU = u;
                _anchorV = v;
                _anchorSx = sx;
                _anchorSy = sy;
            }
            else
            {
                _anchorSet = false;
            }
        }

        private void UpdateDragTransitions(
            HeldInputMask effective,
            HeldInputMask newInputs,
            in CameraInput input,
            float aspect)
        {
            bool newDrag = (newInputs & HeldInputMask.Drag) != 0;
            if (!_dragActive && newDrag && input.PointerInWindow)
            {
                _dragActive = true;
                _vu = 0f;
                _vv = 0f;
                _dragVu = 0f;
                _dragVv = 0f;
                _anchorSet = false;

                float sx = SanitizeFinite(input.PointerNdcX);
                float sy = SanitizeFinite(input.PointerNdcY);
                (_grabU, _grabV) = new CameraPose(_cu, _cv, _h).ScreenToPlane(aspect, sx, sy);
            }

            bool effectiveDrag = (effective & HeldInputMask.Drag) != 0;
            if (_dragActive && !effectiveDrag)
            {
                _dragActive = false;
                float maxSpeed = DragReleaseSpeedMultiplier * _config.PanSpeedPerViewHeight * _h;
                LimitVelocity(_dragVu, _dragVv, maxSpeed, out _vu, out _vv);
            }
        }

        private void StepMotion(HeldInputMask effective, float dt, float hMinEff, float hMaxEff)
        {
            if (_mode == CameraMode.Resetting)
            {
                StepResettingMotion(dt, hMinEff, hMaxEff);
                return;
            }

            float zoomAlpha = Alpha(_config.ZoomSmoothTimeS, dt);
            _h += (_ht - _h) * zoomAlpha;
            if (MathF.Abs(_ht - _h) <= ZoomSnapFraction * _h)
            {
                _h = _ht;
            }

            if (!_dragActive)
            {
                StepKeyboardPan(effective, dt);
            }
        }

        private void StepResettingMotion(float dt, float hMinEff, float hMaxEff)
        {
            float alpha = Alpha(_config.ResetSmoothTimeS, dt);
            _cu += (_frame.VistaU - _cu) * alpha;
            _cv += (_frame.VistaV - _cv) * alpha;
            _ht = Clamp(_config.DefaultViewHeightM, hMinEff, hMaxEff);
            _h += (_ht - _h) * alpha;

            float du = _frame.VistaU - _cu;
            float dv = _frame.VistaV - _cv;
            float snapTol = ResetSnapFraction * _h;
            if ((du * du + dv * dv) <= (snapTol * snapTol) && MathF.Abs(_ht - _h) <= snapTol)
            {
                _cu = _frame.VistaU;
                _cv = _frame.VistaV;
                _h = _ht;
                _mode = CameraMode.Free;
            }
        }

        private void StepKeyboardPan(HeldInputMask effective, float dt)
        {
            int axU = ((effective & HeldInputMask.East) != 0 ? 1 : 0)
                - ((effective & HeldInputMask.West) != 0 ? 1 : 0);
            int axV = ((effective & HeldInputMask.North) != 0 ? 1 : 0)
                - ((effective & HeldInputMask.South) != 0 ? 1 : 0);

            bool hasAxis = axU != 0 || axV != 0;
            float targetVu = 0f;
            float targetVv = 0f;

            if (hasAxis)
            {
                _anchorSet = false;
                float invLen = (axU != 0 && axV != 0) ? (1f / MathF.Sqrt(2f)) : 1f;
                float speed = _config.PanSpeedPerViewHeight * _h;
                targetVu = axU * invLen * speed;
                targetVv = axV * invLen * speed;
            }

            float panAlpha = Alpha(hasAxis ? _config.PanAccelTimeS : _config.PanStopTimeS, dt);
            _vu += (targetVu - _vu) * panAlpha;
            _vv += (targetVv - _vv) * panAlpha;
            _cu += _vu * dt;
            _cv += _vv * dt;

            float stopTol = PanStopSnapFraction * _h;
            if (!hasAxis && (_vu * _vu + _vv * _vv) < (stopTol * stopTol))
            {
                _vu = 0f;
                _vv = 0f;
            }
        }

        private void ApplyPointerConstraints(in CameraInput input, float aspect, float dt)
        {
            if (_mode != CameraMode.Free)
            {
                return;
            }

            if (_dragActive)
            {
                float sx = SanitizeFinite(input.PointerNdcX);
                float sy = SanitizeFinite(input.PointerNdcY);
                (float pu, float pv) = new CameraPose(_cu, _cv, _h).ScreenToPlane(aspect, sx, sy);
                float shiftU = _grabU - pu;
                float shiftV = _grabV - pv;
                _cu += shiftU;
                _cv += shiftV;

                if (dt > 0f)
                {
                    float dragAlpha = Alpha(DragVelocitySmoothTimeS, dt);
                    _dragVu += (shiftU / dt - _dragVu) * dragAlpha;
                    _dragVv += (shiftV / dt - _dragVv) * dragAlpha;
                }
            }
            else if (_anchorSet)
            {
                float w = _h * aspect;
                _cu = _anchorU - _anchorSx * (w * 0.5f);
                _cv = _anchorV - _anchorSy * (_h * 0.5f);
                if (_h == _ht)
                {
                    _anchorSet = false;
                }
            }
        }

        private void ApplyBounds(float aspect, float hMinEff, float hMaxEff)
        {
            _h = Clamp(_h, hMinEff, hMaxEff);
            _ht = Clamp(_ht, hMinEff, hMaxEff);

            float w = _h * aspect;
            float uMin = w * 0.5f;
            float uMax = ViewFrame.WorldWidthM - w * 0.5f;
            ClampAxis(ref _cu, ref _vu, uMin, uMax);

            float vMin = _frame.BottomV + _h * 0.5f;
            float vMax = _frame.CrestV + ViewFrame.SkyFraction * _h - _h * 0.5f;
            ClampAxis(ref _cv, ref _vv, vMin, vMax);
        }

        private void CancelFromTick(HeldInputMask currentHeld)
        {
            ResetMotionState();
            _suppressed |= currentHeld;
            _prevEffective = HeldInputMask.None;
        }

        private void ResetMotionState()
        {
            _vu = 0f;
            _vv = 0f;
            _dragActive = false;
            _dragVu = 0f;
            _dragVv = 0f;
            _anchorSet = false;
            _ht = _h;
            _mode = CameraMode.Free;
        }

        private void RecordTickTailAndPublish(
            HeldInputMask currentHeld,
            HeldInputMask effective,
            bool pointerInWindow)
        {
            _lastHeld = currentHeld;
            _prevEffective = effective;
            _prevPointerInWindow = pointerInWindow;
            _hasTicked = true;
            Pose = new CameraPose(_cu, _cv, _h);
        }

        private static HeldInputMask ExtractHeldMask(in CameraInput input)
        {
            HeldInputMask mask = HeldInputMask.None;
            if (input.PanNorth)
            {
                mask |= HeldInputMask.North;
            }

            if (input.PanSouth)
            {
                mask |= HeldInputMask.South;
            }

            if (input.PanWest)
            {
                mask |= HeldInputMask.West;
            }

            if (input.PanEast)
            {
                mask |= HeldInputMask.East;
            }

            if (input.DragHeld)
            {
                mask |= HeldInputMask.Drag;
            }

            return mask;
        }

        private static void ClampAxis(ref float pos, ref float vel, float min, float max)
        {
            if (min > max)
            {
                pos = 0.5f * (min + max);
                vel = 0f;
                return;
            }

            if (pos < min)
            {
                pos = min;
                vel = 0f;
            }
            else if (pos > max)
            {
                pos = max;
                vel = 0f;
            }
        }

        private static void LimitVelocity(float vu, float vv, float maxSpeed, out float outVu, out float outVv)
        {
            float speedSq = vu * vu + vv * vv;
            if (speedSq > maxSpeed * maxSpeed && speedSq > 0f)
            {
                float scale = maxSpeed / MathF.Sqrt(speedSq);
                outVu = vu * scale;
                outVv = vv * scale;
                return;
            }

            outVu = vu;
            outVv = vv;
        }

        private static float Alpha(float tau, float dt)
        {
            return dt / (tau + dt);
        }

        private static float SanitizeAspect(float aspect)
        {
            return float.IsFinite(aspect) && aspect > 0f ? aspect : DefaultAspect;
        }

        private static float SanitizeScroll(float notches)
        {
            return float.IsFinite(notches) ? Clamp(notches, -MaxScrollNotches, MaxScrollNotches) : 0f;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsFinite(value) ? value : 0f;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
