using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.Sciel
{
    /// <summary>
    /// One legal action key from read.legal-actions, parsed. Root's own key format is still unknown (read.legal-actions is
    /// _unverified until the creator's PC); this assumes "verb:clearing" keys, with "battle:clearing[:defenderSeat]" and
    /// "move:from:to[:count]". Clearings may be written as a number ("5"), a map id ("autumn.05") or "c5".
    /// Keys that do not parse keep their verb and no clearing, and the bot leaves them to Root's own AI.
    /// </summary>
    public sealed class ScielAction
    {
        public const string Spread = "spread";
        public const string Revolt = "revolt";
        public const string Organize = "organize";
        public const string Recruit = "recruit";
        public const string Battle = "battle";
        public const string Move = "move";

        public string Key { get; private set; }
        public string Verb { get; private set; }
        /// <summary>The clearing acted in (for a move: the clearing moved from).</summary>
        public ClearingId? Clearing { get; private set; }
        /// <summary>A move's destination.</summary>
        public ClearingId? To { get; private set; }
        /// <summary>A battle's defender, when the key names one.</summary>
        public SeatId? Defender { get; private set; }

        public bool Is(string verb, ClearingId c) => Verb == verb && Clearing == c;

        public static ScielAction Parse(string key)
        {
            var a = new ScielAction { Key = key, Verb = "" };
            if (string.IsNullOrEmpty(key)) return a;
            var parts = key.Split(':');
            a.Verb = parts[0].Trim().ToLowerInvariant();
            if (parts.Length > 1) a.Clearing = ParseClearing(parts[1]);
            if (a.Verb == Move && parts.Length > 2) a.To = ParseClearing(parts[2]);
            if (a.Verb == Battle && parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
                a.Defender = new SeatId(d);
            return a;
        }

        public static List<ScielAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        private static ClearingId? ParseClearing(string s)
        {
            s = (s ?? "").Trim();
            var digits = new string(s.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? new ClearingId(n) : (ClearingId?)null;
        }

        public override string ToString() => Key;
    }
}
