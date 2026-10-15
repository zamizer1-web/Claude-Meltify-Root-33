using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// Assigns the story roles of sheets/map_roles.json to clearings. Autumn uses the fixed roles of map_autumn.json; every
    /// other map uses the map_roles assignRule rules, in the sheet's order: Lumière, the Monolith, the Axons (Visages,
    /// Sirène), The Reacher, Old Lumière, Esquie's Nest and the Flying Manor, Stone Wave Cliffs, then the Sacred River tag.
    /// "Path distance" counts steps along open paths (read.path-neighbours); rivers are not paths. Ties go to the lowest
    /// clearing number. Pure: reads the board, changes nothing.
    /// </summary>
    public static class RoleMap
    {
        public static readonly RoleId Lumiere = new RoleId(Ids.MapRoles.Lumiere);
        public static readonly RoleId Monolith = new RoleId(Ids.MapRoles.Monolith);
        public static readonly RoleId Visages = new RoleId(Ids.MapRoles.Visages);
        public static readonly RoleId Sirene = new RoleId(Ids.MapRoles.Sirene);
        public static readonly RoleId TheReacher = new RoleId(Ids.MapRoles.TheReacher);
        public static readonly RoleId OldLumiere = new RoleId(Ids.MapRoles.OldLumiere);
        public static readonly RoleId EsquiesNest = new RoleId(Ids.MapRoles.EsquiesNest);
        public static readonly RoleId FlyingManor = new RoleId(Ids.MapRoles.FlyingManor);
        public static readonly RoleId StoneWaveCliffs = new RoleId(Ids.MapRoles.StoneWaveCliffs);
        public static readonly RoleId SacredRiver = new RoleId(Ids.MapRoles.SacredRiver);

        /// <summary>The roles that name one clearing (all but the Sacred River tag).</summary>
        public static readonly IReadOnlyList<RoleId> SingleClearingRoles = new[]
            { Lumiere, Monolith, Visages, Sirene, TheReacher, OldLumiere, EsquiesNest, FlyingManor, StoneWaveCliffs };

        /// <summary>Roles for the board's map: the fixed Autumn roles on Autumn, otherwise <see cref="AssignByRule"/>.</summary>
        public static Dictionary<RoleId, IReadOnlyList<ClearingId>> ForMap(IBoardView board, ClearingId? gustaveKeepCorner)
        {
            if (board.MapName() == AutumnMap.MapName) return AutumnMap.FixedRoles();
            return AssignByRule(board, gustaveKeepCorner);
        }

        /// <summary>The map_roles rules for any map. <paramref name="gustaveKeepCorner"/> is the corner holding Gustave's keep
        /// when he is seated (role.lumiere), or null to choose Lumière by rule (lowest-numbered corner).</summary>
        public static Dictionary<RoleId, IReadOnlyList<ClearingId>> AssignByRule(IBoardView board, ClearingId? gustaveKeepCorner)
        {
            var roles = new Dictionary<RoleId, IReadOnlyList<ClearingId>>();
            var clearings = board.Clearings().OrderBy(c => c.Value).ToList();
            var corners = board.Corners().OrderBy(c => c.Value).ToList();
            IEnumerable<ClearingId> Paths(ClearingId c) => board.PathNeighbours(c);
            void Set(RoleId role, params ClearingId[] cs) => roles[role] = cs;

            // role.lumiere
            var lumiere = gustaveKeepCorner ?? corners.First();
            Set(Lumiere, lumiere);

            // role.monolith: Root's diagonal pairing, else the farthest corner by path distance.
            ClearingId monolith;
            var opposite = board.OppositeCorner(lumiere);
            if (opposite.HasValue) monolith = opposite.Value;
            else
            {
                var d = Graph.Distances(lumiere, Paths);
                monolith = corners.Where(c => c != lumiere)
                    .OrderByDescending(c => d.TryGetValue(c, out var x) ? x : int.MaxValue).ThenBy(c => c.Value).First();
            }
            Set(Monolith, monolith);

            // role.visages / role.sirene / role.the-reacher: the Monolith's open path neighbours in clearing order, skipping
            // corners (a corner already holds Lumière, Esquie's Nest or the Flying Manor). A skipped corner gets no name but is
            // still a Monolith neighbour.
            var cornerSet = new HashSet<ClearingId>(corners);
            var monolithNeighbours = Paths(monolith).Distinct().OrderBy(c => c.Value).ToList();
            var axons = monolithNeighbours.Where(c => !cornerSet.Contains(c)).ToList();
            Set(Visages, axons.Take(1).ToArray());
            Set(Sirene, axons.Skip(1).Take(1).ToArray());
            Set(TheReacher, axons.Skip(2).Take(1).ToArray());

            var neighbourSet = new HashSet<ClearingId>(monolithNeighbours);

            // role.old-lumiere: the most central ruin that is not a corner or a Monolith neighbour; else the most central such clearing.
            int Spread(ClearingId c)
            {
                var d = Graph.Distances(c, Paths);
                return clearings.Sum(o => d.TryGetValue(o, out var x) ? x : clearings.Count + 1);
            }
            var eligible = clearings.Where(c => !cornerSet.Contains(c) && !neighbourSet.Contains(c)).ToList();
            var ruins = eligible.Where(board.HasRuin).ToList();
            var oldLumiere = (ruins.Count > 0 ? ruins : eligible).OrderBy(Spread).ThenBy(c => c.Value).First();
            Set(OldLumiere, oldLumiere);

            // Stone Wave Cliffs candidates for a corner: 1-2 steps away, not a corner, not a Monolith neighbour, not Old Lumière.
            bool Free(ClearingId c) => !cornerSet.Contains(c) && !neighbourSet.Contains(c) && c != oldLumiere;
            List<ClearingId> CliffsCandidates(ClearingId nest)
            {
                var d = Graph.Distances(nest, Paths);
                return d.Where(kv => kv.Value >= 1 && kv.Value <= 2 && Free(kv.Key))
                    .OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.Value).Select(kv => kv.Key).ToList();
            }

            // role.esquies-nest / role.flying-manor
            var free = corners.Where(c => c != lumiere && c != monolith).ToList();
            var withCandidate = free.Where(c => CliffsCandidates(c).Count > 0).ToList();
            var nest = (withCandidate.Count == 1 ? withCandidate : free).OrderBy(c => c.Value).First();
            Set(EsquiesNest, nest);
            Set(FlyingManor, free.Where(c => c != nest).Take(1).ToArray());

            // role.stone-wave-cliffs: nearest candidate, else the nearest free clearing at any distance.
            var cliffs = CliffsCandidates(nest);
            if (cliffs.Count > 0) Set(StoneWaveCliffs, cliffs[0]);
            else
            {
                var d = Graph.Distances(nest, Paths);
                Set(StoneWaveCliffs, d.Where(kv => kv.Value >= 1 && Free(kv.Key))
                    .OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.Value).Select(kv => kv.Key).Take(1).ToArray());
            }

            // role.sacred-river: every clearing a river touches (Lake: the lake shore), as read.on-river reports it.
            roles[SacredRiver] = clearings.Where(board.OnRiver).ToArray();
            return roles;
        }
    }
}
