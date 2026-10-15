using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Lune
{
    /// <summary>
    /// win.lune (Her Parents' Work): a quest win at the end of any of her turns once her recovered relics hold
    /// tune.lune-relic-types distinct types (tablet, figure, jewelry) and state.lune.tagged-relic is among them.
    /// The act (Act II) and dominance gates come from the row through <see cref="WinRules"/>.
    /// </summary>
    public sealed class LuneWin : WinConditionBase, IGoalProgress
    {
        public LuneWin() : base(Ids.WinConditions.Lune) { }

        public override bool QuestComplete(GameContext ctx, SeatId seat) => LuneRelics.TestMet(ctx.Board, seat, ctx.State.LuneTaggedRelic);

        /// <summary>Recoveries still needed: the Parents' Journal (one unit, which also fills its own type) plus every other
        /// missing type.</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat) => LuneRelics.UnitsMissing(ctx.Board, seat, ctx.State.LuneTaggedRelic);

        /// <summary>The counterplay's clearings: where the Parents' Journal is carried, else the clearings around the gold forest.</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var b = ctx.Board;
            var tag = ctx.State.LuneTaggedRelic;
            var list = new List<ClearingId>();
            if (!tag.HasValue || b.RecoveredRelics(seat).Contains(tag.Value)) return list;
            var carried = b.RelicClearing(tag.Value);
            if (carried.HasValue) list.Add(carried.Value);
            var forest = b.RelicForest(tag.Value);
            if (forest.HasValue) list.AddRange(b.ForestClearings(forest.Value).OrderBy(c => c.Value));
            return list;
        }
    }

    /// <summary>Relic facts shared by Lune's win, tweaks and bot (reads only).</summary>
    public static class LuneRelics
    {
        public const string Tablet = "tablet", Figure = "figure", Jewelry = "jewelry";
        /// <summary>The three relic types of win.lune.</summary>
        public static readonly IReadOnlyList<string> Types = new[] { Tablet, Figure, Jewelry };

        /// <summary>tweak.lune.stains: Chroma Sample (jewelry) = fire, Journal (tablet) = ice, Route Chart (figure) = lightning.</summary>
        public static string ElementOf(string relicType)
        {
            switch (relicType)
            {
                case Jewelry: return "fire";
                case Tablet: return "ice";
                case Figure: return "lightning";
                default: return null;
            }
        }

        public static HashSet<string> RecoveredTypes(IBoardView b, SeatId seat) =>
            new HashSet<string>(b.RecoveredRelics(seat).Select(b.RelicType).Where(t => t != null && Types.Contains(t)), StringComparer.Ordinal);

        public static List<string> MissingTypes(IBoardView b, SeatId seat)
        {
            var have = RecoveredTypes(b, seat);
            return Types.Where(t => !have.Contains(t)).ToList();
        }

        /// <summary>The themed test of win.lune (gates excluded).</summary>
        public static bool TestMet(IBoardView b, SeatId seat, int? tagged) =>
            tagged.HasValue && b.RecoveredRelics(seat).Contains(tagged.Value) && RecoveredTypes(b, seat).Count >= Tuning.LuneRelicTypes;

        /// <summary>Recoveries still needed for <see cref="TestMet"/>.</summary>
        public static int UnitsMissing(IBoardView b, SeatId seat, int? tagged)
        {
            var needed = Math.Max(0, Tuning.LuneRelicTypes - RecoveredTypes(b, seat).Count);
            if (tagged.HasValue && b.RecoveredRelics(seat).Contains(tagged.Value)) return needed;
            var journalType = tagged.HasValue ? b.RelicType(tagged.Value) : null;
            var fills = journalType != null && MissingTypes(b, seat).Contains(journalType) ? 1 : 0;
            return 1 + Math.Max(0, needed - fills);
        }

        /// <summary>Every forest of the map (the forests around its clearings), ascending.</summary>
        public static List<int> AllForests(IBoardView b) => b.Clearings().SelectMany(b.ForestsAround).Distinct().OrderBy(f => f).ToList();

        /// <summary>Relic ids lying in any forest, ascending.</summary>
        public static List<int> ForestRelics(IBoardView b) => AllForests(b).SelectMany(b.RelicsInForest).Distinct().OrderBy(r => r).ToList();

        /// <summary>Whether a relic id is still on the table: in a forest, carried, or recovered by a Keepers seat.</summary>
        public static bool Exists(IBoardView b, int relicId) =>
            b.RelicType(relicId) != null || b.RelicForest(relicId).HasValue || b.RelicClearing(relicId).HasValue
            || b.Seats().Any(s => b.EngineOf(s) == EngineId.Keepers && b.RecoveredRelics(s).Contains(relicId));

        /// <summary>Path distances from a set of clearings (multi-source), through clearings <paramref name="allowed"/> admits
        /// (the starts are always allowed).</summary>
        public static Dictionary<ClearingId, int> DistancesFrom(IBoardView b, IEnumerable<ClearingId> starts, Func<ClearingId, bool> allowed = null)
        {
            var dist = new Dictionary<ClearingId, int>();
            var queue = new Queue<ClearingId>();
            foreach (var s in starts.Distinct().OrderBy(c => c.Value)) { dist[s] = 0; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in b.PathNeighbours(c).OrderBy(x => x.Value))
                {
                    if (dist.ContainsKey(n) || (allowed != null && !allowed(n))) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>The shortest path (start included) from the first of <paramref name="starts"/> that reaches a goal,
        /// through allowed clearings; ties toward lower clearing numbers. Null when no goal is reachable.</summary>
        public static List<ClearingId> ShortestPath(IBoardView b, IEnumerable<ClearingId> starts, Func<ClearingId, bool> isGoal,
            Func<ClearingId, bool> allowed = null)
        {
            var prev = new Dictionary<ClearingId, ClearingId?>();
            var queue = new Queue<ClearingId>();
            foreach (var s in starts.Distinct().OrderBy(c => c.Value)) { prev[s] = null; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (isGoal(c))
                {
                    var path = new List<ClearingId>();
                    for (ClearingId? x = c; x.HasValue; x = prev[x.Value]) path.Add(x.Value);
                    path.Reverse();
                    return path;
                }
                foreach (var n in b.PathNeighbours(c).OrderBy(x => x.Value))
                {
                    if (prev.ContainsKey(n) || (allowed != null && !allowed(n))) continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            return null;
        }
    }
}
