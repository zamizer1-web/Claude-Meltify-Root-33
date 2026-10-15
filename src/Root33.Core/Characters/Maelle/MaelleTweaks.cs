using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Maelle
{
    /// <summary>
    /// tweak.maelle.stance-chip (v1, cosmetic). The chip on her card (state.maelle.stance) turns Offensive after she battles,
    /// Defensive after she Aids or is attacked, and Virtuose after she both battles and Aids in one turn
    /// (state.maelle.stance-turn records what she did this turn and resets at the end of her turn; the chip keeps its last
    /// stance). A change shows ui.maelle.stance (do.show-overlay) and her stance bark (do.show-bubble, bark.maelle.04).
    /// Maelle has no spawns rows.
    /// </summary>
    public static class MaelleTweaks
    {
        public const string None = "none";
        public const string Offensive = "offensive";
        public const string Defensive = "defensive";
        public const string Virtuose = "virtuose";
        public const string Battled = "battled";
        public const string Aided = "aided";

        /// <summary>hook.battle-resolved: she battled (attacker) or was attacked (defender).</summary>
        public static void AfterBattle(GameContext ctx, SeatId me, BattleResult r)
        {
            if (r == null) return;
            if (r.Attacker == me)
            {
                ctx.State.MaelleStanceTurn.Add(Battled);
                Set(ctx, me, BothThisTurn(ctx) ? Virtuose : Offensive);
            }
            else if (r.Defender == me) Set(ctx, me, Defensive);
        }

        /// <summary>hook.aid-resolved: her own Aid.</summary>
        public static void AfterAid(GameContext ctx, SeatId me, SeatId vagabond)
        {
            if (vagabond != me) return;
            ctx.State.MaelleStanceTurn.Add(Aided);
            Set(ctx, me, BothThisTurn(ctx) ? Virtuose : Defensive);
        }

        /// <summary>hook.turn-end of her own turn: the 'did both this turn' record resets; the chip keeps its stance.</summary>
        public static void EndTurn(GameContext ctx) => ctx.State.MaelleStanceTurn.Clear();

        /// <summary>hook.load (state.maelle.family): family seats that are now Hostile are dropped (re-picked at her next Birdsong).</summary>
        public static void DropHostileFamily(GameContext ctx, SeatId me)
        {
            foreach (var s in ctx.State.MaelleFamily.ToList())
                if (ctx.Board.RelationshipOf(me, s) == Relationship.Hostile) ctx.State.MaelleFamily.Remove(s);
        }

        private static bool BothThisTurn(GameContext ctx) =>
            ctx.State.MaelleStanceTurn.Contains(Battled) && ctx.State.MaelleStanceTurn.Contains(Aided);

        private static void Set(GameContext ctx, SeatId me, string stance)
        {
            if (ctx.State.MaelleStance == stance) return;
            ctx.State.MaelleStance = stance;
            ctx.Actions.ShowOverlay(Ids.Overlays.MaelleStance, me, null, null);
            ctx.Actions.ShowBubble(me, Ids.Barks.Maelle04);
        }
    }
}
