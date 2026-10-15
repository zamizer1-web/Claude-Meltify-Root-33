using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Root33.Core.Characters.Lune
{
    /// <summary>
    /// One legal Keepers action key from read.legal-actions, parsed. Root's own key format is still unknown (creator's PC);
    /// like the other bots this assumes "verb:args" keys: "move:from:to:count[:relic...]" (relic ids carried along),
    /// "delve:clearing:relic", "recover:clearing:relic", "retinue:column:suit" (column move | battle-delve | move-recover,
    /// suit fox | rabbit | mouse | bird), "battle:clearing:seat", "build:clearing:type" and "flip:clearing:type" (a waystation
    /// showing that relic type). Keys that do not parse keep their verb and are left to Root's own AI.
    /// </summary>
    public sealed class LuneAction
    {
        public const string Move = "move", Delve = "delve", Recover = "recover", Retinue = "retinue", Battle = "battle", Build = "build", Flip = "flip";
        public const string MoveColumn = "move", BattleDelveColumn = "battle-delve", MoveRecoverColumn = "move-recover";

        public string Key { get; private set; } = "";
        public string Verb { get; private set; } = "";
        /// <summary>The clearing acted in, or a move's origin.</summary>
        public ClearingId? Clearing { get; private set; }
        public ClearingId? To { get; private set; }
        public int Count { get; private set; }
        /// <summary>The relic delved or recovered, or the relics a move carries.</summary>
        public List<int> Relics { get; } = new List<int>();
        public SeatId? Target { get; private set; }
        public string Column { get; private set; }
        public Suit? Suit { get; private set; }
        public string RelicType { get; private set; }

        public int? Relic => Relics.Count > 0 ? Relics[0] : (int?)null;

        public static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        public static string SuitName(Suit s) => s.ToString().ToLowerInvariant();
        public static string RetinueKey(string column, Suit suit) => Retinue + ":" + column + ":" + SuitName(suit);
        public static string DelveKey(ClearingId c, int relic) => Delve + ":" + N(c.Value) + ":" + N(relic);
        public static string RecoverKey(ClearingId c, int relic) => Recover + ":" + N(c.Value) + ":" + N(relic);
        public static string BattleKey(ClearingId c, SeatId s) => Battle + ":" + N(c.Value) + ":" + N(s.Value);
        public static string MoveKey(ClearingId from, ClearingId to, int count, params int[] relics) =>
            Move + ":" + N(from.Value) + ":" + N(to.Value) + ":" + N(count) + string.Concat(relics.Select(r => ":" + N(r)));

        private static bool TryInt(string s, out int n) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);

        private static ClearingId? ParseClearing(string s)
        {
            s = (s ?? "").Trim();
            var digits = new string(s.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && TryInt(digits, out var n) ? new ClearingId(n) : (ClearingId?)null;
        }

        public static LuneAction Parse(string key)
        {
            var a = new LuneAction { Key = key ?? "" };
            if (string.IsNullOrEmpty(key)) return a;
            var p = key.Split(':').Select(x => x.Trim()).ToArray();
            a.Verb = p[0].ToLowerInvariant();
            switch (a.Verb)
            {
                case Move:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2) a.To = ParseClearing(p[2]);
                    a.Count = p.Length > 3 && TryInt(p[3], out var n) ? n : 1;
                    for (var i = 4; i < p.Length; i++) if (TryInt(p[i], out var r)) a.Relics.Add(r);
                    break;
                case Delve:
                case Recover:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2 && TryInt(p[2], out var rel)) a.Relics.Add(rel);
                    break;
                case Retinue:
                    if (p.Length > 1) a.Column = p[1].ToLowerInvariant();
                    if (p.Length > 2 && Enum.TryParse<Suit>(p[2], true, out var suit)) a.Suit = suit;
                    break;
                case Battle:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2 && TryInt(p[2], out var s)) a.Target = new SeatId(s);
                    break;
                case Build:
                case Flip:
                    if (p.Length > 1) a.Clearing = ParseClearing(p[1]);
                    if (p.Length > 2) a.RelicType = p[2].ToLowerInvariant();
                    break;
            }
            return a;
        }

        public static List<LuneAction> ParseAll(IEnumerable<string> keys) => (keys ?? new string[0]).Select(Parse).ToList();

        public override string ToString() => Key;
    }
}
