using System;
using Murim.Core.Exploration.Placeholder;
using Murim.Core.Tuning;
using Murim.Core.Valley;

namespace Murim.Core.Exploration.Session
{
    public enum FloorLabelKind : byte
    {
        FullView,
        GroundFloor,
        AirLayer
    }

    /// <summary>
    /// Polled transient floor/altitude label state emitted by <see cref="ExplorationSession"/>.
    /// </summary>
    public readonly struct FloorLabel : IEquatable<FloorLabel>
    {
        public readonly FloorLabelKind Kind;
        public readonly int Index;
        public readonly int Sequence;

        public FloorLabel(FloorLabelKind kind, int index, int sequence)
        {
            Kind = kind;
            Index = index;
            Sequence = sequence;
        }

        public bool Equals(FloorLabel other)
        {
            return Kind == other.Kind && Index == other.Index && Sequence == other.Sequence;
        }

        public override bool Equals(object obj) => obj is FloorLabel other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Index, Sequence);
    }

    /// <summary>
    /// Single Core rules surface for exploration: composes valley layout, placeholder,
    /// floor slicing view, camera model, pause/focus handling, and polled feedback per §3.4–§3.6.
    /// </summary>
    public sealed class ExplorationSession
    {
        private readonly PlaceholderController _placeholder;
        private int _labelSequence;

        private PanKeyState _panLeft;
        private PanKeyState _panRight;
        private PanKeyState _panUp;
        private PanKeyState _panDown;

        public ValleyLayout Layout { get; }
        public ExplorationTuning Tuning { get; }
        public FloorView FloorView { get; }
        public CameraModel Camera { get; }
        public PlaceholderState Placeholder => _placeholder.State;

        public bool Paused { get; private set; }
        public bool Focused { get; private set; }
        public bool AmbientRunning => !Paused;
        public bool CanSimulate => !Paused && Focused;

        public FloorLabel Label { get; private set; }
        public bool LabelVisible { get; private set; }
        public DestinationMarker Marker { get; private set; }
        public bool MarkerVisible { get; private set; }

        public ExplorationSession(ValleyLayout layout)
            : this(layout, ExplorationTuning.Default, CameraModel.DefaultAspect)
        {
        }

        public ExplorationSession(ValleyLayout layout, ExplorationTuning tuning, float aspect = CameraModel.DefaultAspect)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _placeholder = new PlaceholderController(layout, tuning);
            FloorView = new FloorView();
            Camera = new CameraModel(tuning, aspect);
            Focused = true;
        }

        public void Tick()
        {
            if (!CanSimulate)
            {
                return;
            }

            bool wasVisible = FloorView.IsPlaceholderVisible(_placeholder.State.LogicalBand);
            _placeholder.Tick();
            if (wasVisible)
            {
                FloorView.EnsureVisible(_placeholder.State.LogicalBand);
            }
        }

        public DestinationMarker Click(Vec3 rayOrigin, Vec3 rayDir)
        {
            if (!CanSimulate)
            {
                return Marker;
            }

            DestinationMarker marker = _placeholder.Click(rayOrigin, rayDir, FloorView.CutY);
            Marker = marker;
            MarkerVisible = true;
            return marker;
        }

        public DestinationMarker ClickViewport(float viewportX, float viewportY, float aspect)
        {
            if (!CanSimulate)
            {
                return Marker;
            }

            CameraRay ray = Camera.ViewportPointToRay(viewportX, viewportY, aspect);
            return Click(ray.Origin, ray.Direction);
        }

        public FloorLabel Ascend()
        {
            if (!CanSimulate)
            {
                return Label;
            }

            PlaceholderLabel pLabel = _placeholder.Ascend();
            FloorView.EnsureVisible(_placeholder.State.LogicalBand);
            return EmitPlaceholderLabel(pLabel);
        }

        public FloorLabel Descend()
        {
            if (!CanSimulate)
            {
                return Label;
            }

            PlaceholderLabel pLabel = _placeholder.Descend();
            FloorView.EnsureVisible(_placeholder.State.LogicalBand);
            return EmitPlaceholderLabel(pLabel);
        }

        public FloorLabel StepFloorUp()
        {
            if (!CanSimulate)
            {
                return Label;
            }

            return EmitViewedFloorLabel(FloorView.StepUp());
        }

        public FloorLabel StepFloorDown()
        {
            if (!CanSimulate)
            {
                return Label;
            }

            return EmitViewedFloorLabel(FloorView.StepDown());
        }

        public void CenterOnPlaceholder(float aspect)
        {
            if (!CanSimulate)
            {
                return;
            }

            PlaceholderState state = _placeholder.State;
            Camera.CenterOnPlaceholder(new Vec3(state.Position.X, state.Elevation, state.Position.Y), aspect);
            FloorView.EnsureVisible(state.LogicalBand);
        }

        public void ResetView(float aspect)
        {
            if (!CanSimulate)
            {
                return;
            }

            Camera.Reset(aspect);
            FloorView.Reset();
        }

        public void PanKeys(PanInput input, float dt, float aspect)
        {
            bool left = input.Left != 0;
            bool right = input.Right != 0;
            bool up = input.Up != 0;
            bool down = input.Down != 0;

            if (!CanSimulate)
            {
                _panLeft.TrackWhileInactive(left);
                _panRight.TrackWhileInactive(right);
                _panUp.TrackWhileInactive(up);
                _panDown.TrackWhileInactive(down);
                return;
            }

            PanInput armedInput = new PanInput(
                _panLeft.Update(left),
                _panRight.Update(right),
                _panUp.Update(up),
                _panDown.Update(down));
            Camera.PanKeys(armedInput, dt, aspect);
        }

        public void BeginDrag()
        {
            if (!CanSimulate)
            {
                return;
            }

            Camera.BeginDrag();
        }

        public void Drag(float dxPx, float dyPx, float screenHeightPx, float aspect)
        {
            if (!CanSimulate || !Camera.IsDragging)
            {
                return;
            }

            Camera.Drag(dxPx, dyPx, screenHeightPx, aspect);
        }

        public void EndDrag()
        {
            Camera.EndDrag();
        }

        public void ZoomNotch(int direction, float aspect)
        {
            if (!CanSimulate)
            {
                return;
            }

            Camera.ZoomNotch(direction, aspect);
        }

        public void OpenMenu()
        {
            Paused = true;
            InterruptActiveInputs();
            Label = default;
            LabelVisible = false;
            Marker = default;
            MarkerVisible = false;
        }

        public void Resume()
        {
            Paused = false;
        }

        public void ResetViewFromMenu(float aspect)
        {
            Camera.Reset(aspect);
            FloorView.Reset();
            Paused = false;
        }

        public void SetFocused(bool focused)
        {
            Focused = focused;
            if (!focused)
            {
                InterruptActiveInputs();
            }
        }

        private void InterruptActiveInputs()
        {
            _placeholder.Interrupt();
            Camera.EndDrag();
            _panLeft.Disarm();
            _panRight.Disarm();
            _panUp.Disarm();
            _panDown.Disarm();
        }

        private FloorLabel EmitPlaceholderLabel(PlaceholderLabel pLabel)
        {
            FloorLabelKind kind = pLabel.Kind == PlaceholderLabelKind.AirLayer
                ? FloorLabelKind.AirLayer
                : FloorLabelKind.GroundFloor;
            return SetLabel(kind, pLabel.Index);
        }

        private FloorLabel EmitViewedFloorLabel(int viewedFloor)
        {
            return viewedFloor == 0
                ? SetLabel(FloorLabelKind.FullView, 0)
                : SetLabel(FloorLabelKind.GroundFloor, viewedFloor);
        }

        private FloorLabel SetLabel(FloorLabelKind kind, int index)
        {
            FloorLabel label = new FloorLabel(kind, index, ++_labelSequence);
            Label = label;
            LabelVisible = true;
            return label;
        }

        private struct PanKeyState
        {
            private bool _prevHeld;
            private bool _armed;

            public void Disarm()
            {
                _armed = false;
            }

            public void TrackWhileInactive(bool held)
            {
                _prevHeld = held;
            }

            public int Update(bool held)
            {
                if (!held)
                {
                    _armed = false;
                }
                else if (!_prevHeld)
                {
                    _armed = true;
                }

                _prevHeld = held;
                return _armed ? 1 : 0;
            }
        }
    }
}
