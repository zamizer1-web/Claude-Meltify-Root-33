using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Root33.Core;
using Root33.Core.Sheets;

namespace Root33.Host
{
    /// <summary>
    /// The mod's names and texts, the same in every host (the tabletop and the in-game plugin): character names by spoiler
    /// mode, seat titles, win titles, banner and bark lines, act banners, story and log lines, and the sheets' rule texts in
    /// plain words. Everything here is read from the sheets (characters.json, engines.json, win_conditions.json,
    /// rule_tweaks.json, acts.json, story_events.json, barks.json, map_autumn.json, tuning.json).
    /// </summary>
    public static class ModText
    {
        /// <summary>The base-game characters, in the order the select screen lists them (Marquise, Eyrie, Alliance, Vagabonds).</summary>
        public static readonly string[] BaseCharacters = { "gustave", "paintress", "sciel", "esquie", "verso", "maelle" };

        /// <summary>Characters whose finished-mode name stays hidden until a story event reveals it (characters.json notes).</summary>
        private static readonly HashSet<string> RevealedLater = new() { "maelle", "curator" };

        // ------------------------------------------------------------------ seats and characters

        /// <summary>How a seat is named everywhere: the title Root 33 gave it (do.set-seat-title), else the character's name.</summary>
        public static string SeatTitle(IModHost host, SeatId seat) => SeatTitle(host.TitleOf(seat), host.CharacterOf(seat), host.Spoilers);

        /// <summary>The same from its parts: a non-empty <paramref name="title"/> wins, else the character's name.</summary>
        public static string SeatTitle(string? title, string? characterId, SpoilerMode spoilers) =>
            !string.IsNullOrEmpty(title) ? title! : CharacterName(characterId, spoilers);

        /// <summary>The standard way to name a seat in running text: "seat 2 (The Paintress)" (seats shown 1-based).</summary>
        public static string SeatLabel(SeatId seat, string title) => $"seat {seat.Value + 1} ({title})";

        /// <summary>A character's name on the select screen and before the core names the seat.</summary>
        public static string CharacterName(string? characterId, SpoilerMode spoilers)
        {
            if (characterId == null) return "";
            if (!Data.Characters.TryGetValue(characterId, out var row)) return characterId;
            return spoilers == SpoilerMode.Finished && !RevealedLater.Contains(characterId) ? row.FinishedName : row.SafeName;
        }

        /// <summary>Root's printed name for an engine (engines.json rootName: "Marquise de Cat").</summary>
        public static string EngineName(string engineId) =>
            Data.Engines.TryGetValue(engineId, out var e) ? e.RootName : engineId;

        /// <summary>Root's printed name for the engine a character rides ("gustave" gives "Marquise de Cat").</summary>
        public static string CharacterEngineName(string characterId) =>
            Data.Characters.TryGetValue(characterId, out var c) ? EngineName(c.Engine) : characterId;

        /// <summary>A clearing on Autumn as players read it: "Lumière [c1, fox]" (Continent name, number, suit). A number with
        /// no map_autumn row (another map) reads "c7".</summary>
        public static string ClearingName(ClearingId c)
        {
            var id = "autumn." + c.Value.ToString("00", CultureInfo.InvariantCulture);
            return Data.MapAutumn.TryGetValue(id, out var row) ? $"{row.ContinentName} [c{c.Value}, {row.Suit}]" : "c" + c.Value;
        }

        // ------------------------------------------------------------------ wins, barks, abilities

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
        public static string BannerAnnounced(IModHost host, SeatId seat, string winId)
        {
            var counter = Data.WinConditions.TryGetValue(winId, out var w) ? Plain(w.Counterplay) : "";
            return $"COUNTDOWN! {host.Who(seat)} announces {WinTitle(winId)}. They win at the start of their next Birdsong " +
                   $"if it still holds.{(counter.Length > 0 ? " To stop it: " + counter : "")}";
        }

        /// <summary>do.clear-banner while the game goes on.</summary>
        public static string BannerCleared(IModHost host, SeatId seat, string? winId) =>
            $"the countdown banner of {host.Who(seat)} ({WinTitle(winId)}) comes down.";

        /// <summary>do.grant-ability.</summary>
        public static string AbilityGained(IModHost host, SeatId seat, string ability) =>
            $"{host.Who(seat)} gains a new power: {AbilityText(ability)}.";

        /// <summary>A speech bubble as a log line: seat 2 (The Paintress) says: "...".</summary>
        public static string BubbleLine(string who, string barkId) => $"{who} says: \"{BarkText(barkId)}\"";

        /// <summary>The game-over line of a themed win: the first winner, its win, and its coalition partner if any.</summary>
        public static string WinLine(IModHost host, IReadOnlyList<SeatId> winners, string? winId)
        {
            if (winners.Count == 0) return "";
            var seat = winners[0];
            var partners = winners.Skip(1).Select(host.Who).ToList();
            return $"{host.Who(seat)} wins with {WinTitle(winId)}, the Root 33 themed win of {SeatTitle(host, seat)}" +
                   (partners.Count > 0 ? $" (with {string.Join(" and ", partners)} in coalition)" : "");
        }

        // ------------------------------------------------------------------ acts and story

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

        /// <summary>The act banner as one log line: "=== Root 33 · Act II: The Monolith === ...".</summary>
        public static string ActBannerLine(int act) => $"=== Root 33 · {ActTitle(act)} === {ActLine(act)}";

        public static string StoryText(string eventId) =>
            Data.StoryEvents.TryGetValue(eventId, out var e) ? Plain(e.What) : eventId;

        /// <summary>A story event as one log line: "Root 33 story (Act I: The Landing): ...".</summary>
        public static string StoryLine(int act, string eventId) => $"Root 33 story ({ActTitle(act)}): {StoryText(eventId)}";

        /// <summary>A core warning for the player (GameContext.Warnings) as one log line.</summary>
        public static string NoteLine(string warning) => "(Root 33 note: " + warning + ")";

        /// <summary>The first exception the core threw in a hook, as one log line (the game goes on without that rule).</summary>
        public static string CoreErrorLine(string hookId, string message) =>
            $"(Root 33 core error in {hookId}: {message}; the game goes on without it.)";

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
            if (text == null || string.IsNullOrWhiteSpace(text)) return "";
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
                    sen = sen.Substring(0, colon) + ".";
                }
                kept.Add(sen.Replace("GuardsLeft", "the number of guards left"));
            }
            return string.Join(" ", kept);
        }
    }
}
