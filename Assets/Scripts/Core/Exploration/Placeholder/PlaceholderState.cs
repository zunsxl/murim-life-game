using System;
using System.Collections;
using System.Collections.Generic;
using Murim.Core.Valley;

namespace Murim.Core.Exploration.Placeholder
{
    public enum PlaceholderMotion : byte
    {
        GroundedStationary,
        GroundedTranslating,
        AirborneStationary,
        AirborneTranslating,
        Ascending,
        Descending
    }

    public enum MarkerKind : byte
    {
        Reachable,
        Unreachable
    }

    public enum PlaceholderLabelKind : byte
    {
        GroundFloor,
        AirLayer
    }

    public readonly struct DestinationMarker : IEquatable<DestinationMarker>
    {
        public readonly MarkerKind Kind;
        public readonly Vec3 Point;
        public readonly int Sequence;

        public DestinationMarker(MarkerKind kind, Vec3 point, int sequence)
        {
            Kind = kind;
            Point = point;
            Sequence = sequence;
        }

        public bool Equals(DestinationMarker other)
        {
            return Kind == other.Kind && Point.Equals(other.Point) && Sequence == other.Sequence;
        }

        public override bool Equals(object obj) => obj is DestinationMarker other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Point, Sequence);
    }

    public readonly struct PlaceholderLabel : IEquatable<PlaceholderLabel>
    {
        public readonly PlaceholderLabelKind Kind;
        public readonly int Index;
        public readonly int Sequence;

        public PlaceholderLabel(PlaceholderLabelKind kind, int index, int sequence)
        {
            Kind = kind;
            Index = index;
            Sequence = sequence;
        }

        public bool Equals(PlaceholderLabel other)
        {
            return Kind == other.Kind && Index == other.Index && Sequence == other.Sequence;
        }

        public override bool Equals(object obj) => obj is PlaceholderLabel other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Index, Sequence);
    }

    /// <summary>
    /// Mutable simulation state of the exploration test placeholder, updated solely by <see cref="PlaceholderController"/>.
    /// </summary>
    public sealed class PlaceholderState
    {
        private readonly Vec2[] _waypoints;
        private readonly RemainingPathView _remainingPathView;

        internal int WaypointIndex;
        internal int WaypointCount;
        internal bool HasAirborneTarget;
        internal Vec2 AirborneTarget;

        internal PlaceholderState(int maxWaypoints)
        {
            _waypoints = new Vec2[maxWaypoints];
            _remainingPathView = new RemainingPathView(this);
        }

        public Vec2 Position { get; internal set; }
        public float Elevation { get; internal set; }
        public int AirLayer { get; internal set; }
        public float TargetElevation { get; internal set; }
        public PlaceholderMotion Motion { get; internal set; }
        public float LogicalHeight { get; internal set; }
        public int LogicalBand => ValleyConstants.BandOf(LogicalHeight);
        public DestinationMarker LastMarker { get; internal set; }
        public PlaceholderLabel LastLabel { get; internal set; }

        public bool HasDestination => AirLayer == 0 ? WaypointIndex < WaypointCount : HasAirborneTarget;

        public Vec2 Destination
        {
            get
            {
                if (AirLayer == 0)
                {
                    return WaypointIndex < WaypointCount ? _waypoints[WaypointCount - 1] : Position;
                }
                return HasAirborneTarget ? AirborneTarget : Position;
            }
        }

        public IReadOnlyList<Vec2> RemainingPath => _remainingPathView;

        internal Vec2[] WaypointBuffer => _waypoints;

        internal void ClearDestination()
        {
            WaypointIndex = 0;
            WaypointCount = 0;
            HasAirborneTarget = false;
        }

        private sealed class RemainingPathView : IReadOnlyList<Vec2>
        {
            private readonly PlaceholderState _state;

            public RemainingPathView(PlaceholderState state)
            {
                _state = state;
            }

            public int Count
            {
                get
                {
                    if (_state.AirLayer == 0)
                    {
                        int remaining = _state.WaypointCount - _state.WaypointIndex;
                        return remaining > 0 ? remaining : 0;
                    }
                    return _state.HasAirborneTarget ? 1 : 0;
                }
            }

            public Vec2 this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    if (_state.AirLayer == 0)
                    {
                        return _state._waypoints[_state.WaypointIndex + index];
                    }
                    return _state.AirborneTarget;
                }
            }

            public IEnumerator<Vec2> GetEnumerator()
            {
                int count = Count;
                for (int i = 0; i < count; i++)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
