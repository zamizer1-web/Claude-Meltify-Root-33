#if !NOUNITY && !ROOT_MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Root33.Core;
using Root33.Plugin.Adapter;
using tuber.canis;
using tuber.canis.entities;

namespace Root33.Plugin.Patches
{
    /// <summary>
    /// The order the core needs at a new game, whatever order Root runs its setup in: every seat assigned
    /// (hook.seat-assigned), then hook.game-start (which resets the core), then hook.map-loaded. Root builds the board inside
    /// TuberMatch.SetupMatch, so a map built before the seats are known waits here until game-start has run.
    /// </summary>
    internal static class GameFlow
    {
        private static bool _resuming, _started;
        private static string? _pendingMap;

        public static void Begin(TuberMatch match, bool isResume)
        {
            RootGame.Inactive = true;   // fail closed: a match Root 33 cannot classify stays plain Root
            RootGame.Track(match);
            _resuming = isResume;
            _started = false;
            _pendingMap = null;
            StackPatches.Reset();
            // Root 33 plays only on this PC: a solo game against Root's bots, pass-and-play or an all-AI game. In an online
            // public or ranked game the mod stays inactive (no Root 33 game, no rule change, no save file).
            RootGame.Inactive = !(match.IsSolo || match.IsPassAndPlay || match.IsAIOnly);
            if (!RootGame.Inactive) return;
            ModRules.Reset();
            PatchGuard.Once("online:" + match.Pointer, "Root 33: online game, the mod stays inactive");
        }

        /// <summary>Seats are known (after TuberMatch.createPlayers, or at the end of SetupMatch at the latest).</summary>
        public static void SeatsReady()
        {
            if (_resuming || _started) return;
            _started = true;
            var seats = Enumerable.Range(0, RootGame.SeatCount).Select(i => new SeatId(i)).ToList();
            var bots = new List<int>();
            foreach (var seat in seats)
            {
                var p = RootGame.Player(seat);
                if (p.IsAI) bots.Add(p.AILevel);
                EngineId engine;
                try { engine = RootGame.EngineOf(p); }
                catch (NotSupportedException e)
                {
                    PatchGuard.Once("engine:" + p.Faction, "Root 33: " + e.Message);
                    continue;
                }
                var isBot = p.IsAI;
                Root33Runtime.Hook("hook.seat-assigned", g => g.OnSeatAssigned(seat, engine, isBot));
            }
            // tuber.client.utils.TuberConstants.AIDifficultyLevels: Easy=0, Medium=1, Hard=2 (the hardest bot sets it).
            var level = bots.Count == 0 ? 1 : bots.Max();
            var difficulty = level <= 0 ? Difficulty.Easy : level == 1 ? Difficulty.Normal : Difficulty.Hard;
            var spoilers = Root33Runtime.Current?.Game.NextGameOptions.Spoilers ?? SpoilerMode.Safe;
            var online = RootGame.Inactive;
            Root33Runtime.Hook("hook.game-start", g =>
            {
                if (online) g.ForgetCharacterSelect();
                g.OnGameStart(seats, difficulty, spoilers);
            });
            if (_pendingMap != null)
            {
                var map = _pendingMap;
                _pendingMap = null;
                Root33Runtime.Hook("hook.map-loaded", g => g.OnMapLoaded(map));
            }
        }

        public static void MapBuilt(string mapName)
        {
            if (_resuming) return;
            if (_started) Root33Runtime.Hook("hook.map-loaded", g => g.OnMapLoaded(mapName));
            else _pendingMap = mapName;
        }

        public static void SetupDone(TuberMatch match)
        {
            if (!_resuming) SeatsReady();
            else if (!RootGame.Inactive) SaveFiles.Load(match);
        }
    }

    /// <summary>hook.save text, stored beside Root's save under the game's id in the dev copy (BepInEx\root33-saves).</summary>
    internal static class SaveFiles
    {
        private static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "root33-saves");

        private static string? PathFor(TuberMatch match)
        {
            var id = match.AnalyticsGameID?.ToString();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var bad in Path.GetInvalidFileNameChars()) id = id!.Replace(bad, '_');
            return Path.Combine(Folder, id + ".txt");
        }

        public static void Save(TuberMatch match)
        {
            var path = PathFor(match);
            if (path == null) return;
            var text = Root33Runtime.Save();
            if (string.IsNullOrEmpty(text)) return;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, text);
        }

        public static void Load(TuberMatch match)
        {
            var path = PathFor(match);
            if (path == null || !File.Exists(path)) return;
            Root33Runtime.Load(File.ReadAllText(path));
        }
    }

    [HarmonyPatch(typeof(tuber.client.menus.prompts.LandingPromptBehaviour), nameof(tuber.client.menus.prompts.LandingPromptBehaviour.initialize))]
    [RootHook("hook.title-screen")]
    internal static class TitleScreenPatch
    {
        // The spoiler choice lives in BepInEx\config\gg.root33.mod.cfg (written on first start), so it is never unanswered.
        private static void Postfix() => PatchGuard.Run("title-screen", () =>
            Root33Runtime.Hook("hook.title-screen", g => g.OnTitleScreenShown(false)));
    }

    [HarmonyPatch(typeof(tuber.client.menus.prompts.ConfigureOfflineGamePromptBehaviour), nameof(tuber.client.menus.prompts.ConfigureOfflineGamePromptBehaviour.initialize))]
    [RootHook("hook.character-select")]
    internal static class CharacterSelectPatch
    {
        /// <summary>The base game's engines, which every copy of Root owns.</summary>
        private static readonly EngineId[] BaseGame =
            { EngineId.Marquise, EngineId.Eyrie, EngineId.Alliance, EngineId.VagabondTinker, EngineId.VagabondRanger, EngineId.VagabondThief };

        private static void Postfix() => PatchGuard.Run("character-select", () =>
        {
            var owned = Owned();
            Root33Runtime.Hook("hook.character-select", g => g.OnCharacterSelectShown(owned, 4));
        });

        /// <summary>The base game plus the factions Root says this account owns (tuber_canis.data.TuberIAPUtilities.OwnedFactions).</summary>
        private static List<EngineId> Owned()
        {
            var owned = new List<EngineId>(BaseGame);
            var dlc = PatchGuard.Run("owned-factions", () => RootGame.Each(tuber_canis.data.TuberIAPUtilities.OwnedFactions()),
                new List<tuber_canis.data.Factions>());
            foreach (var f in dlc)
            {
                EngineId? e = f switch
                {
                    tuber_canis.data.Factions.RiverfolkCompany => EngineId.Riverfolk,
                    tuber_canis.data.Factions.LizardCult => EngineId.Lizards,
                    tuber_canis.data.Factions.UndergroundDuchy => EngineId.Duchy,
                    tuber_canis.data.Factions.CorvidConspiracy => EngineId.Corvids,
                    tuber_canis.data.Factions.LordOfTheHundreds => EngineId.Hundreds,
                    tuber_canis.data.Factions.KeepersInIron => EngineId.Keepers,
                    _ => null,
                };
                if (e.HasValue && !owned.Contains(e.Value)) owned.Add(e.Value);
            }
            return owned;
        }
    }

    [HarmonyPatch(typeof(TuberMatch), nameof(TuberMatch.SetupMatch))]
    [RootHook("hook.game-start")]
    [RootHook("hook.load")]
    internal static class SetupMatchPatch
    {
        private static void Prefix(TuberMatch __instance, bool isResume) => PatchGuard.Run("setup-match prefix", () => GameFlow.Begin(__instance, isResume));
        private static void Postfix(TuberMatch __instance) => PatchGuard.Run("setup-match", () => GameFlow.SetupDone(__instance));
    }

    [HarmonyPatch(typeof(TuberMatch), nameof(TuberMatch.createPlayers))]
    [RootHook("hook.seat-assigned")]
    internal static class CreatePlayersPatch
    {
        private static void Postfix(TuberMatch __instance) => PatchGuard.Run("seat-assigned", () =>
        {
            RootGame.Track(__instance);
            GameFlow.SeatsReady();
        });
    }

    [HarmonyPatch(typeof(TuberBoard), nameof(TuberBoard.Initialize))]
    [RootHook("hook.map-loaded")]
    internal static class MapLoadedPatch
    {
        private static void Postfix(TuberBoard __instance) => PatchGuard.Run("map-loaded", () =>
        {
            var match = RootGame.Track(__instance.match);
            if (match != null) GameFlow.MapBuilt(RootGame.MapName(match.TuberMatchInitData.ChosenMap));
        });
    }

    [HarmonyPatch(typeof(TuberMatch), nameof(TuberMatch.GameOverCleanup))]
    [RootHook("hook.game-over")]
    internal static class GameOverPatch
    {
        private static void Postfix(TuberMatch __instance) => PatchGuard.Run("game-over", () =>
        {
            RootGame.Track(__instance);
            var winners = new List<SeatId>();
            if (__instance.WinnerExists && RootGame.SeatOfAccount(__instance.Winner) is { } winner)
            {
                winners.Add(winner);
                var p = RootGame.Player(winner);
                for (var i = 0; i < RootGame.SeatCount; i++)
                    if (i != winner.Value && p.InDirectCoalitionWith(RootGame.Player(new SeatId(i)))) winners.Add(new SeatId(i));
            }
            Root33Runtime.Hook("hook.game-over", g => g.OnGameOver(winners));
        });
    }

    [HarmonyPatch(typeof(Canis.Match), nameof(Canis.Match.SavePlayerTurn))]
    [RootHook("hook.save")]
    internal static class SavePatch
    {
        private static void Postfix(Canis.Match __instance) => PatchGuard.Run("save", () =>
        {
            var match = RootGame.Track(__instance);
            if (match != null && !RootGame.Inactive) SaveFiles.Save(match);
        });
    }
}
#endif
