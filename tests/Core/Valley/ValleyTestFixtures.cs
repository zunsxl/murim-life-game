using System;
using Murim.Core.Valley;

namespace Murim.Core.Tests.Valley
{
    /// <summary>
    /// Reusable small test layouts (Flat8, Tier12, Tier12NoSlope) defined in spec §6.2.
    /// </summary>
    public static class ValleyTestFixtures
    {
        public static ValleyLayout CreateFlat8()
        {
            string[] heights = new string[9];
            for (int z = 0; z < 9; z++)
            {
                heights[z] = "000000000";
            }

            string[] cells = new string[8];
            string[] cover = new string[8];
            for (int z = 0; z < 8; z++)
            {
                cells[z] = "mmmmmmmm";
                cover[z] = "........";
            }

            return ValleyLayoutParser.Parse(new LayoutInput(
                heights,
                cells,
                cover,
                Array.Empty<PropPlacement>(),
                new AirBounds(1f, 7f, 1f, 7f),
                1,
                1));
        }

        public static ValleyLayout CreateTier12()
        {
            return BuildTier12(withSlopes: true);
        }

        public static ValleyLayout CreateTier12NoSlope()
        {
            return BuildTier12(withSlopes: false);
        }

        private static ValleyLayout BuildTier12(bool withSlopes)
        {
            // 12x12 cells (13x13 vertices), AirBounds = [1, 11] x [1, 11].
            // River column at cx = 10 (w, 0 m), east border cx = 11 (c, 0 m).
            // Slope 1 (0->3 m): cx = 1..2, cz = 1..6 (6 rows, 0.5 m per cell).
            // Slope 2 (3->6 m): cx = 7..8, cz = 4..9 (6 rows, 0.5 m per cell).
            // 9 m terrace: cx = 1..3, cz = 10 behind Cliff 3 (cz = 9), no slope.
            string[] heightsWithSlopes =
            {
                "0000000000000", // j=0
                "0000000000000", // j=1
                "0111000000000", // j=2: S1=0.5 (i=1..3)
                "0222006666000", // j=3: S1=1.0, right 3m starts at i=6..9
                "6333666666000", // j=4: S1=1.5, Floor2 starts at i=4..9
                "6444666777000", // j=5: S1=2.0, S2=3.5 (i=7..9)
                "6555666888000", // j=6: S1=2.5, S2=4.0
                "6666666999000", // j=7: S1=3.0, S2=4.5
                "6666666aaa000", // j=8: Floor2=3m, S2=5.0
                "cccccccbbb000", // j=9: Floor3=6m (c=6m), S2=5.5 (b=5.5)
                "iiiiiccccc000", // j=10: 9m terrace (i=9m) at i=0..4, S2=6m at i=5..9
                "iiiiiccccc000", // j=11
                "iiiiiccccc000"  // j=12
            };

            string[] cellsWithSlopes =
            {
                "mmmmmmmmmmwc", // cz=0: 0m ground + river(10) + border(11)
                "ssssmmmmmmwc", // cz=1: S1(1..2) + walkable shoulders(0,3)
                "csscmcccccwc", // cz=2: S1 + cliff(cx=5..9)
                "csscccmmmcwc", // cz=3: S1 + cliff(cx=4..5) + 3m(cx=6..8) + cliff(cx=9)
                "csscmmrrrcwc", // cz=4: S1 + 3m(cx=4..5) + S2(7..8) + shoulder r(6)
                "csscmmcrrcwc", // cz=5: S1 + 3m(cx=4..5) + S2(7..8)
                "ssssmmcrrcwc", // cz=6: S1 exit + walkable shoulders(0,3) + 3m(4..5) + S2
                "cmmmmmcrrcwc", // cz=7: 3m terrace(1..5) + S2
                "cccccccrrcwc", // cz=8: Cliff 2(0..6) + S2
                "cccccmrrrcwc", // cz=9: Cliff 3(0..4) + 6m(5) + S2 exit(6..8)
                "cmmmcmmmmcwc", // cz=10: 9m at cx=1..3, 6m at cx=5..8
                "ccccccccccwc"  // cz=11
            };

            string[] heightsNoSlopes =
            {
                "0000000000000", // j=0
                "0000000000000", // j=1
                "0000000000000", // j=2
                "0000006666000", // j=3
                "6666666666000", // j=4
                "6666666666000", // j=5
                "6666666666000", // j=6
                "6666666666000", // j=7
                "6666666666000", // j=8
                "cccccccccc000", // j=9
                "iiiiiccccc000", // j=10
                "iiiiiccccc000", // j=11
                "iiiiiccccc000"  // j=12
            };

            string[] cellsNoSlopes =
            {
                "mmmmmmmmmmwc", // cz=0
                "mmmmmmmmmmwc", // cz=1
                "ccccccccccwc", // cz=2
                "ccccccmmccwc", // cz=3
                "cmmmmmmmccwc", // cz=4
                "cmmmmmmmccwc", // cz=5
                "cmmmmmmmccwc", // cz=6
                "cmmmmmmmccwc", // cz=7
                "ccccccccccwc", // cz=8
                "cccccmccmcwc", // cz=9
                "cmmmcmmmmcwc", // cz=10
                "ccccccccccwc"  // cz=11
            };

            string[] cover = new string[12];
            for (int z = 0; z < 12; z++)
            {
                cover[z] = "............";
            }

            PropPlacement[] props =
            {
                new PropPlacement(PropKind.BoulderUpright, 8, 1, false),
                new PropPlacement(PropKind.BoulderUpright, 5, 5, false)
            };

            return ValleyLayoutParser.Parse(new LayoutInput(
                withSlopes ? heightsWithSlopes : heightsNoSlopes,
                withSlopes ? cellsWithSlopes : cellsNoSlopes,
                cover,
                props,
                new AirBounds(1f, 11f, 1f, 11f),
                4,
                1));
        }
    }
}
