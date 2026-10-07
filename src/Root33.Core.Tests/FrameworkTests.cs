using System.Collections.Generic;
using System.Linq;
using Root33.Core;

namespace Root33.Core.Tests
{
    public class GraphTests
    {
        // A 2x3 grid:  0-1-2
        //              | | |
        //              3-4-5
        private static readonly Dictionary<int, int[]> Grid = new Dictionary<int, int[]>
        {
            [0] = new[] { 1, 3 }, [1] = new[] { 0, 2, 4 }, [2] = new[] { 1, 5 },
            [3] = new[] { 0, 4 }, [4] = new[] { 1, 3, 5 }, [5] = new[] { 2, 4 },
        };

        private static IEnumerable<ClearingId> N(ClearingId c) => Grid[c.Value].Select(i => new ClearingId(i));
        private static ClearingId C(int i) => new ClearingId(i);

        [Fact]
        public void Graph_DistancesCountPathSteps()
        {
            var d = Graph.Distances(C(0), N);
            Assert.Equal(0, d[C(0)]);
            Assert.Equal(1, d[C(3)]);
            Assert.Equal(3, d[C(5)]);
        }

        [Fact]
        public void Graph_ConnectedWithinStopsAtDisallowedClearings()
        {
            var allowed = new HashSet<int> { 0, 3, 4, 2 };
            var set = Graph.ConnectedWithin(C(0), N, c => allowed.Contains(c.Value));
            Assert.Equal(new[] { 0, 3, 4 }, set.Select(c => c.Value).OrderBy(v => v));
            Assert.Empty(Graph.ConnectedWithin(C(1), N, c => allowed.Contains(c.Value)));
        }

        [Fact]
        public void Graph_CheapestPathAvoidsExpensiveClearings()
        {
            var cost = new Dictionary<int, int> { [0] = 0, [1] = 10, [2] = 1, [3] = 1, [4] = 1, [5] = 1 };
            var path = Graph.CheapestPath(C(0), c => c.Value == 2, N, c => cost[c.Value], out var total);
            Assert.Equal(new[] { 0, 3, 4, 5, 2 }, path.Select(c => c.Value));
            Assert.Equal(4, total);
        }

        [Fact]
        public void Graph_CheapestPathReturnsNullWhenUnreachable()
        {
            Assert.Null(Graph.CheapestPath(C(0), c => c.Value == 99, N, c => 1, out var total));
            Assert.Equal(int.MaxValue, total);
        }
    }

    public class CountdownTests
    {
        private static readonly SeatId Seat = new SeatId(1);

        [Fact]
        public void Countdown_WinsWhenConfirmStillPasses()
        {
            var cd = new Countdown();
            Assert.True(cd.EndOfTurn(Seat, "win.paintress", announceTestPassed: true));
            Assert.True(cd.IsAnnounced(Seat));
            var r = cd.StartOfBirdsong(Seat, confirmTestPassed: true);
            Assert.Equal(CountdownOutcome.Win, r.Outcome);
            Assert.Equal("win.paintress", r.WinId);
            Assert.False(cd.IsAnnounced(Seat));
        }

        [Fact]
        public void Countdown_ClearsWhenOpponentsBreakIt()
        {
            var cd = new Countdown();
            cd.EndOfTurn(Seat, "win.sciel", true);
            Assert.Equal(CountdownOutcome.Cleared, cd.StartOfBirdsong(Seat, false).Outcome);
            Assert.Equal(CountdownOutcome.None, cd.StartOfBirdsong(Seat, true).Outcome);
        }

        [Fact]
        public void Countdown_NoBannerWhenAnnounceFails()
        {
            var cd = new Countdown();
            Assert.False(cd.EndOfTurn(Seat, "win.verso", false));
            Assert.Equal(CountdownOutcome.None, cd.StartOfBirdsong(Seat, true).Outcome);
        }
    }

    public class ActClockTests
    {
        private static ActClock Make() => new ActClock(new[] { 10, 20, 0 }, new[] { 4, 8, 0 });

        [Fact]
        public void ActClock_AdvancesByVpOrRoundWhicheverFirst()
        {
            var clock = Make();
            Assert.Equal(0, clock.Update(highestVp: 9, completedRounds: 3));
            Assert.Equal(1, clock.Act);
            Assert.Equal(1, clock.Update(10, 3));
            Assert.Equal(2, clock.Act);
            Assert.Equal(1, clock.Update(12, 8));
            Assert.Equal(3, clock.Act);
        }

        [Fact]
        public void ActClock_CanSkipAnActAndNeverPassesTheLast()
        {
            var clock = Make();
            Assert.Equal(2, clock.Update(25, 1));
            Assert.Equal(3, clock.Act);
            Assert.Equal(0, clock.Update(30, 20));
            Assert.Equal(3, clock.Act);
        }
    }
}
