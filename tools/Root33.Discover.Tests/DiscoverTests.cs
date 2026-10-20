using System.Text.Json;
using Root33.Discover;

namespace Root33.Discover.Tests;

/// <summary>Root33.Discover over the repo's own assemblies (Root33.Core, Root33.Host, Root33.Sim), the way the PC session
/// runs it over Root's BepInEx\interop folder.</summary>
public sealed class DiscoverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "root33-discover-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A folder with copies of the given assemblies from the test's output folder.</summary>
    private string Folder(params string[] assemblies)
    {
        var dir = Path.Combine(_dir, "in-" + assemblies.Length);
        Directory.CreateDirectory(dir);
        foreach (var a in assemblies) File.Copy(Path.Combine(AppContext.BaseDirectory, a + ".dll"), Path.Combine(dir, a + ".dll"));
        return dir;
    }

    private static JsonElement TypeNamed(JsonDocument doc, string fullName) =>
        doc.RootElement.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("fullName").GetString() == fullName);

    private static IEnumerable<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) ? a.EnumerateArray().Select(x => x.GetString()!) : Enumerable.Empty<string>();

    [Fact]
    public void Discover_RepoAssemblies_WritesEveryTypeWithItsMembers()
    {
        var input = Folder("Root33.Core", "Root33.Host", "Root33.Sim");
        var output = Path.Combine(_dir, "out");
        var result = Program.Discover(input, output, Array.Empty<string>());
        Assert.Equal(3, result.Assemblies);
        Assert.Equal(0, result.Unresolved);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types.json")));
        Assert.Equal("in-3", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal(result.Types, doc.RootElement.GetProperty("types").GetArrayLength());

        var game = TypeNamed(doc, "Root33.Core.Engine.Root33Game");
        Assert.Equal("Root33.Core", game.GetProperty("assembly").GetString());
        Assert.Equal("object", game.GetProperty("baseType").GetString());
        Assert.Contains("Root33.Core.IGameHooks", Strings(game, "interfaces"));
        var battle = game.GetProperty("methods").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "OnBattleResolved");
        Assert.Equal("public void OnBattleResolved(Root33.Core.BattleResult r)", battle.GetProperty("signature").GetString());

        // one IGameHooks member per sheets/game_hooks.json row
        var hooks = TypeNamed(doc, "Root33.Core.IGameHooks");
        Assert.Equal("interface", hooks.GetProperty("kind").GetString());
        Assert.Equal(Root33.Core.Sheets.Data.GameHooks.Count, hooks.GetProperty("methods").GetArrayLength());

        var phase = TypeNamed(doc, "Root33.Core.TurnPhase");
        Assert.Equal("enum", phase.GetProperty("kind").GetString());
        Assert.Equal(new[] { "Birdsong=0", "Daylight=1", "Evening=2" },
            phase.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("name").GetString() + "=" + v.GetProperty("value").GetString()));

        // generic and nullable types read as C# writes them; private fields are kept
        var ctx = TypeNamed(doc, "Root33.Core.Engine.GameContext");
        var props = ctx.GetProperty("properties").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("type").GetString());
        Assert.Equal("System.Collections.Generic.IReadOnlyList<Root33.Core.SeatId>", props["TurnOrder"]);
        var bark = TypeNamed(doc, "Root33.Core.Engine.BarkDirector");
        var field = bark.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "_lastByTrigger");
        Assert.Equal("private", field.GetProperty("visibility").GetString());
        Assert.Equal("System.Collections.Generic.Dictionary<System.ValueTuple<Root33.Core.SeatId, string>, int>", field.GetProperty("type").GetString());

        // compiler-generated types (lambdas, iterators) are left out
        Assert.DoesNotContain(doc.RootElement.GetProperty("types").EnumerateArray(), t => t.GetProperty("name").GetString()!.Contains('<'));
        Assert.DoesNotContain(doc.RootElement.GetProperty("types").EnumerateArray(), t => t.GetProperty("fullName").GetString()!.Contains("<>"));
    }

    [Fact]
    public void Discover_Report_GroupsByRootConcept_AndListsTheGameFlow()
    {
        var output = Path.Combine(_dir, "out");
        Program.Discover(Folder("Root33.Core", "Root33.Host", "Root33.Sim"), output, Array.Empty<string>());
        var report = File.ReadAllText(Path.Combine(output, "report.md"));
        foreach (var c in Concept.All) Assert.Contains($"| [{c.Heading}](#concept-{c.Heading}) |", report);
        Assert.Contains("### battle", report);
        Assert.Contains("- `Root33.Core.BattleResult` class", report);
        Assert.Contains("- `Root33.Core.IGameHooks.OnBirdsongStart` method: `void OnBirdsongStart(SeatId seat)`", report);
        Assert.Contains("- `Root33.Core.TurnPhase` (Root33.Core): Birdsong=0, Daylight=1, Evening=2", report);
        Assert.Contains("- `Root33.Core.Engine.Root33Game` class", report);
        Assert.Contains("- `Root33.Core.Engine.BarkDirector` class", report);
        Assert.Contains("### Singletons", report);
        Assert.Contains("### Base types most derived from", report);
        Assert.DoesNotContain("EmbeddedAttribute", report);
    }

    [Fact]
    public void Discover_MissingReferences_ReadAsUnresolved_AndTheScanGoesOn()
    {
        var output = Path.Combine(_dir, "out");
        var result = Program.Discover(Folder("Root33.Sim"), output, Array.Empty<string>());
        Assert.True(result.Types > 50);
        Assert.True(result.Unresolved > 0);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types.json")));
        Assert.True(doc.RootElement.GetProperty("unresolved").GetProperty("<unresolved Root33.Core>").GetInt32() > 0);
        var board = TypeNamed(doc, "Root33.Sim.SimBoard");
        Assert.Contains("interfaces: <unresolved Root33.Core>", Strings(board, "errors"));
        var seats = board.GetProperty("methods").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Seats");
        Assert.Equal("<unresolved Root33.Core>", seats.GetProperty("returnType").GetString());
        var sim = TypeNamed(doc, "Root33.Sim.SimGame");   // members that need nothing missing read normally
        Assert.Contains(sim.GetProperty("methods").EnumerateArray(), m => m.GetProperty("signature").GetString() == "public void Log(string text, int? only)");
        var report = File.ReadAllText(Path.Combine(output, "report.md"));
        Assert.Contains("- `<unresolved Root33.Core>`: ", report);
    }

    [Fact]
    public void Program_Run_ChecksItsArguments()
    {
        var text = new StringWriter();
        Assert.Equal(1, Program.Run(Array.Empty<string>(), text));
        Assert.Contains("usage: Root33.Discover <assemblyFolder> <outFolder>", text.ToString());
        Assert.Equal(1, Program.Run(new[] { Path.Combine(_dir, "missing"), Path.Combine(_dir, "out") }, new StringWriter()));
        Assert.Equal(1, Program.Run(new[] { "a", "b", "--bogus" }, new StringWriter()));
        var ok = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { Folder("Root33.Core"), Path.Combine(_dir, "out1") }, ok));
        Assert.Contains("Read 1 assemblies", ok.ToString());
        Assert.True(File.Exists(Path.Combine(_dir, "out1", "report.md")));
    }

    [Theory]
    [InlineData("AIPlayer", "ai player")]
    [InlineData("VPTrack", "vp track")]
    [InlineData("OnBirdsongStart", "on birdsong start")]
    [InlineData("m_handSize", "m hand size")]
    [InlineData("Clearing2Id", "clearing 2 id")]
    [InlineData("HUDTitleScreen", "hud title screen")]
    [InlineData("get_IsBot", "get is bot")]
    public void Names_Words_SplitIdentifiersIntoWords(string identifier, string words) =>
        Assert.Equal(words, string.Join(" ", Names.Words(identifier)));

    [Theory]
    [InlineData("hand", "PlayerHands", true)]
    [InlineData("hand", "InputHandler", false)]
    [InlineData("quest", "QuestDeck", true)]
    [InlineData("quest", "QuestionDialog", false)]
    [InlineData("keep", "KeepersFaction", true)]
    [InlineData("localization", "LocalizationManager", true)]
    [InlineData("localization", "LocalisedText", true)]
    [InlineData("craft", "CraftingCost", true)]
    [InlineData("ai", "AIController", true)]
    [InlineData("ai", "MainChain", false)]
    [InlineData("turn", "ReturnValue", false)]
    [InlineData("map", "Bitmap", false)]
    [InlineData("vp", "VictoryPoints", false)]
    [InlineData("victory", "VictoryPoints", true)]
    [InlineData("defend", "DefenseBonus", true)]
    [InlineData("recruiter", "RecruitAction", true)]
    public void Concept_Matches_WholeWordsAndTheirForms(string concept, string identifier, bool matches) =>
        Assert.Equal(matches, Concept.All.Single(c => c.Heading == concept).Matches(identifier));
}
