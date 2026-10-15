using System;
using System.Collections.Generic;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Esquie
{
    /// <summary>win.esquie: a quest win at the end of any of his turns once tune.esquie-rocks rocks are held.
    /// The act, coalition and dominance gates come from the row through <see cref="WinRules"/>.</summary>
    public sealed class EsquieWin : WinConditionBase, IGoalProgress
    {
        public EsquieWin() : base("win.esquie") { }

        public override bool QuestComplete(GameContext ctx, SeatId seat) => EsquieTweaks.HeldCount(ctx.State) >= Tuning.EsquieRocks;

        public int UnitsMissing(GameContext ctx, SeatId seat) => Math.Max(0, Tuning.EsquieRocks - EsquieTweaks.HeldCount(ctx.State));

        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var list = new List<ClearingId>();
            for (var i = 0; i < ctx.State.EsquieRocks.Length; i++)
            {
                var c = EsquieTweaks.RockSite(ctx, i);
                if (c.HasValue && !list.Contains(c.Value)) list.Add(c.Value);
            }
            return list;
        }
    }
}
