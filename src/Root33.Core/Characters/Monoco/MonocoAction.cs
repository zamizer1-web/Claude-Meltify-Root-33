using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.Monoco
{
    /// <summary>
    /// One legal Lord of the Hundreds action key from read.legal-actions, parsed. Root's own key format is still unknown
    /// (read.legal-actions is unverified until the creator's PC); like the other bots this assumes "verb:args" keys:
    /// "battle:clearing:seat", "advance:from:to" or "advance:to" (the Warlord's move), "command:from:to" or "move:from:to"
    /// (a Command move of rats), "mood:cardId", "build:clearing[:type]" (an Arena is a stronghold). A clearing is written
    /// "5", "autumn.05" or "c5". Keys that do not parse keep their verb and the bot leaves them to Root's own AI.
    /// </summary>
    public sealed class MonocoAction
    {
        public const string Battle = "battle";
        public const string Advance = "advance";
        public const string Command = "command";
        public const string Move = "move";
        public const string Mood = "mood";
        public const string Build = "build";

        public string Key { get; private set; } = "";
        public string Verb { get; private set; } = "";
        /// <summary>The clearing battled or built in.</summary>
        public ClearingId? Clearing { get; private set; }
        /// <summary>The defender of a battle.</summary>
        public SeatId? Target { get; private set; }
        /// <summary>A move's origin (null when the key names only the destination).</summary>
        public ClearingId? From { get; private set; }
        public ClearingId? To { get; private set; }
        /// <summary>A mood's card id, or a build's piece type (null when the key names none).</summary>
        public string Arg { get; private set; }

        public bool IsCommandMove => Verb == Command || Verb == Move;
        public bool IsAdvance => Verb == Advance;

        public static MonocoAction Parse(string key)
        {
            var a = new MonocoAction { Key = key ?? "" };
            if (string.IsNullOrEmpty(key)) return a;
            var parts = key.Split(':').Select(p => p.Trim()).ToArray();
            a.Verb = parts[0].ToLowerInvariant();
            switch (a.Verb)
            {
                case Mood:
                    a.Arg = parts.Length > 1 ? string.Join(":", parts.Skip(1)) : null;
                    break;
                case Advance:
                case Command:
                case Move:
                    if (parts.Length >= 3) { a.From = ParseClearing(parts[1]); a.To = ParseClearing(parts[2]); }
                    else if (parts.Length == 2) a.To = ParseClearing(parts[1]);
                    break;
                case Battle:
                    if (parts.Length > 1) a.Clearing = ParseClearing(parts[1]);
                    if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) a.Target = new SeatId(s);
                    break;
                case Build:
                    if (parts.Length > 1) a.Clearing = ParseClearing(parts[1]);
                    if (parts.Length > 2) a.Arg = parts[2];
                    break;
            }
            return a;
        }

        public static List<MonocoAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        private static ClearingId? ParseClearing(string s)
        {
            s = (s ?? "").Trim();
            var digits = new string(s.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? new ClearingId(n) : (ClearingId?)null;
        }

        /// <summary>Whether a build key builds an Arena (a stronghold, or a key that names no type).</summary>
        public bool BuildsArena => Verb == Build && (Arg == null || string.Equals(Arg, PieceTypes.Stronghold, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => Key;
    }
}
