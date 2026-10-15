using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>
    /// tweak.sciel.twilight (v1.1, automatic). Placing a Lantern gives her a Sun charge (state.sciel.sun); removing an enemy
    /// warrior in a battle she starts gives a Moon charge (state.sciel.moon). Holding both at the start of her Evening arms
    /// Twilight (state.sciel.twilight-armed): her first battle that Evening deals +tune.sciel-twilight-hits hit, then both
    /// charges reset. Charges are held (yes or no), not counted, and are kept across Evenings until a boosted battle.
    /// Sciel has no spawns rows.
    /// </summary>
    public static class ScielTweaks
    {
        /// <summary>hook.piece-placed: a Lantern she places gives the Sun charge.</summary>
        public static void OnPiecePlaced(GameContext ctx, SeatId me, SeatId owner, PieceKind kind, string type)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.ScielTwilight)) return; // v1.1: off in the first release
            if (owner == me && kind == PieceKind.Token && type == PieceTypes.Sympathy) ctx.State.ScielSun = true;
        }

        /// <summary>hook.evening-start of her own turn: holding Sun and Moon arms Twilight.</summary>
        public static void ArmAtEveningStart(GameContext ctx)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.ScielTwilight)) return; // v1.1: off in the first release
            if (ctx.State.ScielSun && ctx.State.ScielMoon) ctx.State.ScielTwilightArmed = true;
        }

        /// <summary>hook.battle-start: her first battle while Twilight is armed deals the extra hit (do.add-hits).</summary>
        public static void BoostBattle(GameContext ctx, SeatId me, SeatId attacker)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.ScielTwilight)) return; // v1.1: off in the first release
            if (attacker == me && ctx.State.ScielTwilightArmed) ctx.Actions.AddHits(me, Tuning.ScielTwilightHits);
        }

        /// <summary>hook.battle-resolved: the boosted battle resets both charges (after the battle, so it leaves no Moon);
        /// any other battle she starts that removes an enemy warrior gives the Moon charge.</summary>
        public static void AfterBattle(GameContext ctx, SeatId me, BattleResult r)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.ScielTwilight)) return; // v1.1: off in the first release
            if (r == null || r.Attacker != me) return;
            if (ctx.State.ScielTwilightArmed)
            {
                ctx.State.ScielSun = false;
                ctx.State.ScielMoon = false;
                ctx.State.ScielTwilightArmed = false;
                return;
            }
            if (r.Removed.Any(p => p.Owner != me && p.Kind == PieceKind.Warrior)) ctx.State.ScielMoon = true;
        }

        /// <summary>hook.turn-end of her own turn: an unused arming lapses (the charges themselves are kept).</summary>
        public static void EndTurn(GameContext ctx)
        {
            if (!ctx.RuleOn(Ids.RuleTweaks.ScielTwilight)) return; // v1.1: off in the first release
            ctx.State.ScielTwilightArmed = false;
        }
    }
}
