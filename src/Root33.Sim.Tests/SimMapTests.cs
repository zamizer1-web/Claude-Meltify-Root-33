using Root33.Sim;

namespace Root33.Sim.Tests;

public class SimMapTests
{
    [Fact]
    public void Autumn_HasTwelveClearingsWithSymmetricPaths()
    {
        var m = SimMap.Autumn();
        Assert.Equal(12, m.Clearings.Count);
        foreach (var c in m.Clearings.Values)
            foreach (var p in c.Paths)
                Assert.Contains(c.Id, m.Clearings[p].Paths);
        Assert.Equal(4, m.Clearings.Values.Count(c => c.Corner));
        Assert.Equal("Lumière", m.Clearings[1].Name);
    }
}
