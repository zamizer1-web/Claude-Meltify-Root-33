#if !NOUNITY && !ROOT_MONO
using System;
using System.Collections.Generic;
using HarmonyLib;
using Root33.Core;
using Root33.Plugin.Adapter;
using tuber.canis;
using tuber.canis.entities;
using tuber.canis.entities.PlayableClasses;
using Canis.actions.continuations;
using RAction = Canis.actions.Action;
using RFactions = tuber_canis.data.Factions;
using RPiece = tuber_canis.data.PieceTypes;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// Root runs every rule step as a Canis action on one stack: ExecuteStack.PushSubAction when an action starts and
    /// ExecuteStack.PopToParent when it has finished. These two patches raise the game_hooks rows tied to an action's type
    /// (sheets/game_hooks.json engineSymbol). What a hook needs from the start of an action (the seat whose turn it is, VP and
    /// relationships before it) is kept by the action's pointer until it pops.
    /// </summary>
    internal static class StackPatches
    {
        private const string A = "tuber.canis.actions.";
        private const string PlayerTurn = A + "PlayerTurnPhase", Birdsong = A + "BirdsongPhase", Evening = A + "EveningPhase";
        private const string Setup = A + "SetupPlayers", Move = A + "MovePieces", PlaceOne = A + "PlacePieceAtTarget";
        private const string GainVp = A + "GainVP", Battle = A + "BattleActions.Battle", Turmoil = A + "EyrieDynastiesActions.TurmoilResolution";
        private const string Explore = A + "VagabondActions.ExploreResolution", Aid = A + "VagabondActions.AidActions.Aid";
        private const string Relationship = A + "VagabondActions.UpdateRelationshipStatus", Hostile = A + "VagabondActions.MarkHostile";

        /// <summary>Root's setup actions and the faction they place for (hook.setup-home).</summary>
        private static readonly Dictionary<string, RFactions> SetupHomes = new Dictionary<string, RFactions>
        {
            [A + "MarquiseDeCatActions.SetStartingLocationForMarquise"] = RFactions.MarquiseDeCat,
            [A + "EyrieDynastiesActions.SetStartingLocationForEyrie"] = RFactions.EyrieDynasties,
            [A + "LizardCultActions.SetStartingLocationForCult"] = RFactions.LizardCult,
            [A + "CorvidConspiracyActions.SetStartingLocationForCorvid"] = RFactions.CorvidConspiracy,
            [A + "UndergroundDuchyActions.SetStartingLocationForDuchy"] = RFactions.UndergroundDuchy,
            [A + "LordOfTheHundredsActions.SetStartingLocationForHundreds"] = RFactions.LordOfTheHundreds,
        };

        private sealed class Started
        {
            public SeatId? Seat;
            public int[]? Vp;
            public Dictionary<(int, int), Relationship>? Relations;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<IntPtr, Started> Open = new Dictionary<IntPtr, Started>();
        private static SeatId? _roundLeader;
        private static int _round;

        /// <summary>A new or loaded game: nothing open, rounds counted afresh.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Open.Clear();
                _roundLeader = null;
                _round = 0;
            }
        }

        private static string NameOf(RAction action) => action.GetIl2CppType().FullName;

        private static SeatId? Current() => RootGame.Match == null ? null : RootGame.SeatOf(RootGame.Match.CurrentTurnPlayer());

        // ------------------------------------------------------------------ an action starts

        public static void Starting(Canis.Match match, RAction action)
        {
            if (RootGame.Track(match) == null || action == null) return;
            var name = NameOf(action);
            switch (name)
            {
                case PlayerTurn:
                    var seat = Current();
                    Remember(action, new Started { Seat = seat });
                    if (seat.HasValue) RoundCheck(seat.Value);
                    break;
                case Birdsong:
                    if (Current() is { } b) Root33Runtime.Hook("hook.birdsong-start", g => g.OnBirdsongStart(b));
                    break;
                case Evening:
                    if (Current() is { } e) Root33Runtime.Hook("hook.evening-start", g => g.OnEveningStart(e));
                    break;
                case GainVp:
                    Remember(action, new Started { Seat = Current(), Vp = AllVp() });
                    break;
                case Relationship:
                case Hostile:
                    Remember(action, new Started { Seat = Current(), Relations = AllRelations() });
                    break;
                case Move:
                case Battle:
                case Explore:
                case Aid:
                    if (name == Battle) ModRules.BattleOver();
                    Remember(action, new Started { Seat = Current() });
                    break;
                default:
                    if (SetupHomes.TryGetValue(name, out var faction))
                    {
                        if (RootGame.SeatOfFaction(faction) is { } home) Root33Runtime.Hook("hook.setup-home", g => g.OnSetupChoice(home));
                    }
                    else if (name == A + "VagabondActions.PlaceInitialVagabondPawn" && Current() is { } vb)
                        Root33Runtime.Hook("hook.setup-home", g => g.OnSetupChoice(vb));
                    break;
            }
        }

        /// <summary>A new round starts when the seat that opened the first round starts its turn again (hook.round-start).</summary>
        private static void RoundCheck(SeatId seat)
        {
            int round;
            lock (Gate)
            {
                _roundLeader ??= seat;
                if (_roundLeader.Value != seat) return;
                round = ++_round;
            }
            Root33Runtime.Hook("hook.round-start", g => g.OnRoundStart(round));
        }

        // ------------------------------------------------------------------ an action has finished

        public static void Finished(Canis.Match match, RAction completed)
        {
            if (RootGame.Track(match) == null || completed == null) return;
            var name = NameOf(completed);
            var started = Forget(completed);
            var seat = started?.Seat;
            switch (name)
            {
                case Setup:
                    Root33Runtime.Hook("hook.setup-complete", g => g.OnSetupComplete());
                    break;
                case Evening:
                    if (Current() is { } e) Root33Runtime.Hook("hook.evening-end", g => g.OnEveningEnd(e));
                    break;
                case PlayerTurn:
                    if (seat.HasValue) Root33Runtime.TurnEnd(seat.Value);
                    break;
                case Move:
                    Moved(completed.TryCast<tuber.canis.actions.MovePieces>());
                    if (seat.HasValue) Resolved(seat.Value);
                    break;
                case Battle:
                    ModRules.BattleOver();
                    if (seat.HasValue) Resolved(seat.Value);
                    break;
                case Explore:
                    if (seat.HasValue)
                    {
                        if (RootReads.PawnClearing(seat.Value) is { } at) Root33Runtime.Hook("hook.explore-resolved", g => g.OnExploreResolved(seat.Value, at));
                        Resolved(seat.Value);
                    }
                    break;
                case Aid:
                    if (seat.HasValue)
                    {
                        var tm = RootGame.Live;
                        var receiver = RootGame.SeatOf(tm.GetAidDetails()?.GetPlayerToAid(tm));
                        if (receiver.HasValue && RootReads.PawnClearing(seat.Value) is { } at)
                            Root33Runtime.Hook("hook.aid-resolved", g => g.OnAidResolved(seat.Value, receiver.Value, at));
                        Resolved(seat.Value);
                    }
                    break;
                case PlaceOne:
                    Placed(completed.TryCast<tuber.canis.actions.PlacePieceAtTarget>()?.piece);
                    break;
                case GainVp:
                    if (started?.Vp != null) VpChanges(started.Vp);
                    break;
                case Relationship:
                case Hostile:
                    if (started?.Relations != null) RelationChanges(started.Relations);
                    break;
                case Turmoil:
                    if (RootGame.SeatOfFaction(RFactions.EyrieDynasties) is { } eyrie) Root33Runtime.Hook("hook.turmoil", g => g.OnTurmoil(eyrie));
                    break;
            }
        }

        private static void Resolved(SeatId seat) => Root33Runtime.Hook("hook.action-resolved", g => g.OnActionResolved(seat));

        private static void Moved(tuber.canis.actions.MovePieces? move)
        {
            if (move == null || !move.SuccessfulMove) return;
            var info = move.SuccessfulMoveInfo;
            if (info == null) return;
            var tm = RootGame.Live;
            var pieces = info.GetMovingPiecesInterface(tm);
            var owner = RootGame.SeatOfPieces(pieces);
            if (!owner.HasValue) return;
            var kind = pieces.TryCast<VagabondPlayer>() != null ? PieceKind.Pawn : PieceKind.Warrior;
            var from = RootGame.ClearingOf(info.GetNode(tm));
            var to = RootGame.ClearingOf(info.GetDestination(tm));
            var count = kind == PieceKind.Pawn ? 1 : info.NumberToMove;
            Root33Runtime.Hook("hook.piece-moved", g => g.OnPieceMoved(owner.Value, kind, from, to, count));
        }

        private static void Placed(Canis.entities.Entity? piece)
        {
            if (piece == null) return;
            var at = RootGame.ClearingOf(piece.Parent?.TryCast<TuberBoardNode>());
            var owner = RootGame.SeatOfFaction(TuberEntityExtensions.Faction(piece));
            if (!at.HasValue || !owner.HasValue) return;
            PieceKind kind;
            string type;
            switch (TuberEntityExtensions.PieceType(piece))
            {
                case RPiece.Warrior: kind = PieceKind.Warrior; type = ""; break;
                case RPiece.Building: kind = PieceKind.Building; type = RootReads.BuildingKey(piece); break;
                case RPiece.Token: kind = PieceKind.Token; type = RootReads.TokenKey(piece); break;
                case RPiece.Pawn: kind = PieceKind.Pawn; type = ""; break;
                default: return;
            }
            Root33Runtime.Hook("hook.piece-placed", g => g.OnPiecePlaced(owner.Value, kind, type, at.Value));
        }

        private static int[] AllVp()
        {
            var vp = new int[RootGame.SeatCount];
            for (var i = 0; i < vp.Length; i++) vp[i] = RootReads.Vp(new SeatId(i));
            return vp;
        }

        private static void VpChanges(int[] before)
        {
            for (var i = 0; i < before.Length && i < RootGame.SeatCount; i++)
            {
                var seat = new SeatId(i);
                var was = before[i];
                var now = RootReads.Vp(seat);
                if (now != was) Root33Runtime.Hook("hook.vp-changed", g => g.OnVpChanged(seat, was, now));
            }
        }

        /// <summary>Every Vagabond's relationship with every other seat, keyed (vagabond, faction).</summary>
        private static Dictionary<(int, int), Relationship> AllRelations()
        {
            var map = new Dictionary<(int, int), Relationship>();
            for (var v = 0; v < RootGame.SeatCount; v++)
            {
                if (RootGame.Player(new SeatId(v)).TryCast<VagabondPlayer>() == null) continue;
                for (var f = 0; f < RootGame.SeatCount; f++)
                {
                    if (f == v || RootGame.Player(new SeatId(f)).TryCast<VagabondPlayer>() != null) continue;
                    map[(v, f)] = RootReads.RelationshipOf(new SeatId(v), new SeatId(f));
                }
            }
            return map;
        }

        private static void RelationChanges(Dictionary<(int, int), Relationship> before)
        {
            foreach (var kv in AllRelations())
            {
                if (!before.TryGetValue(kv.Key, out var was) || was == kv.Value) continue;
                var vagabond = new SeatId(kv.Key.Item1);
                var faction = new SeatId(kv.Key.Item2);
                var now = kv.Value;
                Root33Runtime.Hook("hook.relationship-changed", g => g.OnRelationshipChanged(vagabond, faction, was, now));
            }
        }

        private static void Remember(RAction action, Started started)
        {
            lock (Gate) Open[action.Pointer] = started;
        }

        private static Started? Forget(RAction action)
        {
            lock (Gate)
            {
                if (!Open.TryGetValue(action.Pointer, out var s)) return null;
                Open.Remove(action.Pointer);
                return s;
            }
        }
    }

    [HarmonyPatch(typeof(ExecuteStack), nameof(ExecuteStack.PushSubAction))]
    [RootHook("hook.setup-home")]
    [RootHook("hook.round-start")]
    [RootHook("hook.birdsong-start")]
    [RootHook("hook.evening-start")]
    internal static class PushSubActionPatch
    {
        private static void Prefix(Canis.Match match, RAction action) =>
            PatchGuard.Run("PushSubAction", () => StackPatches.Starting(match, action));
    }

    [HarmonyPatch(typeof(ExecuteStack), nameof(ExecuteStack.PopToParent))]
    [RootHook("hook.setup-complete")]
    [RootHook("hook.evening-end")]
    [RootHook("hook.turn-end")]
    [RootHook("hook.action-resolved")]
    [RootHook("hook.piece-moved")]
    [RootHook("hook.piece-placed")]
    [RootHook("hook.explore-resolved")]
    [RootHook("hook.aid-resolved")]
    [RootHook("hook.relationship-changed")]
    [RootHook("hook.vp-changed")]
    [RootHook("hook.turmoil")]
    internal static class PopToParentPatch
    {
        private static void Postfix(Canis.Match match, RAction completedAction) =>
            PatchGuard.Run("PopToParent", () => StackPatches.Finished(match, completedAction));
    }
}
#endif
