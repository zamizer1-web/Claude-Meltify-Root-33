using System.Text;

namespace Root33.Sim;

/// <summary>
/// The tabletop's command line. Seats are numbered 1-4 in the order given to --seats (clockwise); the first player is
/// chosen by the seed. Every status, view and do ends with one STATUS or GAME OVER line. Owner: engine (lead).
/// </summary>
public static class Program
{
    public static readonly string[] BaseCharacters = { "gustave", "paintress", "sciel", "esquie", "verso", "maelle" };
    public static readonly string[] VagabondCharacters = { "esquie", "verso", "maelle" };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return Run(args, Console.Out);
    }

    public const string Usage =
        "usage:\n" +
        "  Root33.Sim new --save F --seed N --spoiler safe|finished --seats \"gustave:human,paintress:bot,sciel:human,esquie:human\"\n" +
        "  Root33.Sim select --spoiler safe|finished\n" +
        "  Root33.Sim status  --save F\n" +
        "  Root33.Sim view    --save F --seat N\n" +
        "  Root33.Sim actions --save F --seat N\n" +
        "  Root33.Sim do      --save F --seat N <number-or-key>\n" +
        "  Root33.Sim log     --save F [--since K] [--seat N]\n" +
        "  Root33.Sim rules   --save F --seat N\n" +
        "  Root33.Sim wait    --save F --seat N [--until-round R] [--timeout S]   (blocks until seat N must decide)\n" +
        "Characters: gustave=Marquise, paintress=Eyrie, sciel=Woodland Alliance, esquie=Vagabond Tinker,\n" +
        "            verso=Vagabond Ranger, maelle=Vagabond Thief. --save may also come from $ROOT33_SAVE.";

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0) { output.WriteLine(Usage); return 1; }
        var opts = ParseOptions(args.Skip(1).ToArray(), out var positional);
        try
        {
            switch (args[0])
            {
                case "new": return CmdNew(opts, output);
                case "select": return CmdSelect(opts, output);
                case "status": return WithGame(opts, false, g => { output.WriteLine(Status(g)); });
                case "view": return WithGame(opts, false, g => { output.WriteLine(Render.View(g, SeatArg(g, opts))); output.WriteLine(Render.StatusLine(g)); });
                case "actions": return WithGame(opts, false, g => output.WriteLine(Actions(g, SeatArg(g, opts))));
                case "do": return WithGame(opts, true, g => output.WriteLine(Do(g, SeatArg(g, opts), positional)));
                case "log": return WithGame(opts, false, g => output.WriteLine(LogText(g, opts)));
                case "rules": return WithGame(opts, false, g => output.WriteLine(RulesText(g, SeatArg(g, opts))));
                case "wait": return CmdWait(opts, output);
                case "help": case "--help": case "-h": output.WriteLine(Usage); return 0;
                default: output.WriteLine(Usage); return 1;
            }
        }
        catch (SimUsageException e) { output.WriteLine("error: " + e.Message); return 1; }
    }

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
            if (!BaseCharacters.Contains(ch)) throw new SimUsageException($"unknown or non-base character '{ch}' (base: {string.Join(", ", BaseCharacters)})");
            if (kind != "human" && kind != "bot") throw new SimUsageException($"seat kind must be human or bot, got '{kind}'");
            seats.Add(new SeatSpec(ch, kind == "human"));
        }
        if (seats.Count < 2 || seats.Count > 4) throw new SimUsageException("2 to 4 seats required");
        foreach (var c in BaseCharacters)
            if (seats.Count(s => s.Character == c) > 1) throw new SimUsageException($"'{c}' taken twice");
        if (seats.Count(s => VagabondCharacters.Contains(s.Character)) > 2) throw new SimUsageException("at most two Vagabonds");
        return seats;
    }

    static string SavePath(Dictionary<string, string> o) =>
        o.TryGetValue("save", out var s) ? s : Environment.GetEnvironmentVariable("ROOT33_SAVE") ?? throw new SimUsageException("--save required");

    static int SeatArg(SimGame g, Dictionary<string, string> o)
    {
        if (!o.TryGetValue("seat", out var s) || !int.TryParse(s, out var n)) throw new SimUsageException("--seat N required (1-" + g.SeatCount + ")");
        if (n < 1 || n > g.SeatCount) throw new SimUsageException($"--seat must be 1-{g.SeatCount}");
        return n - 1;
    }

    /// <summary>Load the save under an exclusive lock (players may run commands at the same time), run, save if changed.</summary>
    static int WithGame(Dictionary<string, string> o, bool write, Action<SimGame> act)
    {
        var path = SavePath(o);
        if (!File.Exists(path)) throw new SimUsageException($"no game at {path} (start one with 'new')");
        using var lk = Lock(path);
        var g = SimGame.Load(path);
        act(g);
        if (write) { g.Save(path); WriteLive(g, path); }
        return 0;
    }

    /// <summary>The live watch page next to the save (game1.json -> game1.live.md): the STATUS line, the spectator view
    /// and the last 40 public log entries. Public information only, so anyone can watch without seeing a hand.</summary>
    internal static void WriteLive(SimGame g, string savePath)
    {
        try
        {
            var live = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? ".", Path.GetFileNameWithoutExtension(savePath) + ".live.md");
            var sb = new StringBuilder();
            sb.AppendLine("# Root 33 playtest · live");
            sb.AppendLine();
            sb.AppendLine($"Updated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC after every move. Reopen this file to see the latest.");
            sb.AppendLine();
            sb.AppendLine("**" + Render.StatusLine(g).Replace("*", "") + "**");
            sb.AppendLine();
            sb.AppendLine("## Recent moves (public log)");
            sb.AppendLine("```text");
            foreach (var e in g.LogFor(null, Math.Max(0, g.State.Log.Count - 40))) sb.AppendLine($"{e.N,5} [r{e.Round}] {e.Text}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Board, scores and the Root 33 trackers");
            sb.AppendLine("```text");
            sb.AppendLine(Render.Spectator(g));
            sb.AppendLine("```");
            var tmp = live + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, live, overwrite: true);
        }
        catch (IOException) { /* the watch page is a convenience; never fail a move over it */ }
    }

    /// <summary>
    /// wait --seat N [--until-round R] [--timeout S]: blocks until seat N must decide, the game is over, or (with
    /// --until-round) the game has moved past round R; gives up after S seconds (default 540) so a player can wait again.
    /// Lets several players run at once, each waiting for its own turn. Prints one first word: YOUR-TURN, GAME-OVER,
    /// PAUSE or STILL-WAITING, then the STATUS line.
    /// </summary>
    static int CmdWait(Dictionary<string, string> o, TextWriter output)
    {
        var path = SavePath(o);
        if (!File.Exists(path)) throw new SimUsageException($"no game at {path} (start one with 'new')");
        if (!o.TryGetValue("seat", out var ss) || !int.TryParse(ss, out var seatNo) || seatNo < 1) throw new SimUsageException("--seat N required");
        int untilRound = o.TryGetValue("until-round", out var ur) && int.TryParse(ur, out var r) ? r : int.MaxValue;
        int timeout = o.TryGetValue("timeout", out var ts) && int.TryParse(ts, out var t) ? t : 540;
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        DateTime lastWrite = DateTime.MinValue;
        string status = "";
        while (true)
        {
            var w = File.GetLastWriteTimeUtc(path);
            if (w != lastWrite)
            {
                lastWrite = w;
                SimGame g;
                using (Lock(path)) g = SimGame.Load(path);
                if (seatNo > g.SeatCount) throw new SimUsageException($"--seat must be 1-{g.SeatCount}");
                status = Render.StatusLine(g);
                if (g.State.GameOver) { output.WriteLine("GAME-OVER"); output.WriteLine(status); return 0; }
                if (g.State.SetupDone && g.State.Round > untilRound) { output.WriteLine($"PAUSE: round {untilRound} is over; stop here for now."); output.WriteLine(status); return 0; }
                if (g.Pending is Decision d && d.Seat == seatNo - 1) { output.WriteLine("YOUR-TURN"); output.WriteLine(status); return 0; }
            }
            if (DateTime.UtcNow >= deadline) { output.WriteLine("STILL-WAITING (another player is deciding; run wait again)"); output.WriteLine(status); return 0; }
            Thread.Sleep(2000);
        }
    }

    static FileStream Lock(string path)
    {
        var lockPath = path + ".lock";
        for (int i = 0; ; i++)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(100); }
        }
    }

    static int CmdNew(Dictionary<string, string> o, TextWriter output)
    {
        var save = SavePath(o);
        int seed = o.TryGetValue("seed", out var s) ? int.Parse(s) : 1;
        var spoiler = o.TryGetValue("spoiler", out var sp) ? sp.ToLowerInvariant() : "safe";
        if (spoiler != "safe" && spoiler != "finished") throw new SimUsageException("--spoiler must be safe or finished");
        if (!o.TryGetValue("seats", out var seatSpec)) throw new SimUsageException("--seats required");
        var seats = ParseSeats(seatSpec);
        using var lk = Lock(save);
        var g = SimGame.New(seats.Select(x => new SimGame.SeatSpec(x.Character, x.Human)).ToList(), seed, spoiler);
        Bots.Run(g);
        g.Save(save); WriteLive(g, save);
        output.WriteLine($"New game saved to {save} (seed {seed}, spoiler {spoiler}).");
        foreach (var e in g.LogFor(null)) output.WriteLine(e.Text);
        output.WriteLine(Render.StatusLine(g));
        return 0;
    }

    static int CmdSelect(Dictionary<string, string> o, TextWriter output)
    {
        var spoiler = o.TryGetValue("spoiler", out var sp) ? sp.ToLowerInvariant() : "safe";
        if (spoiler != "safe" && spoiler != "finished") throw new SimUsageException("--spoiler must be safe or finished");
        var text = CoreBridge.SelectCards(spoiler);
        if (!string.IsNullOrWhiteSpace(text)) { output.WriteLine(text.TrimEnd()); return 0; }
        output.WriteLine("Characters for the base game (character = Root faction):");
        foreach (var c in BaseCharacters)
            output.WriteLine($"  {c,-10} {FactionRegistry.EngineName(FactionRegistry.EngineOf(c))}");
        output.WriteLine("Seat 2-4 of them, at most two Vagabonds: new --seats \"gustave:human,paintress:human,sciel:human,esquie:human\"");
        return 0;
    }

    static string Status(SimGame g)
    {
        var sb = new StringBuilder();
        for (int s = 0; s < g.SeatCount; s++)
        {
            var ss = g.Seat(s);
            sb.AppendLine($"  {Render.Who(g, s)} {FactionRegistry.EngineName(ss.Engine)}{(ss.Human ? "" : " [bot]")}: {ss.Vp} VP, {ss.Hand.Count} card{(ss.Hand.Count == 1 ? "" : "s")}" +
                          (ss.Dominance is int d ? $", {Cards.Get(d).Name} active" : ""));
        }
        sb.Append(Render.StatusLine(g));
        return sb.ToString();
    }

    static string Actions(SimGame g, int seat)
    {
        if (g.Pending is Decision d && d.Seat == seat) return Render.Options(g, d);
        return $"No decision for seat {seat + 1} now.\n" + Render.StatusLine(g);
    }

    static string Do(SimGame g, int seat, List<string> positional)
    {
        if (positional.Count == 0) throw new SimUsageException("do needs an option number or key");
        int before = g.State.Log.Count;
        g.Choose(seat, string.Join(" ", positional));
        Bots.Run(g);
        var sb = new StringBuilder();
        foreach (var e in g.LogFor(seat, before)) sb.AppendLine(e.Text);
        if (g.Pending is Decision d && d.Seat == seat) sb.AppendLine(Render.Options(g, d));
        sb.Append(Render.StatusLine(g));
        return sb.ToString();
    }

    static string LogText(SimGame g, Dictionary<string, string> o)
    {
        int since = o.TryGetValue("since", out var s) ? int.Parse(s) : 0;
        int? seat = o.TryGetValue("seat", out var se) ? int.Parse(se) - 1 : null;
        var sb = new StringBuilder();
        foreach (var e in g.LogFor(seat, since)) sb.AppendLine($"{e.N,5} [r{e.Round}] {e.Text}");
        sb.Append($"(last entry {g.State.Log.Count}; use --since {g.State.Log.Count} next time)");
        return sb.ToString();
    }

    static string RulesText(SimGame g, int seat)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Rules for {Render.Who(g, seat)}:");
        sb.AppendLine(g.Faction(seat).RulesText(g, seat).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("EVERY FACTION: craft cards with your crafting pieces; items score their VP; ambush cards deal two hits when you");
        sb.AppendLine("defend in a matching clearing; with 10+ VP you may activate a dominance card in Daylight (then you win at the");
        sb.AppendLine("start of your Birdsong by ruling three clearings of its suit, or two opposite corners for birds). 30 VP wins at once.");
        var card = g.Bridge.CharacterCard(seat);
        if (!string.IsNullOrWhiteSpace(card)) { sb.AppendLine(); sb.AppendLine(card.TrimEnd()); }
        return sb.ToString().TrimEnd();
    }
}
