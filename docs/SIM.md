# Root 33 playtest tabletop (src/Root33.Sim)

A text tabletop of Root's base game that four AI playtesters play through a command line, running on top of
the real Root 33 rules in `src/Root33.Core`. Root itself cannot run in the cloud; this stands in for it.

## Status

**Skeleton only.** The first attempt stopped early for usage limits. What exists:

- `src/Root33.Sim` (net8.0 console, references Root33.Core) and `src/Root33.Sim.Tests` (xunit), both in `src/Root33.sln`.
- `Program.cs`: CLI parsing; `new` validates the table (base-game characters only: gustave, paintress, sciel, and up to
  two Vagabonds from esquie|verso|maelle; 2-4 seats; seats `human` or `bot`) and writes a placeholder save file.
- `select`, `view`, `actions`, `do`, `log` print "not implemented yet" and exit with code 2.
- `SimMap.cs`: the Autumn map built from the core's own map rows (`AutumnMap.Rows`: Continent names, suits, slots,
  paths, river links, ruins, corners, opposite corners) plus the seven forests numbered as in
  `Root33.Core.Tests/Support/FakeBoard.cs`.
- `CliSkeletonTests`, `SimMapTests`: seat validation, save file creation, map symmetry (12 clearings, 4 corners).

Hint for the next attempt: `Root33.Core.Tests/Support/FakeBoard.cs` already implements `IBoardView` and `IBoardActions`
over in-memory dictionaries (ruler ties, relics, pawns, forests); the sim's board adapter can follow it member by member.

## Left to do

1. Read `docs/CORE-ARCHITECTURE.md`, `Generated/IBoardView.g.cs`, `IBoardActions.g.cs`, `IGameHooks.g.cs`, `Engine/`,
   and `Root33.Core.Tests/Support/FakeBoard.cs` (forests derivation for the Autumn map).
2. Sim state (JSON-serialisable): map from `sheets/map_autumn.json`, 54-card base deck, item supply, ruins, quests,
   faction boards (Marquise, Eyrie, Woodland Alliance, Vagabond Thief/Tinker/Ranger), VP, dominance, coalitions.
3. Rules engine with a decision stack; battles (two 0-3 dice, attacker high / defender low, capped by warriors,
   defenseless extra hit, ambush), rule, movement, crafting, Root 33 forced homes applied by the core.
4. `IBoardView` / `IBoardActions` over sim state; call every `IGameHooks` handler at its moment in the turn flow.
5. Bot seats via the core's bot directors, falling back to a simple legal-move policy.
6. CLI commands `select`, `view`, `actions`, `do`, `log`, deterministic with `--seed`.
7. Tests: battle maths, rule, movement, crafting, dominance, each faction's turn; core win conditions/acts/spawns/barks
   firing through the sim; 100 seeded random-legal games finishing or reaching a round cap without exceptions.
8. Play two full games through the CLI (safe and finished spoiler modes) and fix what turns up.

## Rule simplifications

None recorded yet (no rules implemented). Every simplification must be listed here when the engine is written.
Planned default for decisions owed by non-active seats during a turn (hit allocation, ambush): resolved
automatically by a documented sensible default and shown in the log.
