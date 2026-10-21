#if !NOUNITY && !ROOT_MONO
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Root33.Core;
using Root33.Plugin.Adapter;
using tuber.canis.actions.BattleActions;
using tuber.canis.entities;
using tuber.canis.entities.PlayableClasses;
using tuber.canis.entities.ai;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// Root's virtual methods are overridden by some factions (an override is a method of its own to Harmony), so a patch
    /// lists every override. When an override calls its base, both run: <see cref="Outermost"/> lets only the outer call
    /// apply the mod's change, per thread.
    /// </summary>
    internal sealed class Outermost
    {
        [ThreadStatic] private static Dictionary<string, int>? _depth;
        private readonly string _key;

        public Outermost(string key) { _key = key; }

        public void Enter()
        {
            _depth ??= new Dictionary<string, int>();
            _depth[_key] = (_depth.TryGetValue(_key, out var d) ? d : 0) + 1;
        }

        /// <summary>Leaves one level; true when that was the outermost call.</summary>
        public bool Leave()
        {
            if (_depth == null || !_depth.TryGetValue(_key, out var d)) return true;
            _depth[_key] = d - 1;
            return d <= 1;
        }

        public static IEnumerable<MethodBase> Overrides(string name, params Type[] types)
        {
            foreach (var t in types)
            {
                var m = AccessTools.DeclaredMethod(t, name);
                if (m != null) yield return m;
            }
        }
    }

    /// <summary>do.add-hits: the seat's extra hits for the battle being fought go on top of Root's own (Ambush, defenseless).</summary>
    [HarmonyPatch]
    internal static class ExtraHitsPatch
    {
        private static readonly Outermost Guard = new Outermost("extra-hits");

        private static IEnumerable<MethodBase> TargetMethods() => Outermost.Overrides(nameof(TuberPlayerEntity.GetExtraHits),
            typeof(TuberPlayerEntity), typeof(CorvidConspiracyPlayer), typeof(EyrieDynastiesPlayer), typeof(LordOfTheHundredsPlayer));

        private static void Prefix() => Guard.Enter();

        private static void Postfix(TuberPlayerEntity __instance, Il2CppSystem.ValueTuple<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray> __result)
        {
            if (!Guard.Leave()) return;
            PatchGuard.Run("extra-hits", () =>
            {
                if (RootGame.SeatOf(__instance) is { } seat && ModRules.HitsFor(seat) is var n && n != 0) __result.Item1 += n;
            });
        }
    }

    /// <summary>do.ignore-first-hit: one more ignored hit for the seat in the battle being fought (like the Keepers' Devout Knights).</summary>
    [HarmonyPatch]
    internal static class IgnoredHitsPatch
    {
        private static readonly Outermost Guard = new Outermost("ignored-hits");

        private static IEnumerable<MethodBase> TargetMethods() => Outermost.Overrides(nameof(TuberPlayerEntity.GetIgnoredHits),
            typeof(TuberPlayerEntity), typeof(KeepersInIronPlayer), typeof(LordOfTheHundredsPlayer));

        private static void Prefix() => Guard.Enter();

        private static void Postfix(TuberPlayerEntity __instance, Il2CppSystem.ValueTuple<int, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray> __result)
        {
            if (!Guard.Leave()) return;
            PatchGuard.Run("ignored-hits", () =>
            {
                if (RootGame.SeatOf(__instance) is { } seat && ModRules.IgnoresFirstHit(seat)) __result.Item1 += 1;
            });
        }
    }

    /// <summary>do.block-placement: Root's own checks for placing and building say no in a blocked clearing.</summary>
    [HarmonyPatch(typeof(TuberPlayerEntity), nameof(TuberPlayerEntity.CanPlacePieceAt))]
    internal static class CanPlacePatch
    {
        private static void Postfix(TuberPlayerEntity __instance, TuberBoardNode destination, ref bool __result)
        {
            if (!__result) return;
            var blocked = PatchGuard.Run("can-place", () =>
                RootGame.SeatOf(__instance) is { } seat && RootGame.ClearingOf(destination) is { } c && ModRules.IsBlocked(seat, c), false);
            if (blocked) __result = false;
        }
    }

    [HarmonyPatch(typeof(TuberPlayerEntity), nameof(TuberPlayerEntity.CanBuildAt))]
    internal static class CanBuildPatch
    {
        private static void Postfix(TuberPlayerEntity __instance, TuberBoardNode area, ref bool __result)
        {
            if (!__result) return;
            var blocked = PatchGuard.Run("can-build", () =>
                RootGame.SeatOf(__instance) is { } seat && RootGame.ClearingOf(area) is { } c && ModRules.IsBlocked(seat, c), false);
            if (blocked) __result = false;
        }
    }

    /// <summary>hook.battle-start: attacker, defender and clearing are fixed and nothing is rolled. Root asks for an Ambush
    /// first (ChooseAmbush); a battle that skips that state reaches the dice (DiceRoll), so the first of the two raises it once.</summary>
    internal static class BattleStart
    {
        private static IntPtr _last;

        public static void Raise(Battle battle) => PatchGuard.Run("battle-start", () =>
        {
            if (battle.Pointer == _last) return;
            _last = battle.Pointer;
            RootGame.Track(battle.Match);
            var attacker = RootGame.SeatOfPieces(battle._attacker);
            var defender = RootGame.SeatOfPieces(battle._defender);
            var at = RootGame.ClearingOf(battle._battleClearing);
            if (attacker.HasValue && defender.HasValue && at.HasValue)
                Root33Runtime.Hook("hook.battle-start", g => g.OnBattleStarting(attacker.Value, defender.Value, at.Value));
        });
    }

    [HarmonyPatch(typeof(Battle), nameof(Battle.ChooseAmbush))]
    [RootHook("hook.battle-start")]
    internal static class BattleAmbushPatch
    {
        private static void Prefix(Battle __instance) => BattleStart.Raise(__instance);
    }

    [HarmonyPatch(typeof(Battle), nameof(Battle.DiceRoll))]
    internal static class BattleDicePatch
    {
        private static void Prefix(Battle __instance) => BattleStart.Raise(__instance);
    }

    /// <summary>
    /// Where hook.bot-decision will plug in (M6): Root's AI decides every selection in AIProfile.MakeChoice. Until Root's
    /// selections can be turned into the core's action keys (read.legal-actions), this only logs the first time each kind
    /// of selection reaches each faction's AI, and Root's AI always decides.
    /// </summary>
    [HarmonyPatch]
    internal static class BotDecisionWatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => Outermost.Overrides(nameof(AIProfile.MakeChoice),
            typeof(AIProfile), typeof(EyrieDynastiesAIProfile), typeof(KeepersInIronAIProfile), typeof(LordOfTheHundredsAIProfile),
            typeof(MarquiseDeCatAIProfile), typeof(WoodlandAllianceAIProfile));

        private static void Prefix(AIProfile __instance, Networking.selection.messages.SelectionMessage sm) => PatchGuard.Run("bot-decision watch", () =>
        {
            var faction = __instance.player?.Faction.ToString() ?? "?";
            var kind = sm?.GetIl2CppType().FullName ?? "null";
            PatchGuard.Once("ai:" + faction + ":" + kind, $"Root 33: Root's AI ({faction}) decides a {kind}");
        });
    }
}
#endif
