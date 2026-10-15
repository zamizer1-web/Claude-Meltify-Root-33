using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.Clea
{
    /// <summary>
    /// One legal Lizard Cult action key from read.legal-actions, parsed. Root's own key format is still unknown
    /// (read.legal-actions is unverified until the creator's PC); like the other bots this assumes "verb:args" keys that the
    /// plugin maps Root's real keys onto:
    /// <list type="bullet">
    /// <item>"recruit:clearing:cardSuit" (Recruit ritual: the revealed card's suit, "bird" for a bird card),</item>
    /// <item>"build:clearing:cardSuit" (Build ritual: a garden, her atelier),</item>
    /// <item>"convert:clearing:seat" (Convert conspiracy against that seat's warrior),</item>
    /// <item>"crusade:clearing:seat" (Crusade conspiracy: battle that seat there),</item>
    /// <item>"move:from:to:count" (the move part of a Crusade; the Lizards have no other Move),</item>
    /// <item>"sanctify:clearing:seat[:type]" (Sanctify conspiracy against that seat's building),</item>
    /// <item>"sacrifice:bird" (Sacrifice ritual: a bird card for an acolyte),</item>
    /// <item>"home:clearing" (the setup home pick).</item>
    /// </list>
    /// A clearing is written "5", "autumn.05" or "c5". Keys that do not parse keep their verb and are left to Root's own AI.
    /// </summary>
    public sealed class CleaAction
    {
        public const string Recruit = "recruit", Build = "build", Convert = "convert", Crusade = "crusade", Move = "move",
            Sanctify = "sanctify", Sacrifice = "sacrifice", Home = "home";

        public string Key { get; private set; } = "";
        public string Verb { get; private set; } = "";
        /// <summary>The clearing of a ritual, a conspiracy or a home pick.</summary>
        public ClearingId? Clearing { get; private set; }
        /// <summary>The enemy seat a conspiracy targets.</summary>
        public SeatId? Target { get; private set; }
        /// <summary>The suit of the card a ritual reveals (Recruit, Build, Sacrifice).</summary>
        public Suit? CardSuit { get; private set; }
        public ClearingId? From { get; private set; }
        public ClearingId? To { get; private set; }
        public int Count { get; private set; }

        /// <summary>Whether the action puts one of her pieces into <see cref="Clearing"/> (Recruit, Build, Convert, Sanctify).</summary>
        public bool Places => Verb == Recruit || Verb == Build || Verb == Convert || Verb == Sanctify;

        public bool IsCrusade => Verb == Crusade || Verb == Move;

        public static CleaAction Parse(string key)
        {
            var a = new CleaAction { Key = key ?? "" };
            if (string.IsNullOrEmpty(key)) return a;
            var p = key.Split(':').Select(x => x.Trim()).ToArray();
            a.Verb = p[0].ToLowerInvariant();
            switch (a.Verb)
            {
                case Recruit:
                case Build:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2) a.CardSuit = ParseSuit(p[2]);
                    break;
                case Convert:
                case Crusade:
                case Sanctify:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2 && TryInt(p[2], out var s)) a.Target = new SeatId(s);
                    break;
                case Move:
                    if (p.Length > 2) { a.From = ParseClearing(p[1]); a.To = ParseClearing(p[2]); }
                    a.Count = p.Length > 3 && TryInt(p[3], out var n) ? n : 1;
                    break;
                case Sacrifice:
                    a.CardSuit = p.Length > 1 ? ParseSuit(p[1]) : Suit.Bird;
                    break;
                case Home:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    break;
            }
            return a;
        }

        public static List<CleaAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string SuitName(Suit s) => s.ToString().ToLowerInvariant();
        public static string RecruitKey(ClearingId c, Suit card) => Recruit + ":" + N(c.Value) + ":" + SuitName(card);
        public static string BuildKey(ClearingId c, Suit card) => Build + ":" + N(c.Value) + ":" + SuitName(card);
        public static string ConvertKey(ClearingId c, SeatId target) => Convert + ":" + N(c.Value) + ":" + N(target.Value);
        public static string CrusadeKey(ClearingId c, SeatId target) => Crusade + ":" + N(c.Value) + ":" + N(target.Value);
        public static string MoveKey(ClearingId from, ClearingId to, int count) => Move + ":" + N(from.Value) + ":" + N(to.Value) + ":" + N(count);
        public static string SanctifyKey(ClearingId c, SeatId target, string type) => Sanctify + ":" + N(c.Value) + ":" + N(target.Value) + ":" + type;
        public static string SacrificeKey => Sacrifice + ":bird";
        public static string HomeKey(ClearingId c) => Home + ":" + N(c.Value);

        private static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);

        private static ClearingId? ParseClearing(string s)
        {
            s = (s ?? "").Trim();
            var digits = new string(s.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && TryInt(digits, out var n) ? new ClearingId(n) : (ClearingId?)null;
        }

        private static Suit? ParseSuit(string s) =>
            Enum.TryParse<Suit>((s ?? "").Trim(), true, out var suit) ? suit : (Suit?)null;

        public override string ToString() => Key;
    }
}
