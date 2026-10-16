using System.Globalization;
using System.Text.RegularExpressions;
using Root33.Core;
using Root33.Core.Sheets;

namespace Root33.Sim;

/// <summary>
/// The mod's names and texts for the tabletop (owner: engineer D): character names by spoiler mode, seat titles, win
/// titles, banner and bark lines, act banners, and the sheets' rule texts in plain words. Everything here is read from
/// the sheets (characters.json, win_conditions.json, rule_tweaks.json, acts.json, story_events.json, barks.json).
/// </summary>
public static class ModText
{
    /// <summary>The base-game characters, in the order the select screen lists them (Marquise, Eyrie, Alliance, Vagabonds).</summary>
    public static readonly string[] BaseCharacters = { "gustave", "paintress", "sciel", "esquie", "verso", "maelle" };

    /// <summary>Characters whose finished-mode name stays hidden until a story event reveals it (characters.json notes).</summary>
    private static readonly HashSet<string> RevealedLater = new() { "maelle", "curator" };

    /// <summary>How a seat is named everywhere: the title Root 33 gave it (do.set-seat-title), else the character's name.</summary>
    public static string SeatTitle(SimGame g, int seat)
    {
        if (g.State.Mod.SeatTitles.TryGetValue(seat, out var t) && !string.IsNullOrEmpty(t)) return t;
        return CharacterName(g.Seat(seat).Character, g.State.Spoiler);
    }

    /// <summary>A character's name on the select screen and before the core names the seat.</summary>
    public static string CharacterName(string characterId, string spoiler)
    {
        if (!Data.Characters.TryGetValue(characterId, out var row)) return characterId;
        return spoiler == "finished" && !RevealedLater.Contains(characterId) ? row.FinishedName : row.SafeName;
    }

    public static string WinTitle(string? winId) =>
        winId != null && Data.WinConditions.TryGetValue(winId, out var w) ? w.Title : winId ?? "a themed win";

    public static string BarkText(string barkId) =>
        Data.Barks.TryGetValue(barkId, out var b) ? b.Text : barkId;

    public static string AbilityText(string ability) => ability switch
    {
        "rivers-as-paths" => "rivers count as paths for its moves",
        "no-hostile-boot" => "no extra boot to enter a Hostile faction's clearing",
        "glide" => "once per turn, exhaust a torch to move to any clearing",
        _ => ability,
    };

    /// <summary>The countdown banner (ui.global.countdown-banner): who, which win, when it takes effect, how to stop it.</summary>
    public static string BannerAnnounced(SimGame g, int seat, string winId)
    {
        var counter = Data.WinConditions.TryGetValue(winId, out var w) ? Plain(w.Counterplay) : "";
        return $"COUNTDOWN! {Render.Who(g, seat)} announces {WinTitle(winId)}. They win at the start of their next Birdsong " +
               $"if it still holds.{(counter.Length > 0 ? " To stop it: " + counter : "")}";
    }

    public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", _ => n.ToString(CultureInfo.InvariantCulture) };

    /// <summary>"Act II: The Monolith".</summary>
    public static string ActTitle(int act)
    {
        var row = Data.Acts.Values.FirstOrDefault(a => a.Number == act);
        return $"Act {Roman(act)}" + (row != null ? ": " + row.Name : "");
    }

    /// <summary>The act banner's line (ui.global.act-banner): what unlocks now and when the next act begins.</summary>
    public static string ActLine(int act)
    {
        var row = Data.Acts.Values.FirstOrDefault(a => a.Number == act);
        var unlock = act switch
        {
            1 => "Themed wins are locked until Act II.",
            2 => "Themed wins unlock: with 10+ VP a character's countdown banner can go up at the end of their turn, and quests count.",
            3 => "Lumière is the final battlefield.",
            _ => "",
        };
        var next = row != null && (row.EndsAtVp > 0 || row.EndsAfterRound > 0)
            ? $" {ActTitle(act + 1)} begins at the end of a turn once anyone has {row.EndsAtVp}+ VP, or after round {row.EndsAfterRound}."
            : "";
        return (unlock + next).Trim();
    }

    public static string StoryText(string eventId) =>
        Data.StoryEvents.TryGetValue(eventId, out var e) ? Plain(e.What) : eventId;

    /// <summary>Root's printed name for a character's engine ("Marquise de Cat").</summary>
    public static string EngineName(string characterId) =>
        Data.Characters.TryGetValue(characterId, out var c) ? FactionRegistry.EngineName(c.Engine) : characterId;

    // ------------------------------------------------------------------ plain words

    private static readonly Regex TuneToken = new(@"tune\.[a-z0-9-]+", RegexOptions.Compiled);
    private static readonly Regex AutumnId = new(@"autumn\.0?(\d+)", RegexOptions.Compiled);
    private static readonly Regex Parenthetical = new(@"\s*\(([^()]*)\)", RegexOptions.Compiled);
    private static readonly Regex ValueOfMax = new(@"\b(\d+) \((\d+/\d+)\)", RegexOptions.Compiled);
    private static readonly Regex Sentence = new(@"(?<=[.!?])\s+(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>A tuning value as text; rank tunings (Verso) as their letter.</summary>
    public static string TuneValue(string tuneId)
    {
        if (!Data.Tuning.TryGetValue(tuneId, out var t)) return tuneId;
        var v = t.Value;
        if (tuneId.Contains("rank") && v >= 0 && v <= 4 && v == Math.Floor(v)) return "DCBAS"[(int)v].ToString();
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A sheet's rule text as a player reads it: tuning ids become their values, map ids become clearing numbers, and the
    /// notes meant for the plugin builder (engine patches, the AI and human UI, fallbacks, other maps, DLC) are left out.
    /// </summary>
    public static string Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = TuneToken.Replace(text, m => TuneValue(m.Value));
        s = AutumnId.Replace(s, m => "clearing " + m.Groups[1].Value);
        s = ValueOfMax.Replace(s, "$2");
        s = Parenthetical.Replace(s, m =>
        {
            var inner = m.Groups[1].Value;
            return inner.Contains("UI") || inner.Contains("DLC") || inner.Contains("hook.") || inner.Contains("do.") ? "" : m.Value;
        });
        var kept = new List<string>();
        foreach (var raw in Sentence.Split(s))
        {
            var sen = raw.Trim();
            if (sen.Length == 0) continue;
            if (sen.Contains("fallback") || sen.StartsWith("On other maps") || sen.Contains("Applies to any host")) continue;
            if (sen.Contains("patch"))
            {
                var colon = sen.IndexOf(':');
                if (colon <= 0) continue;
                sen = sen[..colon] + ".";
            }
            kept.Add(sen.Replace("GuardsLeft", "the number of guards left"));
        }
        return string.Join(" ", kept);
    }
}
