using System;
using Murim.Core.Valley;

namespace Murim.Core.Exploration.Placeholder
{
    /// <summary>
    /// Cell-pinned 2D endpoint passed to <see cref="GridPathfinder.FindPath"/>.
    /// </summary>
    public readonly struct PathEndpoint
    {
        public readonly Vec2 Position;
        public readonly int CellX;
        public readonly int CellZ;

        public PathEndpoint(Vec2 position, int cellX, int cellZ)
        {
            Position = position;
            CellX = cellX;
            CellZ = cellZ;
        }
    }

    /// <summary>
    /// Deterministic 8-neighbour A* pathfinder with greedy ±0.3 m line-of-sight simplification
    /// and pre-allocated working buffers per spec §3.2.
    /// </summary>
    public sealed class GridPathfinder
    {
        private const int OrthogonalCost = 10;
        private const int DiagonalCost = 14;
        private const float ClearanceOffset = 0.3f - 1e-4f;
        private const float Eps = 1e-5f;

        private readonly int _capacity;
        private readonly int[] _gScore;
        private readonly int[] _fScore;
        private readonly int[] _hScore;
        private readonly int[] _parent;
        private readonly int[] _openStamp;
        private readonly int[] _closedStamp;
        private readonly int[] _heap;
        private readonly int[] _heapPos;
        private readonly int[] _rawCellPath;
        private readonly Vec2[] _rawWaypoints;

        private int _searchId;
        private int _heapSize;

        public GridPathfinder()
            : this(ValleyConstants.GridWidth * ValleyConstants.GridDepth)
        {
        }

        public GridPathfinder(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Pathfinder capacity must be positive.");
            }
            _capacity = capacity;
            _gScore = new int[capacity];
            _fScore = new int[capacity];
            _hScore = new int[capacity];
            _parent = new int[capacity];
            _openStamp = new int[capacity];
            _closedStamp = new int[capacity];
            _heap = new int[capacity];
            _heapPos = new int[capacity];
            _rawCellPath = new int[capacity];
            _rawWaypoints = new Vec2[capacity + 2];
        }

        public int FindPath(
            ValleyLayout layout,
            PathEndpoint start,
            PathEndpoint goal,
            Vec2[] outputWaypoints)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }
            if (outputWaypoints == null)
            {
                throw new ArgumentNullException(nameof(outputWaypoints));
            }
            int cellCount = layout.Width * layout.Depth;
            if (cellCount > _capacity || outputWaypoints.Length < cellCount + 1)
            {
                throw new ArgumentException("Layout exceeds pre-allocated pathfinder capacity.");
            }

            if (start.CellX == goal.CellX && start.CellZ == goal.CellZ)
            {
                outputWaypoints[0] = goal.Position;
                return 1;
            }

            int rawCellCount = RunAStar(layout, start.CellX, start.CellZ, goal.CellX, goal.CellZ);
            if (rawCellCount <= 0)
            {
                return 0;
            }

            int rawWaypointCount = BuildRawWaypoints(layout.Width, start.Position, goal.Position, rawCellCount);
            return SimplifyWaypoints(layout, rawWaypointCount, outputWaypoints);
        }

        internal static bool HasLineOfSight(ValleyLayout layout, Vec2 a, Vec2 b)
        {
            Vec2 delta = b - a;
            float len = delta.Length;
            if (len < 1e-6f)
            {
                int cx = (int)MathF.Floor(a.X);
                int cz = (int)MathF.Floor(a.Y);
                return layout.IsWalkable(cx, cz);
            }

            Vec2 perp = new Vec2(-delta.Y / len, delta.X / len) * ClearanceOffset;
            return IsSegmentWalkable(layout, a, b)
                && IsSegmentWalkable(layout, a + perp, b + perp)
                && IsSegmentWalkable(layout, a - perp, b - perp);
        }

        private int RunAStar(ValleyLayout layout, int startCx, int startCz, int goalCx, int goalCz)
        {
            BeginSearch(layout.Width * layout.Depth);
            int width = layout.Width;
            int startIdx = startCz * width + startCx;
            int goalIdx = goalCz * width + goalCx;

            _gScore[startIdx] = 0;
            _hScore[startIdx] = OctileHeuristic(startCx, startCz, goalCx, goalCz);
            _fScore[startIdx] = _hScore[startIdx];
            _parent[startIdx] = -1;
            _openStamp[startIdx] = _searchId;
            HeapPush(startIdx);

            while (_heapSize > 0)
            {
                int current = HeapPop();
                if (current == goalIdx)
                {
                    return ReconstructCellPath(goalIdx);
                }

                _closedStamp[current] = _searchId;
                ExpandNeighbors(layout, current, goalCx, goalCz);
            }
            return 0;
        }

        private void BeginSearch(int cellCount)
        {
            if (_searchId == int.MaxValue)
            {
                Array.Clear(_openStamp, 0, cellCount);
                Array.Clear(_closedStamp, 0, cellCount);
                _searchId = 0;
            }
            _searchId++;
            _heapSize = 0;
        }

        private void ExpandNeighbors(ValleyLayout layout, int current, int goalCx, int goalCz)
        {
            int width = layout.Width;
            int depth = layout.Depth;
            int cx = current % width;
            int cz = current / width;
            int baseG = _gScore[current];

            for (int dz = -1; dz <= 1; dz++)
            {
                int nz = cz + dz;
                if (nz < 0 || nz >= depth)
                {
                    continue;
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    int nx = cx + dx;
                    if (nx < 0 || nx >= width || !CanStep(layout, cx, cz, dx, dz))
                    {
                        continue;
                    }

                    int neighbor = nz * width + nx;
                    if (_closedStamp[neighbor] == _searchId)
                    {
                        continue;
                    }

                    int stepCost = (dx != 0 && dz != 0) ? DiagonalCost : OrthogonalCost;
                    int h = OctileHeuristic(nx, nz, goalCx, goalCz);
                    RelaxNeighbor(current, neighbor, baseG + stepCost, h);
                }
            }
        }

        private void RelaxNeighbor(int current, int neighbor, int tentativeG, int heuristic)
        {
            if (_openStamp[neighbor] != _searchId)
            {
                _openStamp[neighbor] = _searchId;
                _parent[neighbor] = current;
                _gScore[neighbor] = tentativeG;
                _hScore[neighbor] = heuristic;
                _fScore[neighbor] = tentativeG + heuristic;
                HeapPush(neighbor);
                return;
            }

            if (tentativeG < _gScore[neighbor])
            {
                _parent[neighbor] = current;
                _gScore[neighbor] = tentativeG;
                _fScore[neighbor] = tentativeG + _hScore[neighbor];
                HeapSiftUp(_heapPos[neighbor]);
            }
        }

        private int ReconstructCellPath(int goalIdx)
        {
            int count = 0;
            int curr = goalIdx;
            while (curr != -1)
            {
                _rawCellPath[count++] = curr;
                curr = _parent[curr];
            }

            int left = 0;
            int right = count - 1;
            while (left < right)
            {
                int tmp = _rawCellPath[left];
                _rawCellPath[left] = _rawCellPath[right];
                _rawCellPath[right] = tmp;
                left++;
                right--;
            }
            return count;
        }

        private int BuildRawWaypoints(int width, Vec2 startPos, Vec2 targetPos, int rawCellCount)
        {
            int outIndex = 0;
            _rawWaypoints[outIndex++] = startPos;
            for (int i = 1; i < rawCellCount - 1; i++)
            {
                int idx = _rawCellPath[i];
                int cx = idx % width;
                int cz = idx / width;
                _rawWaypoints[outIndex++] = new Vec2(cx + 0.5f, cz + 0.5f);
            }
            _rawWaypoints[outIndex++] = targetPos;
            return outIndex;
        }

        private int SimplifyWaypoints(ValleyLayout layout, int rawWaypointCount, Vec2[] outputWaypoints)
        {
            int outCount = 0;
            int i = 0;
            while (i < rawWaypointCount - 1)
            {
                int j = rawWaypointCount - 1;
                while (j > i + 1 && !HasLineOfSight(layout, _rawWaypoints[i], _rawWaypoints[j]))
                {
                    j--;
                }
                outputWaypoints[outCount++] = _rawWaypoints[j];
                i = j;
            }
            return outCount;
        }

        private static bool CanStep(ValleyLayout layout, int cx, int cz, int dx, int dz)
        {
            if (!layout.IsWalkable(cx + dx, cz + dz))
            {
                return false;
            }
            if (dx != 0 && dz != 0)
            {
                return layout.IsWalkable(cx + dx, cz) && layout.IsWalkable(cx, cz + dz);
            }
            return true;
        }

        private static int OctileHeuristic(int cx, int cz, int goalCx, int goalCz)
        {
            int adx = Math.Abs(cx - goalCx);
            int adz = Math.Abs(cz - goalCz);
            int min = adx < adz ? adx : adz;
            int max = adx > adz ? adx : adz;
            return DiagonalCost * min + OrthogonalCost * (max - min);
        }

        private static bool IsSegmentWalkable(ValleyLayout layout, Vec2 a, Vec2 b)
        {
            int cx = (int)MathF.Floor(a.X);
            int cz = (int)MathF.Floor(a.Y);
            int endCx = (int)MathF.Floor(b.X);
            int endCz = (int)MathF.Floor(b.Y);

            if (!layout.IsWalkable(cx, cz) || !layout.IsWalkable(endCx, endCz))
            {
                return false;
            }
            if (cx == endCx && cz == endCz)
            {
                return true;
            }

            return TraverseSegmentDda(layout, a, b);
        }

        private static bool TraverseSegmentDda(ValleyLayout layout, Vec2 a, Vec2 b)
        {
            int cx = (int)MathF.Floor(a.X);
            int cz = (int)MathF.Floor(a.Y);
            int endCx = (int)MathF.Floor(b.X);
            int endCz = (int)MathF.Floor(b.Y);

            float dx = b.X - a.X;
            float dz = b.Y - a.Y;
            int stepX = dx > 0f ? 1 : (dx < 0f ? -1 : 0);
            int stepZ = dz > 0f ? 1 : (dz < 0f ? -1 : 0);
            float tDeltaX = stepX != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / dz) : float.PositiveInfinity;
            float tMaxX = InitTMax(a.X, dx, cx, stepX);
            float tMaxZ = InitTMax(a.Y, dz, cz, stepZ);

            int maxSteps = Math.Abs(endCx - cx) + Math.Abs(endCz - cz) + 2;
            while ((cx != endCx || cz != endCz) && maxSteps-- > 0)
            {
                if (MathF.Min(tMaxX, tMaxZ) > 1f + Eps)
                {
                    break;
                }
                if (!AdvanceDdaStep(layout, ref cx, ref cz, stepX, stepZ, ref tMaxX, ref tMaxZ, tDeltaX, tDeltaZ))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AdvanceDdaStep(
            ValleyLayout layout,
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
                if (!layout.IsWalkable(cx + stepX, cz) || !layout.IsWalkable(cx, cz + stepZ))
                {
                    return false;
                }
                cx += stepX;
                cz += stepZ;
                tMaxX += tDeltaX;
                tMaxZ += tDeltaZ;
            }
            else if (tMaxX < tMaxZ)
            {
                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cz += stepZ;
                tMaxZ += tDeltaZ;
            }
            return layout.IsWalkable(cx, cz);
        }

        private static float InitTMax(float origin, float delta, int cell, int step)
        {
            if (step > 0)
            {
                return (cell + 1f - origin) / delta;
            }
            if (step < 0)
            {
                return (origin - cell) / -delta;
            }
            return float.PositiveInfinity;
        }

        private void HeapPush(int cellIdx)
        {
            int pos = _heapSize++;
            _heap[pos] = cellIdx;
            _heapPos[cellIdx] = pos;
            HeapSiftUp(pos);
        }

        private int HeapPop()
        {
            int root = _heap[0];
            _heapSize--;
            if (_heapSize > 0)
            {
                int last = _heap[_heapSize];
                _heap[0] = last;
                _heapPos[last] = 0;
                HeapSiftDown(0);
            }
            return root;
        }

        private void HeapSiftUp(int pos)
        {
            int item = _heap[pos];
            while (pos > 0)
            {
                int parentPos = (pos - 1) >> 1;
                int parentItem = _heap[parentPos];
                if (!IsBetter(item, parentItem))
                {
                    break;
                }
                _heap[pos] = parentItem;
                _heapPos[parentItem] = pos;
                pos = parentPos;
            }
            _heap[pos] = item;
            _heapPos[item] = pos;
        }

        private void HeapSiftDown(int pos)
        {
            int item = _heap[pos];
            int half = _heapSize >> 1;
            while (pos < half)
            {
                int childPos = (pos << 1) + 1;
                int rightPos = childPos + 1;
                if (rightPos < _heapSize && IsBetter(_heap[rightPos], _heap[childPos]))
                {
                    childPos = rightPos;
                }
                int childItem = _heap[childPos];
                if (!IsBetter(childItem, item))
                {
                    break;
                }
                _heap[pos] = childItem;
                _heapPos[childItem] = pos;
                pos = childPos;
            }
            _heap[pos] = item;
            _heapPos[item] = pos;
        }

        private bool IsBetter(int cellA, int cellB)
        {
            int fA = _fScore[cellA];
            int fB = _fScore[cellB];
            if (fA != fB)
            {
                return fA < fB;
            }
            int hA = _hScore[cellA];
            int hB = _hScore[cellB];
            if (hA != hB)
            {
                return hA < hB;
            }
            return cellA < cellB;
        }
    }
}
