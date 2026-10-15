using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>One card on the mod's character select (ui.global.character-select).</summary>
    public sealed class CharacterCard
    {
        public string CharacterId { get; internal set; }
        /// <summary>The safe name in safe mode, the finished name in finished mode; '???' for a character locked in safe mode.</summary>
        public string Name { get; internal set; }
        public string Engine { get; internal set; }
        /// <summary>The dlc.json id the engine needs ("base" for none).</summary>
        public string Dlc { get; internal set; }
        /// <summary>Shown but not pickable (Clea in safe mode).</summary>
        public bool Locked { get; internal set; }
        /// <summary>The spoiler-free pitch (characters.summarySafe); empty on a locked card.</summary>
        public string Summary { get; internal set; }
        public string Portrait { get; internal set; }
    }

    /// <summary>
    /// Character select and table seating (sys.table-seating, sys.dlc-detection): 2-4 seats, at most two Vagabond characters
    /// (Root digital seats two Vagabonds without any DLC since 1.27.0), a character only when its engine's DLC is owned (by the
    /// human or as a bot), never a character that safe mode locks (Clea), and a lore rival of the human's pick seated first.
    /// </summary>
    public static class TableSeating
    {
        /// <summary>sys.table-seating: "2-4 seats in all (Root digital's cap is 4)". No tuning row holds these numbers.</summary>
        public const int MinSeats = 2;
        public const int MaxSeats = 4;
        /// <summary>sys.table-seating: "with at most two Vagabond characters".</summary>
        public const int MaxVagabonds = 2;

        public static EngineDef EngineOf(string characterId) => Data.Engines[Data.Characters[characterId].Engine];

        public static bool IsVagabond(string characterId) => EngineOf(characterId).Kind == "vagabond";

        /// <summary>The DLC the character's engine needs ("base" for none).</summary>
        public static string DlcOf(string characterId) => EngineOf(characterId).Dlc;

        public static bool Owns(string characterId, IEnumerable<string> ownedDlc)
        {
            var dlc = DlcOf(characterId);
            return dlc == Ids.Dlc.Base || (ownedDlc != null && ownedDlc.Contains(dlc));
        }

        /// <summary>Safe mode locks a character whose safeModeSelectable is false: it is never seated, by the human or a bot.</summary>
        public static bool LockedIn(string characterId, SpoilerMode mode) =>
            mode == SpoilerMode.Safe && !Data.Characters[characterId].SafeModeSelectable;

        /// <summary>Whether a character may sit at the table at all (human or bot).</summary>
        public static bool Seatable(string characterId, IEnumerable<string> ownedDlc, SpoilerMode mode) =>
            Data.Characters.ContainsKey(characterId) && Owns(characterId, ownedDlc) && !LockedIn(characterId, mode);

        /// <summary>The cards character select shows: every character whose DLC is owned (a missing DLC hides it), in build
        /// order; a character locked in safe mode shows as a locked '???' card.</summary>
        public static IReadOnlyList<CharacterCard> Cards(IEnumerable<string> ownedDlc, SpoilerMode mode)
        {
            var owned = (ownedDlc ?? new string[0]).ToList();
            return Data.Characters.Values.Where(c => Owns(c.Id, owned)).OrderBy(c => c.BuildOrder).Select(c =>
            {
                var locked = LockedIn(c.Id, mode);
                return new CharacterCard
                {
                    CharacterId = c.Id,
                    Name = mode == SpoilerMode.Finished ? c.FinishedName : c.SafeName,
                    Engine = c.Engine,
                    Dlc = DlcOf(c.Id),
                    Locked = locked,
                    Summary = locked ? "" : c.SummarySafe,
                    Portrait = c.Portrait,
                };
            }).ToList();
        }

        /// <summary>Every rule a table breaks (empty for a legal table).</summary>
        public static IReadOnlyList<string> Problems(IReadOnlyList<string> table, IEnumerable<string> ownedDlc, SpoilerMode mode)
        {
            var owned = (ownedDlc ?? new string[0]).ToList();
            var problems = new List<string>();
            if (table.Count < MinSeats || table.Count > MaxSeats)
                problems.Add("a table seats " + MinSeats + "-" + MaxSeats + " characters, not " + table.Count);
            foreach (var dup in table.GroupBy(c => c).Where(g => g.Count() > 1)) problems.Add(dup.Key + " is seated twice");
            foreach (var c in table.Distinct())
            {
                if (!Data.Characters.ContainsKey(c)) { problems.Add("no characters row " + c); continue; }
                if (!Owns(c, owned)) problems.Add(c + " needs the " + DlcOf(c) + " DLC");
                if (LockedIn(c, mode)) problems.Add(c + " is locked in safe mode");
            }
            var vagabonds = table.Distinct().Count(c => Data.Characters.ContainsKey(c) && IsVagabond(c));
            if (vagabonds > MaxVagabonds) problems.Add("at most " + MaxVagabonds + " Vagabond characters, not " + vagabonds);
            return problems;
        }

        public static bool IsLegal(IReadOnlyList<string> table, IEnumerable<string> ownedDlc, SpoilerMode mode) =>
            Problems(table, ownedDlc, mode).Count == 0;

        /// <summary>The pick's lore rivals (characters.rivals, in order) that may be seated.</summary>
        public static IReadOnlyList<string> Rivals(string pick, IEnumerable<string> ownedDlc, SpoilerMode mode) =>
            (Data.Characters[pick].Rivals ?? new string[0]).Where(r => Seatable(r, ownedDlc, mode)).ToList();

        /// <summary>
        /// The table for a human pick: the pick, then the first lore rival that fits, then the rest of
        /// <paramref name="roster"/> (default: every character) in build order, skipping any character that would break a
        /// rule, until <paramref name="seats"/> are filled. Returns fewer seats when not enough characters fit.
        /// </summary>
        public static IReadOnlyList<string> FillTable(string humanPick, int seats, IEnumerable<string> ownedDlc, SpoilerMode mode,
            IEnumerable<string> roster = null)
        {
            if (seats < MinSeats || seats > MaxSeats) throw new ArgumentOutOfRangeException(nameof(seats));
            var owned = (ownedDlc ?? new string[0]).ToList();
            if (!Seatable(humanPick, owned, mode)) throw new ArgumentException(humanPick + " cannot be seated");
            var table = new List<string> { humanPick };

            bool Fits(string c) =>
                !table.Contains(c) && Seatable(c, owned, mode)
                && (!IsVagabond(c) || table.Count(IsVagabond) < MaxVagabonds);

            var rival = Rivals(humanPick, owned, mode).FirstOrDefault(Fits);
            if (rival != null && table.Count < seats) table.Add(rival);

            var pool = (roster ?? Data.Characters.Keys).Where(Data.Characters.ContainsKey)
                .OrderBy(c => Data.Characters[c].BuildOrder).ToList();
            foreach (var c in pool)
            {
                if (table.Count >= seats) break;
                if (Fits(c)) table.Add(c);
            }
            return table;
        }

        /// <summary>sys.dlc-detection: the Lake and Mountain maps need The Underworld Expansion; Autumn and Winter are base.</summary>
        public static bool MapAllowed(string mapName, IEnumerable<string> ownedDlc)
        {
            var m = (mapName ?? "").ToLowerInvariant();
            if (m == "lake" || m == "mountain") return ownedDlc != null && ownedDlc.Contains(Ids.Dlc.Underworld);
            return true;
        }
    }
}
