using System.Text.Json;

namespace Root33.Sim;

/// <summary>
/// Text tabletop CLI for playtesting Root 33 on top of Root33.Core.
/// STATUS: skeleton only (see docs/SIM.md "Status"). 'new' validates seats and writes the save file;
/// the other commands are stubs until the rules engine (SimGame) is written.
/// </summary>
public static class Program
{
    public static readonly string[] BaseCharacters = { "gustave", "paintress", "sciel", "esquie", "verso", "maelle" };
    public static readonly string[] VagabondCharacters = { "esquie", "verso", "maelle" };

    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0) { output.WriteLine(Usage); return 1; }
        var opts = ParseOptions(args.Skip(1).ToArray(), out var positional);
        try
        {
            switch (args[0])
            {
                case "new": return CmdNew(opts, output);
                case "select":
                case "view":
                case "actions":
                case "do":
                case "log":
                    output.WriteLine($"'{args[0]}' is not implemented yet (sim skeleton; see docs/SIM.md).");
                    return 2;
                default:
                    output.WriteLine(Usage); return 1;
            }
        }
        catch (SimUsageException e) { output.WriteLine("error: " + e.Message); return 1; }
    }

    public const string Usage =
        "usage: Root33.Sim new --save <file> --seed N --spoiler safe|finished --seats \"<char>:human,...\"\n" +
        "       Root33.Sim select|view|actions|do|log --save <file> [--seat N] [choice] [--since K]";

    internal static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length) { d[args[i][2..]] = args[i + 1]; i++; }
            else positional.Add(args[i]);
        }
        return d;
    }

    public sealed record SeatSpec(string Character, bool Human);

    public static List<SeatSpec> ParseSeats(string spec)
    {
        var seats = new List<SeatSpec>();
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':');
            var ch = parts[0].Trim().ToLowerInvariant();
            var kind = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "human";
            if (!BaseCharacters.Contains(ch)) throw new SimUsageException($"unknown or non-base character '{ch}'");
            if (kind != "human" && kind != "bot") throw new SimUsageException($"seat kind must be human or bot, got '{kind}'");
            seats.Add(new SeatSpec(ch, kind == "human"));
        }
        if (seats.Count < 2 || seats.Count > 4) throw new SimUsageException("2 to 4 seats required");
        foreach (var c in new[] { "gustave", "paintress", "sciel" })
            if (seats.Count(s => s.Character == c) > 1) throw new SimUsageException($"'{c}' taken twice");
        if (seats.Count(s => VagabondCharacters.Contains(s.Character)) > 2) throw new SimUsageException("at most two Vagabonds");
        foreach (var v in VagabondCharacters)
            if (seats.Count(s => s.Character == v) > 1) throw new SimUsageException($"'{v}' taken twice");
        return seats;
    }

    static int CmdNew(Dictionary<string, string> o, TextWriter output)
    {
        if (!o.TryGetValue("save", out var save)) throw new SimUsageException("--save required");
        int seed = o.TryGetValue("seed", out var s) ? int.Parse(s) : 1;
        var spoiler = o.TryGetValue("spoiler", out var sp) ? sp.ToLowerInvariant() : "safe";
        if (spoiler != "safe" && spoiler != "finished") throw new SimUsageException("--spoiler must be safe or finished");
        if (!o.TryGetValue("seats", out var seatSpec)) throw new SimUsageException("--seats required");
        var seats = ParseSeats(seatSpec);
        var doc = new { version = 0, seed, spoiler, seats, status = "skeleton" };
        File.WriteAllText(save, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"New game saved to {save} (seed {seed}, spoiler {spoiler}, {seats.Count} seats). Rules engine not implemented yet.");
        return 0;
    }
}

public sealed class SimUsageException : Exception
{
    public SimUsageException(string m) : base(m) { }
}
