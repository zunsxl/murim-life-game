using System;
using System.Collections.Generic;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Parses authored or test valley layouts, derives per-cell navigation state, and enforces structural invariants.
    /// </summary>
    public static class ValleyLayoutParser
    {
        public static ValleyLayout Parse(LayoutInput input)
        {
            if (input == null)
            {
                throw new LayoutFormatException("Layout input cannot be null.");
            }
            ValidateGridShapes(input, out int width, out int depth);

            float[] heights = ParseHeights(input.Heights, width + 1, depth + 1);
            CellKind[] kinds = ParseCells(input.Cells, width, depth);
            CoverKind[] cover = ParseCover(input.Cover, width, depth);

            ValidateWalkableEdgeDeltas(heights, kinds, width, depth);

            PropPlacement[] props = CopyAndValidateProps(input.Props, kinds, width, depth);
            float[] obstacleHeights = ComputeObstacleHeights(kinds, props, width, depth, out bool[] blockedByProp);
            bool[] walkable = ComputeWalkable(heights, kinds, blockedByProp, width, depth);
            ComputeCellHeightsAndFlats(heights, kinds, walkable, obstacleHeights, width, depth, out bool[] flatFloor, out byte[] flatFloorNumber, out float[] solidTops);
            int[] components = ComputeComponents(walkable, width, depth);

            ValidateStartCell(walkable, width, depth, input.StartX, input.StartZ);

            return new ValleyLayout(
                width, depth, heights, kinds, cover, walkable,
                flatFloor, flatFloorNumber, obstacleHeights, solidTops,
                components, props, input.AirBounds, input.StartX, input.StartZ);
        }

        private static void ValidateGridShapes(LayoutInput input, out int width, out int depth)
        {
            if (input.Heights == null || input.Cells == null || input.Cover == null)
            {
                throw new LayoutFormatException("Heights, Cells, and Cover arrays are required.");
            }
            depth = input.Cells.Length;
            if (depth < 1 || input.Cells[0] == null || input.Cells[0].Length < 1)
            {
                throw new LayoutFormatException("Cells grid must be at least 1x1.");
            }
            width = input.Cells[0].Length;
            if (input.Heights.Length != depth + 1)
            {
                throw new LayoutFormatException($"Expected {depth + 1} height rows, got {input.Heights.Length}.");
            }
            if (input.Cover.Length != depth)
            {
                throw new LayoutFormatException($"Expected {depth} cover rows, got {input.Cover.Length}.");
            }
        }

        private static float[] ParseHeights(string[] rows, int vertexWidth, int vertexDepth)
        {
            float[] heights = new float[vertexWidth * vertexDepth];
            for (int vz = 0; vz < vertexDepth; vz++)
            {
                string row = rows[vz];
                if (row == null || row.Length != vertexWidth)
                {
                    throw new LayoutFormatException($"Height row {vz} has invalid length.");
                }
                int rowOffset = vz * vertexWidth;
                for (int vx = 0; vx < vertexWidth; vx++)
                {
                    heights[rowOffset + vx] = DecodeHeightChar(row[vx], vx, vz);
                }
            }
            return heights;
        }

        private static float DecodeHeightChar(char ch, int vx, int vz)
        {
            float value;
            if (ch >= '0' && ch <= '9')
            {
                value = (ch - '0') * ValleyConstants.HeightStep;
            }
            else if (ch >= 'a' && ch <= 'z')
            {
                value = (10 + (ch - 'a')) * ValleyConstants.HeightStep;
            }
            else
            {
                throw new LayoutFormatException($"Unknown height character '{ch}' at ({vx}, {vz}).");
            }
            if (value > ValleyConstants.MaxAuthoredHeight)
            {
                throw new LayoutFormatException($"Out-of-range height {value} m at ({vx}, {vz}).");
            }
            return value;
        }

        private static CellKind[] ParseCells(string[] rows, int width, int depth)
        {
            CellKind[] kinds = new CellKind[width * depth];
            for (int cz = 0; cz < depth; cz++)
            {
                string row = rows[cz];
                if (row == null || row.Length != width)
                {
                    throw new LayoutFormatException($"Cell row {cz} has invalid length.");
                }
                int rowOffset = cz * width;
                for (int cx = 0; cx < width; cx++)
                {
                    if (!ValleyKindMetadata.TryParseCellKind(row[cx], out CellKind kind))
                    {
                        throw new LayoutFormatException($"Unknown cell character '{row[cx]}' at ({cx}, {cz}).");
                    }
                    kinds[rowOffset + cx] = kind;
                }
            }
            return kinds;
        }

        private static CoverKind[] ParseCover(string[] rows, int width, int depth)
        {
            CoverKind[] cover = new CoverKind[width * depth];
            for (int cz = 0; cz < depth; cz++)
            {
                string row = rows[cz];
                if (row == null || row.Length != width)
                {
                    throw new LayoutFormatException($"Cover row {cz} has invalid length.");
                }
                int rowOffset = cz * width;
                for (int cx = 0; cx < width; cx++)
                {
                    if (!ValleyKindMetadata.TryParseCoverKind(row[cx], out CoverKind kind))
                    {
                        throw new LayoutFormatException($"Unknown cover character '{row[cx]}' at ({cx}, {cz}).");
                    }
                    cover[rowOffset + cx] = kind;
                }
            }
            return cover;
        }

        private static void ValidateWalkableEdgeDeltas(float[] heights, CellKind[] kinds, int width, int depth)
        {
            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    int idx = cz * width + cx;
                    if (!ValleyKindMetadata.IsWalkableKind(kinds[idx]))
                    {
                        continue;
                    }
                    if (CellMaxEdgeDelta(heights, width + 1, cx, cz) > ValleyConstants.MaxWalkEdgeDelta)
                    {
                        throw new LayoutFormatException($"Walkable-kind cell ({cx}, {cz}) exceeds max edge delta {ValleyConstants.MaxWalkEdgeDelta} m.");
                    }
                }
            }
        }

        private static PropPlacement[] CopyAndValidateProps(IReadOnlyList<PropPlacement> source, CellKind[] kinds, int width, int depth)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<PropPlacement>();
            }
            PropPlacement[] props = new PropPlacement[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                PropPlacement prop = source[i];
                if (prop.Cx < 0 || prop.Cx >= width || prop.Cz < 0 || prop.Cz >= depth)
                {
                    throw new LayoutFormatException($"Prop {prop.Kind} at ({prop.Cx}, {prop.Cz}) is out of bounds.");
                }
                CellKind cellKind = kinds[prop.Cz * width + prop.Cx];
                if (!ValleyKindMetadata.IsPropAllowedOn(prop.Kind, cellKind))
                {
                    throw new LayoutFormatException($"Prop {prop.Kind} at ({prop.Cx}, {prop.Cz}) is not allowed on {cellKind}.");
                }
                props[i] = prop;
            }
            return props;
        }

        private static float[] ComputeObstacleHeights(
            CellKind[] kinds,
            PropPlacement[] props,
            int width,
            int depth,
            out bool[] blockedByProp)
        {
            int count = width * depth;
            float[] obstacleHeights = new float[count];
            blockedByProp = new bool[count];
            for (int i = 0; i < count; i++)
            {
                obstacleHeights[i] = ValleyKindMetadata.CellObstacleHeight(kinds[i]);
            }
            for (int i = 0; i < props.Length; i++)
            {
                ApplyPropFootprint(props[i], kinds, obstacleHeights, blockedByProp, width, depth);
            }
            return obstacleHeights;
        }

        private static void ApplyPropFootprint(
            PropPlacement prop,
            CellKind[] kinds,
            float[] obstacleHeights,
            bool[] blockedByProp,
            int width,
            int depth)
        {
            float radius = ValleyKindMetadata.PropBlockingRadius(prop.Kind);
            if (radius <= 0f)
            {
                return;
            }
            float radiusSq = radius * radius;
            float propHeight = ValleyKindMetadata.PropObstacleHeight(prop.Kind);
            int span = (int)MathF.Ceiling(radius);
            int minZ = Math.Max(0, prop.Cz - span);
            int maxZ = Math.Min(depth - 1, prop.Cz + span);
            int minX = Math.Max(0, prop.Cx - span);
            int maxX = Math.Min(width - 1, prop.Cx + span);

            for (int cz = minZ; cz <= maxZ; cz++)
            {
                int dz = cz - prop.Cz;
                for (int cx = minX; cx <= maxX; cx++)
                {
                    int dx = cx - prop.Cx;
                    if (dx * dx + dz * dz > radiusSq)
                    {
                        continue;
                    }
                    int idx = cz * width + cx;
                    blockedByProp[idx] = true;
                    if (cx == prop.Cx && cz == prop.Cz || ValleyKindMetadata.IsWalkableKind(kinds[idx]))
                    {
                        obstacleHeights[idx] = MathF.Max(obstacleHeights[idx], propHeight);
                    }
                }
            }
        }

        private static bool[] ComputeWalkable(
            float[] heights,
            CellKind[] kinds,
            bool[] blockedByProp,
            int width,
            int depth)
        {
            bool[] walkable = new bool[width * depth];
            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    int idx = cz * width + cx;
                    walkable[idx] = ValleyKindMetadata.IsWalkableKind(kinds[idx])
                        && !blockedByProp[idx]
                        && CellMaxEdgeDelta(heights, width + 1, cx, cz) <= ValleyConstants.MaxWalkEdgeDelta;
                }
            }
            return walkable;
        }

        private static void ComputeCellHeightsAndFlats(
            float[] heights,
            CellKind[] kinds,
            bool[] walkable,
            float[] obstacleHeights,
            int width,
            int depth,
            out bool[] flatFloor,
            out byte[] flatFloorNumber,
            out float[] solidTops)
        {
            int count = width * depth;
            flatFloor = new bool[count];
            flatFloorNumber = new byte[count];
            solidTops = new float[count];
            int vertexWidth = width + 1;

            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    int idx = cz * width + cx;
                    int v00 = cz * vertexWidth + cx;
                    float h00 = heights[v00];
                    float h10 = heights[v00 + 1];
                    float h01 = heights[v00 + vertexWidth];
                    float h11 = heights[v00 + vertexWidth + 1];

                    float minH = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
                    float maxH = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                    solidTops[idx] = maxH + obstacleHeights[idx];

                    if (walkable[idx] && !ValleyKindMetadata.IsSlopeKind(kinds[idx]))
                    {
                        byte floor = DetectFlatFloor(minH, maxH);
                        flatFloor[idx] = floor > 0;
                        flatFloorNumber[idx] = floor;
                    }
                }
            }
        }

        private static byte DetectFlatFloor(float minH, float maxH)
        {
            for (int floor = 1; floor <= ValleyConstants.GroundFloorCount; floor++)
            {
                float baseH = (floor - 1) * ValleyConstants.FloorHeight;
                if (minH >= baseH && maxH <= baseH + ValleyConstants.FlatFloorTolerance)
                {
                    return (byte)floor;
                }
            }
            return 0;
        }

        private static int[] ComputeComponents(bool[] walkable, int width, int depth)
        {
            int count = width * depth;
            int[] components = new int[count];
            int[] queue = new int[count];
            int nextComponent = 0;

            for (int cz = 0; cz < depth; cz++)
            {
                for (int cx = 0; cx < width; cx++)
                {
                    int startIdx = cz * width + cx;
                    if (!walkable[startIdx] || components[startIdx] != 0)
                    {
                        continue;
                    }
                    nextComponent++;
                    FloodFillComponent(walkable, components, queue, width, depth, startIdx, nextComponent);
                }
            }
            return components;
        }

        private static void FloodFillComponent(
            bool[] walkable,
            int[] components,
            int[] queue,
            int width,
            int depth,
            int startIdx,
            int componentId)
        {
            int head = 0;
            int tail = 0;
            components[startIdx] = componentId;
            queue[tail++] = startIdx;

            while (head < tail)
            {
                int idx = queue[head++];
                int cx = idx % width;
                int cz = idx / width;
                EnqueueNeighbors(walkable, components, queue, width, depth, cx, cz, componentId, ref tail);
            }
        }

        private static void EnqueueNeighbors(
            bool[] walkable,
            int[] components,
            int[] queue,
            int width,
            int depth,
            int cx,
            int cz,
            int componentId,
            ref int tail)
        {
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
                    if (nx < 0 || nx >= width)
                    {
                        continue;
                    }
                    if (!CanStep(walkable, width, cx, cz, dx, dz))
                    {
                        continue;
                    }
                    int nIdx = nz * width + nx;
                    if (components[nIdx] == 0)
                    {
                        components[nIdx] = componentId;
                        queue[tail++] = nIdx;
                    }
                }
            }
        }

        private static bool CanStep(bool[] walkable, int width, int cx, int cz, int dx, int dz)
        {
            int nIdx = (cz + dz) * width + (cx + dx);
            if (!walkable[nIdx])
            {
                return false;
            }
            if (dx != 0 && dz != 0)
            {
                return walkable[cz * width + (cx + dx)] && walkable[(cz + dz) * width + cx];
            }
            return true;
        }

        private static void ValidateStartCell(bool[] walkable, int width, int depth, int startX, int startZ)
        {
            if (startX < 0 || startX >= width || startZ < 0 || startZ >= depth)
            {
                throw new LayoutFormatException($"Start cell ({startX}, {startZ}) is out of bounds.");
            }
            if (!walkable[startZ * width + startX])
            {
                throw new LayoutFormatException($"Start cell ({startX}, {startZ}) is not walkable.");
            }
        }

        private static float CellMaxEdgeDelta(float[] heights, int vertexWidth, int cx, int cz)
        {
            int v00 = cz * vertexWidth + cx;
            float h00 = heights[v00];
            float h10 = heights[v00 + 1];
            float h01 = heights[v00 + vertexWidth];
            float h11 = heights[v00 + vertexWidth + 1];

            float dSouth = MathF.Abs(h10 - h00);
            float dNorth = MathF.Abs(h11 - h01);
            float dWest = MathF.Abs(h01 - h00);
            float dEast = MathF.Abs(h11 - h10);

            float maxH = dSouth > dNorth ? dSouth : dNorth;
            float maxV = dWest > dEast ? dWest : dEast;
            return maxH > maxV ? maxH : maxV;
        }
    }
}
