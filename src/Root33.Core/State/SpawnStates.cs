namespace Root33.Core
{
    /// <summary>One of Esquie's four rocks (state.esquie.rocks; entries in order Florrie, Dorrie, Soarrie, Urrie).
    /// Fields as the state row's range describes them.</summary>
    public sealed partial class RockState
    {
        /// <summary>notSpawned | hidden | guarded | loose | heldByRuler | withVerso | held (see <see cref="RockStatus"/>).</summary>
        public string Status = RockStatus.NotSpawned;
        /// <summary>Null while withVerso, heldByRuler or held.</summary>
        public ClearingId? Clearing;
        /// <summary>The Nevron host for guarded rocks, Verso's seat for withVerso, null otherwise.</summary>
        public SeatId? HostSeat;
        /// <summary>0-3.</summary>
        public int GuardsLeft;

        public RockState Clone() => new RockState { Status = Status, Clearing = Clearing, HostSeat = HostSeat, GuardsLeft = GuardsLeft };
    }

    /// <summary>The status strings of <see cref="RockState.Status"/>, exactly as state.esquie.rocks names them.</summary>
    public static class RockStatus
    {
        public const string NotSpawned = "notSpawned";
        public const string Hidden = "hidden";
        public const string Guarded = "guarded";
        public const string Loose = "loose";
        public const string HeldByRuler = "heldByRuler";
        public const string WithVerso = "withVerso";
        public const string Held = "held";
    }

    /// <summary>One of Monoco's five feet (state.monoco.feet; entries in order Agile, Caster, Balanced, Heavy, Almighty).</summary>
    public sealed partial class FootState
    {
        public bool Earned;
        /// <summary>The seat the foot came from.</summary>
        public SeatId? Faction;
        /// <summary>1 for the first foot earned and rising; 0 while not earned.</summary>
        public int Order;

        public FootState Clone() => new FootState { Earned = Earned, Faction = Faction, Order = Order };
    }
}
