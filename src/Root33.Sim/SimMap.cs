using Root33.Core;
using Root33.Core.Engine;

namespace Root33.Sim;

/// <summary>
/// The Autumn map as the sim plays it: built from the core's map_autumn rows (Data.MapAutumn via AutumnMap), with the
/// seven forests numbered 1-7 exactly as src/Root33.Core.Tests/Support/FakeBoard.cs derives them (design/autumn-map-verified.json).
/// </summary>
public sealed class SimMap
{
    public sealed class Clearing
    {
        public int Id;
        public string Name = "";
        public Suit Suit;
        public int Slots;
        public bool Ruin;
        public bool Corner;
        public bool OnRiver;
        public int? OppositeCorner;
        public List<int> Paths = new();
        public List<int> RiverLinks = new();
    }

    public static readonly IReadOnlyDictionary<int, int[]> ForestDefs = new Dictionary<int, int[]>
    {
        [1] = new[] { 4, 11, 3, 10 }, [2] = new[] { 4, 10, 8, 12 }, [3] = new[] { 3, 10, 8, 7, 9 }, [4] = new[] { 12, 8, 1 },
        [5] = new[] { 8, 1, 5, 6 }, [6] = new[] { 8, 7, 2, 6 }, [7] = new[] { 7, 9, 2 },
    };

    public SortedDictionary<int, Clearing> Clearings { get; } = new();

    public static SimMap Autumn()
    {
        var m = new SimMap();
        foreach (var r in AutumnMap.Rows)
        {
            var id = AutumnMap.ClearingOf(r.Id).Value;
            m.Clearings[id] = new Clearing
            {
                Id = id,
                Name = r.ContinentName,
                Suit = AutumnMap.SuitOf(r),
                Slots = r.Slots,
                Ruin = r.Ruin,
                Corner = r.Corner,
                OnRiver = r.River,
                OppositeCorner = string.IsNullOrEmpty(r.OppositeCorner) ? null : AutumnMap.ClearingOf(r.OppositeCorner).Value,
                Paths = r.Paths.Select(p => AutumnMap.ClearingOf(p).Value).ToList(),
                RiverLinks = (r.RiverLinks ?? Array.Empty<string>()).Select(p => AutumnMap.ClearingOf(p).Value).ToList(),
            };
        }
        return m;
    }

    public IEnumerable<int> ForestsAround(int clearing) => ForestDefs.Where(f => f.Value.Contains(clearing)).Select(f => f.Key);

    /// <summary>True when the clearing borders the forest (Vagabond pawn placement in a forest, explore adjacency).</summary>
    public bool ForestTouches(int forest, int clearing) => ForestDefs[forest].Contains(clearing);
}
