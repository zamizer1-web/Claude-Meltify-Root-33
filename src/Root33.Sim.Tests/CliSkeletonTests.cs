using Root33.Sim;

namespace Root33.Sim.Tests;

public class CliSkeletonTests
{
    [Fact]
    public void ParseSeats_AcceptsFourHumansWithTwoVagabonds()
    {
        var seats = Program.ParseSeats("gustave:human,paintress:human,esquie:human,verso:human");
        Assert.Equal(4, seats.Count);
        Assert.All(seats, s => Assert.True(s.Human));
    }

    [Theory]
    [InlineData("gustave:human,gustave:human")]
    [InlineData("gustave:human,lune:human")]
    [InlineData("esquie:human,verso:human,maelle:human")]
    [InlineData("gustave:human")]
    public void ParseSeats_RejectsIllegalTables(string spec)
    {
        Assert.Throws<SimUsageException>(() => Program.ParseSeats(spec));
    }

    [Fact]
    public void New_WritesSaveFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "root33-sim-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var w = new StringWriter();
            int rc = Program.Run(new[] { "new", "--save", path, "--seed", "7", "--spoiler", "safe",
                "--seats", "gustave:human,paintress:human,sciel:human,esquie:human" }, w);
            Assert.Equal(0, rc);
            Assert.True(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
