using System;
using Murim.Core.Exploration.Placeholder;
using Murim.Core.Tests.Valley;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Placeholder
{
    [TestFixture]
    public sealed class GridPathfinderTests
    {
        [Test]
        public void DiagonalStepBlockedWhenOrthogonalNeighborUnwalkable()
        {
            string[] heights =
            {
                "00000",
                "00000",
                "00000",
                "00000",
                "00000"
            };
            string[] cells =
            {
                "mmmm",
                "mcmc",
                "mcmm",
                "mmmm"
            };
            string[] cover =
            {
                "....",
                "....",
                "....",
                "...."
            };
            ValleyLayout layout = ValleyLayoutParser.Parse(new LayoutInput(
                heights, cells, cover, Array.Empty<PropPlacement>(), new AirBounds(0f, 4f, 0f, 4f), 0, 0));

            // From (0, 1) to (2, 2): (1, 1) and (1, 2) are cliff, so diagonal (0,1)->(1,2) is invalid;
            // path must go south around z=0 and east, then north.
            GridPathfinder pathfinder = new GridPathfinder(16);
            Vec2[] output = new Vec2[20];
            int count = pathfinder.FindPath(
                layout,
                new PathEndpoint(new Vec2(0.5f, 1.5f), 0, 1),
                new PathEndpoint(new Vec2(2.5f, 2.5f), 2, 2),
                output);
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(output[count - 1], Is.EqualTo(new Vec2(2.5f, 2.5f)));

            for (int i = 0; i < count; i++)
            {
                Vec2 prev = i == 0 ? new Vec2(0.5f, 1.5f) : output[i - 1];
                Assert.That(GridPathfinder.HasLineOfSight(layout, prev, output[i]), Is.True);
            }
        }

        [Test]
        public void OpenMeadowPathSimplifiesToSingleDirectSegment()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            GridPathfinder pathfinder = new GridPathfinder(64);
            Vec2[] output = new Vec2[80];
            Vec2 start = new Vec2(1.5f, 1.5f);
            Vec2 target = new Vec2(6.5f, 5.5f);

            int count = pathfinder.FindPath(
                flat8,
                new PathEndpoint(start, 1, 1),
                new PathEndpoint(target, 6, 5),
                output);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(target));
        }

        [Test]
        public void ZeroHeapAllocationsDuringClickAndTick()
        {
            ValleyLayout authored = ValleyLayoutData.CreateLayout();
            PlaceholderController controller = new PlaceholderController(authored, ExplorationTuning.Default);
            Vec3 target = new Vec3(56f, 3f, 50f);
            Vec3 dir = new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);
            Vec3 origin = target - dir * 30f;

            // Warm-up JIT
            controller.Click(origin, dir, ValleyConstants.FullViewCutY);
            controller.Tick();

            long b1 = GC.GetAllocatedBytesForCurrentThread();
            controller.Click(origin, dir, ValleyConstants.FullViewCutY);
            long afterClick = GC.GetAllocatedBytesForCurrentThread() - b1;

            long b2 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 60; i++)
            {
                controller.Tick();
            }
            long afterTicks = GC.GetAllocatedBytesForCurrentThread() - b2;

            Assert.That(afterClick, Is.EqualTo(0), $"Click allocated {afterClick}");
            Assert.That(afterTicks, Is.EqualTo(0), $"Ticks allocated {afterTicks}");
        }
    }
}
