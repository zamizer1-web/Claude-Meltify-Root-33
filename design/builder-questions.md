# Builder questions: triage

The questions come from the core-rules build (workflow `wf_54303220-2f7`): the foundation builder and the six first-version character builders (Paintress, Sciel, Maelle, Gustave, Verso, Esquie). This pass is read-only: no sheet or code has been changed. The Esquie and Verso builders were being re-run while this was written, so any new questions from those re-runs are not included here.

**65 questions, merged into 31 items:**

| Category | Items | Meaning |
|---|---|---|
| C: creator decision | 2 | A real design choice for you |
| A: safe sheet fix | 8 | The roster or sheets already show the intent; only a sheet edit is needed |
| B: code only | 17 | No sheet change; how the code should handle it |
| D: needs Root's code | 4 | Can only be settled on your PC, by reading Root's own code |

Tag key: F = foundation, P = Paintress, S = Sciel, M = Maelle, G = Gustave, V = Verso, E = Esquie. For example, `S-Q4` is question 4 from the Sciel builder.

---

## C. Decisions for the creator

### C1. The "first update" rules: switch them on now, or later?

Your plan put four rules in the first update (v1.1), not in the first release:

- **Sciel's Twilight.** When she holds both a Sun and a Moon charge, her first fight of the evening hits harder.
- **The Paintress's Still Life.** When an enemy attacks one of her portraits, she pulls one warrior in from next door.
- **Esquie's rock powers.** Swimming rivers, ignoring the Hostile boot cost, and gliding.
- **The Monolith Barrier.** This one already has its own setting, which is off by default.

The builders have already written Twilight and Still Life, and right now both are always on.

- **(a)** Follow the plan: switch them off for the first release, and turn them on in the update once they have been tested in Root.
- **(b)** Turn them on in the first release.
- **(c)** Add an "Extra rules (from the update)" setting that players can turn on. It is off by default.

**Recommendation: (a).** It is what you approved, and it leaves fewer things to test on your PC before release. Esquie's rock powers also need Root's own code checked first (see D4). (Sources: S-Q4, P-Q3, E-Q5, F-Q5. The code side is simple: switch rules on or off by the `version` column of `rule_tweaks.json`.)

### C2. Still Life: does a human Paintress choose, or does the game choose for her?

Still Life lets her move one warrior into a portrait under attack before the dice are rolled. The attack happens on the other player's turn, and the mod has no way to stop and ask her. Right now the game does it automatically: it takes a warrior from the neighbouring clearing that has the most of her warriors.

- **(a)** Automatic, as it works now.
- **(b)** A switch on her board that she sets ahead of time ("Still Life: on/off"), like Gustave's Overcharge switch.
- **(c)** A pop-up that asks her every time. This is more work to build, and it pauses the other players' turns.

**Recommendation: (a).** Add (b) later only if playtesting shows that players want control. This only matters once the update rules are switched on (C1). (Source: P-Q3)

---

## A. Safe sheet fixes (the intent is already clear)

After every fix: run `python3 -I tools/preflight.py --target core`, then `python3 -I tools/gen_cs.py`, then `dotnet test src/Root33.sln`.

### A1. `story_events` gets an `owner` column (F-Q1, G-Q9)
`story_events._notes` already says "every event whose 'what' names a character fires only when that character is seated". However, three events with no spawn can only find their owner in the text.
- `_schema.json` → `sheets.story_events.columns.owner` = `{"type":"ref:characters","required":false,"neededBy":["core"],"description":"the character whose event this is; it fires (and is logged) only when that character is seated; null for table-wide events"}`.
- Values: `story.act1.landing` null · `story.act1.florrie` esquie · `story.act1.francois-decoy` esquie · `story.act2.beach` **gustave** · `story.act2.unlock-wins` null · `story.act2.dorrie` esquie · `story.act2.soarrie` esquie · `story.act2.parents-journal` lune · `story.act3.fall` null · `story.act3.alicia` **maelle** · `story.act3.cell-crack` curator · `story.act3.urrie` esquie · `story.act3.almighty` **monoco**.
- Code: `Root33Game` fires and logs an owned event only when its owner is seated, and sends it to that owner's `OnStoryEvent`. Gustave's module then handles `story.act2.beach`. In finished mode with Faithful Gustave on, it calls `do.set-seat-title` with "Expedition 33". Otherwise, the plugin shows the vignette from `StoryLog`.

### A2. `map_autumn` gets river links and opposite corners (F-Q2, first part)
These are verified facts (`design/autumn-map-verified.json`, translated to `autumn.NN`) that only the test double knows today.
- `_schema.json` → `map_autumn.columns.riverLinks` = `{"type":"refs:map_autumn","required":false,"neededBy":["core"]}` and `oppositeCorner` = `{"type":"ref:map_autumn","required":false,"neededBy":["core"]}`.
- `riverLinks`: autumn.01 → [06]; autumn.06 → [01, 07]; autumn.07 → [06, 10]; autumn.10 → [07, 11]; autumn.11 → [10]; every other clearing → [].
- `oppositeCorner`: autumn.01 ↔ autumn.03, autumn.02 ↔ autumn.04; every other clearing → null.
- Forests are **not** added yet, because Root's own forest ids are unknown (see D2).

### A3. State rows so a game keeps its Faithful Gustave and Barrier settings (F-Q4)
`opt.spoiler-mode` and `opt.difficulty` already stay fixed for each game. These two options should work the same way.
- `state.global.faithful-gustave`: csType `bool`. Initial: "false; at hook.game-start copied from opt.faithful-gustave when state.global.spoiler-mode is Finished, otherwise false". writtenBy `[hook.game-start, hook.load]`. onLoad: "Restored from the save, so a game keeps the setting it started with; false if the save has none."
- `state.global.monolith-barrier`: csType `bool`. Initial: "false; copied from opt.monolith-barrier at hook.game-start". writtenBy and onLoad as above.
- Code: `GameContext.Options` reads these two rows, not the current settings.

### A4. Monolith neighbours that are corners never get the Axon names (F-Q6)
Every other role rule already keeps single-clearing roles apart (`MapRoles_SingleClearingRolesNeverShareAClearing`). This can only happen on non-Autumn maps.
- `map_roles.json` → `role.visages.assignRule`: after "taken in order of Root clearing number", add ", skipping corners (a corner already holds Lumière, Esquie's Nest or the Flying Manor)". After "A fourth or later neighbour", add "or a skipped corner".
- `role.sirene` and `role.the-reacher` already say "see Visages", so they inherit the change.
- Add test `MapRoles_AxonsSkipCornerNeighbours` to `role.visages.tests`.

### A5. Tuning row for Root's own 30 VP win (M-Q0)
- `tuning.json` → new row `tune.root-win-vp`: value `30`, description "Root's own VP win (a Root rule, not a mod setting): bots use it to judge 'within N VP of winning'.", usedBy `["bot.maelle"]`.
- Code: `MaelleBot.RootWinVp` becomes `Tuning.RootWinVp`.

### A6. Soarrie hand-off has one owner: Esquie (V-Q0, V-Q1)
`spawn.soarrie` (owner esquie) already lists `do.score-vp`, `do.show-overlay` and `do.hide-overlay`. It is the only row that also covers the Monolith-ruler case when Verso is not seated, and Esquie's module always exists whenever Soarrie matters.
- `rule_tweaks.json` → `tweak.verso.soarrie-handoff`: add `_notes`: "Carried out by Esquie's module as part of spawn.soarrie (state.esquie.rocks; HookOrder.Esquie runs at hook.evening-end before the win check, so the rock counts that turn). Verso's module does nothing for this row; its tests register both modules. The overlay redrawn or hidden is ui.esquie.rocks (Soarrie leaves Verso's card)."
- Code: remove the hand-off from `VersoModule` and keep the one in `EsquieTweaks`.

### A7. A guard is lost however its warrior dies (E-Q0)
`spawn.florrie._notes` already says that removals outside a battle (Strike, Bomb, Revolt) lower GuardsLeft. Without this, a rock can get stuck: if Esquie Strikes both guards, nothing is left to battle, the counter never reaches 0, and the rock is never loose.
- `spawns.json` → `spawn.florrie.claimRule` and `spawn.urrie.claimRule`: replace "for every host warrior removed in that clearing in any battle(, whoever fought it)" with "for every host warrior removed from that clearing, in a battle (whoever fought it) or any other way (Strike, Bomb, Revolt)".
- Code: count only at `hook.piece-removed`, once per removed host warrior in a guarded-rock clearing, and not again at `hook.battle-resolved`. `win.esquie` already lists `hook.piece-removed`.

### A8. Paintress "Repaint" counts any lost easel outside the Monolith (P-Q2)
`win.paintress` says Portraits are never stored. In a battle her warriors are removed before the easel, so at the moment of removal nothing can tell whether it was a Living Portrait. The bot's purpose (don't rebuild where an easel just fell) is the same either way.
- `bot_rules.json` → `bot.paintress.05-repaint.rule`: "A destroyed Portrait is rebuilt elsewhere" → "A destroyed easel outside the Monolith (any of her roosts removed there) is rebuilt elsewhere".
- `state.json` → `state.paintress.repaint-avoid.range`: "clearings where a Portrait was destroyed" → "clearings where one of her roosts outside the Monolith was destroyed".
- The code already behaves this way.

---

## B. Code only (no sheet change)

| # | Item (sources) | Answer |
|---|---|---|
| B1 | Generator emits C# keywords as parameter names (F-Q0) | Fix `tools/gen_cs.py` to write `@params`, `@public`, `@default` (any C# keyword gets an `@` prefix), then delete the obj/ copy step from `Root33.Core.csproj`. The hand-written `RockState`/`FootState` fields are the intended pattern. |
| B2 | One shared Goal Tracker API for all 11 spoiler layers (P-Q0, S-Q0) | Yes. Make `IGoalProgress` (UnitsMissing, KeyClearings) the shared API, implemented by every win and read through `GameContext.WinOf` / `BotView`. Gustave's Beachhead clearing comes from his building next to the Monolith, Esquie's key clearings include the guarded-rock clearings, and Lune's are the clearings around the tagged relic's forest. Delete `ScielBot.FallbackKeyClearings` once every win implements it. |
| B3 | Re-apply the gates at confirm for every countdown win (F-Q3) | Yes, as intended: `win.gustave.confirmTest` says so explicitly, and the others say "the same test". For Verso it changes nothing (a Vagabond's VP cannot fall). Optional wording: `win.verso.confirmTest` "(gates act >= 2, Vp >= tune.countdown-vp and no coalition re-checked)". |
| B4 | Who runs the Monolith Barrier when the White-Haired Man is absent (F-Q5) | A shared framework system in `Root33Game`, because it is a table option and not a character power. It uses `state.white-haired-man.barrier-down` (row exists) and the A3 option row. It is v1.1, so it stays off in the first release unless C1 says otherwise. |
| B5 | Campaign rows: profile copy and save copy (F-Q7) | Keep both as built. The profile carries the campaign forward; the save keeps that game's own number and journal. |
| B6 | When overlays are redrawn (P-Q9, M-Q7, V-Q2, S-Q10, F note) | One shared rule in the framework: after `hook.turn-end`, `hook.battle-resolved`, `hook.aid-resolved`, `hook.act-change` and `hook.load`, re-show (do.show-overlay) every v1 overlay owned by a seated character and the global tracker; at `hook.game-over`, show only the declared win's `ui.<character>.ending`. The plugin draws the content from the board and the overlay's `state` list. Verso's rank-up and hit stingers are part of `ui.verso.rank` (they play when the letter changes). |
| B7 | Speech bubbles (barks) and name swaps are not wired (F note, M-Q6, S-Q10, E-Q3) | Build one shared bark system: trigger → that character's barks filtered by tier and spoiler mode → `do.show-bubble`. Keep cooldowns in memory with no state row (they are cosmetic, and a load resetting them is harmless). Modules raise a trigger at the moment it happens (for example, Maelle's stance change), so the `bark_triggers.hook` column is only descriptive. The decoy "pointing to Stone Wave Cliffs" is drawn by `ui.esquie.francois-decoy`, not by a bubble parameter. Name swaps (reskins, Faithful retitle) belong in the same pass. |
| B8 | Fallback when the plugin cannot add extra hits (S-Q9, G-Q8) | Correct as built: the core always calls `AddHits`, and the plugin's `AddHits` does the one-hit-removal fallback itself. |
| B9 | No shared seeded random (S-Q6) | Add one seeded random to `GameContext`, seeded from round + seat + a fixed salt, so Easy coin flips repeat after a load. No state row is needed. |
| B10 | Bot Maelle could still fight through Root's own AI (M-Q4) | Add the `CanStartBattle` veto in Maelle's module, for a **bot** Maelle seat only: allow battles against Hostile factions, or against a faction at `tune.root-win-vp - tune.maelle-threat-vp` VP or more (A5). Record this in `bot.maelle.03-no-fights._notes`. |
| B11 | Paintress bot readings (P-Q4 to P-Q8) | All five readings are fine as built: a one-turn lookahead against the current board, Easy's extra card, Easy's early bark, which battles count, and the spoiler cost. |
| B12 | Sciel bot readings (S-Q3, S-Q4, S-Q5, S-Q7, S-Q8) | Fine as built, with one correction for S-Q3: a Spread does not always cost 1 supporter, because the cost rises along her sympathy track. Read the cost from the track position (Root's printed numbers, confirm in D3). Twilight resetting last, so the boosted battle gives no new Moon charge, matches "then both reset". The evict wait, idle warrior and announce-turn readings are fine. |
| B13 | Maelle bot readings (M-Q1, M-Q2, M-Q3, M-Q5) | Fine as built: no win with zero non-Vagabond factions, "passes 22" means 23 or more with a full re-rank, Aid only where she stands in goal mode, and the 2-player walk waits for 15 VP. |
| B14 | Gustave bot readings (G-Q1 to G-Q6) | Fine as built. G-Q1: the route's planned costs are not saved, which matches `state.gustave.route.onLoad` (recomputed at next Birdsong). G-Q2: "his building on an unflagged Route clearing = built this turn" is good enough for Easy. G-Q3: arming at battle-start is fine if it goes through the same path as `hook.ui-toggle`. G-Q4 to G-Q6: fine. |
| B15 | Verso's lone-warrior fight vs the 0.5 limit (V-Q3) | The sheet already answers it: `bot.verso.01-climb` allows a lone defending warrior by name, matching the roster's "meaning a lone defending warrior". Build it that way. |
| B16 | Esquie edge cases (E-Q1, E-Q2, E-Q4) | E-Q1: cannot happen on a real 12-clearing map (map_roles has fallbacks for every role); on a hand-made test map leave the rock notSpawned and add a `Ctx.Warnings` line. E-Q2: 0 guards means loose at once, as `claimRule` says. E-Q4: write the GuardRules tests against `EsquieTweaks.HostedGuards` and the Paintress now; add Clea and the White-Haired Man when they are built. Make `PaintressPortraits.HostedGuards` call the shared helper. |
| B17 | Process notes (P-Q10, P-Q11, M-Q9, M-Q10, G-Q10, G-Q11, V-Q4, F note) | No action. `Characters/<Name>Tests.cs` from CORE-ARCHITECTURE.md is the right place. The read-only `git status` calls changed nothing. |

---

## D. Needs Root's code (creator's PC)

### D1. Root's action keys, shared by all 11 bots (P-Q1, S-Q1, M-Q8, G-Q0; the Verso and Esquie bots will need it too)
- **Look for:** the legal-action generator that Root's UI uses to highlight clickable clearings, cards and items, and each faction AI's decision entry point. These are the `engineSymbol` cells of `read.legal-actions`, `do.submit-action` and `hook.bot-decision`. Write down what one action object holds (type or verb, clearing, target seat, card, count) and whether it has a stable string form.
- **Meanwhile (code only):** put one shared key grammar in `Engine/ActionKey.cs` and make every bot use it. The builders already mostly agree: `move:from:to[:n]`, `battle:clearing:defenderSeat`, `build:clearing[:type]`, `recruit:clearing`, plus faction verbs. Settle the small differences: Paintress `decree:<column>:<suit>`, Maelle `fN` forests, Gustave `extra-action:bird`. The plugin then needs only one translation table.

### D2. Root's forest ids (F-Q2, second part)
- **Look for:** the board's forest objects in Root's map data: their ids and which clearings each one touches. These fill `read.forests-around` and a forest table (a new sheet, or a column next to A2's). Until then, FakeBoard's forests numbered 1 to 7 are test-only, and no rule may hard-code them.

### D3. Root's printed numbers that the code keeps as constants (G-Q7, S-Q2, S-Q3)
- **Look for:** the Marquise building-cost tracks (the code assumes 0, 1, 2, 3, 3, 4), the Woodland Alliance sympathy (Spread) cost track, and the Martial Law threshold (the code assumes 3 warriors). If digital Root matches, nothing changes. These are Root's rules, not tuning knobs, so they need no tuning rows. Martial Law is already excluded by `tune.sciel-spread-max-enemy` (2).

### D4. Esquie's rock powers (E-Q5; v1.1, see C1)
- **Look for:** Root's Riverfolk "rivers as paths" rule, the Hostile-faction boot cost, and the Harrier's Glide, and check whether each can be granted to another Vagabond seat (`do.grant-ability`). The once-per-turn limit on Glide is handled inside the plugin's Glide logic; the core only grants the ability.
