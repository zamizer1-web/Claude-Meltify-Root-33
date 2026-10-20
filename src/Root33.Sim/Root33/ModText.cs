using Root33.Core;
using H = Root33.Host;

namespace Root33.Sim;

/// <summary>
/// The tabletop's entry to the mod's names and texts. The texts themselves live in Root33.Host.ModText, shared with the
/// in-game plugin; this class only adds the tabletop's own arguments (a SimGame and 0-based seat numbers, spoiler mode as
/// text) and keeps the names the sim and its tests already use.
/// </summary>
public static class ModText
{
    /// <summary>The base-game characters, in the order the select screen lists them (Marquise, Eyrie, Alliance, Vagabonds).</summary>
    public static string[] BaseCharacters => H.ModText.BaseCharacters;

    /// <summary>How a seat is named everywhere: the title Root 33 gave it (do.set-seat-title), else the character's name.</summary>
    public static string SeatTitle(SimGame g, int seat) =>
        H.ModText.SeatTitle(g.State.Mod.SeatTitles.TryGetValue(seat, out var t) ? t : null, g.Seat(seat).Character,
            SimHost.SpoilersOf(g.State.Spoiler));

    /// <summary>A character's name on the select screen and before the core names the seat.</summary>
    public static string CharacterName(string characterId, string spoiler) => H.ModText.CharacterName(characterId, SimHost.SpoilersOf(spoiler));

    public static string WinTitle(string? winId) => H.ModText.WinTitle(winId);
    public static string BarkText(string barkId) => H.ModText.BarkText(barkId);
    public static string AbilityText(string ability) => H.ModText.AbilityText(ability);

    /// <summary>The countdown banner (ui.global.countdown-banner): who, which win, when it takes effect, how to stop it.</summary>
    public static string BannerAnnounced(SimGame g, int seat, string winId) => H.ModText.BannerAnnounced(new SimHost(g), new SeatId(seat), winId);

    public static string Roman(int n) => H.ModText.Roman(n);
    /// <summary>"Act II: The Monolith".</summary>
    public static string ActTitle(int act) => H.ModText.ActTitle(act);
    /// <summary>The act banner's line (ui.global.act-banner): what unlocks now and when the next act begins.</summary>
    public static string ActLine(int act) => H.ModText.ActLine(act);
    public static string StoryText(string eventId) => H.ModText.StoryText(eventId);
    /// <summary>Root's printed name for a character's engine ("Marquise de Cat").</summary>
    public static string EngineName(string characterId) => H.ModText.CharacterEngineName(characterId);
    /// <summary>A tuning value as text; rank tunings (Verso) as their letter.</summary>
    public static string TuneValue(string tuneId) => H.ModText.TuneValue(tuneId);
    /// <summary>A sheet's rule text as a player reads it (H.ModText.Plain).</summary>
    public static string Plain(string? text) => H.ModText.Plain(text);
}
