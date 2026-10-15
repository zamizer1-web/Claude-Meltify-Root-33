using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.Verso
{
    /// <summary>
    /// One legal Vagabond (Ranger) action key from read.legal-actions, parsed. Root's own key format is still unknown
    /// (read.legal-actions is unverified until the creator's PC); like the other bots this assumes "verb:args" keys:
    /// "move:from:to" and "slip:to" (a place is a clearing "5" / "autumn.05" / "c5", or a forest "f3"),
    /// "battle:clearing:seat", "strike:clearing:seat", "aid:clearing:seat[:item]", "explore:clearing", "quest:clearing",
    /// "repair:item", "hideout" (the Ranger's Hideout, shown as Get Back Up; "get-back-up" and "getbackup" also parse),
    /// "rest", "pass" / "end". Keys that do not parse keep their verb and the bot leaves them to Root's own AI.
    /// </summary>
    public sealed class VersoAction
    {
        public const string Move = "move";
        public const string Slip = "slip";
        public const string Battle = "battle";
        public const string Strike = "strike";
        public const string Aid = "aid";
        public const string Explore = "explore";
        public const string Quest = "quest";
        public const string Repair = "repair";
        public const string Hideout = "hideout";
        public const string Rest = "rest";
        public const string Pass = "pass";
        public const string End = "end";

        public string Key { get; private set; } = "";
        public string Verb { get; private set; } = "";
        /// <summary>The clearing acted in, or a move's origin clearing.</summary>
        public ClearingId? Clearing { get; private set; }
        public int? FromForest { get; private set; }
        public ClearingId? To { get; private set; }
        public int? ToForest { get; private set; }
        /// <summary>The faction battled, struck or aided.</summary>
        public SeatId? Target { get; private set; }

        public bool IsMove => Verb == Move || Verb == Slip;
        public bool IsBattle => Verb == Battle;
        public bool IsHideout => Verb == Hideout;
        /// <summary>Ends his turn where he stands.</summary>
        public bool IsEnd => Verb == Pass || Verb == End || Verb == Rest;

        public static VersoAction Parse(string key)
        {
            var a = new VersoAction { Key = key ?? "" };
            if (string.IsNullOrEmpty(key)) return a;
            var parts = key.Split(':').Select(p => p.Trim()).ToArray();
            var verb = parts[0].ToLowerInvariant();
            a.Verb = verb == "get-back-up" || verb == "getbackup" ? Hideout : verb;
            if (a.IsMove)
            {
                var places = parts.Skip(1).ToList();
                if (places.Count == 0) return a;
                ParsePlace(places[places.Count - 1], out var to, out var toForest);
                a.To = to;
                a.ToForest = toForest;
                if (places.Count >= 2)
                {
                    ParsePlace(places[0], out var from, out var fromForest);
                    a.Clearing = from;
                    a.FromForest = fromForest;
                }
                return a;
            }
            if (a.Verb == Repair || a.IsHideout || a.IsEnd) return a;
            if (parts.Length > 1) a.Clearing = ParseClearing(parts[1]);
            if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) a.Target = new SeatId(s);
            return a;
        }

        public static List<VersoAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        private static void ParsePlace(string s, out ClearingId? clearing, out int? forest)
        {
            clearing = null;
            forest = null;
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("forest", StringComparison.Ordinal) || (s.StartsWith("f", StringComparison.Ordinal) && s.Length > 1 && char.IsDigit(s[1])))
            {
                var digits = new string(s.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f)) forest = f;
                return;
            }
            clearing = ParseClearing(s);
        }

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
