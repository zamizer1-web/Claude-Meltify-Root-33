# Root 33: handoff to the session on the creator's PC

Root 33 is a mod for **Root** (Dire Wolf Digital, Unity, Steam app 965580) that makes **Clair Obscur: Expedition 33** (Sandfall, UE5, Steam app 1903340) characters playable. Each character rides on a Root faction or Vagabond and adds its own themed way to win; bots play the characters and chase those wins. Single player against bots is the main mode. The map is Root's Autumn map renamed as the Continent. The creator's plan approval and the full design are in `design/roster-draft.md`.

The creator's standing preferences: verification over speed (test on your side, then have them check on theirs), and keep projects and big files on the **D: drive**. They asked to be interrupted only when something truly needs them.

## What is already done (in the cloud session)

- `design/roster-draft.md|json`: the agreed roster (11 characters, 6 in the first version), story framing, spoiler tiers, tuning, open spoiler decisions. `design/research-facts.json`: research with uncertainties.
- `sheets/`: the design sheets, the source of truth. Read `sheets/CONVENTIONS.md`. Change a sheet before the code.
- `tools/preflight.py`: lays all sheets over each other and lists every empty, unconfirmed or broken cell. `--target core|plugin|release`, `--allow-missing-tests` before a build, `--results <trx>` after tests.
- `tools/gen_cs.py`: turns sheet rows into C# (`src/Root33.Core/Generated/`). Refuses to run when the preflight fails.
- `src/Root33.Core`: engine-independent rules (netstandard2.0) with unit tests in `src/Root33.Core.Tests`.
- `art/title-rift.png`: the original "33 tearing out of a rift" title art (credits in `art/README.md`).
- `tools/pc/find-games.ps1`: read-only scan that finds both games and reports Unity version and Mono vs IL2CPP.

Every cell still marked `_unverified` for target `plugin` needs Root's or Clair Obscur's own files: run `python3 -I tools/preflight.py --target plugin` to list them. That list is your work order.

## Your job, in order

1. **Scan.** `powershell -ExecutionPolicy Bypass -File tools\pc\find-games.ps1` writes `pc-report.json` (ignored by git; it holds local paths). Tell the creator in two lines what you found. If either game is missing, ask them for its folder.
2. **Work on a copy, never the real install.** Copy Root's folder to `D:\Mods\Root33-dev\Root` (add `steam_appid.txt` containing `965580` next to the exe so Steam starts it from there). The creator's real Root and their Dire Wolf online account stay untouched. Copy nothing into the Clair Obscur folder; read it only.
3. **Loader.** Mono build → BepInEx 5.4.23.x (Melty installs 5.4.23.5). IL2CPP → BepInEx 6.0.0-pre.2 IL2CPP or MelonLoader 0.7.x (Melty installs both). Get them from their official GitHub releases. Add UnityExplorer for live inspection while developing (dev copy only, never in the release).
4. **Read Root's code** (ILSpy `ilspycmd` for Mono; Cpp2IL/Il2CppDumper for IL2CPP). Find, and write into the sheets' plugin columns, removing each `_unverified` entry as you confirm it:
   - the board, clearings (ids, suits, slots, corners, ruins, paths, rivers) → `map_autumn.json` and `board_reads.json`;
   - seats, factions, pieces, rule, VP, items, relationships, turn phases → `board_reads.json`, `engines.json`;
   - battle resolution, end of Evening, start of Birdsong, explore, aid and the other moments → `game_hooks.json` (method to patch and prefix/postfix);
   - how the AI picks actions → where the bot plans plug in (`board_actions.json`, `bot_plans.json` notes);
   - placing another seat's warriors, tunnels, tags → `board_actions.json` (each has a fallback if Root won't allow it);
   - the title screen logo and localisation keys for renamed pieces → `overlays.json`, `reskins.json`.
5. **Clair Obscur assets.** Find the character portraits and music cues in its Paks (FModel/CUE4Parse/retoc). Write their paths into `assets.json`. Check every library's license before bundling it; credit authors in the release.
6. **Build the plugin** (`src/Root33.Plugin`, a new project) that implements `IBoardView`, `IBoardActions` and `IGameHooks` for Root with Harmony patches and hands everything else to `Root33.Core`. Preflight with `--target plugin` must be clean first.
7. **Test in the game**: start single-player games against bots with each first-version character; prove each win condition, each bot plan and the title screen work; take real screenshots (only the game window, no private windows). Then ask the creator to check on their side.
8. **Melty.** Ask the creator to paste a fresh Publish prompt from Melty Studio (the key is never stored in files, commits, logs or screenshots). Root and Clair Obscur are not in Melty's catalog, so the release uses the tested own-copy route:

   ```json
   {"schemaVersion":1,"mode":"standalone",
    "games":[{"slug":"custom-root","role":"primary","ownCopy":true},{"slug":"custom-clair-obscur-expedition-33","role":"secondary"}],
    "ownCopies":[{"game":"custom-root","disc":{"files":["<every file Root needs, from the scan>"]},"names":["Root"],"to":"{managed}/own/root"}],
    "launch":{"kind":"exe","path":"{managed}/Root33Launcher.exe","args":["--root","{managed}/own/root"]}}
   ```

   In the cloud, `one_click_check` said this shape is one click (with the loader requested as `{"kind":"loader","id":"bepinex-5"}` or bundled), and `validate_recipe` flagged it for a human review. Re-run `inspect_package`, `validate_recipe` and `one_click_check` on the real files. Prove on the PC that Melty finds the Steam copy of Root and that the copy starts. If Melty's review refuses the route, tell the creator, keep the listing a draft, and suggest asking Melty (Discord: https://discord.com/invite/meltygg) to add Root to its catalog.

## Things the creator decided

- Single player against bots first; pass-and-play and online come later (online would touch Dire Wolf's servers).
- Speech bubbles only; no imitation of any voice actor. Generated voices would cost money: ask first.
- Spoiler choice on first launch; "Mom plays too many board games" lines only in finished mode, Act III.
- Map: Autumn renamed as the Continent now; a brand-new Continent layout later.
- Title: ROOT 33, with the 33 coming out of a rift.
