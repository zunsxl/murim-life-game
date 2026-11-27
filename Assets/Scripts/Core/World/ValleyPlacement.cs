using System;
using System.Collections.Generic;

namespace Murim.Core.World
{
    /// <summary>
    /// Prop zone evaluation, occupancy grid, anchors, and 16 placement rules (§3.2.2–§3.2.3).
    /// </summary>
    internal static class ValleyPlacement
    {
        private const int OccGridSide = 256;
        private const int OccCellCm = 200;

        private delegate int RuleEval(in ZoneContext c);

        private readonly struct RuleDef
        {
            public RuleDef(byte variant, int cellCm, RuleEval eval)
            {
                Variant = variant;
                CellCm = cellCm;
                Eval = eval;
            }

            public byte Variant { get; }
            public int CellCm { get; }
            public RuleEval Eval { get; }
        }

        private readonly struct ZoneContext
        {
            public ZoneContext(ValleyGenerationState state, int xCm, int zCm)
            {
                int ix = Primitives.Clamp(Primitives.FloorDiv(xCm + 50, 100), 0, ValleyWorld.SizeM);
                int iz = Primitives.Clamp(Primitives.FloorDiv(zCm + 50, 100), 0, ValleyWorld.SizeM);
                int idx = iz * ValleyWorld.VertsPerSide + ix;

                G = state.Grade[idx];
                Wd = state.Wd[idx];
                Rt = state.Rt[idx];
                L = state.L[idx];
                Eb = state.Eb[idx];
                S1 = state.S1[idx];
                S2 = state.S2[idx];
                E1 = state.E1[idx];
                E2 = state.E2[idx];
                B1 = state.B1[ix];
                B2 = state.B2[ix];
                Hw1 = state.Hw1[ix];
                Hw2 = state.Hw2[ix];
                Grove = Primitives.Noise(ix, iz, 56, state.Salts[12]);
                Flower = Primitives.Noise(ix, iz, 28, state.Salts[13]);
            }

            public int G { get; }
            public int Wd { get; }
            public int Rt { get; }
            public int L { get; }
            public int Eb { get; }
            public int S1 { get; }
            public int S2 { get; }
            public int E1 { get; }
            public int E2 { get; }
            public int B1 { get; }
            public int B2 { get; }
            public int Hw1 { get; }
            public int Hw2 { get; }
            public int Grove { get; }
            public int Flower { get; }

            public bool IsBankFace => G >= 60;

            public bool IsBankTop =>
                Rt < 300 && (MatchBank(E1, Hw1, B1, -150, 350) || MatchBank(E2, Hw2, B2, -150, 350));

            public bool IsBankLip =>
                Rt < 300 && (MatchBank(E1, Hw1, B1, -220, -40) || MatchBank(E2, Hw2, B2, -220, -40));

            public bool IsBankFoot =>
                Rt < 300 && (MatchBank(E1, -Hw1, B1, -350, 150) || MatchBank(E2, -Hw2, B2, -350, 150));

            public bool IsSlopeSite =>
                (B1 >= 512 && S1 >= 60 && S1 <= 964) || (B2 >= 512 && S2 >= 60 && S2 <= 964);

            public bool IsPlateau =>
                G < 20 && Rt == 0 && (S1 <= 13 || S1 >= 1011) && (S2 <= 13 || S2 >= 1011);

            private static bool MatchBank(int e, int anchor, int b, int minOff, int maxOff)
            {
                return b < 512 && e >= anchor + minOff && e <= anchor + maxOff;
            }
        }

        private sealed class PlacementState
        {
            public readonly bool[] Occupied = new bool[OccGridSide * OccGridSide];
            public readonly int[] FamilyCounts = new int[5];
            public readonly int[] VariantCounts = new int[PropCatalog.VariantCount];
            public readonly List<PropInstance> Props = new List<PropInstance>(8192);
        }

        private static readonly RuleDef[] Rules =
        {
            new RuleDef(PropCatalog.RockOutcrop, 1600, Rule0),
            new RuleDef(PropCatalog.RockBoulder, 800, Rule1),
            new RuleDef(PropCatalog.BambooDense, 600, Rule2),
            new RuleDef(PropCatalog.PineTall, 900, Rule3),
            new RuleDef(PropCatalog.PineLeaning, 800, Rule4),
            new RuleDef(PropCatalog.PineYoung, 800, Rule5),
            new RuleDef(PropCatalog.BambooEdge, 600, Rule6),
            new RuleDef(PropCatalog.BambooYoung, 1200, Rule7),
            new RuleDef(PropCatalog.RockRidged, 1000, Rule8),
            new RuleDef(PropCatalog.RockRiverstone, 500, Rule9),
            new RuleDef(PropCatalog.RockCluster, 900, Rule10),
            new RuleDef(PropCatalog.BankLip, 300, Rule11),
            new RuleDef(PropCatalog.FloraShrub, 500, Rule12),
            new RuleDef(PropCatalog.FloraFern, 250, Rule13),
            new RuleDef(PropCatalog.FloraWildflower, 300, Rule14),
            new RuleDef(PropCatalog.FloraTussock, 300, Rule15),
        };

        public static void PlaceProps(ValleyGenerationState state)
        {
            var ps = new PlacementState();
            PlaceAnchors(state, ps);
            for (int r = 0; r < Rules.Length; r++)
            {
                RunRule(state, ps, r);
            }

            state.Props = ps.Props.ToArray();
        }

        private static void PlaceAnchors(ValleyGenerationState state, PlacementState ps)
        {
            ValleyLayout l = state.Layout;
            int zTop2 = Primitives.FindFirstZAtLeast(state.S2, l.Slope2X, 1000);

            AddAnchor(state, ps, new PropInstance(PropCatalog.PineAncient, (l.WfX - 18) * 100, 0, (l.FallZ + 9) * 100, 105, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.PineAncient, (l.WfX + 18) * 100, 0, (l.FallZ + 9) * 100, 105, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.PineAncient, l.Slope2X * 100, 0, (zTop2 + 10) * 100, 105, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.PineLeaning, (l.WfX - 9) * 100, 0, (l.FallZ + 4) * 100, 100, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.PineLeaning, (l.WfX + 9) * 100, 0, (l.FallZ + 4) * 100, 100, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.RockOutcrop, (l.WfX - 7) * 100, 0, (l.FallZ - 1) * 100, 100, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.RockOutcrop, (l.WfX + 7) * 100, 0, (l.FallZ - 1) * 100, 100, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.BambooYoung, (l.WfX - 14) * 100, 0, (l.ZPool - 2) * 100, 100, 100));
            AddAnchor(state, ps, new PropInstance(PropCatalog.BambooYoung, (l.WfX + 14) * 100, 0, (l.ZPool - 2) * 100, 100, 100));
        }

        private static void AddAnchor(ValleyGenerationState state, PlacementState ps, PropInstance raw)
        {
            PropVariantSpec spec = PropCatalog.Get(raw.Variant);
            int yCm = ComputeYCm(state.Height, raw.XCm, raw.ZCm, spec.YRule);
            var placed = new PropInstance(raw.Variant, raw.XCm, yCm, raw.ZCm, raw.ScalePct, raw.TonePct);
            ps.Props.Add(placed);
            ps.FamilyCounts[(int)spec.Family]++;
            ps.VariantCounts[spec.Variant]++;
            MarkOccupancy(ps.Occupied, raw.XCm, raw.ZCm, spec.FootprintCm + 100);
        }

        private readonly struct Candidate
        {
            public Candidate(int ruleIndex, int xCm, int zCm, uint h2, int densityPct)
            {
                RuleIndex = ruleIndex;
                XCm = xCm;
                ZCm = zCm;
                H2 = h2;
                DensityPct = densityPct;
            }

            public int RuleIndex { get; }
            public int XCm { get; }
            public int ZCm { get; }
            public uint H2 { get; }
            public int DensityPct { get; }
        }

        private readonly struct CellBounds
        {
            public CellBounds(int xCm, int zCm, int radiusCm)
            {
                MinGx = Primitives.Clamp(Primitives.FloorDiv(xCm - radiusCm - 100, OccCellCm), 0, OccGridSide - 1);
                MaxGx = Primitives.Clamp(Primitives.FloorDiv(xCm + radiusCm - 100, OccCellCm), 0, OccGridSide - 1);
                MinGz = Primitives.Clamp(Primitives.FloorDiv(zCm - radiusCm - 100, OccCellCm), 0, OccGridSide - 1);
                MaxGz = Primitives.Clamp(Primitives.FloorDiv(zCm + radiusCm - 100, OccCellCm), 0, OccGridSide - 1);
            }

            public int MinGx { get; }
            public int MaxGx { get; }
            public int MinGz { get; }
            public int MaxGz { get; }
        }

        private static void RunRule(ValleyGenerationState state, PlacementState ps, int ruleIndex)
        {
            RuleDef rule = Rules[ruleIndex];
            PropVariantSpec spec = PropCatalog.Get(rule.Variant);
            int maxIndex = Primitives.FloorDiv(51200, rule.CellCm);
            uint salt1 = Primitives.Salt(100 + ruleIndex, state.Seed);
            uint salt2 = Primitives.Salt(200 + ruleIndex, state.Seed);
            int densityPct = GetFamilyDensity(state.Config, spec.Family);

            for (int j = 0; j <= maxIndex; j++)
            {
                for (int i = 0; i <= maxIndex; i++)
                {
                    uint h = Primitives.Hash32(i, j, salt1);
                    int xCm = i * rule.CellCm + (int)(((h & 0xFFFFu) * (uint)rule.CellCm) >> 16);
                    int zCm = j * rule.CellCm + (int)((((h >> 16) & 0xFFFFu) * (uint)rule.CellCm) >> 16);
                    if (xCm < 200 || xCm > 51000 || zCm < 200 || zCm > 51000)
                    {
                        continue;
                    }

                    var candidate = new Candidate(ruleIndex, xCm, zCm, Primitives.Hash32(i, j, salt2), densityPct);
                    TryPlaceCandidate(state, ps, spec, in candidate);
                }
            }
        }

        private static void TryPlaceCandidate(
            ValleyGenerationState state,
            PlacementState ps,
            PropVariantSpec spec,
            in Candidate c)
        {
            if (HasReachedCap(ps, spec))
            {
                return;
            }

            var ctx = new ZoneContext(state, c.XCm, c.ZCm);
            int threshold = Rules[c.RuleIndex].Eval(in ctx);
            if (threshold <= 0)
            {
                return;
            }

            int roll = (int)(c.H2 & 0xFFu);
            int cutoff = Math.Min(255, Primitives.FloorDiv(threshold * c.DensityPct, 100));
            if (roll >= cutoff)
            {
                return;
            }

            if (!CheckAndUpdateOccupancy(ps.Occupied, spec, c.XCm, c.ZCm))
            {
                return;
            }

            byte tonePct = (byte)(92 + Primitives.FloorDiv((int)((c.H2 >> 8) & 0xFFu) * 8, 255));
            ushort scalePct = (ushort)(spec.MinScalePct + Primitives.FloorDiv((int)((c.H2 >> 24) & 0xFFu) * (spec.MaxScalePct - spec.MinScalePct), 255));
            int yCm = ComputeYCm(state.Height, c.XCm, c.ZCm, spec.YRule);
            ps.Props.Add(new PropInstance(spec.Variant, c.XCm, yCm, c.ZCm, scalePct, tonePct));
            ps.FamilyCounts[(int)spec.Family]++;
            ps.VariantCounts[spec.Variant]++;
        }

        private static bool HasReachedCap(PlacementState ps, PropVariantSpec spec)
        {
            return ps.FamilyCounts[(int)spec.Family] >= PropCatalog.GetFamilyCap(spec.Family)
                || ps.VariantCounts[spec.Variant] >= PropCatalog.GetVariantCap(spec.Variant);
        }

        private static int GetFamilyDensity(WorldConfig config, PropFamily family)
        {
            switch (family)
            {
                case PropFamily.Pine:
                case PropFamily.Bamboo:
                    return config.TreeDensityPct;
                case PropFamily.Rock:
                case PropFamily.Lip:
                    return config.RockDensityPct;
                default:
                    return config.FloraDensityPct;
            }
        }

        private static int Rule0(in ZoneContext c)
        {
            if (c.Wd < 400)
            {
                return 0;
            }

            if (c.IsBankFace && c.Rt < 300)
            {
                return 90;
            }

            return c.Rt >= 512 && c.Grove < 0 ? 100 : 0;
        }

        private static int Rule1(in ZoneContext c)
        {
            if (c.Wd < 200)
            {
                return 0;
            }

            if (c.IsBankFace && c.Eb >= 512)
            {
                return 130;
            }

            if (c.Rt > 0)
            {
                return 40;
            }

            return c.G < 60 ? 13 : 0;
        }

        private static int Rule2(in ZoneContext c)
        {
            return c.G < 80 && c.Rt >= 300 && c.Grove >= -100 ? 204 : 0;
        }

        private static int Rule3(in ZoneContext c)
        {
            if (c.G >= 60 || c.Wd < 500)
            {
                return 0;
            }

            if (c.Rt >= 200 && c.Grove < 100)
            {
                return 140;
            }

            if (c.IsPlateau && c.L >= 1996)
            {
                return 26;
            }

            if (c.IsPlateau && c.L >= 972)
            {
                return 13;
            }

            return c.IsPlateau ? 8 : 0;
        }

        private static int Rule4(in ZoneContext c)
        {
            if (c.G >= 70 || c.Wd < 300)
            {
                return 0;
            }

            if (c.IsBankTop)
            {
                return 128;
            }

            return c.Wd <= 800 ? 26 : 0;
        }

        private static int Rule5(in ZoneContext c)
        {
            if (c.G >= 60 || c.Wd < 400)
            {
                return 0;
            }

            if (c.IsSlopeSite)
            {
                return 64;
            }

            if (c.Rt > 0 && c.Rt < 300)
            {
                return 77;
            }

            return c.IsPlateau ? 10 : 0;
        }

        private static int Rule6(in ZoneContext c)
        {
            return c.G < 60 && c.Wd >= 400 && c.Rt > 50 && c.Rt < 300 && c.Grove >= -200 ? 153 : 0;
        }

        private static int Rule7(in ZoneContext c)
        {
            if (c.G >= 50 || c.Wd < 300)
            {
                return 0;
            }

            if (c.Wd <= 900)
            {
                return 38;
            }

            if (c.IsBankTop)
            {
                return 26;
            }

            return c.IsPlateau && c.Grove >= 250 ? 26 : 0;
        }

        private static int Rule8(in ZoneContext c)
        {
            if (c.Wd < 200)
            {
                return 0;
            }

            if (c.IsSlopeSite)
            {
                return 51;
            }

            return c.Rt >= 300 && c.Grove < 0 ? 64 : 0;
        }

        private static int Rule9(in ZoneContext c)
        {
            return c.Wd >= 1 && c.Wd <= 300 ? 200 : 0;
        }

        private static int Rule10(in ZoneContext c)
        {
            if (c.Wd < 100)
            {
                return 0;
            }

            if (c.IsBankFoot)
            {
                return 90;
            }

            return c.G < 60 ? 26 : 0;
        }

        private static int Rule11(in ZoneContext c)
        {
            return c.Wd >= 300 && c.IsBankLip ? 180 : 0;
        }

        private static int Rule12(in ZoneContext c)
        {
            if (c.Wd < 150 || c.G >= 35)
            {
                return 0;
            }

            if (c.IsBankFoot || c.IsBankTop || (c.Rt > 0 && c.Rt < 250))
            {
                return 77;
            }

            return c.IsPlateau ? 8 : 0;
        }

        private static int Rule13(in ZoneContext c)
        {
            return c.G < 35 && c.Wd >= 20 && c.Wd <= 500 ? 140 : 0;
        }

        private static int Rule14(in ZoneContext c)
        {
            if (c.Wd < 150)
            {
                return 0;
            }

            if (c.IsPlateau && c.Flower >= 250 && c.L < 1536)
            {
                return 140;
            }

            return c.IsPlateau ? 5 : 0;
        }

        private static int Rule15(in ZoneContext c)
        {
            return c.Wd >= 100 && c.G < 35 && c.Rt < 400 ? 56 : 0;
        }

        private static bool CheckAndUpdateOccupancy(bool[] occupied, PropVariantSpec spec, int xCm, int zCm)
        {
            if (spec.Solid)
            {
                int radiusCm = spec.FootprintCm + 100;
                if (ScanDisc(occupied, xCm, zCm, radiusCm, false))
                {
                    return false;
                }

                ScanDisc(occupied, xCm, zCm, radiusCm, true);
                return true;
            }

            if (spec.Variant == PropCatalog.RockRiverstone)
            {
                return true;
            }

            int cx = Primitives.Clamp(Primitives.FloorDiv(xCm, OccCellCm), 0, OccGridSide - 1);
            int cz = Primitives.Clamp(Primitives.FloorDiv(zCm, OccCellCm), 0, OccGridSide - 1);
            return !occupied[cz * OccGridSide + cx];
        }

        private static void MarkOccupancy(bool[] occupied, int xCm, int zCm, int radiusCm)
        {
            ScanDisc(occupied, xCm, zCm, radiusCm, true);
        }

        private static bool ScanDisc(bool[] occupied, int xCm, int zCm, int radiusCm, bool mark)
        {
            var bounds = new CellBounds(xCm, zCm, radiusCm);
            long rSq = (long)radiusCm * radiusCm;
            for (int gz = bounds.MinGz; gz <= bounds.MaxGz; gz++)
            {
                int cz = gz * OccCellCm + 100 - zCm;
                long czSq = (long)cz * cz;
                int rowOffset = gz * OccGridSide;
                for (int gx = bounds.MinGx; gx <= bounds.MaxGx; gx++)
                {
                    int cx = gx * OccCellCm + 100 - xCm;
                    if ((long)cx * cx + czSq > rSq)
                    {
                        continue;
                    }

                    if (mark)
                    {
                        occupied[rowOffset + gx] = true;
                    }
                    else if (occupied[rowOffset + gx])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int ComputeYCm(int[] height, int xCm, int zCm, PropYRule rule)
        {
            int cx = Primitives.Clamp(Primitives.FloorDiv(xCm, 100), 0, ValleyWorld.SizeM - 1);
            int cz = Primitives.Clamp(Primitives.FloorDiv(zCm, 100), 0, ValleyWorld.SizeM - 1);
            int i00 = cz * ValleyWorld.VertsPerSide + cx;
            int h00 = height[i00];
            int h10 = height[i00 + 1];
            int h01 = height[i00 + ValleyWorld.VertsPerSide];
            int h11 = height[i00 + ValleyWorld.VertsPerSide + 1];

            if (rule == PropYRule.CellMin)
            {
                return Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
            }

            int fx = xCm - cx * 100;
            int fz = zCm - cz * 100;
            int h0 = h00 + Primitives.FloorDiv((h10 - h00) * fx, 100);
            int h1 = h01 + Primitives.FloorDiv((h11 - h01) * fx, 100);
            return h0 + Primitives.FloorDiv((h1 - h0) * fz, 100);
        }
    }
}
