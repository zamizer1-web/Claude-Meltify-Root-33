using System;
using System.Collections.Generic;

namespace Root33.Core
{
    /// <summary>Map searches over clearings, independent of how the board is stored.</summary>
    public static class Graph
    {
        /// <summary>Path distance from <paramref name="start"/> to every reachable clearing.</summary>
        public static Dictionary<ClearingId, int> Distances(ClearingId start, Func<ClearingId, IEnumerable<ClearingId>> neighbours)
        {
            var dist = new Dictionary<ClearingId, int> { [start] = 0 };
            var queue = new Queue<ClearingId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in neighbours(c))
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>Clearings reachable from <paramref name="start"/> moving only through clearings that pass <paramref name="allowed"/>.
        /// Empty when the start itself is not allowed.</summary>
        public static HashSet<ClearingId> ConnectedWithin(ClearingId start, Func<ClearingId, IEnumerable<ClearingId>> neighbours, Func<ClearingId, bool> allowed)
        {
            var seen = new HashSet<ClearingId>();
            if (!allowed(start)) return seen;
            var queue = new Queue<ClearingId>();
            seen.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in neighbours(c))
                {
                    if (seen.Contains(n) || !allowed(n)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        /// <summary>Cheapest path (Dijkstra) from <paramref name="start"/> to the first clearing that satisfies <paramref name="isGoal"/>,
        /// where entering a clearing costs <paramref name="enterCost"/> (the start costs its own enter cost too, so a plan can price
        /// retaking it). Returns null when no goal is reachable. Ties break toward lower clearing ids, so results are deterministic.</summary>
        public static List<ClearingId> CheapestPath(ClearingId start, Func<ClearingId, bool> isGoal,
            Func<ClearingId, IEnumerable<ClearingId>> neighbours, Func<ClearingId, int> enterCost, out int totalCost)
        {
            var best = new Dictionary<ClearingId, int> { [start] = enterCost(start) };
            var prev = new Dictionary<ClearingId, ClearingId>();
            var done = new HashSet<ClearingId>();
            var open = new SortedSet<(int cost, int id)> { (best[start], start.Value) };
            while (open.Count > 0)
            {
                var (cost, id) = open.Min;
                open.Remove(open.Min);
                var c = new ClearingId(id);
                if (!done.Add(c)) continue;
                if (isGoal(c))
                {
                    totalCost = cost;
                    var path = new List<ClearingId> { c };
                    while (prev.TryGetValue(path[path.Count - 1], out var p)) path.Add(p);
                    path.Reverse();
                    return path;
                }
                foreach (var n in neighbours(c))
                {
                    if (done.Contains(n)) continue;
                    var next = cost + enterCost(n);
                    if (best.TryGetValue(n, out var old) && old <= next) continue;
                    if (best.ContainsKey(n)) open.Remove((old, n.Value));
                    best[n] = next;
                    prev[n] = c;
                    open.Add((next, n.Value));
                }
            }
            totalCost = int.MaxValue;
            return null;
        }
    }
}
