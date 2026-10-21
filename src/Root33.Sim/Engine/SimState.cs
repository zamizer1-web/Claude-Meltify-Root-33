using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Root33.Core;

namespace Root33.Sim;

/// <summary>
/// Everything a game is, as plain data: one JSON document (System.Text.Json, <see cref="SimJson.Options"/>).
/// Seats are 0-based inside the engine and shown 1-based to players ("seat 1"). Clearings are numbered 1-12 as in
/// sheets/map_autumn.json. A faction's own typed data lives in <see cref="SeatState.FactionData"/> and is read through
/// <see cref="SimGame.Data{T}"/>. Owner: engine (lead). Factions and the core bridge read and write it through SimGame.
/// </summary>
public sealed class SimState
{
    public int Version { get; set; } = 1;
    public int Seed { get; set; }
    /// <summary>"safe" or "finished" (Clair Obscur spoiler mode).</summary>
    public string Spoiler { get; set; } = "safe";
    /// <summary>Game RNG (dice, shuffles, random card takes). Advanced only by <see cref="SimGame.Random"/>.</summary>
    public ulong Rng { get; set; }
    /// <summary>Bot-policy RNG, separate so a bot's choices never shift the dice.</summary>
    public ulong BotRng { get; set; }

    public List<SeatState> Seats { get; set; } = new();
    /// <summary>The seat that takes the first turn (chosen by the seed). Turn order is clockwise: seat+1, wrapping.</summary>
    public int FirstSeat { get; set; }
    /// <summary>0 during setup; 1 from the first Birdsong.</summary>
    public int Round { get; set; }
    /// <summary>Ask a human seat the questions it owes on another seat's turn (ambush, Field Hospitals, hit losses...),
    /// as Root does. Set by the CLI for every new game; when false (engine tests, older saves) every off-turn question
    /// is answered by its documented default.</summary>
    public bool AskHumansOffTurn { get; set; }

    public Dictionary<int, ClearingState> Clearings { get; set; } = new();
    public List<int> Deck { get; set; } = new();
    public List<int> Discard { get; set; } = new();
    /// <summary>Dominance cards that were discarded or spent: available to be taken in Daylight (Law 3.3.3-3.3.4).</summary>
    public List<int> AvailableDominance { get; set; } = new();
    /// <summary>The item supply on the map (Law 5.1.5): 2 Boots, 2 Bags, 1 Crossbow, 1 Hammer, 2 Swords, 2 Tea, 2 Coins.</summary>
    public List<ItemType> ItemSupply { get; set; } = new();

    public bool SetupDone { get; set; }
    /// <summary>Seats still to set up, in setup order (Marquise A, Eyrie B, Vagabonds C, Alliance D).</summary>
    public List<int> SetupQueue { get; set; } = new();
    public int SetupSeat { get; set; } = -1;
    public TurnState Turn { get; set; } = new();

    /// <summary>The decision stack: the top frame is the procedure now running (see Decision.cs).</summary>
    public List<Frame> Stack { get; set; } = new();
    /// <summary>The one pending decision, or null when the game is over.</summary>
    public Decision? Pending { get; set; }
    /// <summary>The battle being fought, or null.</summary>
    public BattleState? Battle { get; set; }

    public List<LogEntry> Log { get; set; } = new();
    public int DecisionsTaken { get; set; }

    public bool GameOver { get; set; }
    public List<int> Winners { get; set; } = new();
    public string? GameOverText { get; set; }

    /// <summary>What the Root 33 core changed through IBoardActions (owner of the writes: engineer D's SimBoard).</summary>
    public ModEffects Mod { get; set; } = new();
}

public sealed class SeatState
{
    public int Seat { get; set; }
    /// <summary>E33 character id from sheets/characters.json (gustave, paintress, sciel, esquie, verso, maelle).</summary>
    public string Character { get; set; } = "";
    /// <summary>characters.json engine: marquise, eyrie, alliance, vagabond-tinker, vagabond-ranger, vagabond-thief.</summary>
    public string Engine { get; set; } = "";
    /// <summary>FactionRegistry id: marquise, eyrie, alliance, vagabond.</summary>
    public string Faction { get; set; } = "";
    public bool Human { get; set; }
    public int Vp { get; set; }
    public List<int> Hand { get; set; } = new();
    /// <summary>Crafted persistent effects and revealed cards in the play area (Law 2.1.4).</summary>
    public List<int> PlayArea { get; set; } = new();
    /// <summary>Items crafted by a non-Vagabond faction (its Crafted Items box). Vagabonds keep items in their own data.</summary>
    public List<ItemType> CraftedItems { get; set; } = new();
    /// <summary>The activated dominance card, or null (Law 3.3.1).</summary>
    public int? Dominance { get; set; }
    /// <summary>For a Vagabond in a coalition: the partner seat (it wins when the partner wins).</summary>
    public int? CoalitionWith { get; set; }
    public int SupplyWarriors { get; set; }
    /// <summary>Buildings and tokens left in supply (for Marquise: on the buildings tracks), by PieceTypes string.</summary>
    public Dictionary<string, int> Supply { get; set; } = new();
    /// <summary>A pawn's clearing (Vagabond), or null.</summary>
    public int? PawnClearing { get; set; }
    /// <summary>A pawn's forest (1-7, SimMap.ForestDefs), or null.</summary>
    public int? PawnForest { get; set; }
    /// <summary>The faction's own typed state, serialised. Use SimGame.Data&lt;T&gt;(seat), never this node directly.</summary>
    public JsonNode? FactionData { get; set; }
}

public sealed class ClearingState
{
    public int Id { get; set; }
    /// <summary>Warriors by seat (seats with none are absent).</summary>
    public Dictionary<int, int> Warriors { get; set; } = new();
    public List<Piece> Buildings { get; set; } = new();
    public List<Piece> Tokens { get; set; } = new();
    /// <summary>A ruin still fills one building slot here.</summary>
    public bool Ruin { get; set; }
    /// <summary>The face-down item under the ruin (hidden from every player until explored).</summary>
    public ItemType? RuinItem { get; set; }

    public int WarriorsOf(int seat) => Warriors.TryGetValue(seat, out var n) ? n : 0;
}

public sealed class Piece
{
    public int Seat { get; set; }
    /// <summary>PieceTypes string: sawmill, workshop, recruiter, keep, wood, roost, base.fox, sympathy...</summary>
    public string Type { get; set; } = "";
    public Piece() { }
    public Piece(int seat, string type) { Seat = seat; Type = type; }
}

public sealed class TurnState
{
    public int Seat { get; set; }
    public TurnPhase Phase { get; set; }
    /// <summary>Turn machine step (SimGame.TurnMachine): 0 Birdsong start ... 7 end of turn.</summary>
    public int Step { get; set; }
    /// <summary>Crafting pieces activated this turn (keys from IFaction.CraftingPieces).</summary>
    public List<string> UsedCraftPieces { get; set; } = new();
    /// <summary>Once-per-turn or once-per-phase effects already used ("tax-collector", "codebreakers", "royal-claim"...).</summary>
    public List<string> UsedOnce { get; set; } = new();
    /// <summary>Free per-turn counters for factions (cleared at the end of each turn).</summary>
    public Dictionary<string, int> Counters { get; set; } = new();
}

public sealed class LogEntry
{
    public int N { get; set; }
    public int Round { get; set; }
    /// <summary>When set, only this seat may see the entry (cards drawn, a hand looked at).</summary>
    public int? Only { get; set; }
    public string Text { get; set; } = "";
}

public sealed class BattleState
{
    public int Attacker { get; set; }
    public int Defender { get; set; }
    public int Clearing { get; set; }
    public int AttackerRoll { get; set; }
    public int DefenderRoll { get; set; }
    public int AttackerHits { get; set; }
    public int DefenderHits { get; set; }
    public int? AmbushCard { get; set; }
    public bool Foiled { get; set; }
    public bool BrutalTactics { get; set; }
    public bool AttackerArmorers { get; set; }
    public bool DefenderArmorers { get; set; }
    public bool Sappers { get; set; }
    public List<RemovedRecord> Removed { get; set; } = new();
    /// <summary>Damaged Vagabond items before the battle, by seat (filled by the Vagabond faction).</summary>
    public Dictionary<int, int> DamagedBefore { get; set; } = new();
    public Dictionary<int, int> DamagedAfter { get; set; } = new();
    /// <summary>The seat with the most VP when the battle started (null on a tie).</summary>
    public int? VpLeaderAtStart { get; set; }
}

public sealed class RemovedRecord
{
    public int Owner { get; set; }
    public PieceKind Kind { get; set; }
    public string? Type { get; set; }
    public int? Remover { get; set; }
    public int Clearing { get; set; }
}

/// <summary>
/// Effects the Root 33 core asks for through IBoardActions. Engineer D's SimBoard writes them; the engine's shared
/// rules read them: Battle (ExtraHits, ExtraMaxHits, IgnoreFirstHit, cleared when a battle ends), Move (GuardFloors,
/// Abilities "rivers-as-paths"), placement (BlockedPlacements), setup (CoreBridge.ForcedHome), game over
/// (DeclaredWinners with CoreBridge.IsGameOver). Everything else is display or core bookkeeping kept for D.
/// </summary>
public sealed class ModEffects
{
    public Dictionary<int, int> ExtraHits { get; set; } = new();
    public Dictionary<int, int> ExtraMaxHits { get; set; } = new();
    public List<int> IgnoreFirstHit { get; set; } = new();
    public List<GuardFloor> GuardFloors { get; set; } = new();
    public List<SeatClearing> BlockedPlacements { get; set; } = new();
    public Dictionary<int, List<int>> HomeRestrictions { get; set; } = new();
    public Dictionary<int, List<int>> StartForestRestrictions { get; set; } = new();
    public Dictionary<int, List<string>> Abilities { get; set; } = new();
    public Dictionary<int, string> Banners { get; set; } = new();
    public Dictionary<int, string> SeatTitles { get; set; } = new();
    public List<string> Overlays { get; set; } = new();
    public List<int> DeclaredWinners { get; set; } = new();
    public string? DeclaredWinId { get; set; }
    /// <summary>The core's ModState text from hook.save, restored with hook.load.</summary>
    public string CoreState { get; set; } = "";
    /// <summary>Free key/value storage for the bridge (D).</summary>
    public Dictionary<string, string> Extra { get; set; } = new();
}

public sealed class GuardFloor { public int Seat { get; set; } public int Clearing { get; set; } public int Floor { get; set; } }
public sealed class SeatClearing { public int Seat { get; set; } public int Clearing { get; set; } }

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };
}
