using System;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Verso
{
    /// <summary>
    /// Verso's rank (state.verso.rank, updated at hook.battle-resolved as win.verso's notes say) and tweak.verso.perfection.
    /// tweak.verso.soarrie-handoff lives in EsquieTweaks (spawn.soarrie is Esquie's).
    /// </summary>
    public static class VersoTweaks
    {
        /// <summary>hook.battle-resolved, when he is attacker or defender: +rank-up for a clean removal, -small for 1 to
        /// heavy-1 damaged items, -large for heavy or more; clamped 0..tune.verso-rank-s.</summary>
        public static void UpdateRank(GameContext ctx, SeatId me, BattleResult r)
        {
            if (r == null || (r.Attacker != me && r.Defender != me)) return;
            r.DamagedItemsBefore.TryGetValue(me, out var before);
            r.DamagedItemsAfter.TryGetValue(me, out var after);
            var damaged = after - before;
            var rank = ctx.State.VersoRank;
            if (damaged <= 0) { if (r.Removed.Any(p => p.Owner != me)) rank += Tuning.VersoRankUp; }
            else if (damaged < Tuning.VersoHeavyDamage) rank -= Tuning.VersoRankDownSmall;
            else rank -= Tuning.VersoRankDownLarge;
            ctx.State.VersoRank = Clamp(rank);
        }

        /// <summary>hook.load: restored and clamped to 0..tune.verso-rank-s.</summary>
        public static void ClampOnLoad(GameContext ctx) => ctx.State.VersoRank = Clamp(ctx.State.VersoRank);

        public static int Clamp(int rank) => Math.Max(0, Math.Min(Tuning.VersoRankS, rank));

        /// <summary>tweak.verso.perfection (hook.battle-start): attacking at rank A or S, +tune.verso-perfection-hits once.</summary>
        public static void Perfection(GameContext ctx, SeatId me, SeatId attacker)
        {
            if (attacker != me || ctx.State.VersoRank < Tuning.VersoPerfectionRank) return;
            ctx.Actions.AddHits(me, Tuning.VersoPerfectionHits);
        }
    }
}
