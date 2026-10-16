# Root 33 playtest tabletop (src/Root33.Sim)

A text tabletop of Root's base game that four AI playtesters play through a command line, running on top of
the real Root 33 rules in `src/Root33.Core`. Root itself cannot run in the cloud; this stands in for it.

## Status

| Part | State | Owner |
|---|---|---|
| Engine: state, save/load, RNG, deck, items, ruins, VP, decision stack, turn machine | done, tested | lead |
| Shared rules: move, rule, battle, pieces, craft, dominance, card effects, 30 VP | done, tested | lead |
| Marquise de Cat | done, tested | lead |
| CLI (`Program.cs`), `tools/sim.sh`, bot policy | done, tested | lead |
| Eyrie Dynasties, Woodland Alliance (`Factions/Eyrie.cs`, `Factions/Alliance.cs`) | done, tested (`EyrieTests.cs`, `AllianceTests.cs`) | engineer B |
| Vagabond Tinker/Ranger/Thief (`Factions/Vagabond*.cs`) | done, tested (`VagabondTests.cs`) | engineer C |
| Root 33 core link (`Root33/CoreBridge.cs`, `Root33/SimBoard.cs`, `Root33/ModText.cs`) | done, tested (`CoreBridgeTests.cs`) | engineer D |

Tests: `src/Root33.Sim.Tests` (deck, battle maths, move and rule, crafting, dominance, the Marquise turn, save/load
round trip, CLI, and 100 seeded all-bot games that must finish or reach round 30 without an exception or a stuck
decision).

## Playing

```
tools/sim.sh select --spoiler safe
tools/sim.sh new --save game.json --seed 7 --spoiler safe --seats "gustave:human,paintress:human,sciel:human,esquie:human"
tools/sim.sh status  --save game.json
tools/sim.sh view    --save game.json --seat 1
tools/sim.sh actions --save game.json --seat 1
tools/sim.sh do      --save game.json --seat 1 3          # option number, or its key: do ... --seat 1 build:sawmill:5
tools/sim.sh log     --save game.json --since 120 [--seat 1]
tools/sim.sh rules   --save game.json --seat 1
```

- Seats are numbered 1-4 in the order of `--seats`, which is the clockwise seating. The seed picks the first player.
  Characters: gustave=Marquise, paintress=Eyrie, sciel=Woodland Alliance, esquie=Vagabond Tinker,
  verso=Vagabond Ranger, maelle=Vagabond Thief (`sheets/characters.json` engine column). 2-4 seats, at most two
  Vagabonds. `human` seats are the playtesters; `bot` seats play a seeded random legal policy at once.
- `status`, `view` and `do` end with one line: `STATUS: round R · seat N (<character>) to act · <decision>` or
  `GAME OVER: <who won and how>`. Only the deciding seat may `do`. `do` prints what happened (dice, hits, VP, bot turns)
  and the next options when the same seat still decides.
- `view` shows only what that seat may see: every clearing (name, suit, slots, pieces, ruler), every faction's VP,
  hand size, items, play area and board; its own hand; then `CoreBridge.ModView(seat)`; then its options when it decides.
- `log` without `--seat` shows only public entries; private ones (cards you drew, a hand seen with Codebreakers) need
  `--seat N`.
- `tools/sim.sh` runs the Release dll directly and rebuilds it (under the shared flock) only when it is missing or
  older than a `.cs`, `.csproj` or sheet. The lock path can be changed with `ROOT33_DOTNET_LOCK`.
- Concurrent commands on one save are serialised by `<save>.lock`; saves are written atomically.

## Developer map

Everything is in namespace `Root33.Sim`. Seats are 0-based ints inside the code (`seat 1` in text is seat 0);
clearings are 1-12 (`sheets/map_autumn.json`); forests 1-7 (`SimMap.ForestDefs`).

### Files and owners

| File | Owner | What |
|---|---|---|
| `Engine/SimState.cs` | lead | the whole game as JSON data (`SimState`, `SeatState`, `ClearingState`, `BattleState`, `ModEffects`, `SimJson.Options`) |
| `Engine/Cards.cs` | lead | the 54-card base deck (`Cards.All`, `CardDef`, `Cards.Describe`, `Cards.Matches`) |
| `Engine/Decision.cs` | lead | `Decision`, `Option`, `Frame`, `IStep`, `StepRegistry` |
| `Engine/SimGame.cs` | lead | creation, save/load, `Data<T>`, RNG, log, decision loop, turn machine, VP, cards, mod effects |
| `Engine/Pieces.cs` | lead | rule, place, remove (VP for enemy buildings/tokens), move warriors, notifications |
| `Engine/Rules.cs` | lead | `Moves`, `Battles`, `Crafting`, card-effect steps, open-point options, dominance |
| `Engine/Render.cs` | lead | `Render.View`, `Render.StatusLine`, `Render.Options`, `Bots` |
| `Factions/IFaction.cs` | lead | `IFaction`, `FactionBase` (Root's general rules as defaults), `FactionRegistry` |
| `Factions/Marquise.cs` | lead | the Marquise de Cat |
| `Factions/Eyrie.cs`, `Factions/Alliance.cs` | **B** | Eyrie Dynasties, Woodland Alliance |
| `Factions/Vagabond.cs` | **C** | Vagabond (Tinker, Ranger, Thief) |
| `Root33/CoreBridge.cs`, `Root33/SimBoard.cs`, `Root33/ModText.cs` | **D** | the link to `Root33.Core` |
| `Program.cs`, `tools/sim.sh`, `src/Directory.Build.props` | lead | CLI, launcher, build settings |
| `src/Root33.Sim.Tests/*` | each owner adds its own test file (`EyrieTests.cs`, `AllianceTests.cs`, `VagabondTests.cs`, `CoreBridgeTests.cs`); `SimTestKit.cs` (lead) has table helpers |

If you need a change in a file you do not own, ask its owner; never edit it.

### The decision model (how to write a faction)

- The game is a stack of `Frame`s (`State.Stack`). The top frame runs: its `IStep.Next(g, f)` either changes the
  state (pops itself with `g.Pop(f)`, pushes frames with `g.Push`/`g.PushSequence`, advances `f.Step`) and returns
  null, or returns a `Decision` **without changing anything** (no dice, no pieces). `IStep.Choose(g, f, option)`
  applies the option picked. Keep arguments in `f.I` / `f.S` / `f.L` (they are saved).
- Exactly one decision is pending at a time (`State.Pending`), owned by one seat, with options whose keys are stable
  (`build:sawmill:5`, `battle:5:1`) and whose text is plain English. A decision with one option (not `Open`) is applied
  at once.
- **A decision owed by a seat that is not the active seat is never shown**: the engine applies `Decision.DefaultKey`
  (or the first option) and logs `seat N (not their turn, decided by the default): ...`. Give every decision a seat
  other than the active one may own (ambush, hit losses, Field Hospitals, Outrage payments...) a sensible
  `DefaultKey` and list the rule under "Rule simplifications".
- Set `Decision.Open = true` on decisions between actions in your Daylight (and Birdsong if you have one). The engine
  then adds the card options usable "in Daylight" (activate/take dominance, Tax Collector, Codebreakers) and your
  `IFaction.OpenOptions` (keys `open:...`, handled by `ChooseOpen`). Their keys never reach your frame.
- After each of your actions resolves, call `g.ActionResolved(seat)` (hook.action-resolved). When an action pushes
  sub-frames (battle, moves), mark it pending on your frame and call it when your frame runs again (see
  `Marquise.DaylightNext`).
- Register every frame kind in `RegisterSteps` with your faction prefix (`eyrie.decree`, `vagabond.slip`).
- The turn machine (`SimGame.TurnMachine`) runs when the stack is empty: Birdsong start (round start on the first
  player, `OnBirdsongStart`, dominance check, Better Burrow Bank) -> your `BeginPhase(Birdsong)` -> Royal Claim / Stand
  and Deliver -> Command Warren -> `BeginPhase(Daylight)` -> `OnEveningStart`, Cobbler -> `BeginPhase(Evening)` ->
  `OnEveningEnd`, `OnTurnEnd` -> next seat. Setup runs first, seat by seat in setup order (A Marquise, B Eyrie,
  C Vagabonds, D Alliance), each calling `IFaction.Setup`. Your Evening must draw and discard to 5 itself (push
  `new Frame("discard", seat).Set("limit", 5)`).
- Faction-specific saved state: a class of yours with a public parameterless constructor, read with
  `g.Data<MyState>(seat)` (created on first use, serialised into `SeatState.FactionData` on save).
- Shared helpers to use rather than touching `State` by hand: `g.PlaceWarriors/PlaceBuilding/PlaceToken`,
  `g.RemoveWarriors/RemoveBuilding/RemoveToken` (score 1 VP for an enemy's building or token; pass the remover),
  `g.MoveWarriors` (after `Moves.CanMove`), `g.NotifyMoved` (pawns), `g.AddVp`, `g.Draw`, `g.DiscardFromHand`,
  `g.ToDiscard`, `g.Ruler/Rules/RuledBy`, `g.CanPlace`, `g.FreeSlots`, `g.HomeChoices`, `Moves.Frame`,
  `Battles.Targets/Start`, `Battles.HitsFrame`, `Crafting.Frame/Craftable/Craft`, `g.Random` (game RNG only),
  `g.Log(text, only: seat)`.

### IFaction hooks (defaults in FactionBase are Root's general rules)

| Hook | Default | Who overrides |
|---|---|---|
| `WinsRuleTies` | false | Eyrie (Lords of the Forest; already true in the stub) |
| `IgnoresRuleToMove` | false | Vagabond (already true in the stub) |
| `BlocksPlacement` | false | Marquise (the Keep) |
| `CraftingPieces` / `SpendCraftingPieces` | none / mark keys used this turn | Marquise workshops, Eyrie roosts, Alliance sympathy, Vagabond hammers (exhaust) |
| `GainCraftedItem` | `SeatState.CraftedItems` | Vagabond (onto its card, `OnItemChanged`) |
| `CraftVp` | card VP | Eyrie (Disdain for Trade: 1) |
| `CanAttackFrom`, `PresentIn`, `MaxRolledHits`, `IsDefenseless`, `AttackingPieces` | warriors-based | Vagabond (pawn, swords) |
| `TakeHitsKind` | null (shared `hits` frame) | Vagabond (damage items) |
| `DefenderTakesHigherRoll` | false | Alliance (Guerrilla War) |
| `LossPriority` | 5 | each faction (order a non-active seat loses buildings/tokens) |
| `UsesDominanceNormally`, `OpenOptions`, `ChooseOpen` | true / none | Vagabond (coalitions) |
| `OnPiecePlaced`, `OnPieceRemoved`, `OnWarriorsRemoved`, `OnPiecesMoved`, `OnBattleResolved` | nothing | Marquise Field Hospitals; Alliance Outrage and base removal; Vagabond Hostile; Eyrie roost loss |
| `PieceName`, `BoardText`, `RulesText` | type name / stub text | each faction |
| `Items`, `RelationshipWith`, `EyrieLeader`, `DecreeColumn`, `Supporters`, `Officers` | empty | C, B (read by D's SimBoard) |

### Core-bridge seams (engineer D)

`CoreBridge` has one method per `sheets/game_hooks.json` row, named after the row (`hook.battle-resolved` ->
`OnBattleResolved`), with tabletop arguments (0-based seats, clearings 1-12). The engine calls them at these moments:

| Moment (lead's code) | CoreBridge call |
|---|---|
| `SimGame.New`: after hands are drawn | `OnSeatAssigned(seat)` for each seat, `OnGameStart()`, `OnMapLoaded()` |
| each seat's setup (turn machine) | `OnSetupHome(seat)`, then the faction asks `ForcedHome(seat)` through `g.HomeChoices` |
| all seats set up | `OnSetupComplete()` |
| first player's Birdsong | `OnRoundStart(round)` |
| every Birdsong start | `OnBirdsongStart(seat)`, then the dominance win check |
| Evening start / end, turn end | `OnEveningStart`, `OnEveningEnd`, `OnTurnEnd` |
| a faction calls `g.ActionResolved(seat)` | `OnActionResolved(seat)` |
| `g.MoveWarriors`, `g.NotifyMoved` | `OnPieceMoved(owner, kind, from, to, count)` |
| every piece placed / removed | `OnPiecePlaced(owner, kind, type, c)` / `OnPieceRemoved(remover, owner, kind, type, c)` (once per piece) |
| `Battles.Targets` | `OnBattleLegal(attacker, defender, c)` (false hides the battle) |
| battle declared, before ambushes | `OnBattleStart(attacker, defender, c)`; extra hits go in `State.Mod.ExtraHits/ExtraMaxHits/IgnoreFirstHit` (cleared after the battle) |
| battle over | `OnBattleResolved(BattleState)` (`Removed` lists every piece) |
| `g.AddVp` | `OnVpChanged(seat, before, after)` |
| game over | `OnGameOver(winners)` |
| `SimGame.ToJson` / `FromJson` | `OnSave()` -> `State.Mod.CoreState`; `OnLoad(State.Mod.CoreState)` |
| bot seat decides | `OnBotDecision(seat, decision)` (null: random legal policy) |
| every engine step | `IsGameOver(out text)`: true ends the game with `State.Mod.DeclaredWinners` |
| `view` / `rules` / `select` | `ModView(seat)`, `CharacterCard(seat)`, static `SelectCards(spoiler)`; `ModText.SeatTitle(g, seat)` names seats |

Called from faction files: `OnExploreResolved`, `OnAidResolved`, `OnHostilityChange` (returns false to skip),
`OnRelationshipChanged`, `OnItemChanged` (C); `OnTurmoil` (B). Not called (no such piece in the base game, or raised
by the core itself): `OnTitleScreen`, `OnCharacterSelect`, `OnActChange`, `OnWarlordRemoved`, `OnRelicRecovered`,
`OnRelicMoved`, `OnConvert`, `OnUiToggle`.

`State.Mod` (`ModEffects`) is where `SimBoard`'s IBoardActions write. The shared rules honour: `ExtraHits`,
`ExtraMaxHits`, `IgnoreFirstHit` (battle), `GuardFloors` and `Abilities["rivers-as-paths"]` (moves),
`BlockedPlacements` (`g.CanPlace`), `DeclaredWinners` (with `IsGameOver`). `HomeRestrictions`,
`StartForestRestrictions` are for D's `ForcedHome`; `Banners`, `SeatTitles`, `Overlays`, `Extra` are display/bookkeeping.
Other abilities from do.grant-ability (`no-hostile-boot`, `glide`) are for C's Vagabond moves.

## Cards

The Root Database's base deck page shows the cards only as images, so the list follows the printed base deck:
fox 14 (Ambush, Dominance, Gently Used Knapsack, Root Tea, Travel Gear, Protection Racket, Foxfolk Steel, Anvil,
Stand and Deliver! x2, Tax Collector x3, Favor of the Foxes), rabbit 13 (Ambush, Dominance, Smuggler's Trail, Root Tea,
A Visit to Friends, Bake Sale, Command Warren x2, Better Burrow Bank x2, Cobbler x2, Favor of the Rabbits), mouse 13
(Ambush, Dominance, Mouse-in-a-Sack, Root Tea, Travel Gear, Investments, Sword, Crossbow, Scouting Party x2,
Codebreakers x2, Favor of the Mice), bird 14 (Ambush x2, Dominance, Birdy Bindle, Woodland Runners, Arms Trader,
Crossbow, Armorers x2, Brutal Tactics x2, Royal Claim, Sappers x2). The Law confirms 54 cards, five ambushes (one
mouse, rabbit, fox and two birds) and four dominance cards. Costs: bags 1 mouse (1 VP), boots 1 rabbit (1 VP), tea 1
mouse (2 VP), coins 2 rabbit (3 VP), swords 2 fox (2 VP), crossbows 1 fox (1 VP), hammer 1 fox (2 VP); Royal Claim 4 of
any suit. A veteran should check this list against a physical deck.

## Rule simplifications

Every place the tabletop is not exactly Root's rules:

1. **Decisions of a seat that is not taking its turn are made by a default**, logged as "(not their turn, decided by the
   default)":
   - Ambush (defender): always plays a matching ambush card if it holds one, a suited one before a bird.
   - Armorers (defender): used when it would ignore 2+ rolled hits, or 1 hit that would reach a building or token.
   - Sappers (defender): used whenever the attacker still has pieces in the clearing (and Armorers was not chosen).
   - Losing buildings/tokens after the warriors: in the faction's `LossPriority` order (Marquise: wood, recruiter,
     workshop, sawmill, keep last).
   - Field Hospitals (Marquise, outside her turn): used when 2+ warriors were removed and she holds a matching card
     that is neither a bird nor a dominance card (the first such card).
2. **Birdsong card effects** (Royal Claim, Stand and Deliver!) are offered once, after the faction's own Birdsong
   steps, not at any moment of Birdsong.
3. **"In Daylight" card effects** (activate or take dominance, Tax Collector, Codebreakers) are offered at every open
   decision of the active seat's Daylight (between actions, while crafting), not in the middle of an action.
4. **Order of after-roll battle effects**: the attacker uses Brutal Tactics / Armorers first, then the defender's
   default; in Root the attacker chooses the order when both have effects.
5. **The Marquise pays wood automatically**: from the building's clearing first, then the nearest connected ruled
   clearings (lower clearing number on ties). In Root she chooses which wood to remove.
6. **Marquise recruit with a short supply**: she chooses where the last warriors go only when the supply cannot fill
   every recruiter; with enough warriors all are placed at once.
7. **Bots** are a seeded random legal policy (passing options three times less likely) unless the core's bot director
   answers hook.bot-decision. They are not Root's AI.
8. **Game end**: 30 VP ends the game the moment it is reached. Simultaneous 30 VP ties (Law 3.1.1) cannot happen
   because scoring is applied one event at a time.
9. **Hidden information**: the deck order and the ruins' items are hidden from every view; discards are public.
   Codebreakers shows the hand in the private log of the seat that used it.
10. **Mod abilities** granted by the core (`rivers-as-paths`) apply to warrior moves; guard floors limit how many
    warriors a move may take out of a clearing.
11. **Vagabond** (engineer C; `Factions/Vagabond.cs`, `VagabondActions.cs`, `VagabondData.cs`):
   - Board and card numbers from the printed base game (the Root Database shows the board, characters and quests only
     as images; a veteran should check them): relationships need 1, 2, 2 Aids in one turn and score 1, 2, 2 VP
     (Amiable, Friendly, Allied); starting items Tinker boots, torch, bag, hammer / Ranger boots, torch, crossbow,
     sword / Thief boots, torch, tea, sword; Hideout repairs 3 items and ends Daylight (no move). The 15 quests: fox
     Fundraising (tea+coins), Errand (tea+boots), Logistics Help (boots+bag), Repair a Shed (torch+hammer), Give a
     Speech (torch+tea); rabbit Guard Duty (torch+sword), Errand, Give a Speech, Fend Off a Bear (torch+crossbow),
     Expel Bandits (sword+sword); mouse Expel Bandits, Guard Duty, Fend Off a Bear, Escort (boots+boots), Logistics
     Help. A quest scores 1 VP per completed quest of its suit, this one included (Law 9.5.5), or draws 2 cards.
   - Items are tracked by kind and state only: a face-up undamaged tea, coins or bag is on its track while the track
     has room (3), so there is no separate "move to the track" step. Refresh flips undamaged items before damaged
     face-down ones; the player chooses only when there are more exhausted undamaged items than refreshes.
   - Taking hits outside the Vagabond's turn (as defender, Full Removal), the default damages face-down items first,
     then crossbow, hammer, coins, tea, torch, bag, boots, and swords last.
   - Allied hits: when the Vagabond attacks with Allied warriors and takes a hit by removing one, the hit's dealer is
     the remover (a defending Vagabond can turn Hostile with that faction).
   - Coalition (Law 9.2.8): needs 10 VP (as for any dominance card) and 4+ players; the partner is a non-Vagabond with
     the fewest VP among the other players without an active dominance card.
   - Two Vagabonds share the 3 face-up quests and the quest deck (Law 9.7.2), but the ruins keep one item each (the
     second "R" set of Law 9.7.1 is not added) and the setup order is turn order, not random.
   - Aid may target the other Vagabond (a card only: it has no Crafted Items box and no relationship marker).
   - Cobbler moves warriors only, so it does nothing for the Vagabond (the lead's shared `card.cobbler` step).
   - Root 33 abilities: `no-hostile-boot` drops the extra Hostile boot; `glide` is a Daylight action (exhaust a torch,
     once per turn, pawn alone to any clearing); `rivers-as-paths` also counts for pawn moves and Slip.
12. **Eyrie Dynasties** (engineer B):
   - Board numbers from the printed faction board (the Root Database shows the boards only as images; a veteran
     should check them): Roosts track 0,1,2,3,4,4,5 VP for 1-7 roosts on the map; draw bonuses at the 3rd and 6th
     roost on the map.
   - Decree options are grouped by suit: when two cards of one suit wait in a column, the engine resolves "a fox card"
     (which of the two identical-suit cards is ticked off does not matter). Bird cards and Loyal Viziers are wild.
   - A card the Eyrie cannot resolve is only checked when no unresolved card of the current column can be resolved;
     the player picks the order inside a column among the cards that can be resolved now (Law 7.5.2).
   - Charismatic Recruit with one warrior left in supply places it, then falls into Turmoil (cannot fully recruit).
   - Commander's extra hit is added through the shared extra-hit channel (`State.Mod.ExtraHits`) when a Decree battle
     starts; a battle started by Command Warren does not get it (there is no faction hook at battle start).
   - Turmoil's Humiliate cannot take the score below 0. `hook.turmoil` (`OnTurmoil`) is called when the Eyrie fall
     into Turmoil, before Humiliate.
   - Setup corner: a corner that is not the keep's corner and, if possible, opposite it (Law 7.3.2); with no Marquise,
     any free corner.
13. **Woodland Alliance** (engineer B):
   - Board numbers from the printed faction board (to be checked by a veteran): sympathy costs 1,1,1,2,2,2,3,3,3,3
     supporters and scores 0,1,1,1,2,2,3,4,4,4 VP by tokens already on the map; Evening draws 1 + 1 per base on the map.
   - Supporters are spent automatically: cards of the exact suit first, birds last (in Root the Alliance picks).
   - Outrage: the payer chooses the card on its own turn; outside its turn (it removed sympathy as a defender) the
     default gives a card of the clearing's suit before a bird. The Vagabond's pawn moving into a sympathetic clearing
     does not cause Outrage (Law 8.2.6 says warriors; the pawn is not a warrior, Law 9.2.2).
   - Revolt with the Vagabond's pawn in the clearing (Full Removal, Law 9.2.2.I): the Vagabond takes 3 hits through
     its own hit frame (damaging items).
   - Losing the last base with more than 5 supporters: outside the Alliance's turn the default discards non-bird
     supporters first.
   - Supporters are hidden: `view` shows only their number. The Alliance sees them in `rules --seat N` and in the text
     of its own options (Birdsong actions, the Daylight "end" option).
   - Decree cards and supporters are held by the faction (`Eyrie.DecreeCards`, `Alliance.SupporterCards`), not in a
     hand or play area.

14. **Root 33 core link** (engineer D; `Root33/CoreBridge.cs`, `SimBoard.cs`, `ModText.cs`):
   - The tabletop is a real Root 33 game: `CoreBridge` builds `Root33Game` over `SimBoard` (IBoardView and
     IBoardActions over the sim state) with `ModuleRegistry.Default`, selects each seat's character, and forwards every
     `sheets/game_hooks.json` row the base game raises. The difficulty is always Normal.
   - Hooks the core causes while it runs (a guard warrior it places, VP it scores) are queued and sent to the core right
     after the hook that caused them; the core never runs inside itself.
   - A core exception is caught, logged once per hook ("Root 33 core error ...") and the game goes on without that rule.
   - No tunnels, free digs or relics in the base game: those board actions are reported unsupported, so the core takes
     its sheet fallbacks.
   - Countdown wins: the banner goes up at the end of the owner's turn and the win is confirmed at the start of their next
     Birdsong; a win ends the game at once and `GAME OVER` names the character's own win. A reload keeps the banner up
     until that Birdsong.
   - Speech bubbles: every bubble is logged publicly; `view` shows each seat the bubbles since the end of its own last
     turn (the last 120 are kept). The core does not save bubble cooldowns, so the bridge carries them across reloads by
     reading the bark director's private fields; if the core renames them, cooldowns simply restart after each command.
   - Overlays (art on the board) are kept as keys only; `view` draws each character's public tracker or gauge from the
     core's state as text (Road and Arm, Portraits 0/3, Sun and Moon, Perfection letter, family row, rock ring), marks a
     seat "ONE STEP AWAY" from its themed test, and shows the Monolith Number, the act and countdown banners.
   - do.submit-action is remembered and taken at that bot seat's next decision if still legal; do.native-ai-step uses
     the tabletop's own bot policy. Other bot decisions ask the core's bot director first (hook.bot-decision).
   - Rule texts come from the sheets with the notes meant for the plugin builder left out (patches, UI, fallbacks, other
     maps, DLC); v1.1 rules are off in this release and not shown.
   - At every save the dictionaries that lose keys in play (a clearing's warriors, banners) are rebuilt in key order, so
     a game the CLI reloads at every command plays exactly like one kept in memory.

## Next steps

1. B: done (Eyrie and Alliance); C: done (the Vagabonds); D: done (the Root 33 link: `Root33Game`
   over `SimBoard`, every hook, wins, acts, story, spawns, banners, bubbles, `ModView`).
2. Lead: review their seams, then run the 4-player robustness test with all real factions and two full CLI games
   (safe and finished spoiler modes).
