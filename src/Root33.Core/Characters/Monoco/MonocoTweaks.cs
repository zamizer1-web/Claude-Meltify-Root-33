using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Monoco
{
    /// <summary>The five feet of state.monoco.feet, in the row's order.</summary>
    public static class Foot
    {
        public const int Agile = 0;
        public const int Caster = 1;
        public const int Balanced = 2;
        public const int Heavy = 3;
        public const int Almighty = 4;
    }

    /// <summary>Pure helpers over state.monoco.feet.</summary>
    public static class MonocoFeetRules
    {
        public static int DistinctFactions(IEnumerable<FootState> feet) =>
            feet.Where(f => f.Earned && f.Faction.HasValue).Select(f => f.Faction!.Value).Distinct().Count();

        public static int Earned(IEnumerable<FootState> feet) => feet.Count(f => f.Earned);

        /// <summary>Earns a foot from <paramref name="faction"/>: a foot not earned yet is earned; a foot already earned is
        /// re-earned from the new faction only if that raises the count of distinct factions. Returns true on a change.</summary>
        public static bool TryEarn(FootState[] feet, int foot, SeatId faction)
        {
            var f = feet[foot];
            var next = feet.Max(x => x.Order) + 1;
            if (!f.Earned)
            {
                f.Earned = true;
                f.Faction = faction;
                f.Order = next;
                return true;
            }
            if (f.Faction == faction) return false;
            var before = DistinctFactions(feet);
            var old = f.Faction;
            f.Faction = faction;
            if (DistinctFactions(feet) > before)
            {
                f.Order = next;
                return true;
            }
            f.Faction = old;
            return false;
        }

        public static void Clear(FootState f)
        {
            f.Earned = false;
            f.Faction = null;
            f.Order = 0;
        }
    }

    /// <summary>Monoco's feet (win.monoco notes) and tweak.monoco.sacred-river-rebirth.</summary>
    public static class MonocoTweaks
    {
        /// <summary>hook.battle-resolved: feet when the Warlord is in the battle and Hundreds hits removed enemy pieces.</summary>
        public static void EarnFeet(GameContext ctx, SeatId me, BattleResult r)
        {
            if (r == null || (r.Attacker != me && r.Defender != me)) return;
            var warlordIn = ctx.Board.PawnClearing(me) == r.Clearing
                || r.Removed.Any(p => p.Owner == me && p.Kind == PieceKind.Pawn);
            if (!warlordIn) return;
            var enemy = r.Removed.Where(p => p.Owner != me).ToList();
            if (enemy.Count == 0) return;
            var feet = ctx.State.MonocoFeet;
            foreach (var p in enemy)
            {
                if (p.Kind == PieceKind.Warrior && r.Attacker == me && r.AttackerMovedInThisTurn) MonocoFeetRules.TryEarn(feet, Foot.Agile, p.Owner);
                if (p.Kind == PieceKind.Building) MonocoFeetRules.TryEarn(feet, Foot.Heavy, p.Owner);
                if (p.Kind == PieceKind.Token) MonocoFeetRules.TryEarn(feet, Foot.Caster, p.Owner);
            }
            if (r.Removed.Any(p => p.Owner == me))
                foreach (var owner in enemy.Select(p => p.Owner).Distinct().OrderBy(s => s.Value))
                    MonocoFeetRules.TryEarn(feet, Foot.Balanced, owner);
            if (ctx.Act < Tuning.MonocoAlmightyAct) return;
            foreach (var leader in Leaders(ctx, me, r))
            {
                var mine = enemy.Where(p => p.Owner == leader).ToList();
                if (mine.Any(p => p.Kind == PieceKind.Building) || mine.Count >= Tuning.MonocoAlmightyPieces)
                    MonocoFeetRules.TryEarn(feet, Foot.Almighty, leader);
            }
        }

        /// <summary>The VP leader at battle start other than Monoco; on a tie (VpLeaderAtStart null) every seat tied for the
        /// top score other than Monoco. The board only reports VP now, so a tie is read from the current scores.</summary>
        public static IEnumerable<SeatId> Leaders(GameContext ctx, SeatId me, BattleResult r)
        {
            if (r.VpLeaderAtStart.HasValue)
                return r.VpLeaderAtStart.Value == me ? new SeatId[0] : new[] { r.VpLeaderAtStart.Value };
            var seats = ctx.Board.Seats();
            if (seats.Count == 0) return new SeatId[0];
            var top = seats.Max(s => ctx.Board.Vp(s));
            return seats.Where(s => s != me && ctx.Board.Vp(s) == top).ToList();
        }

        /// <summary>tweak.monoco.sacred-river-rebirth (hook.warlord-removed): lose Almighty and the most recently earned other
        /// foot. Shows the wheel (do.show-overlay) when a foot was lost. Returns how many feet were lost.</summary>
        public static int SacredRiverRebirth(GameContext ctx, SeatId me, SeatId hundreds)
        {
            if (hundreds != me) return 0;
            var feet = ctx.State.MonocoFeet;
            var lost = 0;
            if (feet[Foot.Almighty].Earned) { MonocoFeetRules.Clear(feet[Foot.Almighty]); lost++; }
            var latest = Enumerable.Range(0, feet.Length).Where(i => i != Foot.Almighty && feet[i].Earned)
                .OrderByDescending(i => feet[i].Order).Cast<int?>().FirstOrDefault();
            if (latest.HasValue) { MonocoFeetRules.Clear(feet[latest.Value]); lost++; }
            if (lost > 0) ctx.Actions.ShowOverlay(Ids.Overlays.MonocoWheel, me, null, null);
            return lost;
        }

        /// <summary>state.monoco.feet onLoad: Almighty cleared below tune.monoco-almighty-act; feet from a seat no longer
        /// seated are cleared.</summary>
        public static void CheckOnLoad(GameContext ctx)
        {
            var feet = ctx.State.MonocoFeet;
            if (ctx.Act < Tuning.MonocoAlmightyAct) MonocoFeetRules.Clear(feet[Foot.Almighty]);
            var seats = ctx.Board.Seats();
            foreach (var f in feet)
                if (f.Earned && (!f.Faction.HasValue || !seats.Contains(f.Faction.Value))) MonocoFeetRules.Clear(f);
        }
    }
}
