# Sheet conventions

The sheets are the source of truth for Root 33. Change a sheet before the code. `tools/preflight.py` checks every cell and every reference; `tools/gen_cs.py` turns rows into C#.

## File shape

```json
{ "rows": [ { "id": "...", "...": "...", "_unverified": { "column": "why it is not confirmed yet" } } ] }
```

- Every column in `_schema.json` for that sheet; no extra columns (keys starting with `_` are notes).
- Leave nothing as `TODO`. If a cell can only be confirmed later (most often: Root's own class or method names, found on the creator's PC), fill your best value or the literal `"unknown"` and name the column in `_unverified` with the reason.
- Do not mark a cell unverified to dodge work you can do now. Lore and rules facts that are uncertain must be reworded so they are true, as the roster did.

## Ids

| Sheet | Pattern | Example |
|---|---|---|
| systems | `sys.<name>` | `sys.win-conditions` |
| dlc | fixed | `base`, `riverfolk`, `underworld`, `marauder`, `exiles-partisans`, `hirelings-landmarks` |
| engines | fixed | `marquise`, `eyrie`, `alliance`, `vagabond-tinker`, `vagabond-ranger`, `vagabond-thief`, `riverfolk`, `lizards`, `duchy`, `corvids`, `hundreds`, `keepers` |
| characters | fixed | `gustave`, `esquie`, `paintress`, `sciel`, `verso`, `maelle`, `lune`, `monoco`, `curator`, `clea`, `white-haired-man` |
| reskins | `reskin.<character>.<thing>` | `reskin.gustave.keep` |
| win_conditions | `win.<character>` | `win.paintress` |
| rule_tweaks | `tweak.<character>.<name>` | `tweak.gustave.overcharge` |
| state | `state.<owner>.<name>` | `state.gustave.flags`, `state.global.act` |
| game_hooks | `hook.<name>` | `hook.battle-resolved` |
| board_reads | `read.<name>` | `read.ruler` |
| board_actions | `do.<name>` | `do.place-warriors` |
| bot_plans | `bot.<character>` | `bot.esquie` |
| bot_rules | `bot.<character>.<nn>-<name>` | `bot.esquie.01-target` |
| acts | fixed | `act1`, `act2`, `act3` |
| story_events | `story.<act>.<name>` | `story.act2.dorrie` |
| spawns | `spawn.<name>` | `spawn.florrie` |
| map_roles | `role.<name>` | `role.monolith` |
| map_autumn | `autumn.<nn>` | `autumn.01` .. `autumn.12` |
| overlays | `ui.<owner>.<name>` | `ui.global.title`, `ui.sciel.gauges` |
| barks | `bark.<character>.<nn>` | `bark.esquie.01` |
| bark_triggers | `trigger.<name>` | `trigger.turn-start` |
| options | `opt.<name>` | `opt.spoiler-mode` |
| assets | `asset.<name>` | `asset.title-rift` |
| tuning | `tune.<name>` | `tune.countdown-vp` |

## C# vocabulary

`csSignature` and `csHandler` cells and `state.csType` may only use these types (see `src/Root33.Core/Model.cs`) plus `int`, `bool`, `string`, `void`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `HashSet<T>`, `Dictionary<K,V>` and `T?`:

`ClearingId`, `SeatId`, `Suit`, `EngineId`, `PieceKind`, `ItemType`, `ItemState`, `Relationship`, `TurnPhase`, `SpoilerMode`, `Difficulty`, `RemovedPiece`, `BattleResult`, `RoleId`, and the string constants in `PieceTypes`.

A state type the core needs that is not listed (for example `RockState`) is declared by naming it in `state.csType` and describing its fields in `range`; the generator emits it as a class.

## Tests

Name tests `<Subject>_<Behaviour>`, e.g. `PaintressWin_AnnouncesWithThreeSuitsAndMonolith`. A row's tests prove that row. They are written during the build; the pre-build check (`--allow-missing-tests`) only needs them named.
