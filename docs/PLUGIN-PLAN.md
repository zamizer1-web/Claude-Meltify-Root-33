# Root 33 plugin: the plan for the session on the creator's PC

This is the work order for building Root 33 into Root, on the dev copy of Root at `D:\Mods\Root33-dev` (BepInEx 6 IL2CPP
working). Each milestone ends with an acceptance check; do not start the next one before it passes. The creator's
preferences hold throughout: **verification first** (prove it on your side, then ask them to check on theirs), everything on
the **D: drive**, the real Steam install and their Dire Wolf account untouched, and they are interrupted only when something
truly needs them.

## The kit (built and checked in the cloud)

| Part | What it is | Checked in the cloud |
|---|---|---|
| `src/Root33.Core` | every rule of Root 33 (netstandard2.0) | 432 tests |
| `src/Root33.Host` | what a host shows, the same in the tabletop and in Root: `ModText` (names, titles, banners, barks, acts, story), `ModView` (act banner, Monolith Number, countdown banners, Goal Tracker with each character's tracker, story, per-seat speech bubbles), `ModFeed`, `ModCards`, `ModGlossary` (the mod's own words for newcomers, in the spoiler mode's words), `ModDisplay`, `HookDispatcher` (nested hooks queued, core errors logged once per hook) | 45 tests; the tabletop now runs on it with byte-identical output |
| `src/Root33.Sim` | the text tabletop of Root that runs the real core (the playtests) | 114 tests |
| `tools/Root33.Discover` | reads a folder of assemblies as metadata (nothing runs) and writes `types.json` and `report.md` (Root concepts, game-flow types, enums) | 27 tests over the repo's own assemblies |
| `src/Root33.Plugin` | the BepInEx 6 plugin (not in `src/Root33.sln`) | compiles with `-p:NoUnity=true` (and `-p:RootMono=true`); see "Not verified" |
| `tools/gen_plugin_stubs.py` | writes `Adapter/RootBoard.cs`, `Adapter/EngineIds.cs`, `Patches/HookMap.cs` from the sheets | `--check` mode |
| `tools/pc/build-plugin.ps1`, `tools/pc/discover.ps1` | build and install into the dev copy; run Discover on it | parse-checked; refuse any path not under `D:\Mods` |

The plugin, file by file:

- `Plugin.cs`: `[BepInPlugin("gg.root33.mod", "Root 33", "0.1.0")] Root33Plugin : BasePlugin`. `Load()` logs
  `Root 33 loaded (core <version>, sheets <count> rows)`, reads `BepInEx\config\gg.root33.mod.cfg` (SpoilerMode safe/finished,
  E33Mode, ProbeMode), starts `Root33Runtime`, applies every `[HarmonyPatch]` class (Patches/), then injects and adds the panel
  and, in probe mode, the probe. A `#if ROOT_MONO` entry (`BaseUnityPlugin`) is kept for a Mono build (`-p:RootMono=true`).
- `Root33Runtime.cs`: one `Root33Game` for the session over `RootBoard`, called through Root33.Host's `HookDispatcher`. Patches
  call `Root33Runtime.Hook(id, g => ...)`, `Ask(...)`, `TurnEnd(seat)`, `Save()`, `Load(text)`. Actions not mapped yet are
  reported to the core as unsupported, so it takes their sheet fallbacks. `PanelText()` is what the overlay shows.
- `RootHost.cs`: what the plugin supplies to Root33.Host (`IModHost`).
- `Adapter/RootBoard.cs` (generated): every `IBoardView` / `IBoardActions` member not mapped yet; each throws
  `NotMappedException` naming the sheet row and its `engineSymbol` guess. `Adapter/RootBoard.Mapped.cs` (yours): mapped members,
  marked `[Mapped("read.seats")]`, plus `Display` (banners, titles, overlays) and `BubbleShown`. `Adapter/EngineIds.cs`
  (generated): Root's own clearing ids (map_autumn.engineClearingId) and faction types (engines.engineType).
- `Patches/HookMap.cs` (generated): one commented placeholder per game_hooks row (when it fires, the core handler, the
  engineSymbol guess, the patch kind) with the Harmony patch to write. Your patches go in `Patches/*.cs`, marked
  `[RootHook("hook.evening-end")]`.
- `Unity/Probe.cs`: logs every scene load; F9 writes `BepInEx\root33-probe.json` (each scene's objects, depth-limited, with
  their components' type names, and how often each component type occurs). `Unity/Overlay.cs`: the F8 panel (plain IMGUI:
  `GUI.Box` and `GUI.Label`, no GUILayout); F7 switches it between the game and the mod's word list (Root33.Host's
  `ModGlossary`, in the game's spoiler mode, or the configured one between games). `Unity/KeyWatch.cs`: F7/F8/F9 through legacy Input or, if the game strips it, IMGUI events.

Workflow for every mapped member or patch: confirm Root's symbol (report.md, the probe, UnityExplorer), write it into the
sheet's `engineSymbol` (or `engineClearingId`, `engineType`, `patchKind`) and remove that column from the row's `_unverified`,
write the code marked `[Mapped(...)]` / `[RootHook(...)]`, then:

```powershell
python -I tools\gen_plugin_stubs.py                  # stubs and HookMap follow the sheets and your marks
python -I tools\preflight.py --target plugin         # the work order shrinks
powershell -ExecutionPolicy Bypass -File tools\pc\build-plugin.ps1
```

Keep out of the repo: Root's assemblies, decompiled code, `types.json`, `report.md` and probe files (they describe Root's
code; they stay in `D:\Mods\Root33-dev\discover`), `pc-report.json`, and any key, token or private path of the creator's PC.
Only Root's symbol names go into the sheets.

## M0: the plugin loads in the dev copy

```powershell
cd D:\Mods\Claude-Meltify-Root-33        # the clone
git pull
powershell -ExecutionPolicy Bypass -File tools\pc\build-plugin.ps1        # -RootDir if the copy is elsewhere under D:\Mods
```

The script finds the game in `D:\Mods\Root33-dev` (or `D:\Mods\Root33-dev\Root`), checks the stubs, builds against
`BepInEx\core` and `BepInEx\interop`, and installs `Root33.Plugin.dll`, `Root33.Core.dll` and `Root33.Host.dll` into
`BepInEx\plugins\Root33\`. Start the dev copy.

Acceptance:
- `BepInEx\LogOutput.log` has `Root 33 loaded (core 1.0.0+<rev>, sheets <n> rows)`, `Root 33 settings: ...`,
  `Root 33: 0 Root method(s) patched; ...`, `Root 33: panel (F8) ready` and `Root 33: probe (F9) ready`, and no error from Root 33.
- The panel is up at the top left: `Root 33 loaded · waiting for the board adapter`, then
  `board adapter: 0/59 reads, 0/26 actions mapped · hooks: 0/34 patched · core ...`. F8 hides and shows it.
- F9 writes `BepInEx\root33-probe.json` (log: `Root 33 probe: wrote ...`), and every scene change logs `Root 33 probe: scene loaded: ...`.
- `BepInEx\config\gg.root33.mod.cfg` exists with SpoilerMode, E33Mode and ProbeMode.

If the build fails on a Unity call (the Unity files were checked against Unity 2020.3's real API in the Mono variant and
against Il2CppInterop with stand-in Unity types, not against Root's own interop assemblies), fix it against
`BepInEx\interop` and note the fix here. If a component logs `could not start` (ClassInjector), or nothing draws, see
"Risks" below.

Fixes found on the PC (Root build 22238765, BepInEx 6.0.0-be.790):
- Root's interop assemblies put types named `ConfigFile` and `Paths` in the global namespace, and they hide BepInEx's
  under a `using`. The plugin names `BepInEx.Configuration.ConfigFile` and `BepInEx.Paths` in full (Plugin.cs,
  PluginSettings.cs, Unity/Probe.cs). Any new file that uses them must do the same.
- M0 passed: the log shows every acceptance line and no Root 33 error, F9 wrote the probe and every scene change is logged.

## M1: discover

```powershell
powershell -ExecutionPolicy Bypass -File tools\pc\discover.ps1          # -All to report on Unity and framework assemblies too
```

It writes `D:\Mods\Root33-dev\discover\types.json` and `report.md` and copies the probe file next to them. Press F9 in the
dev copy on the title screen, on the faction select screen, during setup and during a game against bots first, and keep
each probe file (rename them; F9 overwrites).

Read `report.md` top down: "Concepts at a glance", Singletons (live objects the reads can start from), Managers / controllers
/ state machines, Enums (factions, phases, suits, piece types, AI levels), then each concept. Confirm in the running game with
UnityExplorer (dev copy only). Then fill the sheets' plugin columns and clear their `_unverified` entries:

| Sheet | Column | Rows |
|---|---|---|
| `board_reads.json` | `engineSymbol` | 59 |
| `board_actions.json` | `engineSymbol` (a row Root cannot do keeps its fallback; say so in `_notes`) | 26 |
| `game_hooks.json` | `engineSymbol`, `patchKind` (prefix/postfix/poll) | 34 |
| `map_autumn.json` | `engineClearingId` (Root's id for each clearing, matched by position) | 12 |
| `engines.json` | `engineType`, `digitalAi` | 12 |

Found on the PC (see each row's `_notes` for the source):
- Root33.Discover needs .NET 8 to build; it also builds for net6.0 unchanged. MetadataLoadContext cannot name a few
  nested types in Unity's own interop assemblies (8 types, none of Root's); the scanner now leaves those out and says so in
  the report instead of failing.
- Root's game logic is `tuber-canis` (`tuber.canis.*`, `tuber_canis.data.*` enums), the menus are `tuber-client`, the
  views are `voodoo` (`lotus.ViewManager.ViewFor(EntityID, viewLayer)`). The live match is
  `Canis.boardgames.CanisMatch.Match` (a MonoBehaviour), cast to `tuber.canis.TuberMatch`.
- Every rule step is a `Canis.actions.Action` whose `execute()` is an iterator, run by
  `Canis.actions.continuations.ExecuteStack`: `PushSubAction(Match, Action)` when an action starts and
  `PopToParent(Match, Action completedAction)` when it finishes. Most hooks are one of those two patches, dispatched on
  the action's type. The match runs on its own thread (`Canis.Match.Run`), so core calls go to the main thread.
- The AI decides in `tuber.canis.entities.ai.AIProfile.MakeChoice(SelectionMessage, TuberAIChoiceContext)`.
- Root supports an extra win condition per seat: `TuberPlayerEntity.altWinCondition` (`AdHocWinCondition`,
  `WinConditionUtils.Or`), which `do.declare-winner` uses.
- Autumn clearing ids come from a dev-only dump of Root's own `FallBoardLayout` (node index, suit, slots, neighbours,
  rivers, opposite corners), matched to the sheet by shape. Root's ruins are at its clearings 3, 5, 6 and 7, which are
  autumn.10, .08, .07 and .09. The sheet has a ruin at autumn.12 instead of autumn.07: the creator decides which to keep.
- Rows that still need a running game keep their `_unverified` entry with the reason (relics, setup restrictions, view
  layers, the AI's action keys, round counting).

`assets.json` (Clair Obscur paths) and `reskins.json` (localisation keys) belong to HANDOFF step 5 and M5.

Acceptance:
- `python -I tools\preflight.py --target plugin` lists no UNVERIFIED cell in board_reads, board_actions, game_hooks, map_autumn
  or engines (the rows Root cannot support say so in `_notes` and keep a fallback).
- `python -I tools\gen_plugin_stubs.py` regenerated; `--check` is clean; each stub's message now names Root's real symbol.
- `python -I tools\preflight.py --target core --allow-missing-tests` still 0 blocking; `dotnet test src\Root33.sln` green.

## M2: the read-only board adapter

Map the reads in `Adapter/RootBoard.Mapped.cs`, each marked `[Mapped("read.x")]`, in this order: read.seats, read.seat-engine,
read.is-bot, read.current-seat, read.phase, read.round, read.vp, read.active-dominance, read.coalition-partner, read.map-name,
read.clearings, read.suits-of, read.path-neighbours, read.river-neighbours, read.on-river, read.corners, read.opposite-corner,
read.has-ruin, read.total-slots, read.free-slots, read.forests-around, read.forest-clearings, read.ruler, read.warriors,
read.buildings, read.buildings-of-type, read.tokens, read.tokens-of-type, read.pawn-clearing, read.pawn-forest,
read.supply-warriors, read.supply-pieces, read.hand-size, read.hand-suits, read.items, read.relationship, read.crafted-items,
read.eyrie-leader, read.eyrie-decree, read.alliance-supporters, read.alliance-officers. Clearings are the core's numbers
(`EngineIds.AutumnClearingFromRoot`); seats are 0-based in Root's turn order. The DLC reads (relics, Duchy, Corvids, Lizards,
Hundreds) can wait until those factions are seated.

The core only runs in a game created through the mod, so M2 also patches the game-creation hooks: hook.character-select,
hook.seat-assigned, hook.game-start, hook.map-loaded, hook.setup-complete (copy each placeholder from `HookMap.cs`; mark it
`[RootHook(...)]`). The panel names the next read to map (`next to map: read.x (...)`) until the Goal Tracker can be drawn.

Acceptance, in a real game (Sciel, Gustave, the Paintress and Esquie, bots for all but Sciel):
- The panel shows `ROOT 33 · Act I: The Landing · Monolith Number <33 minus the top VP>` and a Goal Tracker row for each
  character, and every value matches the board on screen: VP gates, Sciel's Sanctuaries and Aquafarms, the Paintress's
  Portraits and Monolith easel, Gustave's flags and Chain, Esquie's rocks. Check two turns, and after a battle.
- The STORY SO FAR lists Act I: The Landing; the log has `=== Root 33 · Act I: The Landing ===`.
- No `Root 33 core error` in the log from a mapped read.

## M3: the hooks

Patch the rest of the game flow, one `[RootHook]` class each, from the placeholders in `HookMap.cs`: hook.round-start,
hook.birdsong-start, hook.evening-start, hook.evening-end, hook.turn-end (`Root33Runtime.TurnEnd(seat)`), hook.action-resolved,
hook.piece-placed, hook.piece-removed, hook.piece-moved, hook.battle-legal, hook.battle-start, hook.battle-resolved,
hook.explore-resolved, hook.aid-resolved, hook.hostility-change, hook.relationship-changed, hook.item-changed, hook.vp-changed,
hook.turmoil, hook.setup-home, hook.game-over, hook.save (`Root33Runtime.Save()`) and hook.load (`Root33Runtime.Load(text)`),
hook.title-screen. Map the six display actions first, one line each (the lines are in `RootBoard.Mapped.cs`): do.show-banner,
do.clear-banner, do.show-bubble, do.show-overlay, do.hide-overlay, do.set-seat-title.

Root may inline small methods (an inlined method cannot be patched: patch its caller) or run the AI on its own thread
(the core is not thread-safe: hand the call to the main thread).

Acceptance, in a game against bots:
- The act changes on the panel and in the log (`=== Root 33 · Act II: The Monolith ===`) when someone reaches 10 VP or after
  round 4, with the Act II story events.
- Speech bubbles appear: the log has `seat N (<name>) says: "..."` lines from barks.json, and the panel lists
  `SPEECH BUBBLES SINCE YOUR LAST TURN`, which empties after your own turn.
- A countdown banner goes up when a character meets its themed win at the end of its turn (stage one with VP and pieces in a
  test game if needed), and the win is confirmed or the banner comes down at its next Birdsong.
- Save, quit, load: act, banners and trackers are back; three rounds with no `Root 33 core error` in the log.

## M4: the actions

Map the board actions that change Root's game: do.restrict-home and do.restrict-start-forest (forced homes: Gustave's keep in
Lumière, the Paintress's first roost in the Monolith, Esquie's start forest next to his Nest), do.place-warriors,
do.set-guard-floor and do.block-placement (Esquie's rocks and their guards), do.set-seat-title on Root's own seat labels,
do.score-vp, do.draw-cards, do.add-hits, do.add-max-hits, do.ignore-first-hit, do.move-warriors, do.grant-ability,
do.hide-clockwork, do.declare-winner. A row Root cannot do stays unmapped: the core takes its sheet fallback.

Acceptance, new games with Gustave, the Paintress and Esquie:
- Setup offers only Lumière for the keep, the Monolith for the first roost and the forests next to Esquie's Nest.
- Florrie appears at Stone Wave Cliffs with her guards; the host cannot move them below the floor or build there; Esquie
  claims her as the rules say.
- Seats show the characters' names on Root's own HUD; a declared themed win ends the game with that character's win.

## M5: the reskins

Runtime string patching only (no file in Root's folder changes): the Continent's clearing names (map_autumn continentName),
the characters' names and pieces (reskins.json, safe or finished by the spoiler mode), and the ROOT 33 title with the 33
tearing out of the rift (`art/title-rift.png`, overlays.json ui.global.title). Fill reskins.json `engineSymbol` and clear its
`_unverified` entries.

Acceptance: screenshots (the game window only) of the title screen, the faction select and the map, in safe and in finished
mode; switching SpoilerMode changes the names from the next game; `preflight --target plugin` lists no reskins row.

## M6: bots chasing the themed wins

Patch hook.bot-decision and map read.legal-actions, do.submit-action and do.native-ai-step, so each character's bot director
(bot_plans.json) picks among Root's legal actions and hands everything else to Root's own AI.

Acceptance: all-bot games (four characters): bots announce countdowns and complete quests by their plans (the log shows
their goal moves), never take an illegal action, and fall back to Root's AI when nothing goal-relevant is legal; five games
end without a `Root 33 core error`.

## M7: the creator plays

The creator plays three rounds as Sciel against Root's bots (Gustave, the Paintress, Esquie), in safe mode, and gives
feedback. Prepare: a fresh build installed in the dev copy, ProbeMode off, a two-line note on F8. Record their feedback in the
repo as plain notes (no screenshots of anything but the game window) and turn each point into a sheet change or a task.

## Release notes

- The Melty recipe's loader request must match what the PC found. Root under BepInEx 6 IL2CPP needs
  `{"kind":"loader","id":"bepinex-6-il2cpp"}`, not the `bepinex-5` used in the cloud's dry run (HANDOFF step 8). Re-run
  `inspect_package`, `validate_recipe` and `one_click_check` with it.
- Ship `Root33.Plugin.dll`, `Root33.Core.dll`, `Root33.Host.dll` in `BepInEx\plugins\Root33\`, with ProbeMode defaulting to
  off (`PluginSettings.cs`). Never ship UnityExplorer, the discover output or probe files.

## Not verified in the cloud (and why)

- Nothing ran inside Root: Root and its assemblies exist only on the creator's PC. The plugin was compiled against NuGet
  BepInEx 6 (be.725, the build nearest 6.0.0-pre.2) and Il2CppInterop 1.4.6; on the PC it builds against the dev copy's own
  BepInEx and interop assemblies, which may differ slightly.
- The `Unity/` files were compiled twice in scratch checks, not against Root: as the Mono variant against Unity 2020.3's real
  reference assemblies, and as IL2CPP against the real Il2CppInterop with stand-in Unity wrapper types. Root's interop
  assemblies may name or shape a member differently (for example `GetIl2CppType()`, `Scene.handle`).
- `Root33Runtime` was smoke-tested in the cloud with a real BepInEx config and log source over the unmapped adapter (waiting
  text, "next to map", core errors logged once per hook); the Harmony patches themselves need Root.
- The PowerShell scripts were parse-checked with PowerShell 7.4 and their D:\Mods guard tested; they have not run on Windows.

## Risks

- IMGUI: IL2CPP games can strip IMGUI. If `GUI.Box` throws or draws nothing, draw the panel with a uGUI Canvas and Text
  created at runtime (or TextMeshPro, which Root likely uses).
- Input: if Root uses only the new Input System, legacy `Input` throws; `KeyWatch` then reads IMGUI key events. If those are
  stripped too, read the keys through the Input System.
- IL2CPP patching: methods inlined by IL2CPP cannot be patched; generic methods need their concrete instantiation; a patch
  that runs on another thread must hand the core call to the main thread.
- The AI: Root's AI may decide inside one large method; hook.bot-decision may need a poll (`patchKind: poll`) rather than a prefix.
- Saves: hook.save text must be stored beside Root's save under the game's id, offline games only (Dire Wolf's servers never
  see Root 33 state).
- A Root update regenerates `BepInEx\interop`: rebuild, re-run discover, and re-check every mapped symbol.
