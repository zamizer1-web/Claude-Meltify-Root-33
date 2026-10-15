using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Lune
{
    /// <summary>
    /// Lune's rule tweak and spawn: tweak.lune.stains (hook.relic-recovered, hook.ui-toggle, hook.battle-start,
    /// hook.battle-resolved), spawn.parents-journal (story.act2.parents-journal at hook.act-change, re-attached at
    /// hook.relic-moved) and the onLoad rules of state.lune.*.
    /// </summary>
    public static class LuneTweaks
    {
        /// <summary>Toggle id prefix of the Stain buttons on her board (hook.ui-toggle): 'stain.fire', 'stain.ice', 'stain.lightning'.</summary>
        public const string StainTogglePrefix = "stain.";

        public static string StainToggle(string element) => StainTogglePrefix + element;

        // ------------------------------------------------------------ tweak.lune.stains

        public static int Stains(ModState s, string element) =>
            element != null && s.LuneStains != null && s.LuneStains.TryGetValue(element, out var n) ? n : 0;

        /// <summary>hook.relic-recovered: a relic she recovers gives a Stain of its element; the thesis tracker refreshes,
        /// and the Parents' Journal opens its page (ui.lune.journal-page, then bark.lune.06; any other relic: bark.lune.05).</summary>
        public static void Recovered(GameContext ctx, SeatId lune, SeatId keepers, int relicId, string relicType)
        {
            if (keepers != lune) return;
            var element = LuneRelics.ElementOf(relicType ?? ctx.Board.RelicType(relicId));
            if (element != null)
            {
                if (ctx.State.LuneStains == null) ctx.State.LuneStains = new Dictionary<string, int>();
                ctx.State.LuneStains[element] = Stains(ctx.State, element) + 1;
                ctx.Actions.ShowOverlay(Ids.Overlays.LuneStains, lune, null, null);
            }
            ctx.Actions.ShowOverlay(Ids.Overlays.LuneThesis, lune, null, null);
            if (ctx.State.LuneTaggedRelic == relicId)
            {
                HideGlow(ctx, null);
                ctx.Actions.ShowOverlay(Ids.Overlays.LuneJournalPage, lune, null, null);
                ctx.Actions.ShowBubble(lune, Ids.Barks.Lune06);
            }
            else ctx.Actions.ShowBubble(lune, Ids.Barks.Lune05);
        }

        /// <summary>hook.ui-toggle: arming needs a Stain of that element (one armed at a time); switching off always works.
        /// Returns the armed element.</summary>
        public static string Toggle(GameContext ctx, SeatId lune, SeatId seat, string toggleId, bool on)
        {
            if (seat != lune || toggleId == null || !toggleId.StartsWith(StainTogglePrefix, StringComparison.Ordinal)) return ctx.State.LuneStainArmed;
            var element = toggleId.Substring(StainTogglePrefix.Length);
            if (!ModState.StainElements.Contains(element)) return ctx.State.LuneStainArmed;
            if (on && Stains(ctx.State, element) > 0) ctx.State.LuneStainArmed = element;
            else if (!on && ctx.State.LuneStainArmed == element) ctx.State.LuneStainArmed = null;
            ctx.Actions.ShowOverlay(Ids.Overlays.LuneStains, lune, null, null);
            return ctx.State.LuneStainArmed;
        }

        /// <summary>hook.battle-start: with a Stain armed, her next battle (attacking or defending) gets
        /// +tune.lune-stain-hits hit (do.add-hits). Returns true when the hit was registered.</summary>
        public static bool BattleStarting(GameContext ctx, SeatId lune, SeatId attacker, SeatId defender)
        {
            if (attacker != lune && defender != lune) return false;
            var armed = ctx.State.LuneStainArmed;
            if (armed == null) return false;
            if (Stains(ctx.State, armed) <= 0) { ctx.State.LuneStainArmed = null; return false; }
            ctx.Actions.AddHits(lune, Tuning.LuneStainHits);
            return true;
        }

        /// <summary>hook.battle-resolved: the armed Stain is spent when her battle resolves.</summary>
        public static void BattleResolved(GameContext ctx, SeatId lune, BattleResult r)
        {
            if (r == null || (r.Attacker != lune && r.Defender != lune)) return;
            var armed = ctx.State.LuneStainArmed;
            if (armed == null) return;
            ctx.State.LuneStains[armed] = Math.Max(0, Stains(ctx.State, armed) - 1);
            ctx.State.LuneStainArmed = null;
            ctx.Actions.ShowOverlay(Ids.Overlays.LuneStains, lune, null, null);
        }

        // ------------------------------------------------------------ spawn.parents-journal

        /// <summary>The forest touching both role.sirene and role.monolith (lowest number if several), or null (no Sirène).</summary>
        public static int? JournalForest(GameContext ctx)
        {
            var sirene = ctx.Role(Ids.MapRoles.Sirene);
            var monolith = ctx.Role(Ids.MapRoles.Monolith);
            if (!sirene.HasValue || !monolith.HasValue) return null;
            var around = ctx.Board.ForestsAround(monolith.Value);
            return ctx.Board.ForestsAround(sirene.Value).Where(around.Contains).OrderBy(f => f).Select(f => (int?)f).FirstOrDefault();
        }

        /// <summary>
        /// The relic to tag: the highest-value tablet lying in a forest (ties: lowest relic id), else the highest-value
        /// relic of any type. Fallback when face-down values are hidden from the mod: the forest tablet nearest to Sirène
        /// (path distance from the clearings around its forest; ties: lowest forest number, then relic id), and when types
        /// are hidden too, the forest relic nearest Sirène. Null when no relic lies in a forest.
        /// </summary>
        public static int? ChooseJournal(GameContext ctx)
        {
            var b = ctx.Board;
            var relics = LuneRelics.ForestRelics(b);
            if (relics.Count == 0) return null;
            var tablets = relics.Where(r => b.RelicType(r) == LuneRelics.Tablet).ToList();
            var pool = tablets.Count > 0 ? tablets : relics;
            if (pool.All(r => b.RelicValue(r).HasValue))
                return pool.OrderByDescending(r => b.RelicValue(r).Value).ThenBy(r => r).First();

            var typesHidden = relics.Any(r => b.RelicType(r) == null);
            if (typesHidden) pool = relics;
            var sirene = ctx.Role(Ids.MapRoles.Sirene);
            var dist = sirene.HasValue ? Graph.Distances(sirene.Value, b.PathNeighbours) : new Dictionary<ClearingId, int>();
            int ForestDistance(int r)
            {
                var f = b.RelicForest(r).Value;
                var ds = b.ForestClearings(f).Where(dist.ContainsKey).Select(c => dist[c]).ToList();
                return ds.Count == 0 ? int.MaxValue : ds.Min();
            }
            return pool.OrderBy(ForestDistance).ThenBy(r => b.RelicForest(r).Value).ThenBy(r => r).First();
        }

        /// <summary>story.act2.parents-journal (Lune seated): tag the chosen relic (do.tag-relic), move it into the forest
        /// beside Sirène when the engine allows (do.move-relic; fallback: tagged where it lies) and light its forest
        /// (ui.lune.journal-glow). Returns the tagged relic id.</summary>
        public static int? TagParentsJournal(GameContext ctx, SeatId lune)
        {
            var pick = ChooseJournal(ctx);
            if (!pick.HasValue) return null;
            ctx.State.LuneTaggedRelic = pick;
            ctx.Actions.TagRelic(pick.Value);
            var target = JournalForest(ctx);
            if (target.HasValue && ctx.Board.RelicForest(pick.Value) != target && ctx.SupportsAction(Ids.BoardActions.MoveRelic))
                ctx.Actions.MoveRelicToForest(pick.Value, target.Value);
            ShowGlow(ctx, lune);
            ctx.Actions.ShowOverlay(Ids.Overlays.LuneThesis, lune, null, null);
            return pick;
        }

        /// <summary>hook.relic-moved: the tag stays on the Parents' Journal wherever it goes (carried, knocked loose face up
        /// into a forest by Prized Trophies); the tag is re-attached and the gold glow follows its forest.</summary>
        public static void Moved(GameContext ctx, SeatId lune, int relicId)
        {
            if (ctx.State.LuneTaggedRelic != relicId) return;
            ctx.Actions.TagRelic(relicId);
            ShowGlow(ctx, lune);
        }

        /// <summary>ui.lune.journal-glow on the Journal's forest (hidden from every other forest); none while it is carried
        /// or recovered.</summary>
        public static void ShowGlow(GameContext ctx, SeatId lune)
        {
            var tag = ctx.State.LuneTaggedRelic;
            var forest = tag.HasValue ? ctx.Board.RelicForest(tag.Value) : null;
            HideGlow(ctx, forest);
            if (forest.HasValue) ctx.Actions.ShowOverlay(Ids.Overlays.LuneJournalGlow, null, null, forest.Value);
        }

        private static void HideGlow(GameContext ctx, int? except)
        {
            foreach (var f in LuneRelics.AllForests(ctx.Board))
                if (f != except) ctx.Actions.HideOverlay(Ids.Overlays.LuneJournalGlow, null, null, f);
        }

        // ------------------------------------------------------------ hook.load

        /// <summary>state.lune.stains: restored (missing elements read 0). state.lune.stain-armed: dropped without a Stain of
        /// that element. state.lune.tagged-relic: restored and re-attached; a relic id that no longer exists (and is not among
        /// her recovered relics), or a save without the row from Act II on, runs the tagging rule again.</summary>
        public static void RecheckOnLoad(GameContext ctx, SeatId lune, IReadOnlyCollection<string> loadedKeys)
        {
            var s = ctx.State;
            if (s.LuneStains == null) s.LuneStains = new Dictionary<string, int>();
            foreach (var e in ModState.StainElements)
                if (!s.LuneStains.ContainsKey(e)) s.LuneStains[e] = 0;
            if (s.LuneStainArmed != null && Stains(s, s.LuneStainArmed) <= 0) s.LuneStainArmed = null;

            var tag = s.LuneTaggedRelic;
            var b = ctx.Board;
            if (tag.HasValue && (LuneRelics.Exists(b, tag.Value) || b.RecoveredRelics(lune).Contains(tag.Value)))
                ctx.Actions.TagRelic(tag.Value);
            else if (ctx.Act >= 2 && (tag.HasValue || !loadedKeys.Contains(Ids.State.LuneTaggedRelic)))
            {
                s.LuneTaggedRelic = null;
                TagParentsJournal(ctx, lune);
            }
            ShowGlow(ctx, lune);
        }
    }
}
