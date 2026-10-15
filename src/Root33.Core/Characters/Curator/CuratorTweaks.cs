using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Core.Characters.Curator
{
    /// <summary>
    /// The Curator's rule tweaks, spawn and saved state: tweak.curator.quiet-helper (hook.piece-removed),
    /// tweak.curator.home-corner (hook.setup-home), spawn.cell-crack and state.curator.cell-crack (hook.setup-complete,
    /// hook.act-change through story.act3.cell-crack, hook.action-resolved, hook.piece-removed, hook.load).
    /// </summary>
    public static class CuratorTweaks
    {
        /// <summary>state.curator.cell-crack values.</summary>
        public const string None = "none", Inert = "inert", Tunnel = "tunnel", DigOwed = "dig-owed", Gone = "gone";

        // ------------------------------------------------------------ tweak.curator.home-corner

        /// <summary>Corners his home may use: free corners (no other seat's piece) other than Lumière and the Monolith.</summary>
        public static List<ClearingId> HomeCorners(GameContext ctx, SeatId curator)
        {
            var b = ctx.Board;
            var excluded = new HashSet<ClearingId>(ctx.RoleClearings(Ids.MapRoles.Lumiere).Concat(ctx.RoleClearings(Ids.MapRoles.Monolith)));
            return b.Corners().Where(c => !excluded.Contains(c) && b.Seats().Where(s => s != curator)
                .All(s => b.Warriors(s, c) + b.Buildings(s, c) + b.Tokens(s, c) == 0 && b.PawnClearing(s) != c)).ToList();
        }

        /// <summary>hook.setup-home for his seat (do.restrict-home). Fallback: a bot is steered with do.submit-action, a human warned.</summary>
        public static void RestrictHome(GameContext ctx, SeatId curator)
        {
            var allowed = HomeCorners(ctx, curator);
            if (allowed.Count == 0) return;
            if (ctx.SupportsAction(Ids.BoardActions.RestrictHome)) { ctx.Actions.RestrictHomeChoice(curator, allowed); return; }
            if (ctx.Board.IsBot(curator))
            {
                var legal = ctx.Board.LegalActions(curator);
                var key = allowed.Select(CuratorActionKey.HomeKey).FirstOrDefault(legal.Contains);
                if (key != null) ctx.Actions.SubmitAction(curator, key);
            }
            else ctx.Warnings.Add("The Curator's home must be a free corner other than Lumière and the Monolith ("
                + string.Join(", ", allowed.Select(c => c.Value.ToString(CultureInfo.InvariantCulture))) + ").");
        }

        // ------------------------------------------------------------ spawn.cell-crack

        /// <summary>hook.setup-complete: the inert crack overlay in the Monolith.</summary>
        public static void CrackAtSetup(GameContext ctx, SeatId curator)
        {
            ctx.State.CuratorCellCrack = Inert;
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (m.HasValue) ctx.Actions.ShowOverlay(Ids.Overlays.CuratorCellCrack, curator, m.Value, null);
        }

        /// <summary>story.act3.cell-crack: the crack becomes a real tunnel from his supply (a free extra one when all are on the
        /// map). Fallback: 'dig-owed' with a free Dig into the Monolith; with no free Dig either he Digs there normally.</summary>
        public static void OpenCrack(GameContext ctx, SeatId curator)
        {
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (!m.HasValue || ctx.State.CuratorCellCrack == Tunnel) return;
            if (ctx.SupportsAction(Ids.BoardActions.PlaceTunnel))
            {
                var extra = ctx.Board.SupplyPieces(curator, PieceTypes.Tunnel) <= 0;
                if (ctx.Actions.PlaceTunnel(curator, m.Value, extra))
                {
                    ctx.State.CuratorCellCrack = Tunnel;
                    ctx.Actions.HideOverlay(Ids.Overlays.CuratorCellCrack, curator, m.Value, null);
                    return;
                }
            }
            OweDig(ctx, curator, m.Value);
        }

        private static void OweDig(GameContext ctx, SeatId curator, ClearingId monolith)
        {
            ctx.State.CuratorCellCrack = DigOwed;
            if (ctx.SupportsAction(Ids.BoardActions.GrantFreeDig) && ctx.Actions.GrantFreeDig(curator, monolith)) return;
            ctx.Warnings.Add("The Cell Crack could not open: the Curator Digs into the Monolith normally, paying the card.");
        }

        /// <summary>hook.action-resolved: once his tunnel stands in the Monolith again ('dig-owed' or 'gone'), the crack is a tunnel.</summary>
        public static void ActionResolved(GameContext ctx, SeatId curator, SeatId seat)
        {
            var s = ctx.State.CuratorCellCrack;
            if (seat != curator || (s != DigOwed && s != Gone)) return;
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (!m.HasValue || ctx.Board.TokensOfType(curator, m.Value, PieceTypes.Tunnel) == 0) return;
            ctx.State.CuratorCellCrack = Tunnel;
            ctx.Actions.HideOverlay(Ids.Overlays.CuratorCellCrack, curator, m.Value, null);
        }

        /// <summary>hook.piece-removed: Quiet Helper, and his Monolith tunnel leaving the map ('tunnel' becomes 'gone').</summary>
        public static void PieceRemoved(GameContext ctx, SeatId curator, SeatId? remover, RemovedPiece piece, ClearingId c)
        {
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (!m.HasValue || c != m.Value) return;
            var p = ctx.SeatOf(CharacterIds.Paintress);
            if (p.HasValue && piece.Owner == p.Value && piece.Kind == PieceKind.Building && IsRoost(piece.Type)
                && remover.HasValue && remover.Value != curator && remover.Value != p.Value)
            {
                ctx.DrawCards(remover.Value, Tuning.CuratorQuietHelperCards);
                ctx.Barks.Say(curator, Ids.BarkTriggers.QuietHelper);
            }
            if (piece.Owner == curator && piece.Kind == PieceKind.Token && piece.Type == PieceTypes.Tunnel
                && ctx.State.CuratorCellCrack == Tunnel && ctx.Board.TokensOfType(curator, m.Value, PieceTypes.Tunnel) == 0)
                ctx.State.CuratorCellCrack = Gone;
        }

        private static bool IsRoost(string type) =>
            type != null && (type == PieceTypes.Roost || type.StartsWith(PieceTypes.Roost + ".", System.StringComparison.Ordinal));

        /// <summary>hook.load: 'tunnel' with no tunnel of his in the Monolith becomes 'gone'; 'inert' in Act III becomes
        /// 'dig-owed' (the opening never happened). A save without the row is read as 'inert' (he is seated).</summary>
        public static void RecheckOnLoad(GameContext ctx, SeatId curator, IReadOnlyCollection<string> loadedKeys)
        {
            var s = ctx.State.CuratorCellCrack;
            if (!loadedKeys.Contains(Ids.State.CuratorCellCrack) || string.IsNullOrEmpty(s) || s == None) s = Inert;
            ctx.State.CuratorCellCrack = s;
            var m = ctx.Role(Ids.MapRoles.Monolith);
            if (!m.HasValue) return;
            if (s == Tunnel && ctx.Board.TokensOfType(curator, m.Value, PieceTypes.Tunnel) == 0) ctx.State.CuratorCellCrack = Gone;
            else if (s == Inert && ctx.Act >= 3) OweDig(ctx, curator, m.Value);
            var crack = ctx.State.CuratorCellCrack;
            if (crack == Inert || crack == DigOwed) ctx.Actions.ShowOverlay(Ids.Overlays.CuratorCellCrack, curator, m.Value, null);
            else ctx.Actions.HideOverlay(Ids.Overlays.CuratorCellCrack, curator, m.Value, null);
        }
    }
}
