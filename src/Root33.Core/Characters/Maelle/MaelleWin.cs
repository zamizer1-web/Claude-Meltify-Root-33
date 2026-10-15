using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Maelle
{
    /// <summary>The parts of win.maelle's test, shared by the win, the Goal Tracker and the bot (public board reads only).</summary>
    public static class MaelleRules
    {
        /// <summary>F: the seats whose engine is not a Vagabond (read.seats, read.seat-engine).</summary>
        public static List<SeatId> Factions(IBoardView b, SeatId me) =>
            b.Seats().Where(o => o != me && !GameContext.IsVagabondEngine(b.EngineOf(o))).ToList();

        public static int Allied(IBoardView b, SeatId me) => Factions(b, me).Count(o => b.RelationshipOf(me, o) == Relationship.Allied);

        public static int Hostile(IBoardView b, SeatId me) => Factions(b, me).Count(o => b.RelationshipOf(me, o) == Relationship.Hostile);

        /// <summary>tune.maelle-allied with two or more non-Vagabond factions; 1 when only one is seated.</summary>
        public static int AlliedNeeded(IBoardView b, SeatId me) => Factions(b, me).Count >= 2 ? Tuning.MaelleAllied : 1;

        /// <summary>tune.countdown-vp with two or more non-Vagabond factions; tune.maelle-2p-vp when only one is seated.</summary>
        public static int VpNeeded(IBoardView b, SeatId me) => Factions(b, me).Count >= 2 ? Tuning.CountdownVp : Tuning.Maelle2pVp;

        /// <summary>Items with Damaged == false (exhausted items count).</summary>
        public static int Undamaged(IBoardView b, SeatId me) => b.Items(me).Count(i => !i.Damaged);

        /// <summary>The relationship parts: Allied with tune.maelle-allied factions of F (with |F| == 1: Allied with it) and
        /// Hostile with none. False when no non-Vagabond faction is seated.</summary>
        public static bool RelationshipsHold(IBoardView b, SeatId me)
        {
            var f = Factions(b, me);
            if (f.Count == 0) return false;
            var allied = Allied(b, me);
            var alliedOk = f.Count >= 2 ? allied >= Tuning.MaelleAllied : allied == 1;
            return alliedOk && Hostile(b, me) == 0;
        }

        public static bool InLumiere(IBoardView b, SeatId me, ClearingId? lumiere) =>
            lumiere.HasValue && b.PawnClearing(me) == lumiere.Value;

        /// <summary>The whole themed test of win.maelle (gates excluded; the one-faction VP rule is part of it).</summary>
        public static bool Test(IBoardView b, SeatId me, ClearingId? lumiere) =>
            RelationshipsHold(b, me) && b.Vp(me) >= VpNeeded(b, me) && InLumiere(b, me, lumiere)
            && Undamaged(b, me) >= Tuning.MaelleItems;
    }

    /// <summary>
    /// win.maelle, A Home in the Canvas (countdown). The row's gates (act &gt;= 2, Vp &gt;= tune.countdown-vp, no active
    /// dominance, no coalition partner) are applied by <see cref="WinRules"/>. Announce at the end of her turn: Allied with
    /// tune.maelle-allied non-Vagabond factions (only one seated: Allied with it and tune.maelle-2p-vp VP), Hostile with
    /// none, her pawn in Lumière and tune.maelle-items undamaged items. Confirm at her next Birdsong: the same test.
    /// </summary>
    public sealed class MaelleWin : WinConditionBase, IGoalProgress
    {
        public MaelleWin() : base(Ids.WinConditions.Maelle) { }

        public override bool Announce(GameContext ctx, SeatId seat) =>
            MaelleRules.Test(ctx.Board, seat, ctx.Role(Ids.MapRoles.Lumiere));

        public override bool Confirm(GameContext ctx, SeatId seat) => Announce(ctx, seat);

        /// <summary>Goal Tracker units: missing Allied factions, each Hostile faction, Lumière, missing undamaged items, and
        /// (one faction seated) the tune.maelle-2p-vp mark once the row's own VP gate is met.</summary>
        public int UnitsMissing(GameContext ctx, SeatId seat)
        {
            var b = ctx.Board;
            var units = Math.Max(0, MaelleRules.AlliedNeeded(b, seat) - MaelleRules.Allied(b, seat))
                        + MaelleRules.Hostile(b, seat)
                        + (MaelleRules.InLumiere(b, seat, ctx.Role(Ids.MapRoles.Lumiere)) ? 0 : 1)
                        + Math.Max(0, Tuning.MaelleItems - MaelleRules.Undamaged(b, seat));
            var vp = b.Vp(seat);
            if (MaelleRules.Factions(b, seat).Count == 1 && vp >= VpMin && vp < Tuning.Maelle2pVp) units++;
            return units;
        }

        /// <summary>Lumière (where the counterplay attacks her), then her pawn's clearing when she is elsewhere.</summary>
        public IReadOnlyList<ClearingId> KeyClearings(GameContext ctx, SeatId seat)
        {
            var keys = new List<ClearingId>();
            var lumiere = ctx.Role(Ids.MapRoles.Lumiere);
            if (lumiere.HasValue) keys.Add(lumiere.Value);
            var pawn = ctx.Board.PawnClearing(seat);
            if (pawn.HasValue && !keys.Contains(pawn.Value)) keys.Add(pawn.Value);
            return keys;
        }
    }
}
