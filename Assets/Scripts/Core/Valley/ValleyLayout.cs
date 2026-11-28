using System;
using System.Collections.Generic;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Immutable valley layout holding vertex heights, cell properties, cover, props, and spatial queries.
    /// </summary>
    public sealed class ValleyLayout
    {
        private readonly float[] _heights;
        private readonly CellKind[] _kinds;
        private readonly CoverKind[] _cover;
        private readonly bool[] _walkable;
        private readonly bool[] _flatFloor;
        private readonly byte[] _flatFloorNumber;
        private readonly float[] _obstacleHeights;
        private readonly float[] _solidTops;
        private readonly int[] _components;
        private readonly PropPlacement[] _props;

        public int Width { get; }
        public int Depth { get; }
        public int VertexWidth => Width + 1;
        public int VertexDepth => Depth + 1;
        public AirBounds AirBounds { get; }
        public int StartX { get; }
        public int StartZ { get; }
        public IReadOnlyList<PropPlacement> Props => _props;

        internal ValleyLayout(
            int width,
            int depth,
            float[] heights,
            CellKind[] kinds,
            CoverKind[] cover,
            bool[] walkable,
            bool[] flatFloor,
            byte[] flatFloorNumber,
            float[] obstacleHeights,
            float[] solidTops,
            int[] components,
            PropPlacement[] props,
            AirBounds airBounds,
            int startX,
            int startZ)
        {
            Width = width;
            Depth = depth;
            _heights = heights;
            _kinds = kinds;
            _cover = cover;
            _walkable = walkable;
            _flatFloor = flatFloor;
            _flatFloorNumber = flatFloorNumber;
            _obstacleHeights = obstacleHeights;
            _solidTops = solidTops;
            _components = components;
            _props = props;
            AirBounds = airBounds;
            StartX = startX;
            StartZ = startZ;
        }

        public bool InCellBounds(int cx, int cz)
        {
            return cx >= 0 && cx < Width && cz >= 0 && cz < Depth;
        }

        public int CellIndex(int cx, int cz)
        {
            return cz * Width + cx;
        }

        public float VertexHeight(int vx, int vz)
        {
            if (vx < 0 || vx > Width || vz < 0 || vz > Depth)
            {
                throw new ArgumentOutOfRangeException(nameof(vx), "Vertex index out of bounds.");
            }
            return _heights[vz * VertexWidth + vx];
        }

        public CellKind KindAt(int cx, int cz)
        {
            ValidateCell(cx, cz);
            return _kinds[CellIndex(cx, cz)];
        }

        public CoverKind CoverAt(int cx, int cz)
        {
            ValidateCell(cx, cz);
            return _cover[CellIndex(cx, cz)];
        }

        public bool IsWalkable(int cx, int cz)
        {
            if (!InCellBounds(cx, cz))
            {
                return false;
            }
            return _walkable[CellIndex(cx, cz)];
        }

        public int ComponentOf(int cx, int cz)
        {
            if (!InCellBounds(cx, cz))
            {
                return 0;
            }
            return _components[CellIndex(cx, cz)];
        }

        public float ObstacleHeightAt(int cx, int cz)
        {
            ValidateCell(cx, cz);
            return _obstacleHeights[CellIndex(cx, cz)];
        }

        public float SolidTop(int cx, int cz)
        {
            ValidateCell(cx, cz);
            return _solidTops[CellIndex(cx, cz)];
        }

        public bool IsFlatFloor(int cx, int cz, out int floor)
        {
            if (!InCellBounds(cx, cz))
            {
                floor = 0;
                return false;
            }
            int idx = CellIndex(cx, cz);
            floor = _flatFloorNumber[idx];
            return _flatFloor[idx];
        }

        public float HeightAt(float x, float z)
        {
            float cxFloat = MathF.Min(MathF.Max(x, 0f), Width);
            float czFloat = MathF.Min(MathF.Max(z, 0f), Depth);
            int cx = Math.Min((int)MathF.Floor(cxFloat), Width - 1);
            int cz = Math.Min((int)MathF.Floor(czFloat), Depth - 1);
            float u = cxFloat - cx;
            float v = czFloat - cz;

            int v00 = cz * VertexWidth + cx;
            float h00 = _heights[v00];
            float h11 = _heights[v00 + VertexWidth + 1];

            if (u >= v)
            {
                float h10 = _heights[v00 + 1];
                return h00 + u * (h10 - h00) + v * (h11 - h10);
            }

            float h01 = _heights[v00 + VertexWidth];
            return h00 + u * (h11 - h01) + v * (h01 - h00);
        }

        public float SurfaceAt(float x, float z)
        {
            float cxFloat = MathF.Min(MathF.Max(x, 0f), Width);
            float czFloat = MathF.Min(MathF.Max(z, 0f), Depth);
            int cx = Math.Min((int)MathF.Floor(cxFloat), Width - 1);
            int cz = Math.Min((int)MathF.Floor(czFloat), Depth - 1);
            return HeightAt(x, z) + _obstacleHeights[CellIndex(cx, cz)];
        }

        public bool IsAirClear(int cx, int cz, int layer)
        {
            float altitude = ValleyConstants.AltitudeOfLayer(layer);
            if (!InCellBounds(cx, cz) || !AirBounds.ContainsCellCenter(cx, cz))
            {
                return false;
            }
            return altitude >= _solidTops[CellIndex(cx, cz)] + ValleyConstants.MinAirClearance;
        }

        public float MaxEdgeDelta(int cx, int cz)
        {
            ValidateCell(cx, cz);
            int v00 = cz * VertexWidth + cx;
            float h00 = _heights[v00];
            float h10 = _heights[v00 + 1];
            float h01 = _heights[v00 + VertexWidth];
            float h11 = _heights[v00 + VertexWidth + 1];

            float south = MathF.Abs(h10 - h00);
            float north = MathF.Abs(h11 - h01);
            float west = MathF.Abs(h01 - h00);
            float east = MathF.Abs(h11 - h10);

            float maxHorizontal = south > north ? south : north;
            float maxVertical = west > east ? west : east;
            return maxHorizontal > maxVertical ? maxHorizontal : maxVertical;
        }

        private void ValidateCell(int cx, int cz)
        {
            if (!InCellBounds(cx, cz))
            {
                throw new ArgumentOutOfRangeException(nameof(cx), "Cell index out of bounds.");
            }
        }
    }
}
