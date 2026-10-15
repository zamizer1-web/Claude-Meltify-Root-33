using System.Collections.Generic;
using System.Linq;
using Root33.Core.Engine;
using Root33.Core.Sheets;
using Root33.Core.Tests.Support;
using Xunit;

namespace Root33.Core.Tests.MapRoles
{
    public class MapRolesCornerTests
    {
        [Fact]
        public void MapRoles_AxonsSkipCornerNeighbours()
        {
            // A made-up map: corners 1-4, Lumière 1, the Monolith its diagonal 3. The Monolith's neighbours are 2 (a corner),
            // 5, 7 and 9: the corner is skipped, so the Axons are 5 and 7 and The Reacher 9.
            var b = FakeBoard.Map("made-up").Clearings(12).Corners(1, 2, 3, 4).Opposite(1, 3)
                .Paths("1-5 1-6 2-3 2-6 3-5 3-7 3-9 4-8 4-10 5-6 6-7 7-8 8-9 9-10 10-11 11-12 12-1");
            var roles = RoleMap.AssignByRule(b, null);
            IReadOnlyList<ClearingId> Of(string id) => roles.TryGetValue(new RoleId(id), out var l) ? l : new ClearingId[0];
            Assert.Equal(new[] { new ClearingId(3) }, Of(Ids.MapRoles.Monolith));
            Assert.Equal(new[] { new ClearingId(5) }, Of(Ids.MapRoles.Visages));
            Assert.Equal(new[] { new ClearingId(7) }, Of(Ids.MapRoles.Sirene));
            Assert.Equal(new[] { new ClearingId(9) }, Of(Ids.MapRoles.TheReacher));

            // The skipped corner gets no Axon name, holds a corner role instead, and is still a Monolith neighbour.
            var axons = Of(Ids.MapRoles.Visages).Concat(Of(Ids.MapRoles.Sirene)).Concat(Of(Ids.MapRoles.TheReacher)).ToList();
            Assert.DoesNotContain(new ClearingId(2), axons);
            Assert.Contains(new ClearingId(2), Of(Ids.MapRoles.EsquiesNest).Concat(Of(Ids.MapRoles.FlyingManor)));
            Assert.Contains(new ClearingId(2), b.PathNeighbours(new ClearingId(3)));
        }
    }
}
