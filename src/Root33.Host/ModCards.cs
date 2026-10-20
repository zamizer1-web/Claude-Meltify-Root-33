using System.Collections.Generic;
using System.Linq;
using System.Text;
using Root33.Core;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Host
{
    /// <summary>
    /// The character cards as players read them, the same in every host: the select screen's cards (name, pitch, how the
    /// character wins, which Root faction it plays) and one seat's card (its themed win, how the table stops it, and its
    /// Root 33 rule changes in plain words). Read from characters.json, engines.json, win_conditions.json and rule_tweaks.json.
    /// </summary>
    public static class ModCards
    {
        /// <summary>The spoiler mode's name as players pick it ("safe", "finished").</summary>
        public static string ModeName(SpoilerMode mode) => mode == SpoilerMode.Finished ? "finished" : "safe";

        /// <summary>The character select screen as text (works without a game): every character of
        /// <paramref name="characterIds"/> that the mode lets players pick, in that order. Ends with a line break.</summary>
        public static string SelectCards(SpoilerMode mode, IEnumerable<string> characterIds)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ROOT 33 · choose your characters (spoiler mode: {ModeName(mode)}). Each character plays a base-game Root faction.");
            foreach (var id in characterIds)
            {
                if (!Data.Characters.TryGetValue(id, out var c)) continue;
                if (mode != SpoilerMode.Finished && !c.SafeModeSelectable) continue;
                sb.AppendLine();
                sb.AppendLine($"[{id}] {ModText.CharacterName(id, mode)} · plays the {ModText.EngineName(c.Engine)}");
                sb.AppendLine("  " + c.SummarySafe);
                if (Data.WinConditions.TryGetValue(c.WinCondition, out var w))
                {
                    var kind = w.Kind == "quest" ? "quest win" : "countdown win";
                    sb.AppendLine($"  How they win: {w.Title} ({kind}). {ModText.Plain(w.PlayerText)}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("Every character can also win the Root way: 30 VP, or a dominance card.");
            return sb.ToString();
        }

        /// <summary>The seat's character card: who it is, its themed win, and its Root 33 rule changes in plain words
        /// (v1.1 rules only while this game runs them). Empty for a seat without a character.</summary>
        public static string CharacterCard(GameContext ctx, IModHost host, SeatId seat)
        {
            var id = host.CharacterOf(seat);
            if (id == null || !Data.Characters.TryGetValue(id, out var c)) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"ROOT 33 CHARACTER: {ModText.SeatTitle(host, seat)}, playing the {ModText.EngineName(c.Engine)}.");
            sb.AppendLine("  " + c.SummarySafe);
            if (Data.WinConditions.TryGetValue(c.WinCondition, out var w))
            {
                sb.AppendLine($"THEMED WIN · {w.Title}: {ModText.Plain(w.PlayerText)}");
                var counter = ModText.Plain(w.Counterplay);
                if (counter.Length > 0) sb.AppendLine($"  How the table stops it: {counter}");
            }
            sb.AppendLine("You can also win the Root way: 30 VP, or a dominance card.");
            var tweaks = Data.RuleTweaks.Values.Where(t => t.Character == id && RuleOn(ctx, t.Id)).ToList();
            if (tweaks.Count > 0)
            {
                sb.AppendLine("ROOT 33 RULE CHANGES:");
                foreach (var t in tweaks) sb.AppendLine($"  - {t.Name}: {ModText.Plain(t.Rule)}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Whether a rule_tweaks row acts: the game's own answer in an E33 game, else "v1 rows only" (the first
        /// release runs no v1.1 row).</summary>
        public static bool RuleOn(GameContext ctx, string tweakId)
        {
            try { return ctx.E33Mode ? ctx.RuleOn(tweakId) : Data.RuleTweaks[tweakId].Version != "v1.1"; }
            catch { return Data.RuleTweaks[tweakId].Version != "v1.1"; }
        }
    }
}
