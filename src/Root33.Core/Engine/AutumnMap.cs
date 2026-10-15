using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// The Autumn map as sheets/map_autumn.json describes it. Until Root's own clearing ids are known (engineClearingId is
    /// unverified), clearing "autumn.NN" is <c>new ClearingId(NN)</c>; the plugin translates Root's ids to these numbers.
    /// </summary>
    public static class AutumnMap
    {
        public const string MapName = "autumn";

        /// <summary>The core's clearing id for a map_autumn row id ("autumn.03" gives 3).</summary>
        public static ClearingId ClearingOf(string autumnRowId) =>
            new ClearingId(int.Parse(autumnRowId.Substring(autumnRowId.LastIndexOf('.') + 1), CultureInfo.InvariantCulture));

        /// <summary>The map_autumn row id of a clearing (3 gives "autumn.03").</summary>
        public static string RowIdOf(ClearingId c) => "autumn." + c.Value.ToString("00", CultureInfo.InvariantCulture);

        public static IEnumerable<MapAutumnDef> Rows => Data.MapAutumn.Values.OrderBy(r => r.Id, System.StringComparer.Ordinal);

        public static Suit SuitOf(MapAutumnDef row) =>
            row.Suit == "fox" ? Suit.Fox : row.Suit == "rabbit" ? Suit.Rabbit : row.Suit == "mouse" ? Suit.Mouse : Suit.Bird;

        /// <summary>state.global.roles on Autumn: each row's fixed role, plus the Sacred River tag on every river clearing
        /// (map_roles role.sacred-river: "the river clearings in map_autumn.json").</summary>
        public static Dictionary<RoleId, IReadOnlyList<ClearingId>> FixedRoles()
        {
            var roles = new Dictionary<RoleId, IReadOnlyList<ClearingId>>();
            foreach (var r in Rows)
                if (!string.IsNullOrEmpty(r.Role)) roles[new RoleId(r.Role)] = new[] { ClearingOf(r.Id) };
            roles[new RoleId(Ids.MapRoles.SacredRiver)] = Rows.Where(r => r.River).Select(r => ClearingOf(r.Id)).ToArray();
            return roles;
        }
    }
}
