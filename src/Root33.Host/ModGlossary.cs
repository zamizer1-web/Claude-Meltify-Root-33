using System.Collections.Generic;
using System.Linq;
using System.Text;
using Root33.Core;
using Root33.Core.Characters.Paintress;
using Root33.Core.Sheets;

namespace Root33.Host
{
    /// <summary>One word of the mod and what it means, in one line.</summary>
    public sealed class GlossaryEntry
    {
        public GlossaryEntry(string term, string text) { Term = term; Text = text; }
        public string Term { get; }
        public string Text { get; }
    }

    /// <summary>
    /// The mod's own words for a newcomer, one line each, the same in every host (the tabletop's 'rules' output, the plugin's
    /// panel): the act, themed wins and their banners, the Goal Tracker, the Monolith Number, speech bubbles, and each base
    /// character's terms. Written in the spoiler mode's words: names, win titles and act names come from
    /// <see cref="ModText"/>, and finished-mode extras appear only in finished mode. Numbers come from the sheets.
    /// </summary>
    public static class ModGlossary
    {
        /// <summary>Every entry, in reading order.</summary>
        public static IReadOnlyList<GlossaryEntry> Entries(SpoilerMode spoilers)
        {
            string Name(string id) => ModText.CharacterName(id, spoilers);
            string Win(string id) => ModText.WinTitle(id, spoilers);
            var act1 = Data.Acts.Values.First(a => a.Number == 1);
            var act2 = Data.Acts.Values.First(a => a.Number == 2);
            var labels = spoilers == SpoilerMode.Finished
                ? " In finished mode each one is labelled with the family member it shows: " +
                  string.Join(", ", PaintressPortraits.Labels.Select((l, i) => $"{l} ({PaintressPortraits.NeededSuits[i].ToString().ToLowerInvariant()})")) + "."
                : "";

            return new List<GlossaryEntry>
            {
                new GlossaryEntry("Act",
                    $"the game runs in three acts: {ModText.ActTitle(1, spoilers)}, {ModText.ActTitle(2, spoilers)} and {ModText.ActTitle(3, spoilers)}. " +
                    $"Act II begins at the end of a turn once anyone has {act1.EndsAtVp}+ VP or after round {act1.EndsAfterRound}; " +
                    $"Act III at {act2.EndsAtVp}+ VP or after round {act2.EndsAfterRound}. Themed wins are locked in Act I."),
                new GlossaryEntry("Themed win",
                    "each character's own extra way to win, on top of Root's 30 VP and dominance. A countdown win is warned with a " +
                    "countdown banner first; a quest win (Esquie's rocks) wins at the end of the owner's turn with no warning."),
                new GlossaryEntry("Countdown banner",
                    "the warning everyone sees when a character meets its themed test at the end of its turn. The character wins at the start of " +
                    "its next Birdsong if the test still holds, so every other player gets one turn to break it; the banner says how."),
                new GlossaryEntry("Goal Tracker",
                    "the public list in every view: each character's themed win, what still locks it (act, VP, dominance, coalition), " +
                    "how many steps are missing (ONE STEP AWAY at one) and the character's own tracker."),
                new GlossaryEntry("Monolith Number",
                    $"{Tuning.MonolithNumberStart} minus the top score, shown on the Monolith. It falls as the leader scores and reaches " +
                    $"{Tuning.MonolithNumberStart - Tuning.RootWinVp} when someone has {Tuning.RootWinVp} VP. It has no rule of its own."),
                new GlossaryEntry("Speech bubbles",
                    "short lines the characters say when something happens. Flavour only: they never change the game. Your view shows " +
                    "the ones since your last turn."),
                new GlossaryEntry("Expedition Flag",
                    $"at the end of each of {Name("gustave")}'s turns, every clearing he rules with one of his buildings gets a permanent " +
                    "flag (Lumière starts flagged). A flag counts only while he rules that clearing."),
                new GlossaryEntry("Chain and Beachhead",
                    $"{Name("gustave")}'s Chain is his ruled, flagged clearings joined to Lumière by paths; a Beachhead is a clearing " +
                    $"next to the Monolith. {Win("win.gustave")} needs the Chain to reach a Beachhead holding his building."),
                new GlossaryEntry("Gustave's Arm and Overcharge",
                    $"the Arm (0-{Tuning.GustaveArmMax}) gains a charge when {Name("gustave")} starts a battle that removes an enemy piece. " +
                    $"With the Arm full he may arm Overcharge before he attacks: +{Tuning.GustaveOverchargeHits} hit in that battle, then the Arm empties."),
                new GlossaryEntry("Living Portrait",
                    $"one of {Name("paintress")}'s roosts (easels) outside the Monolith, in a clearing she rules, with " +
                    $"{Tuning.PaintressPortraitWarriors}+ of her warriors. {Win("win.paintress")} needs one in a fox, a rabbit and a mouse clearing." + labels),
                new GlossaryEntry("Lantern", $"{Name("sciel")}'s sympathy token."),
                new GlossaryEntry("Aquafarm", $"{Name("sciel")}'s Alliance base."),
                new GlossaryEntry("Sanctuary",
                    $"a clearing with {Name("sciel")}'s Lantern and no enemy pieces at all (warriors, pawns, buildings or tokens). " +
                    $"{Win("win.sciel")} needs {Tuning.ScielSanctuaries}+ across fox, rabbit and mouse for its warning."),
                new GlossaryEntry("Esquie's rocks",
                    $"Florrie, Dorrie, Soarrie and Urrie, which {Name("esquie")} collects to win. The Goal Tracker counts the collected " +
                    "ones as 'home'; a collected rock is never lost."),
                new GlossaryEntry("Guards",
                    "another faction's warriors sitting on Florrie or Urrie. They stay put, their owner cannot place more there, and " +
                    $"anyone's battle can remove them; they are the only enemies {Name("esquie")} may attack. With none left the rock is loose."),
                new GlossaryEntry("Perfection rank",
                    $"{Name("verso")}'s public rank D, C, B, A, S (starting at D). After a battle he is in: up one if he removed an enemy " +
                    $"piece with no item damaged, down one or two if items were damaged. At A or S he deals +{Tuning.VersoPerfectionHits} hit when attacking."),
                new GlossaryEntry("Maelle's relationships",
                    $"{Name("maelle")} is a Vagabond: with each other faction she is Indifferent, Amiable, Friendly, Allied (Aid them to " +
                    $"climb) or Hostile (after she removes their warriors). {Win("win.maelle")} needs Allied with {Tuning.MaelleAllied} non-Vagabond " +
                    $"factions (or with the only one seated, and {Tuning.Maelle2pVp}+ VP) and Hostile with none."),
                new GlossaryEntry("Maelle's stances",
                    "a cosmetic chip on her card: Offensive after she battles, Defensive after she Aids or is attacked, Virtuose after " +
                    "she does both in one turn. It changes only her portrait and lines."),
            };
        }

        /// <summary>The glossary as text: a heading line, then "  Term: meaning." one per line. Ends without a line break.</summary>
        public static string Text(SpoilerMode spoilers)
        {
            var sb = new StringBuilder();
            sb.Append("ROOT 33 WORDS (the mod's own terms, spoiler mode: ").Append(ModCards.ModeName(spoilers)).Append(')');
            foreach (var e in Entries(spoilers)) sb.AppendLine().Append("  ").Append(e.Term).Append(": ").Append(e.Text);
            return sb.ToString();
        }
    }
}
