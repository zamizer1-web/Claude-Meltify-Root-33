using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;

namespace Root33.Core.Tests.MapRoles
{
    /// <summary>sheets/map_roles.json: every row's tests.</summary>
    public class MapRolesTests
    {
        //  A 3x4 test map ('winter' so the rules run, not the fixed Autumn roles):
        //    1  2  3  4        corners 1, 4, 9, 12; Root's diagonal pairs 1-12 and 4-9; one ruin at 6
        //    5  6  7  8
        //    9 10 11 12
        private const string GridPaths = "1-2 2-3 3-4 5-6 6-7 7-8 9-10 10-11 11-12 1-5 5-9 2-6 6-10 3-7 7-11 4-8 8-12";

        private static FakeBoard Grid(string map = "winter", bool pairs = true, string paths = GridPaths)
        {
            var b = FakeBoard.Map(map).Clearings(12).Corners(1, 4, 9, 12).Paths(paths).Ruins(6);
            if (pairs) b.Opposite(1, 12).Opposite(4, 9);
            return b;
        }

        //  A map where neither free corner has a Stone Wave Cliffs candidate within 2 steps:
        //  corners 1-4, pairs 1-4 and 2-3, Monolith 4 with neighbours 5, 6, 7, the only ruin at 8.
        private static FakeBoard NoCliffsNearCorners() =>
            FakeBoard.Map("winter").Clearings(12).Corners(1, 2, 3, 4).Opposite(1, 4).Opposite(2, 3).Ruins(8)
                .Paths("1-10 1-11 2-5 2-6 3-5 3-7 4-5 4-6 4-7 6-8 7-8 8-9 9-10 9-12 10-11 11-12");

        private static Dictionary<RoleId, IReadOnlyList<ClearingId>> Rules(FakeBoard b, int? keep = null) =>
            RoleMap.AssignByRule(b, keep.HasValue ? new ClearingId(keep.Value) : (ClearingId?)null);

        private static int One(Dictionary<RoleId, IReadOnlyList<ClearingId>> roles, RoleId role) => roles[role].Single().Value;
        private static int[] All(Dictionary<RoleId, IReadOnlyList<ClearingId>> roles, RoleId role) => roles[role].Select(c => c.Value).OrderBy(v => v).ToArray();
        private static ClearingId Cl(int id) => new ClearingId(id);

        [Fact]
        public void MapRoles_LumiereIsTheKeepCorner()
        {
            var board = Grid().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress");
            var t = TestTable.Start(board, runSetup: false);
            Assert.False(t.Ctx.RolesAssigned); // with Gustave seated the roles wait for his keep

            board.Token(0, 4, PieceTypes.Keep);
            t.Game.OnPiecePlaced(new SeatId(0), PieceKind.Token, PieceTypes.Keep, Cl(4));
            Assert.Equal(Cl(4), t.Ctx.Role(Ids.MapRoles.Lumiere));
            Assert.Equal(Cl(9), t.Ctx.Role(Ids.MapRoles.Monolith));
            Assert.Contains(Cl(4), t.State.GustaveFlags);

            // Autumn: Lumière is fixed to autumn.01 at map load (his keep is restricted there).
            var autumn = TestTable.Start(FakeBoard.Autumn().Seat(0, EngineId.Marquise, "gustave").Seat(1, EngineId.Eyrie, "paintress"), runSetup: false);
            Assert.Equal(AutumnMap.ClearingOf("autumn.01"), autumn.Ctx.Role(Ids.MapRoles.Lumiere));
            Assert.Equal(autumn.Board.C("Lumière"), autumn.Ctx.Role(Ids.MapRoles.Lumiere));
        }

        [Fact]
        public void MapRoles_LumiereIsLowestNumberedCornerWithoutGustave()
        {
            var board = FakeBoard.Map("winter").Clearings(12).Corners(12, 9, 4, 3).Paths(GridPaths).Path(3, 4).Ruins(6)
                .Seat(0, EngineId.Eyrie, "paintress").Seat(1, EngineId.Alliance, "sciel");
            var t = TestTable.Start(board, runSetup: false);
            Assert.True(t.Ctx.RolesAssigned); // no Gustave: assigned at map load
            Assert.Equal(Cl(3), t.Ctx.Role(Ids.MapRoles.Lumiere));
            Assert.Equal(1, One(Rules(Grid()), RoleMap.Lumiere));
        }

        [Fact]
        public void MapRoles_SingleClearingRolesNeverShareAClearing()
        {
            var maps = new[] { Grid(), Grid(pairs: false), FakeBoard.Autumn().MapNamed("winter"), NoCliffsNearCorners() };
            foreach (var map in maps)
                foreach (var keep in new int?[] { null }.Concat(map.Corners().Select(c => (int?)c.Value)))
                {
                    var roles = Rules(map, keep);
                    var used = RoleMap.SingleClearingRoles.SelectMany(r => roles[r]).ToList();
                    Assert.Equal(used.Count, used.Distinct().Count());
                }
            var fixedRoles = AutumnMap.FixedRoles();
            var fixedUsed = RoleMap.SingleClearingRoles.SelectMany(r => fixedRoles[r]).ToList();
            Assert.Equal(RoleMap.SingleClearingRoles.Count, fixedUsed.Distinct().Count());
        }

        [Fact]
        public void MapRoles_AssignsEveryRoleOnAnyTwelveClearingMap()
        {
            foreach (var map in new[] { Grid(), Grid(pairs: false), FakeBoard.Autumn().MapNamed("winter"), NoCliffsNearCorners() })
            {
                Assert.Equal(12, map.Clearings().Count);
                var roles = Rules(map);
                foreach (var r in new[] { RoleMap.Lumiere, RoleMap.Monolith, RoleMap.Visages, RoleMap.OldLumiere, RoleMap.EsquiesNest, RoleMap.FlyingManor, RoleMap.StoneWaveCliffs })
                    Assert.Single(roles[r]);
                var neighbours = map.PathNeighbours(roles[RoleMap.Monolith][0]).Count;
                Assert.Equal(neighbours >= 2 ? 1 : 0, roles[RoleMap.Sirene].Count);
                Assert.Equal(neighbours >= 3 ? 1 : 0, roles[RoleMap.TheReacher].Count);
                Assert.Equal(map.Clearings().Where(map.OnRiver).ToArray(), roles[RoleMap.SacredRiver].ToArray());
                Assert.Equal(Data.MapRoles.Count, roles.Count);
            }

            // The rules, run on the Autumn graph, land every role where map_autumn.json fixes it.
            var byRule = Rules(FakeBoard.Autumn().MapNamed("winter"));
            foreach (var kv in AutumnMap.FixedRoles()) Assert.Equal(kv.Value.ToArray(), byRule[kv.Key].ToArray());
        }

        [Fact]
        public void MapRoles_MonolithIsDiagonallyOppositeLumiere()
        {
            // Root pairs 1 with 4 here, although 12 is the farthest corner: the pairing wins.
            var roles = Rules(Grid().Opposite(1, 4).Opposite(9, 12));
            Assert.Equal(1, One(roles, RoleMap.Lumiere));
            Assert.Equal(4, One(roles, RoleMap.Monolith));

            var autumn = FakeBoard.Autumn();
            Assert.Equal(autumn.OppositeCorner(autumn.C("role.lumiere")), AutumnMap.FixedRoles()[RoleMap.Monolith][0]);
        }

        [Fact]
        public void MapRoles_MonolithFallsBackToFarthestCorner()
        {
            // No pairing: from 1, corner 4 is 3 steps, 9 is 2, 12 is 5.
            Assert.Equal(12, One(Rules(Grid(pairs: false)), RoleMap.Monolith));
            // From 4: 1 is 3 steps, 12 is 2, 9 is 5.
            Assert.Equal(9, One(Rules(Grid(pairs: false), keep: 4), RoleMap.Monolith));
        }

        [Fact]
        public void MapRoles_NestIsTheFreeCornerWithACliffsCandidate()
        {
            // Without the 3-4 path and with Old Lumière at 7, corner 4 only reaches 8 (Monolith neighbour), 7 and 12.
            var paths = GridPaths.Replace("3-4 ", "");
            var roles = Rules(Grid(paths: paths).NoRuin(6).Ruins(7));
            Assert.Equal(7, One(roles, RoleMap.OldLumiere));
            Assert.Equal(9, One(roles, RoleMap.EsquiesNest));
            Assert.Equal(4, One(roles, RoleMap.FlyingManor));
        }

        [Fact]
        public void MapRoles_NestTieGoesToLowerClearingNumber()
        {
            // Both free corners (4 and 9) have a candidate.
            Assert.Equal(4, One(Rules(Grid()), RoleMap.EsquiesNest));
            // Neither free corner (2 and 3) has one.
            Assert.Equal(2, One(Rules(NoCliffsNearCorners()), RoleMap.EsquiesNest));
        }

        [Fact]
        public void MapRoles_FlyingManorIsTheLastCorner()
        {
            foreach (var map in new[] { Grid(), NoCliffsNearCorners(), FakeBoard.Autumn().MapNamed("winter") })
            {
                var roles = Rules(map);
                var homes = new[] { RoleMap.Lumiere, RoleMap.Monolith, RoleMap.EsquiesNest, RoleMap.FlyingManor }.Select(r => One(roles, r));
                Assert.Equal(map.Corners().Select(c => c.Value).OrderBy(v => v), homes.OrderBy(v => v));
            }
            Assert.Equal(9, One(Rules(Grid()), RoleMap.FlyingManor));
        }

        [Fact]
        public void MapRoles_CliffsIsOneOrTwoStepsFromNestAndNotAMonolithNeighbour()
        {
            // A 12-3 path makes 3 a Monolith neighbour: the Nest (4) then reaches only 2 and 7 at two steps.
            var map = Grid().Path(12, 3);
            var roles = Rules(map);
            var nest = roles[RoleMap.EsquiesNest][0];
            var cliffs = roles[RoleMap.StoneWaveCliffs][0];
            Assert.Equal(4, nest.Value);
            Assert.Equal(2, cliffs.Value);
            Assert.Equal(2, Graph.Distances(nest, map.PathNeighbours)[cliffs]);
            Assert.DoesNotContain(cliffs, map.PathNeighbours(roles[RoleMap.Monolith][0]));
            Assert.NotEqual(roles[RoleMap.OldLumiere][0], cliffs);
            Assert.DoesNotContain(cliffs, map.Corners());
        }

        [Fact]
        public void MapRoles_CliffsPrefersNearestThenLowestNumber()
        {
            // Nest 4: 3 is one step, 2 is two steps (lower number): nearest wins.
            Assert.Equal(3, One(Rules(Grid()), RoleMap.StoneWaveCliffs));
            // Nest 9: 5 and 10 are both one step: the lower number wins.
            var paths = GridPaths.Replace("3-4 ", "");
            Assert.Equal(5, One(Rules(Grid(paths: paths).NoRuin(6).Ruins(7)), RoleMap.StoneWaveCliffs));
        }

        [Fact]
        public void MapRoles_CliffsFallsBackToNearestFreeClearing()
        {
            // Nest 2: within two steps only corners, Monolith neighbours and Old Lumière; 9 is the nearest free clearing (3 steps).
            var roles = Rules(NoCliffsNearCorners());
            Assert.Equal(8, One(roles, RoleMap.OldLumiere));
            Assert.Equal(2, One(roles, RoleMap.EsquiesNest));
            Assert.Equal(9, One(roles, RoleMap.StoneWaveCliffs));
        }

        [Fact]
        public void MapRoles_OldLumiereIsTheMostCentralRuin()
        {
            // Ruins at 1 (corner), 8 (Monolith neighbour), 2 and 6: 6 is the most central eligible ruin, although 2 is lower.
            var roles = Rules(Grid().Ruins(1, 2, 8));
            Assert.Equal(6, One(roles, RoleMap.OldLumiere));
            // Autumn: autumn.08, the central fox clearing.
            Assert.Equal(8, One(Rules(FakeBoard.Autumn().MapNamed("winter")), RoleMap.OldLumiere));
        }

        [Fact]
        public void MapRoles_OldLumiereFallsBackToMostCentralClearing()
        {
            // The only ruins are a corner (1) and a Monolith neighbour (8): the most central eligible clearing is 6 (tied with 7, lower number).
            var roles = Rules(Grid().NoRuin(6).Ruins(1, 8));
            Assert.Equal(6, One(roles, RoleMap.OldLumiere));
        }

        [Fact]
        public void MapRoles_AxonsFollowMonolithNeighboursInClearingOrder()
        {
            var roles = Rules(Grid().Path(12, 7));
            Assert.Equal(7, One(roles, RoleMap.Visages));
            Assert.Equal(8, One(roles, RoleMap.Sirene));
            Assert.Equal(11, One(roles, RoleMap.TheReacher));

            var fixedRoles = AutumnMap.FixedRoles();
            var autumn = FakeBoard.Autumn();
            var ring = autumn.PathNeighbours(fixedRoles[RoleMap.Monolith][0]).OrderBy(c => c.Value).ToArray();
            Assert.Equal(new[] { fixedRoles[RoleMap.Visages][0], fixedRoles[RoleMap.Sirene][0], fixedRoles[RoleMap.TheReacher][0] }, ring);
        }

        [Fact]
        public void MapRoles_ClosedMountainPathsDoNotMakeAxons()
        {
            // 12-7 is a closed Mountain path and 12-3 a river: neither makes an Axon.
            var map = Grid("mountain").ClosedPath(12, 7).River(12, 3);
            var roles = Rules(map);
            Assert.Equal(8, One(roles, RoleMap.Visages));
            Assert.Equal(11, One(roles, RoleMap.Sirene));
            Assert.Empty(roles[RoleMap.TheReacher]);

            map.OpenPath(12, 7);
            Assert.Equal(7, One(Rules(map), RoleMap.Visages));
        }

        [Fact]
        public void MapRoles_SireneIsTheSecondMonolithNeighbour()
        {
            Assert.Equal(11, One(Rules(Grid()), RoleMap.Sirene));
            // With 11-12 closed the Monolith has one open neighbour: no Sirène.
            var roles = Rules(Grid("mountain").ClosedPath(11, 12));
            Assert.Equal(8, One(roles, RoleMap.Visages));
            Assert.Empty(roles[RoleMap.Sirene]);
        }

        [Fact]
        public void MapRoles_ReacherOnlyWithAThirdMonolithNeighbour()
        {
            Assert.Empty(Rules(Grid())[RoleMap.TheReacher]);
            Assert.Equal(11, One(Rules(Grid().Path(12, 7)), RoleMap.TheReacher));
        }

        [Fact]
        public void MapRoles_SacredRiverTagsEveryRiverClearing()
        {
            var autumn = FakeBoard.Autumn();
            var river = AutumnMap.FixedRoles()[RoleMap.SacredRiver].Select(c => c.Value).OrderBy(v => v).ToArray();
            Assert.Equal(new[] { 1, 6, 7, 10, 11 }, river);
            Assert.Equal(autumn.Clearings().Where(autumn.OnRiver).Select(c => c.Value).ToArray(), river);

            Assert.Equal(new[] { 2, 6, 10 }, All(Rules(Grid().River(2, 6).River(6, 10)), RoleMap.SacredRiver));
            // The tag does not replace a clearing's own role: Sirène stays a single role even on the river.
            var roles = Rules(Grid().River(11, 7));
            Assert.Contains(Cl(11), roles[RoleMap.SacredRiver]);
            Assert.Equal(11, One(roles, RoleMap.Sirene));
        }

        [Fact]
        public void MapRoles_LakeShoreCountsAsSacredRiver()
        {
            // On Lake, read.on-river reports the lake shore; there are no river links at all.
            var lake = Grid("lake").OnRiverAt(5, 6, 7);
            Assert.Empty(lake.RiverNeighbours(Cl(6)));
            Assert.Equal(new[] { 5, 6, 7 }, All(Rules(lake), RoleMap.SacredRiver));
        }
    }
}
