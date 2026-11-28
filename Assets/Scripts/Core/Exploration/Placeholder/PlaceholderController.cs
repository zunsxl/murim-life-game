using System;
using Murim.Core.Tuning;
using Murim.Core.Valley;

namespace Murim.Core.Exploration.Placeholder
{
    /// <summary>
    /// Deterministic simulation controller for on-foot and airborne placeholder movement per spec §3.2–§3.3.
    /// </summary>
    public sealed class PlaceholderController
    {
        private const float ClearanceInset = 0.3f;
        private const float StayPutDistance = 0.05f;
        private const float SnapEpsilon = 0.0001f;
        private const float Eps = 1e-5f;

        private readonly ValleyLayout _layout;
        private readonly ExplorationTuning _tuning;
        private readonly GridPathfinder _pathfinder;

        private int _markerSequence;
        private int _labelSequence;

        public PlaceholderState State { get; }

        public PlaceholderController(ValleyLayout layout)
            : this(layout, ExplorationTuning.Default)
        {
        }

        public PlaceholderController(ValleyLayout layout, ExplorationTuning tuning)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));

            int capacity = Math.Max(
                ValleyConstants.GridWidth * ValleyConstants.GridDepth,
                layout.Width * layout.Depth);
            _pathfinder = new GridPathfinder(capacity);
            State = new PlaceholderState(capacity + 2);

            Vec2 startPos = new Vec2(layout.StartX + 0.5f, layout.StartZ + 0.5f);
            float startElev = layout.HeightAt(startPos.X, startPos.Y);
            State.Position = startPos;
            State.Elevation = startElev;
            State.TargetElevation = startElev;
            State.AirLayer = 0;
            State.LogicalHeight = startElev;
            State.Motion = PlaceholderMotion.GroundedStationary;
        }

        public DestinationMarker Click(Vec3 rayOrigin, Vec3 rayDir, float cutY)
        {
            return State.AirLayer == 0
                ? ClickOnFoot(rayOrigin, rayDir, cutY)
                : ClickAirborne(rayOrigin, rayDir);
        }

        public PlaceholderLabel Ascend()
        {
            State.ClearDestination();
            CellOf(State.Position, out int cx, out int cz);

            if (State.AirLayer == 0)
            {
                float minAltitude = State.LogicalHeight + ValleyConstants.MinAirClearance;
                for (int layer = 1; layer <= ValleyConstants.AirLayerCount; layer++)
                {
                    float alt = ValleyConstants.AltitudeOfLayer(layer);
                    if (alt >= minAltitude && _layout.IsAirClear(cx, cz, layer))
                    {
                        SetAirLayer(layer, alt);
                        break;
                    }
                }
            }
            else if (State.AirLayer < ValleyConstants.AirLayerCount)
            {
                int nextLayer = State.AirLayer + 1;
                if (_layout.IsAirClear(cx, cz, nextLayer))
                {
                    SetAirLayer(nextLayer, ValleyConstants.AltitudeOfLayer(nextLayer));
                }
            }

            UpdateMotion();
            return EmitCurrentLabel();
        }

        public PlaceholderLabel Descend()
        {
            State.ClearDestination();
            CellOf(State.Position, out int cx, out int cz);

            if (State.AirLayer > 0)
            {
                int lowerLayer = State.AirLayer - 1;
                if (lowerLayer >= 1 && _layout.IsAirClear(cx, cz, lowerLayer))
                {
                    SetAirLayer(lowerLayer, ValleyConstants.AltitudeOfLayer(lowerLayer));
                }
                else if (_layout.IsWalkable(cx, cz))
                {
                    float groundY = _layout.HeightAt(State.Position.X, State.Position.Y);
                    State.AirLayer = 0;
                    State.LogicalHeight = groundY;
                    State.TargetElevation = groundY;
                }
            }

            UpdateMotion();
            return EmitCurrentLabel();
        }

        public void Interrupt()
        {
            State.ClearDestination();
            UpdateMotion();
        }

        public void Tick()
        {
            if (State.Elevation != State.TargetElevation)
            {
                TickVertical();
                return;
            }

            if (State.AirLayer == 0)
            {
                TickGrounded();
            }
            else
            {
                TickAirborne();
            }
        }

        private DestinationMarker ClickOnFoot(Vec3 rayOrigin, Vec3 rayDir, float cutY)
        {
            State.ClearDestination();
            CellOf(State.Position, out int curCx, out int curCz);
            int curComp = _layout.ComponentOf(curCx, curCz);

            RayHit hit = ValleyRaycaster.Raycast(_layout, rayOrigin, rayDir, cutY);
            if (hit.Kind == RayHitKind.Ground
                && _layout.IsWalkable(hit.CellX, hit.CellZ)
                && _layout.ComponentOf(hit.CellX, hit.CellZ) == curComp)
            {
                Vec2 clamped = ClampToCell(hit.Point.XZ, hit.CellX, hit.CellZ);
                Vec2 target = InsetFromNonWalkableEdges(clamped, hit.CellX, hit.CellZ);
                SetGroundedPath(curCx, curCz, target, hit.CellX, hit.CellZ);
                UpdateMotion();
                return EmitMarker(MarkerKind.Reachable, hit.Point);
            }

            return HandleBlockedOnFootClick(rayOrigin, rayDir, hit, curCx, curCz);
        }

        private DestinationMarker HandleBlockedOnFootClick(
            Vec3 rayOrigin,
            Vec3 rayDir,
            RayHit hit,
            int curCx,
            int curCz)
        {
            float planeY = (State.LogicalBand - 1) * ValleyConstants.FloorHeight;
            bool hasQ = TryIntersectHorizontalPlane(rayOrigin, rayDir, planeY, out Vec3 q);
            if (!hasQ)
            {
                q = new Vec3(State.Position.X, planeY, State.Position.Y);
            }

            Vec3 markerPoint = hit.Kind != RayHitKind.None ? hit.Point : q;
            DestinationMarker marker = EmitMarker(MarkerKind.Unreachable, markerPoint);
            if (!hasQ)
            {
                UpdateMotion();
                return marker;
            }

            int bestIdx = FindBestFlatCandidateIndex(q.XZ, curCx, curCz, State.LogicalBand);
            int bestCx = bestIdx % _layout.Width;
            int bestCz = bestIdx / _layout.Width;
            Vec2 clamped = ClampToCell(q.XZ, bestCx, bestCz);
            Vec2 target = InsetFromNonWalkableEdges(clamped, bestCx, bestCz);

            if (bestCx == curCx && bestCz == curCz
                && Vec2.DistanceSquared(State.Position, target) <= StayPutDistance * StayPutDistance)
            {
                UpdateMotion();
                return marker;
            }

            SetGroundedPath(curCx, curCz, target, bestCx, bestCz);
            UpdateMotion();
            return marker;
        }

        private DestinationMarker ClickAirborne(Vec3 rayOrigin, Vec3 rayDir)
        {
            State.ClearDestination();
            float planeY = ValleyConstants.AltitudeOfLayer(State.AirLayer);
            if (!TryIntersectHorizontalPlane(rayOrigin, rayDir, planeY, out Vec3 hitPoint))
            {
                UpdateMotion();
                Vec3 fallback = new Vec3(State.Position.X, planeY, State.Position.Y);
                return EmitMarker(MarkerKind.Unreachable, fallback);
            }

            Vec2 start = State.Position;
            Vec2 end = hitPoint.XZ;
            Vec2 delta = end - start;
            float totalLen = delta.Length;

            if (totalLen < 1e-6f)
            {
                CellOf(start, out int cx, out int cz);
                bool clearAtPoint = _layout.AirBounds.Contains(start.X, start.Y)
                    && _layout.IsAirClear(cx, cz, State.AirLayer);
                UpdateMotion();
                return EmitMarker(clearAtPoint ? MarkerKind.Reachable : MarkerKind.Unreachable, hitPoint);
            }

            bool blocked = TryFindAirborneBlock(start, end, totalLen, out float stopDist);
            if (blocked)
            {
                if (stopDist > 1e-6f)
                {
                    State.AirborneTarget = start + (delta / totalLen) * stopDist;
                    State.HasAirborneTarget = true;
                }
                UpdateMotion();
                return EmitMarker(MarkerKind.Unreachable, hitPoint);
            }

            State.AirborneTarget = end;
            State.HasAirborneTarget = true;
            UpdateMotion();
            return EmitMarker(MarkerKind.Reachable, hitPoint);
        }

        private int FindBestFlatCandidateIndex(Vec2 q, int curCx, int curCz, int band)
        {
            int curComp = _layout.ComponentOf(curCx, curCz);
            int bestIndex = _layout.CellIndex(curCx, curCz);
            float bestDistSq = CellCenterDistSq(curCx, curCz, q);

            int width = _layout.Width;
            int depth = _layout.Depth;
            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    if (_layout.ComponentOf(cx, cz) != curComp
                        || !_layout.IsFlatFloor(cx, cz, out int floor)
                        || floor != band)
                    {
                        continue;
                    }

                    float distSq = CellCenterDistSq(cx, cz, q);
                    int idx = cz * width + cx;
                    if (distSq < bestDistSq || (distSq == bestDistSq && idx < bestIndex))
                    {
                        bestDistSq = distSq;
                        bestIndex = idx;
                    }
                }
            }
            return bestIndex;
        }

        private void SetGroundedPath(int startCx, int startCz, Vec2 target, int goalCx, int goalCz)
        {
            if (Vec2.DistanceSquared(State.Position, target) <= 1e-8f)
            {
                State.ClearDestination();
                return;
            }
            int count = _pathfinder.FindPath(
                _layout,
                new PathEndpoint(State.Position, startCx, startCz),
                new PathEndpoint(target, goalCx, goalCz),
                State.WaypointBuffer);
            State.WaypointIndex = 0;
            State.WaypointCount = count;
        }

        private Vec2 InsetFromNonWalkableEdges(Vec2 pos, int cx, int cz)
        {
            float minX = _layout.IsWalkable(cx - 1, cz) ? cx : cx + ClearanceInset;
            float maxX = _layout.IsWalkable(cx + 1, cz) ? cx + 1f : cx + 1f - ClearanceInset;
            float minZ = _layout.IsWalkable(cx, cz - 1) ? cz : cz + ClearanceInset;
            float maxZ = _layout.IsWalkable(cx, cz + 1) ? cz + 1f : cz + 1f - ClearanceInset;

            return new Vec2(
                MathF.Min(MathF.Max(pos.X, minX), maxX),
                MathF.Min(MathF.Max(pos.Y, minZ), maxZ));
        }

        private bool TryFindAirborneBlock(Vec2 start, Vec2 end, float totalLen, out float stopDist)
        {
            stopDist = 0f;
            int cx = (int)MathF.Floor(start.X);
            int cz = (int)MathF.Floor(start.Y);
            if (!_layout.AirBounds.Contains(start.X, start.Y) || !_layout.IsAirClear(cx, cz, State.AirLayer))
            {
                return true;
            }

            Vec2 dir = (end - start) / totalLen;
            float boundsExit = ComputeAirBoundsExitDistance(start, dir);
            float cellBlock = FindFirstBlockedCellDistance(start, dir, totalLen);

            float hitDist = MathF.Min(boundsExit, cellBlock);
            bool endOutside = !_layout.AirBounds.Contains(end.X, end.Y);
            if (hitDist < totalLen - Eps || (endOutside && hitDist <= totalLen + Eps))
            {
                stopDist = MathF.Max(0f, hitDist - ClearanceInset);
                return true;
            }
            return false;
        }

        private float ComputeAirBoundsExitDistance(Vec2 start, Vec2 dir)
        {
            AirBounds b = _layout.AirBounds;
            float tExitX = SlabExitDistance(start.X, dir.X, b.MinX, b.MaxX);
            float tExitZ = SlabExitDistance(start.Y, dir.Y, b.MinZ, b.MaxZ);
            return MathF.Min(tExitX, tExitZ);
        }

        private float FindFirstBlockedCellDistance(Vec2 start, Vec2 dir, float totalLen)
        {
            int cx = (int)MathF.Floor(start.X);
            int cz = (int)MathF.Floor(start.Y);
            int stepX = dir.X > 0f ? 1 : (dir.X < 0f ? -1 : 0);
            int stepZ = dir.Y > 0f ? 1 : (dir.Y < 0f ? -1 : 0);
            float tDeltaX = stepX != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;
            float tMaxX = InitCellTMax(start.X, dir.X, cx, stepX);
            float tMaxZ = InitCellTMax(start.Y, dir.Y, cz, stepZ);

            int maxSteps = _layout.Width + _layout.Depth + 2;
            while (maxSteps-- > 0)
            {
                float nextS = MathF.Min(tMaxX, tMaxZ);
                if (nextS > totalLen - Eps)
                {
                    break;
                }
                if (StepAirborneDda(ref cx, ref cz, stepX, stepZ, ref tMaxX, ref tMaxZ, tDeltaX, tDeltaZ))
                {
                    return nextS;
                }
            }
            return float.PositiveInfinity;
        }

        private bool StepAirborneDda(
            ref int cx,
            ref int cz,
            int stepX,
            int stepZ,
            ref float tMaxX,
            ref float tMaxZ,
            float tDeltaX,
            float tDeltaZ)
        {
            if (MathF.Abs(tMaxX - tMaxZ) < Eps)
            {
                bool cornerBlocked = !_layout.IsAirClear(cx + stepX, cz, State.AirLayer)
                    || !_layout.IsAirClear(cx, cz + stepZ, State.AirLayer)
                    || !_layout.IsAirClear(cx + stepX, cz + stepZ, State.AirLayer);
                cx += stepX;
                cz += stepZ;
                tMaxX += tDeltaX;
                tMaxZ += tDeltaZ;
                return cornerBlocked;
            }
            if (tMaxX < tMaxZ)
            {
                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cz += stepZ;
                tMaxZ += tDeltaZ;
            }
            return !_layout.IsAirClear(cx, cz, State.AirLayer);
        }

        private void TickVertical()
        {
            float diff = State.TargetElevation - State.Elevation;
            float vStep = _tuning.VerticalSpeed * ValleyConstants.TickSeconds;
            if (MathF.Abs(diff) <= vStep)
            {
                State.Elevation = State.TargetElevation;
                UpdateMotion();
            }
            else
            {
                State.Elevation += diff > 0f ? vStep : -vStep;
                State.Motion = diff > 0f
                    ? PlaceholderMotion.Ascending
                    : PlaceholderMotion.Descending;
            }
        }

        private void TickGrounded()
        {
            if (State.WaypointIndex < State.WaypointCount)
            {
                float step = _tuning.WalkSpeed * ValleyConstants.TickSeconds;
                float remainingTotal = ComputeRemainingPathDistance();
                if (remainingTotal <= step + SnapEpsilon)
                {
                    State.Position = State.WaypointBuffer[State.WaypointCount - 1];
                    State.ClearDestination();
                    State.Motion = PlaceholderMotion.GroundedStationary;
                }
                else
                {
                    AdvanceAlongWaypoints(step);
                    State.Motion = State.WaypointIndex < State.WaypointCount
                        ? PlaceholderMotion.GroundedTranslating
                        : PlaceholderMotion.GroundedStationary;
                }
            }
            else
            {
                State.Motion = PlaceholderMotion.GroundedStationary;
            }

            float groundY = _layout.HeightAt(State.Position.X, State.Position.Y);
            State.Elevation = groundY;
            State.TargetElevation = groundY;
            State.LogicalHeight = groundY;
        }

        private void TickAirborne()
        {
            if (!State.HasAirborneTarget)
            {
                State.Motion = PlaceholderMotion.AirborneStationary;
                return;
            }

            float step = _tuning.AirSpeed * ValleyConstants.TickSeconds;
            Vec2 delta = State.AirborneTarget - State.Position;
            float dist = delta.Length;
            if (dist <= step + SnapEpsilon)
            {
                State.Position = State.AirborneTarget;
                State.HasAirborneTarget = false;
                State.Motion = PlaceholderMotion.AirborneStationary;
            }
            else
            {
                State.Position += delta * (step / dist);
                State.Motion = PlaceholderMotion.AirborneTranslating;
            }
        }

        private float ComputeRemainingPathDistance()
        {
            float total = 0f;
            Vec2 prev = State.Position;
            for (int i = State.WaypointIndex; i < State.WaypointCount; i++)
            {
                Vec2 next = State.WaypointBuffer[i];
                total += Vec2.Distance(prev, next);
                prev = next;
            }
            return total;
        }

        private void AdvanceAlongWaypoints(float remainingStep)
        {
            while (State.WaypointIndex < State.WaypointCount && remainingStep > 0f)
            {
                Vec2 next = State.WaypointBuffer[State.WaypointIndex];
                Vec2 delta = next - State.Position;
                float segDist = delta.Length;
                if (segDist <= remainingStep)
                {
                    State.Position = next;
                    remainingStep -= segDist;
                    State.WaypointIndex++;
                }
                else
                {
                    State.Position += delta * (remainingStep / segDist);
                    remainingStep = 0f;
                }
            }
            if (State.WaypointIndex >= State.WaypointCount)
            {
                State.ClearDestination();
            }
        }

        private void SetAirLayer(int layer, float altitude)
        {
            State.AirLayer = layer;
            State.LogicalHeight = altitude;
            State.TargetElevation = altitude;
        }

        private void UpdateMotion()
        {
            if (State.Elevation < State.TargetElevation)
            {
                State.Motion = PlaceholderMotion.Ascending;
            }
            else if (State.Elevation > State.TargetElevation)
            {
                State.Motion = PlaceholderMotion.Descending;
            }
            else if (State.AirLayer > 0)
            {
                State.Motion = State.HasAirborneTarget
                    ? PlaceholderMotion.AirborneTranslating
                    : PlaceholderMotion.AirborneStationary;
            }
            else
            {
                State.Motion = State.WaypointIndex < State.WaypointCount
                    ? PlaceholderMotion.GroundedTranslating
                    : PlaceholderMotion.GroundedStationary;
            }
        }

        private DestinationMarker EmitMarker(MarkerKind kind, Vec3 point)
        {
            DestinationMarker marker = new DestinationMarker(kind, point, ++_markerSequence);
            State.LastMarker = marker;
            return marker;
        }

        private PlaceholderLabel EmitCurrentLabel()
        {
            PlaceholderLabel label = State.AirLayer > 0
                ? new PlaceholderLabel(PlaceholderLabelKind.AirLayer, State.AirLayer, ++_labelSequence)
                : new PlaceholderLabel(PlaceholderLabelKind.GroundFloor, State.LogicalBand, ++_labelSequence);
            State.LastLabel = label;
            return label;
        }

        private void CellOf(Vec2 pos, out int cx, out int cz)
        {
            cx = Math.Clamp((int)MathF.Floor(pos.X), 0, _layout.Width - 1);
            cz = Math.Clamp((int)MathF.Floor(pos.Y), 0, _layout.Depth - 1);
        }

        private static Vec2 ClampToCell(Vec2 pos, int cx, int cz)
        {
            return new Vec2(
                MathF.Min(MathF.Max(pos.X, cx), cx + 1f),
                MathF.Min(MathF.Max(pos.Y, cz), cz + 1f));
        }

        private static float CellCenterDistSq(int cx, int cz, Vec2 q)
        {
            float dx = cx + 0.5f - q.X;
            float dz = cz + 0.5f - q.Y;
            return dx * dx + dz * dz;
        }

        private static bool TryIntersectHorizontalPlane(Vec3 origin, Vec3 dir, float planeY, out Vec3 point)
        {
            if (MathF.Abs(dir.Y) < 1e-8f)
            {
                point = Vec3.Zero;
                return false;
            }
            float t = (planeY - origin.Y) / dir.Y;
            if (t < 0f)
            {
                point = Vec3.Zero;
                return false;
            }
            point = new Vec3(origin.X + dir.X * t, planeY, origin.Z + dir.Z * t);
            return true;
        }

        private static float SlabExitDistance(float origin, float dirCoord, float min, float max)
        {
            if (MathF.Abs(dirCoord) < 1e-8f)
            {
                return origin >= min && origin <= max ? float.PositiveInfinity : 0f;
            }
            return dirCoord > 0f ? (max - origin) / dirCoord : (min - origin) / dirCoord;
        }

        private static float InitCellTMax(float origin, float dirCoord, int cell, int step)
        {
            if (step > 0)
            {
                return (cell + 1f - origin) / dirCoord;
            }
            if (step < 0)
            {
                return (origin - cell) / -dirCoord;
            }
            return float.PositiveInfinity;
        }
    }
}
