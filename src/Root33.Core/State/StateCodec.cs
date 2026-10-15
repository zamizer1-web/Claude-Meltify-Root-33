using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Root33.Core
{
    /// <summary>
    /// Invariant, JSON-free text forms for <see cref="ModState"/> values. Lists use ',', maps use ';' between entries and '='
    /// between key and value, records use '|' between fields; null is '~'. Every string is %-escaped, so no value can break
    /// the structure.
    /// </summary>
    internal static class StateCodec
    {
        public const string Null = "~";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Esc(string s)
        {
            if (s == null) return Null;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '%': case '~': case ',': case ';': case '=': case '|': case '\n': case '\r':
                        sb.Append('%').Append(((int)ch).ToString("X2", Inv));
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Unesc(string s)
        {
            if (s == Null) return null;
            if (s.IndexOf('%') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && i + 2 < s.Length)
                {
                    sb.Append((char)int.Parse(s.Substring(i + 1, 2), NumberStyles.HexNumber, Inv));
                    i += 2;
                }
                else if (s[i] == '%') throw new FormatException("bad escape in '" + s + "'");
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Int(int v) => v.ToString(Inv);
        public static int ParseInt(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, Inv);
        public static string NInt(int? v) => v.HasValue ? Int(v.Value) : Null;
        public static int? ParseNInt(string s) => s == Null ? (int?)null : ParseInt(s);
        public static string Bool(bool v) => v ? "true" : "false";

        public static bool ParseBool(string s)
        {
            if (s == "true") return true;
            if (s == "false") return false;
            throw new FormatException("not a bool: '" + s + "'");
        }

        public static string Enum<T>(T v) where T : struct => v.ToString();

        public static T ParseEnum<T>(string s) where T : struct
        {
            if (!System.Enum.TryParse<T>(s, false, out var v) || !System.Enum.IsDefined(typeof(T), v))
                throw new FormatException("not a " + typeof(T).Name + ": '" + s + "'");
            return v;
        }

        public static string Seat(SeatId s) => Int(s.Value);
        public static SeatId ParseSeat(string s) => new SeatId(ParseInt(s));
        public static string NSeat(SeatId? s) => s.HasValue ? Seat(s.Value) : Null;
        public static SeatId? ParseNSeat(string s) => s == Null ? (SeatId?)null : ParseSeat(s);
        public static string Clearing(ClearingId c) => Int(c.Value);
        public static ClearingId ParseClearing(string s) => new ClearingId(ParseInt(s));
        public static string NClearing(ClearingId? c) => c.HasValue ? Clearing(c.Value) : Null;
        public static ClearingId? ParseNClearing(string s) => s == Null ? (ClearingId?)null : ParseClearing(s);

        public static string[] SplitList(string s) => s.Length == 0 ? new string[0] : s.Split(',');

        public static string List<T>(IEnumerable<T> items, Func<T, string> one) => string.Join(",", items.Select(one));

        public static List<T> ParseList<T>(string s, Func<string, T> one) => SplitList(s).Select(one).ToList();

        /// <summary>Map entries sorted by their encoded key, so equal maps always save to equal text.</summary>
        public static string Map<K, V>(IEnumerable<KeyValuePair<K, V>> map, Func<K, string> key, Func<V, string> value) =>
            string.Join(";", map.Select(kv => key(kv.Key) + "=" + value(kv.Value)).OrderBy(e => e, StringComparer.Ordinal));

        public static Dictionary<K, V> ParseMap<K, V>(string s, Func<string, K> key, Func<string, V> value)
        {
            var d = new Dictionary<K, V>();
            if (s.Length == 0) return d;
            foreach (var entry in s.Split(';'))
            {
                var eq = entry.IndexOf('=');
                if (eq < 0) throw new FormatException("map entry without '=': '" + entry + "'");
                d[key(entry.Substring(0, eq))] = value(entry.Substring(eq + 1));
            }
            return d;
        }

        public static string[] Fields(string record, int count)
        {
            var f = record.Split('|');
            if (f.Length != count) throw new FormatException("expected " + count + " fields in '" + record + "'");
            return f;
        }
    }
}
