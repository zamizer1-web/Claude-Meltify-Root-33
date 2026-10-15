using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.WhiteHairedMan
{
    /// <summary>
    /// One legal Corvid action key from read.legal-actions, parsed. Root's own key format is still unknown (read.legal-actions
    /// is unverified until the creator's PC); like the other bots this assumes "verb:args" keys that the plugin maps Root's
    /// keys onto: "recruit:suit" (Corvid Recruit: spend a card of that suit, a warrior in every clearing of it),
    /// "plot:clearing:type" (place a face-down plot: bomb, snare, extortion or raid), "flip:clearing" (flip his face-down plot
    /// face up), "trick:clearing:clearing", "move:from:to:count", "battle:clearing:seat". A clearing is written "5",
    /// "autumn.05" or "c5". Keys that do not parse keep their verb and the bot leaves them to Root's own AI.
    /// </summary>
    public sealed class WhiteHairedManAction
    {
        public const string Recruit = "recruit";
        public const string Plot = "plot";
        public const string Flip = "flip";
        public const string Trick = "trick";
        public const string Move = "move";
        public const string Battle = "battle";

        public string Key { get; private set; } = "";
        public string Verb { get; private set; } = "";
        /// <summary>The clearing plotted, flipped or battled in.</summary>
        public ClearingId? Clearing { get; private set; }
        /// <summary>The defender of a battle.</summary>
        public SeatId? Target { get; private set; }
        public ClearingId? From { get; private set; }
        public ClearingId? To { get; private set; }
        /// <summary>Warriors moved (1 when the key names none).</summary>
        public int Count { get; private set; } = 1;
        /// <summary>The plot type of a plot key, lower case.</summary>
        public string PlotType { get; private set; }
        /// <summary>The suit of a recruit key.</summary>
        public Suit? CardSuit { get; private set; }

        public static WhiteHairedManAction Parse(string key)
        {
            var a = new WhiteHairedManAction { Key = key ?? "" };
            if (string.IsNullOrEmpty(key)) return a;
            var p = key.Split(':').Select(s => s.Trim()).ToArray();
            a.Verb = p[0].ToLowerInvariant();
            switch (a.Verb)
            {
                case Recruit:
                    if (p.Length > 1 && Enum.TryParse<Suit>(p[1], true, out var suit)) a.CardSuit = suit;
                    break;
                case Plot:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2) a.PlotType = p[2].ToLowerInvariant();
                    break;
                case Flip:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    break;
                case Trick:
                    if (p.Length > 2) { a.From = ParseClearing(p[1]); a.To = ParseClearing(p[2]); }
                    break;
                case Move:
                    if (p.Length > 2) { a.From = ParseClearing(p[1]); a.To = ParseClearing(p[2]); }
                    if (p.Length > 3 && TryInt(p[3], out var n)) a.Count = n;
                    break;
                case Battle:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2 && TryInt(p[2], out var s)) a.Target = new SeatId(s);
                    break;
            }
            return a;
        }

        public static List<WhiteHairedManAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        public bool IsMove => Verb == Move && From.HasValue && To.HasValue;
        public bool IsBattle => Verb == Battle && Clearing.HasValue && Target.HasValue;
        public bool IsPlot => Verb == Plot && Clearing.HasValue && PlotType != null;
        public bool IsFlip => Verb == Flip && Clearing.HasValue;
        public bool IsRecruit => Verb == Recruit && CardSuit.HasValue;

        // ------------------------------------------------------------ formatting (the keys the director answers with)

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string SuitName(Suit s) => s.ToString().ToLowerInvariant();
        public static string RecruitKey(Suit s) => Recruit + ":" + SuitName(s);
        public static string PlotKey(ClearingId c, string type) => Plot + ":" + N(c.Value) + ":" + type;
        public static string FlipKey(ClearingId c) => Flip + ":" + N(c.Value);
        public static string BattleKey(ClearingId c, SeatId defender) => Battle + ":" + N(c.Value) + ":" + N(defender.Value);
        public static string MoveKey(ClearingId from, ClearingId to, int count) => Move + ":" + N(from.Value) + ":" + N(to.Value) + ":" + N(count);

        private static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);

        private static ClearingId? ParseClearing(string s)
        {
            s = (s ?? "").Trim();
            var digits = new string(s.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && TryInt(digits, out var n) ? new ClearingId(n) : (ClearingId?)null;
        }

        public override string ToString() => Key;
    }
}
