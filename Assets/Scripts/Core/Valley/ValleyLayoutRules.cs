using System;
using System.Collections.Generic;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Validates authored invariants for the 160x96 hand-built valley per spec §3.1 and T-CORE-03/08.
    /// </summary>
    public static class ValleyLayoutRules
    {
        private readonly struct PropSummary
        {
            public readonly int Pines;
            public readonly int PineUpright;
            public readonly int PineLeaning;
            public readonly int PineSpreading;
            public readonly int Boulders;
            public readonly int BoulderUpright;
            public readonly int BoulderSquat;
            public readonly int BoulderLow;
            public readonly int Shrubs;
            public readonly int CliffCrest;
            public readonly int CliffToe;
            public readonly int RiverRocks;
            public readonly int FallFoam;
            public readonly int WestMassifPines;

            public PropSummary(
                int pines, int pineUpright, int pineLeaning, int pineSpreading,
                int boulders, int boulderUpright, int boulderSquat, int boulderLow,
                int shrubs, int cliffCrest, int cliffToe, int riverRocks, int fallFoam,
                int westMassifPines)
            {
                Pines = pines;
                PineUpright = pineUpright;
                PineLeaning = pineLeaning;
                PineSpreading = pineSpreading;
                Boulders = boulders;
                BoulderUpright = boulderUpright;
                BoulderSquat = boulderSquat;
                BoulderLow = boulderLow;
                Shrubs = shrubs;
                CliffCrest = cliffCrest;
                CliffToe = cliffToe;
                RiverRocks = riverRocks;
                FallFoam = fallFoam;
                WestMassifPines = westMassifPines;
            }
        }

        public static void CheckAuthored(ValleyLayout layout)
        {
            IReadOnlyList<string> violations = ValidateAuthored(layout);
            if (violations.Count > 0)
            {
                throw new LayoutFormatException(violations[0]);
            }
        }

        public static IReadOnlyList<string> ValidateAuthored(ValleyLayout layout)
        {
            if (layout == null)
            {
                return new[] { "AuthoredDimensions: layout cannot be null." };
            }
            List<string> violations = new List<string>();
            CheckDimensionsAndBounds(layout, violations);
            if (layout.Width != ValleyConstants.GridWidth || layout.Depth != ValleyConstants.GridDepth)
            {
                return violations;
            }

            CheckBoundaryRows(layout, violations);
            CheckStartCell(layout, violations);
            CheckCellsAndBands(layout, violations);
            CheckPropsAndCover(layout, violations);
            return violations;
        }

        private static void CheckDimensionsAndBounds(ValleyLayout layout, List<string> violations)
        {
            if (layout.Width != ValleyConstants.GridWidth || layout.Depth != ValleyConstants.GridDepth)
            {
                violations.Add($"AuthoredDimensions: expected {ValleyConstants.GridWidth}x{ValleyConstants.GridDepth}, got {layout.Width}x{layout.Depth}.");
            }
            if (!layout.AirBounds.Equals(ValleyConstants.AuthoredAirBounds))
            {
                violations.Add("AuthoredAirBounds: air bounds must match (8, 152, 4, 89).");
            }
        }

        private static void CheckBoundaryRows(ValleyLayout layout, List<string> violations)
        {
            for (int vx = 0; vx < layout.VertexWidth; vx++)
            {
                if (layout.VertexHeight(vx, 0) != 0f)
                {
                    violations.Add($"Row0Height: vertex ({vx}, 0) must be 0 m.");
                    break;
                }
            }
            for (int cx = 0; cx < layout.Width; cx++)
            {
                if (!ValleyKindMetadata.IsWaterKind(layout.KindAt(cx, 0)))
                {
                    violations.Add($"Row0Water: cell ({cx}, 0) must be river channel or riffle.");
                    break;
                }
            }
            for (int vx = 0; vx < layout.VertexWidth; vx++)
            {
                if (layout.VertexHeight(vx, ValleyConstants.NorthBackRowZ) != ValleyConstants.NorthBackRowHeight)
                {
                    violations.Add($"Row96Height: vertex ({vx}, {ValleyConstants.NorthBackRowZ}) must be 13 m.");
                    break;
                }
            }
        }

        private static void CheckStartCell(ValleyLayout layout, List<string> violations)
        {
            if (layout.StartX != ValleyConstants.AuthoredStartX || layout.StartZ != ValleyConstants.AuthoredStartZ)
            {
                violations.Add($"StartCell: start must be ({ValleyConstants.AuthoredStartX}, {ValleyConstants.AuthoredStartZ}).");
                return;
            }
            if (!layout.IsWalkable(layout.StartX, layout.StartZ)
                || !layout.IsFlatFloor(layout.StartX, layout.StartZ, out int floor)
                || floor != 1)
            {
                violations.Add("StartCell: start cell must be walkable on floor 1.");
            }
        }

        private static void CheckCellsAndBands(ValleyLayout layout, List<string> violations)
        {
            int[] bandCounts = new int[5];
            bool[] kindSeen = new bool[12];
            int coverCells = 0;
            bool outsideWalkable = false;
            bool airCeilingExceeded = false;
            bool coverOnImpassable = false;

            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    CellKind kind = layout.KindAt(cx, cz);
                    kindSeen[(int)kind] = true;

                    if (layout.CoverAt(cx, cz) != CoverKind.None)
                    {
                        coverCells++;
                        if (!ValleyKindMetadata.IsWalkableKind(kind))
                        {
                            coverOnImpassable = true;
                        }
                    }

                    if (layout.IsWalkable(cx, cz))
                    {
                        if (!IsInsideWalkablePerimeter(cx, cz))
                        {
                            outsideWalkable = true;
                        }
                        float centerH = layout.HeightAt(cx + 0.5f, cz + 0.5f);
                        int band = ValleyConstants.BandOf(centerH);
                        bandCounts[band - 1]++;
                    }

                    if (layout.AirBounds.ContainsCellCenter(cx, cz) && layout.SolidTop(cx, cz) > ValleyConstants.MaxSolidTopInAirBounds)
                    {
                        airCeilingExceeded = true;
                    }
                }
            }

            RecordCellRuleViolations(violations, bandCounts, kindSeen, coverCells, outsideWalkable, airCeilingExceeded, coverOnImpassable);
        }

        private static bool IsInsideWalkablePerimeter(int cx, int cz)
        {
            return cx >= ValleyConstants.AuthoredWalkableMinX
                && cx < ValleyConstants.AuthoredWalkableMaxXExclusive
                && cz >= ValleyConstants.AuthoredWalkableMinZ
                && cz < ValleyConstants.AuthoredWalkableMaxZExclusive;
        }

        private static void RecordCellRuleViolations(
            List<string> violations,
            int[] bandCounts,
            bool[] kindSeen,
            int coverCells,
            bool outsideWalkable,
            bool airCeilingExceeded,
            bool coverOnImpassable)
        {
            if (outsideWalkable)
            {
                violations.Add("WalkablePerimeter: walkable cell exists outside x in [16, 144), z in [12, 90).");
            }
            if (airCeilingExceeded)
            {
                violations.Add("AirBoundsSolidTop: cell inside air bounds exceeds SolidTop 15.5 m.");
            }
            if (coverCells > ValleyConstants.MaxCoverCells)
            {
                violations.Add($"CoverCount: {coverCells} exceeds max {ValleyConstants.MaxCoverCells}.");
            }
            if (coverOnImpassable)
            {
                violations.Add("CoverWalkableOnly: cover placed on non-walkable cell.");
            }
            for (int band = 1; band <= 4; band++)
            {
                if (bandCounts[band - 1] < 400)
                {
                    violations.Add($"BandWalkableCount: band {band} has {bandCounts[band - 1]} walkable cells (< 400).");
                }
            }
            for (int k = 0; k < kindSeen.Length; k++)
            {
                if (!kindSeen[k])
                {
                    violations.Add($"AllKindsPresent: missing cell kind {(CellKind)k}.");
                }
            }
        }

        private static void CheckPropsAndCover(ValleyLayout layout, List<string> violations)
        {
            if (layout.Props.Count > ValleyConstants.MaxExplicitProps)
            {
                violations.Add($"PropCount: {layout.Props.Count} exceeds max {ValleyConstants.MaxExplicitProps}.");
            }

            PropSummary summary = InspectProps(layout, violations);
            CheckPropMinimums(summary, violations);
        }

        private static PropSummary InspectProps(ValleyLayout layout, List<string> violations)
        {
            int[] counts = new int[12];
            int westPines = 0;
            bool onSlope = false;
            bool nearStart = false;

            for (int i = 0; i < layout.Props.Count; i++)
            {
                PropPlacement p = layout.Props[i];
                counts[(int)p.Kind]++;
                if (ValleyKindMetadata.IsPine(p.Kind) && p.Cx < 24 && p.Cz >= 12)
                {
                    westPines++;
                }
                if (ValleyKindMetadata.IsBlockingProp(p.Kind))
                {
                    if (ValleyKindMetadata.IsSlopeKind(layout.KindAt(p.Cx, p.Cz)))
                    {
                        onSlope = true;
                    }
                    if (Math.Abs(p.Cx - layout.StartX) <= 3 && Math.Abs(p.Cz - layout.StartZ) <= 3)
                    {
                        nearStart = true;
                    }
                }
            }

            if (onSlope)
            {
                violations.Add("BlockingPropOnSlope: blocking prop placed on a slope cell.");
            }
            if (nearStart)
            {
                violations.Add("BlockingPropNearStart: blocking prop placed within 3 cells of start.");
            }
            return BuildPropSummary(counts, westPines);
        }

        private static PropSummary BuildPropSummary(int[] counts, int westPines)
        {
            int pineU = counts[(int)PropKind.PineUpright];
            int pineL = counts[(int)PropKind.PineLeaning];
            int pineS = counts[(int)PropKind.PineSpreading];
            int bU = counts[(int)PropKind.BoulderUpright];
            int bS = counts[(int)PropKind.BoulderSquat];
            int bL = counts[(int)PropKind.BoulderLow];
            int shrubs = counts[(int)PropKind.ShrubRound] + counts[(int)PropKind.ShrubSpreading];

            return new PropSummary(
                pineU + pineL + pineS, pineU, pineL, pineS,
                bU + bS + bL, bU, bS, bL,
                shrubs,
                counts[(int)PropKind.CliffCrest],
                counts[(int)PropKind.CliffToe],
                counts[(int)PropKind.RiverRocks],
                counts[(int)PropKind.FallFoam],
                westPines);
        }

        private static void CheckPropMinimums(PropSummary s, List<string> violations)
        {
            if (s.Pines < 24 || s.PineUpright < 4 || s.PineLeaning < 4 || s.PineSpreading < 4)
            {
                violations.Add("MinPines: requires >= 24 pines total and >= 4 per variant.");
            }
            if (s.Boulders < 18 || s.BoulderUpright < 4 || s.BoulderSquat < 4 || s.BoulderLow < 4)
            {
                violations.Add("MinBoulders: requires >= 18 boulders total and >= 4 per variant.");
            }
            if (s.Shrubs < 20)
            {
                violations.Add($"MinShrubs: requires >= 20 shrubs, found {s.Shrubs}.");
            }
            if (s.WestMassifPines < 8)
            {
                violations.Add($"WestMassifPines: requires >= 8 pines on West massif, found {s.WestMassifPines}.");
            }
            if (s.CliffCrest < 20 || s.CliffToe < 20)
            {
                violations.Add("MinCliffProps: requires >= 20 CliffCrest and >= 20 CliffToe.");
            }
            if (s.RiverRocks < 8)
            {
                violations.Add($"MinRiverRocks: requires >= 8 RiverRocks, found {s.RiverRocks}.");
            }
            if (s.FallFoam < 3)
            {
                violations.Add($"MinFallFoam: requires >= 3 FallFoam, found {s.FallFoam}.");
            }
        }
    }
}
