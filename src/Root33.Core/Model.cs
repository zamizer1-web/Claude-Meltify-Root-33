using System;
using System.Collections.Generic;

namespace Root33.Core
{
    /// <summary>A clearing on the current map, numbered the way Root numbers it.</summary>
    public readonly struct ClearingId : IEquatable<ClearingId>, IComparable<ClearingId>
    {
        public readonly int Value;
        public ClearingId(int value) { Value = value; }
        public bool Equals(ClearingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ClearingId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(ClearingId other) => Value.CompareTo(other.Value);
        public static bool operator ==(ClearingId a, ClearingId b) => a.Value == b.Value;
        public static bool operator !=(ClearingId a, ClearingId b) => a.Value != b.Value;
        public override string ToString() => "c" + Value;
    }

    /// <summary>A seat at the table (0-3), human or bot.</summary>
    public readonly struct SeatId : IEquatable<SeatId>
    {
        public readonly int Value;
        public SeatId(int value) { Value = value; }
        public bool Equals(SeatId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SeatId o && Equals(o);
        public override int GetHashCode() => Value;
        public static bool operator ==(SeatId a, SeatId b) => a.Value == b.Value;
        public static bool operator !=(SeatId a, SeatId b) => a.Value != b.Value;
        public override string ToString() => "seat" + Value;
    }

    public enum Suit { Fox, Rabbit, Mouse, Bird }

    /// <summary>Root engines the mod knows. Matches engines.json ids.</summary>
    public enum EngineId
    {
        Marquise, Eyrie, Alliance, VagabondTinker, VagabondRanger, VagabondThief, VagabondOther,
        Riverfolk, Lizards, Duchy, Corvids, Hundreds, Keepers
    }

    public enum PieceKind { Warrior, Building, Token, Pawn }

    public enum ItemType { Boots, Sword, Crossbow, Hammer, Torch, Bag, Coins, Tea }

    public enum Relationship { Indifferent, Amiable, Friendly, Allied, Hostile }

    public enum TurnPhase { Birdsong, Daylight, Evening }

    public enum SpoilerMode { Safe, Finished }

    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>Building and token types, as plain strings so every engine fits:
    /// "sawmill", "workshop", "recruiter", "keep", "wood", "roost", "base.fox", "sympathy", ...</summary>
    public static class PieceTypes
    {
        public const string Sawmill = "sawmill";
        public const string Workshop = "workshop";
        public const string Recruiter = "recruiter";
        public const string Keep = "keep";
        public const string Wood = "wood";
        public const string Roost = "roost";
        public const string Base = "base";
        public const string Sympathy = "sympathy";
        public const string Tunnel = "tunnel";
        public const string Garden = "garden";
        public const string Plot = "plot";
        public const string Waystation = "waystation";
        public const string Stronghold = "stronghold";
    }

    /// <summary>One item on a Vagabond's card.</summary>
    public readonly struct ItemState
    {
        public readonly ItemType Type;
        public readonly bool Damaged;
        public readonly bool Exhausted;
        public ItemState(ItemType type, bool damaged, bool exhausted) { Type = type; Damaged = damaged; Exhausted = exhausted; }
    }

    /// <summary>One piece removed in a battle (or by another effect).</summary>
    public readonly struct RemovedPiece
    {
        public readonly SeatId Owner;
        public readonly PieceKind Kind;
        public readonly string Type;   // building/token type, or null for warriors and pawns
        public RemovedPiece(SeatId owner, PieceKind kind, string type) { Owner = owner; Kind = kind; Type = type; }
    }

    /// <summary>Everything the core needs to know after a battle resolves.</summary>
    public sealed class BattleResult
    {
        public ClearingId Clearing;
        public SeatId Attacker;
        public SeatId Defender;
        public List<RemovedPiece> Removed = new List<RemovedPiece>();
        /// <summary>For each Vagabond seat in the battle: damaged item count before and after.</summary>
        public Dictionary<SeatId, int> DamagedItemsBefore = new Dictionary<SeatId, int>();
        public Dictionary<SeatId, int> DamagedItemsAfter = new Dictionary<SeatId, int>();
        /// <summary>True when the attacker's pawn or warlord entered this clearing earlier this turn.</summary>
        public bool AttackerMovedInThisTurn;
        /// <summary>The seat with the most VP when the battle started (ties: null).</summary>
        public SeatId? VpLeaderAtStart;
    }

    /// <summary>A story place the rules refer to (map_roles.json id, e.g. "role.monolith").</summary>
    public readonly struct RoleId : IEquatable<RoleId>
    {
        public readonly string Value;
        public RoleId(string value) { Value = value; }
        public bool Equals(RoleId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RoleId o && Equals(o);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();
        public override string ToString() => Value;
    }
}
