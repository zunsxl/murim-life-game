using System;
using System.Collections.Generic;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Valley
{
    [TestFixture]
    public sealed class ValleyLayoutTests
    {
        [Test]
        public void T_CORE_01_AuthoredDimensionsAndMalformedInputs()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            Assert.That(layout.Width, Is.EqualTo(160));
            Assert.That(layout.Depth, Is.EqualTo(96));
            Assert.That(layout.VertexWidth, Is.EqualTo(161));
            Assert.That(layout.VertexDepth, Is.EqualTo(97));

            // Wrong row length
            string[] badHeightRow = (string[])ValleyLayoutData.Heights.Clone();
            badHeightRow[0] = "000";
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                badHeightRow, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props,
                ValleyConstants.AuthoredAirBounds, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ)));

            // Unknown cell character
            string[] badCellChar = (string[])ValleyLayoutData.Cells.Clone();
            badCellChar[28] = "?" + badCellChar[28].Substring(1);
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                ValleyLayoutData.Heights, badCellChar, ValleyLayoutData.Cover, ValleyLayoutData.Props,
                ValleyConstants.AuthoredAirBounds, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ)));

            // Out-of-range height ('z' = 17.5 m > 13 m)
            string[] outOfRangeHeight = (string[])ValleyLayoutData.Heights.Clone();
            outOfRangeHeight[95] = "z" + outOfRangeHeight[95].Substring(1);
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                outOfRangeHeight, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props,
                ValleyConstants.AuthoredAirBounds, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ)));

            // Unknown height character
            string[] badHeightChar = (string[])ValleyLayoutData.Heights.Clone();
            badHeightChar[10] = "!" + badHeightChar[10].Substring(1);
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                badHeightChar, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props,
                ValleyConstants.AuthoredAirBounds, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ)));

            // Non-walkable start cell throws LayoutFormatException
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                ValleyLayoutData.Heights, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props,
                ValleyConstants.AuthoredAirBounds, 0, 0)));

            // Prop on disallowed kind throws LayoutFormatException
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                ValleyLayoutData.Heights, ValleyLayoutData.Cells, ValleyLayoutData.Cover,
                new[] { new PropPlacement(PropKind.BoulderUpright, 0, 0, false) },
                ValleyConstants.AuthoredAirBounds, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ)));
        }

        [Test]
        public void T_CORE_02_EdgeDeltasAndSlopeShoulders()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();

            // Every cell with edge delta > 0.5 m must be an impassable kind
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.MaxEdgeDelta(cx, cz) > ValleyConstants.MaxWalkEdgeDelta)
                    {
                        Assert.That(ValleyKindMetadata.IsWalkableKind(layout.KindAt(cx, cz)), Is.False,
                            $"Cell ({cx}, {cz}) has edge delta > 0.5 m but walkable kind {layout.KindAt(cx, cz)}.");
                    }
                }
            }

            // Every slope shoulder cell of S1-S4 follows the §3.1 rule
            (int x0, int x1, int z0, int z1, CellKind slopeKind)[] slopes =
            {
                (52, 60, 40, 46, CellKind.EarthenSlope),
                (100, 106, 40, 46, CellKind.RockySlope),
                (80, 86, 58, 64, CellKind.RockySlope),
                (40, 46, 72, 78, CellKind.EarthenSlope)
            };

            foreach ((int x0, int x1, int z0, int z1, CellKind slopeKind) in slopes)
            {
                foreach (int sx in new[] { x0 - 1, x1 })
                {
                    for (int cz = z0; cz < z1; cz++)
                    {
                        bool gentle = layout.MaxEdgeDelta(sx, cz) <= ValleyConstants.MaxWalkEdgeDelta;
                        CellKind expected = gentle ? slopeKind : CellKind.Cliff;
                        Assert.That(layout.KindAt(sx, cz), Is.EqualTo(expected), $"Shoulder ({sx}, {cz}) mismatch.");
                        Assert.That(layout.IsWalkable(sx, cz), Is.EqualTo(gentle));
                    }
                }
            }

            // Fixture with a 1 m step under a meadow cell throws LayoutFormatException
            string[] badHeights = { "000", "020", "020" };
            string[] badCells = { "mm", "mm" };
            string[] badCover = { "..", ".." };
            Assert.Throws<LayoutFormatException>(() => ValleyLayoutParser.Parse(new LayoutInput(
                badHeights, badCells, badCover, Array.Empty<PropPlacement>(), new AirBounds(0f, 2f, 0f, 2f), 0, 0)));
        }

        [Test]
        public void T_CORE_03_CheckAuthoredAndBrokenRules()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            Assert.DoesNotThrow(() => ValleyLayoutRules.CheckAuthored(layout));

            // 1. Row 0 height != 0
            string[] hRow0 = (string[])ValleyLayoutData.Heights.Clone();
            hRow0[0] = "1" + hRow0[0].Substring(1);
            AssertBrokenRule("Row0Height", hRow0, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 2. Row 0 non-water
            string[] cRow0 = (string[])ValleyLayoutData.Cells.Clone();
            cRow0[0] = "c" + cRow0[0].Substring(1);
            AssertBrokenRule("Row0Water", ValleyLayoutData.Heights, cRow0, ValleyLayoutData.Cover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 3. Row z=96 height != 13
            string[] hRow96 = (string[])ValleyLayoutData.Heights.Clone();
            hRow96[96] = "p" + hRow96[96].Substring(1);
            AssertBrokenRule("Row96Height", hRow96, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 4. Walkable outside perimeter x in [16, 144), z in [12, 90)
            string[] cOutside = (string[])ValleyLayoutData.Cells.Clone();
            cOutside[20] = ReplaceChar(cOutside[20], 15, 'm');
            string[] hOutside = (string[])ValleyLayoutData.Heights.Clone();
            hOutside[20] = ReplaceChar(hOutside[20], 15, '0');
            hOutside[21] = ReplaceChar(hOutside[21], 15, '0');
            AssertBrokenRule("WalkablePerimeter", hOutside, cOutside, ValleyLayoutData.Cover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 5. Band 4 < 400 walkable cells
            string[] cNoBand4 = (string[])ValleyLayoutData.Cells.Clone();
            string[] vNoBand4 = (string[])ValleyLayoutData.Cover.Clone();
            for (int z = 77; z < 83; z++)
            {
                cNoBand4[z] = ReplaceRange(cNoBand4[z], 24, 124, 'c');
                vNoBand4[z] = ReplaceRange(vNoBand4[z], 24, 124, '.');
            }
            List<PropPlacement> propsNoBand4 = new List<PropPlacement>();
            foreach (PropPlacement p in ValleyLayoutData.Props)
            {
                if (p.Cz < 77 || p.Cz >= 83)
                {
                    propsNoBand4.Add(p);
                }
            }
            AssertBrokenRule("BandWalkableCount", ValleyLayoutData.Heights, cNoBand4, vNoBand4, propsNoBand4, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 6. Missing a cell kind (remove all 'v' riffles)
            string[] cNoRiffle = (string[])ValleyLayoutData.Cells.Clone();
            for (int z = 0; z < 96; z++)
            {
                cNoRiffle[z] = cNoRiffle[z].Replace('v', 'w');
            }
            AssertBrokenRule("AllKindsPresent", ValleyLayoutData.Heights, cNoRiffle, ValleyLayoutData.Cover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 7. Wrong start cell
            AssertBrokenRule("StartCell", ValleyLayoutData.Heights, ValleyLayoutData.Cells, ValleyLayoutData.Cover, ValleyLayoutData.Props, 71, 28);

            // 8. SolidTop > 15.5 inside air bounds (PineUpright 8 m on 9 m floor 4 inside air bounds -> 17 m)
            List<PropPlacement> propsTooTall = new List<PropPlacement>(ValleyLayoutData.Props)
            {
                new PropPlacement(PropKind.PineUpright, 70, 80, false)
            };
            AssertBrokenRule("AirBoundsSolidTop", ValleyLayoutData.Heights, ValleyLayoutData.Cells, ValleyLayoutData.Cover, propsTooTall, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 9. Cover on impassable cell
            string[] vOnWater = (string[])ValleyLayoutData.Cover.Clone();
            vOnWater[0] = "a" + vOnWater[0].Substring(1);
            AssertBrokenRule("CoverWalkableOnly", ValleyLayoutData.Heights, ValleyLayoutData.Cells, vOnWater, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 10. Explicit props > 400
            List<PropPlacement> tooManyProps = new List<PropPlacement>(ValleyLayoutData.Props);
            while (tooManyProps.Count <= ValleyConstants.MaxExplicitProps)
            {
                tooManyProps.Add(new PropPlacement(PropKind.ShrubRound, 70, 28, false));
            }
            AssertBrokenRule("PropCount", ValleyLayoutData.Heights, ValleyLayoutData.Cells, ValleyLayoutData.Cover, tooManyProps, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);

            // 11. Cover cells > 4000
            string[] tooMuchCover = (string[])ValleyLayoutData.Cover.Clone();
            for (int z = 15; z < 43; z++)
            {
                tooMuchCover[z] = ReplaceRange(tooMuchCover[z], 16, 124, 'a');
            }
            for (int z = 45; z < 61; z++)
            {
                tooMuchCover[z] = ReplaceRange(tooMuchCover[z], 16, 124, 'a');
            }
            for (int z = 0; z < 96; z++)
            {
                char[] cRow = ValleyLayoutData.Cells[z].ToCharArray();
                char[] vRow = tooMuchCover[z].ToCharArray();
                for (int x = 0; x < 160; x++)
                {
                    if (!ValleyKindMetadata.IsWalkableKind(ParseKind(cRow[x])))
                    {
                        vRow[x] = '.';
                    }
                }
                tooMuchCover[z] = new string(vRow);
            }
            AssertBrokenRule("CoverCount", ValleyLayoutData.Heights, ValleyLayoutData.Cells, tooMuchCover, ValleyLayoutData.Props, ValleyConstants.AuthoredStartX, ValleyConstants.AuthoredStartZ);
        }

        [Test]
        public void T_CORE_04_AuthoredComponentsAndSlopes()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            int startComp = layout.ComponentOf(layout.StartX, layout.StartZ);
            Assert.That(startComp, Is.GreaterThan(0));

            bool[] bandsReached = new bool[5];
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.ComponentOf(cx, cz) == startComp)
                    {
                        int band = ValleyConstants.BandOf(layout.HeightAt(cx + 0.5f, cz + 0.5f));
                        bandsReached[band] = true;
                    }
                }
            }

            Assert.That(bandsReached[1], Is.True);
            Assert.That(bandsReached[2], Is.True);
            Assert.That(bandsReached[3], Is.True);
            Assert.That(bandsReached[4], Is.True);

            int loneComp = layout.ComponentOf(32, 52);
            int gladeComp = layout.ComponentOf(65, 86);
            Assert.That(loneComp, Is.GreaterThan(0));
            Assert.That(gladeComp, Is.GreaterThan(0));
            Assert.That(loneComp, Is.Not.EqualTo(startComp));
            Assert.That(gladeComp, Is.Not.EqualTo(startComp));
            Assert.That(loneComp, Is.Not.EqualTo(gladeComp));

            (int x0, int x1, int z0, int z1)[] slopes =
            {
                (52, 60, 40, 46),
                (100, 106, 40, 46),
                (80, 86, 58, 64),
                (40, 46, 72, 78)
            };
            foreach ((int x0, int x1, int z0, int z1) in slopes)
            {
                for (int cz = z0; cz < z1; cz++)
                {
                    int walkableInRow = 0;
                    for (int cx = x0; cx < x1; cx++)
                    {
                        if (layout.IsWalkable(cx, cz))
                        {
                            walkableInRow++;
                        }
                    }
                    Assert.That(walkableInRow, Is.GreaterThanOrEqualTo(6));
                }
            }
        }

        [Test]
        public void T_CORE_05_HeightAtInterpolationOnTier12()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            Assert.That(flat8.HeightAt(3.5f, 3.5f), Is.EqualTo(0f));

            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            // Slope 1 cell (1, 2) rises 0.5 -> 1.0 northward
            Assert.That(tier12.HeightAt(1.5f, 2.5f), Is.EqualTo(0.75f).Within(1e-5f));
            // Edge midpoints
            Assert.That(tier12.HeightAt(1.5f, 2.0f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(tier12.HeightAt(1.5f, 3.0f), Is.EqualTo(1.0f).Within(1e-5f));
            // Test both triangles (u > v and v > u) on asymmetric shoulder cell (3, 2):
            // corners (3,2)=0.5, (4,2)=0.0, (3,3)=1.0, (4,3)=0.0
            // u = 0.75, v = 0.25 (SE triangle u >= v): 0.5 + 0.75*(0-0.5) + 0.25*(0-0) = 0.125
            Assert.That(tier12.HeightAt(3.75f, 2.25f), Is.EqualTo(0.125f).Within(1e-5f));
            // u = 0.25, v = 0.75 (NW triangle v > u): 0.5 + 0.25*(0-1.0) + 0.75*(1.0-0.5) = 0.625
            Assert.That(tier12.HeightAt(3.25f, 2.75f), Is.EqualTo(0.625f).Within(1e-5f));
        }

        [Test]
        public void T_CORE_06_SolidTopSurfaceAtAndAirClearance()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ValleyLayout noSlope = ValleyTestFixtures.CreateTier12NoSlope();
            Assert.That(noSlope.IsWalkable(1, 3), Is.False);

            // 3 m ground at (4, 5): Layer 1 (5 m >= 3 + 1.5) is clear
            Assert.That(tier12.IsAirClear(4, 5, 1), Is.True);
            // BoulderUpright on 3 m terrace at (5, 5): SolidTop = 5 m, Layer 1 (5 < 5 + 1.5) is NOT clear
            Assert.That(tier12.SolidTop(5, 5), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(tier12.SurfaceAt(5.5f, 5.5f), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(tier12.IsAirClear(5, 5, 1), Is.False);
            Assert.That(tier12.IsAirClear(5, 5, 2), Is.True);
            // Over cliff cell (4, 3): SurfaceAt == HeightAt
            Assert.That(tier12.SurfaceAt(4.5f, 3.5f), Is.EqualTo(tier12.HeightAt(4.5f, 3.5f)).Within(1e-5f));

            ValleyLayout authored = ValleyLayoutData.CreateLayout();
            for (int cz = 0; cz < authored.Depth; cz++)
            {
                for (int cx = 0; cx < authored.Width; cx++)
                {
                    bool inside = authored.AirBounds.ContainsCellCenter(cx, cz);
                    if (inside)
                    {
                        Assert.That(authored.SolidTop(cx, cz), Is.LessThanOrEqualTo(ValleyConstants.MaxSolidTopInAirBounds));
                        Assert.That(authored.IsAirClear(cx, cz, 3), Is.True);
                    }
                    else
                    {
                        Assert.That(authored.IsAirClear(cx, cz, 3), Is.False);
                    }
                }
            }
        }

        [Test]
        public void T_CORE_07_RaycastGroundCapAndUncutCrest()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            Vec3 dir45 = new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);

            // 45° ray onto 0 m low ground at (4.5, 0, 1.5)
            RayHit lowHit = ValleyRaycaster.Raycast(tier12, new Vec3(4.5f, 10f, -8.5f), dir45, ValleyConstants.FullViewCutY);
            Assert.That(lowHit.Kind, Is.EqualTo(RayHitKind.Ground));
            Assert.That(lowHit.CellX, Is.EqualTo(4));
            Assert.That(lowHit.CellZ, Is.EqualTo(1));
            Assert.That(lowHit.Point.Y, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(lowHit.Point.Z, Is.EqualTo(1.5f).Within(1e-3f));

            // 45° ray onto 3 m terrace at (4.5, 3, 5.5) in Full View
            RayHit terraceHit = ValleyRaycaster.Raycast(tier12, new Vec3(4.5f, 13f, -4.5f), dir45, ValleyConstants.FullViewCutY);
            Assert.That(terraceHit.Kind, Is.EqualTo(RayHitKind.Ground));
            Assert.That(terraceHit.CellX, Is.EqualTo(4));
            Assert.That(terraceHit.CellZ, Is.EqualTo(5));
            Assert.That(terraceHit.Point.Y, Is.EqualTo(3f).Within(1e-3f));

            // Same ray with cutY = 2.98 hits Cap at y = 2.98 over the terrace
            RayHit capHit = ValleyRaycaster.Raycast(tier12, new Vec3(4.5f, 13f, -4.5f), dir45, ValleyConstants.CutYForFloor(1));
            Assert.That(capHit.Kind, Is.EqualTo(RayHitKind.Cap));
            Assert.That(capHit.Point.Y, Is.EqualTo(2.98f).Within(1e-4f));

            // Ray leaving grid returns None
            RayHit miss = ValleyRaycaster.Raycast(tier12, new Vec3(20f, 10f, 5f), dir45, ValleyConstants.FullViewCutY);
            Assert.That(miss.Kind, Is.EqualTo(RayHitKind.None));

            // Authored: ray onto crest at z = 93 with cutY = 2.98 returns Ground at full height
            ValleyLayout authored = ValleyLayoutData.CreateLayout();
            float crestH = authored.HeightAt(80.5f, 93.5f);
            Vec3 crestOrigin = new Vec3(80.5f, 84.85f, 93.5f - (84.85f - crestH));
            RayHit crestHit = ValleyRaycaster.Raycast(authored, crestOrigin, dir45, ValleyConstants.CutYForFloor(1));
            Assert.That(crestHit.Kind, Is.EqualTo(RayHitKind.Ground));
            Assert.That(crestHit.Point.Z, Is.GreaterThanOrEqualTo(ValleyConstants.NorthUncutZ));
            Assert.That(crestHit.Point.Y, Is.EqualTo(crestH).Within(1e-2f));
        }

        [Test]
        public void T_CORE_08_AuthoredPropsAndCover()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            Assert.That(layout.Props.Count, Is.LessThanOrEqualTo(ValleyConstants.MaxExplicitProps));

            int[] counts = new int[12];
            int westMassifPines = 0;

            foreach (PropPlacement prop in layout.Props)
            {
                CellKind kind = layout.KindAt(prop.Cx, prop.Cz);
                Assert.That(ValleyKindMetadata.IsPropAllowedOn(prop.Kind, kind), Is.True);
                counts[(int)prop.Kind]++;

                if (ValleyKindMetadata.IsPine(prop.Kind) && prop.Cx < 24 && prop.Cz >= 12)
                {
                    westMassifPines++;
                }

                if (ValleyKindMetadata.IsBlockingProp(prop.Kind))
                {
                    Assert.That(ValleyKindMetadata.IsSlopeKind(kind), Is.False);
                    int dx = Math.Abs(prop.Cx - layout.StartX);
                    int dz = Math.Abs(prop.Cz - layout.StartZ);
                    Assert.That(Math.Max(dx, dz), Is.GreaterThan(3));
                }
            }

            int pines = counts[(int)PropKind.PineUpright] + counts[(int)PropKind.PineLeaning] + counts[(int)PropKind.PineSpreading];
            int boulders = counts[(int)PropKind.BoulderUpright] + counts[(int)PropKind.BoulderSquat] + counts[(int)PropKind.BoulderLow];
            int shrubs = counts[(int)PropKind.ShrubRound] + counts[(int)PropKind.ShrubSpreading];

            Assert.That(pines, Is.GreaterThanOrEqualTo(24));
            Assert.That(counts[(int)PropKind.PineUpright], Is.GreaterThanOrEqualTo(4));
            Assert.That(counts[(int)PropKind.PineLeaning], Is.GreaterThanOrEqualTo(4));
            Assert.That(counts[(int)PropKind.PineSpreading], Is.GreaterThanOrEqualTo(4));
            Assert.That(westMassifPines, Is.GreaterThanOrEqualTo(8));

            Assert.That(boulders, Is.GreaterThanOrEqualTo(18));
            Assert.That(counts[(int)PropKind.BoulderUpright], Is.GreaterThanOrEqualTo(4));
            Assert.That(counts[(int)PropKind.BoulderSquat], Is.GreaterThanOrEqualTo(4));
            Assert.That(counts[(int)PropKind.BoulderLow], Is.GreaterThanOrEqualTo(4));

            Assert.That(shrubs, Is.GreaterThanOrEqualTo(20));
            Assert.That(counts[(int)PropKind.CliffCrest], Is.GreaterThanOrEqualTo(20));
            Assert.That(counts[(int)PropKind.CliffToe], Is.GreaterThanOrEqualTo(20));
            Assert.That(counts[(int)PropKind.RiverRocks], Is.GreaterThanOrEqualTo(8));
            Assert.That(counts[(int)PropKind.FallFoam], Is.GreaterThanOrEqualTo(3));

            int coverCount = 0;
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.CoverAt(cx, cz) != CoverKind.None)
                    {
                        coverCount++;
                        Assert.That(ValleyKindMetadata.IsWalkableKind(layout.KindAt(cx, cz)), Is.True);
                    }
                }
            }
            Assert.That(coverCount, Is.LessThanOrEqualTo(ValleyConstants.MaxCoverCells));
        }

        private static void AssertBrokenRule(
            string expectedRule,
            string[] heights,
            string[] cells,
            string[] cover,
            IReadOnlyList<PropPlacement> props,
            int startX,
            int startZ)
        {
            ValleyLayout broken = ValleyLayoutParser.Parse(new LayoutInput(
                heights, cells, cover, props, ValleyConstants.AuthoredAirBounds, startX, startZ));
            LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => ValleyLayoutRules.CheckAuthored(broken));
            Assert.That(ex.Message, Does.Contain(expectedRule));
        }

        private static string ReplaceChar(string row, int index, char ch)
        {
            char[] chars = row.ToCharArray();
            chars[index] = ch;
            return new string(chars);
        }

        private static string ReplaceRange(string row, int startInclusive, int endExclusive, char ch)
        {
            char[] chars = row.ToCharArray();
            for (int i = startInclusive; i < endExclusive; i++)
            {
                chars[i] = ch;
            }
            return new string(chars);
        }

        private static CellKind ParseKind(char ch)
        {
            ValleyKindMetadata.TryParseCellKind(ch, out CellKind kind);
            return kind;
        }
    }
}
