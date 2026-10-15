using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// sheets/overlays.json as data the UI layer reads: what each overlay shows, whether it is public, which state rows it
    /// reads, and whether it belongs on the current table (global overlays always; a character's overlays only while that
    /// character is seated). The core never draws; the plugin draws what this says.
    /// </summary>
    public static class OverlayCatalog
    {
        public const string GlobalOwner = "global";

        public static IEnumerable<OverlayDef> All => Data.Overlays.Values;

        public static OverlayDef Get(string overlayId) =>
            Data.Overlays.TryGetValue(overlayId, out var o) ? o : throw new ArgumentException("no overlays row " + overlayId);

        /// <summary>The overlays a character (or "global") owns, in sheet order.</summary>
        public static IReadOnlyList<OverlayDef> OwnedBy(string owner) => All.Where(o => o.Owner == owner).ToList();

        /// <summary>The overlays that read a state row, so the UI knows what to redraw when it changes.</summary>
        public static IReadOnlyList<OverlayDef> Reading(string stateId) =>
            All.Where(o => o.State != null && o.State.Contains(stateId)).ToList();

        /// <summary>Overlays every seat may see (all of the Goal Tracker's inputs are public, so bots may read them too).</summary>
        public static IReadOnlyList<OverlayDef> PublicOverlays => All.Where(o => o.Public).ToList();

        /// <summary>Whether an overlay belongs on this table: global, or owned by a seated character.</summary>
        public static bool OnTable(GameContext ctx, string overlayId)
        {
            var o = Get(overlayId);
            return o.Owner == GlobalOwner || ctx.IsSeated(o.Owner);
        }

        /// <summary>The overlays of this table, global ones first, then each seated character's in seat order.</summary>
        public static IReadOnlyList<OverlayDef> ForTable(GameContext ctx)
        {
            var list = OwnedBy(GlobalOwner).ToList();
            foreach (var kv in ctx.State.GlobalSeatCharacters.OrderBy(kv => kv.Key.Value)) list.AddRange(OwnedBy(kv.Value));
            return list;
        }

        /// <summary>The saved values of the state rows an overlay reads (as <see cref="ModState.Save"/> writes them), for the
        /// UI to draw. Empty for an overlay that reads the board live.</summary>
        public static IReadOnlyDictionary<string, string> StateFor(GameContext ctx, string overlayId)
        {
            var o = Get(overlayId);
            var saved = ctx.State.Save();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in o.State ?? new string[0])
                if (saved.TryGetValue(id, out var v)) result[id] = v;
            return result;
        }
    }

    /// <summary>
    /// sheets/options.json: the four (v1.1: five) player options, their values and defaults (sys.settings). Spoiler mode stays
    /// with the player's profile; the others are chosen when a game is set up; a spoiler-mode change applies from the next game.
    /// </summary>
    public static class OptionRules
    {
        /// <summary>A <see cref="GameOptions"/> holding every option's sheet default.</summary>
        public static GameOptions Defaults()
        {
            var o = new GameOptions();
            foreach (var row in Data.Options.Values) Set(o, row.Id, row.Default);
            return o;
        }

        /// <summary>An option's current value, as options.json spells it.</summary>
        public static string Get(GameOptions o, string optionId)
        {
            switch (optionId)
            {
                case Ids.Options.SpoilerMode: return o.Spoilers == SpoilerMode.Finished ? "finished" : "safe";
                case Ids.Options.Difficulty: return o.Difficulty == Difficulty.Easy ? "easy" : o.Difficulty == Difficulty.Hard ? "hard" : "normal";
                case Ids.Options.FaithfulGustave: return o.FaithfulGustave ? "on" : "off";
                case Ids.Options.Campaign: return o.Campaign ? "on" : "off";
                case Ids.Options.MonolithBarrier: return o.MonolithBarrier ? "on" : "off";
                default: throw new ArgumentException("no options row " + optionId);
            }
        }

        /// <summary>Sets an option to one of its sheet values; any other value is refused.</summary>
        public static void Set(GameOptions o, string optionId, string value)
        {
            if (!Data.Options.TryGetValue(optionId, out var row)) throw new ArgumentException("no options row " + optionId);
            if (!row.Values.Contains(value)) throw new ArgumentException(optionId + " has no value " + value);
            switch (optionId)
            {
                case Ids.Options.SpoilerMode: o.Spoilers = value == "finished" ? SpoilerMode.Finished : SpoilerMode.Safe; break;
                case Ids.Options.Difficulty:
                    o.Difficulty = value == "easy" ? Difficulty.Easy : value == "hard" ? Difficulty.Hard : Difficulty.Normal; break;
                case Ids.Options.FaithfulGustave: o.FaithfulGustave = value == "on"; break;
                case Ids.Options.Campaign: o.Campaign = value == "on"; break;
                case Ids.Options.MonolithBarrier: o.MonolithBarrier = value == "on"; break;
            }
        }

        /// <summary>Whether the settings panel shows an option: Faithful Gustave is finished mode only (hidden in safe mode).</summary>
        public static bool Visible(string optionId, SpoilerMode profileMode) =>
            optionId != Ids.Options.FaithfulGustave || profileMode == SpoilerMode.Finished;
    }

    /// <summary>
    /// Spoiler-tiered names (sys.spoiler-mode): safe mode shows each character's safeName, finished mode its finishedName,
    /// except that (characters.json notes) Maelle shows 'Maelle' until story.act3.alicia has fired and the Curator 'The Curator'
    /// until story.act3.cell-crack has fired. With Faithful Gustave on (finished mode), Gustave's seat is titled
    /// 'Expedition 33' from Act II (opt.faithful-gustave, story.act2.beach).
    /// </summary>
    public static class SpoilerText
    {
        /// <summary>opt.faithful-gustave: the title of Gustave's seat from Act II.</summary>
        public const string FaithfulGustaveTitle = "Expedition 33";

        private static readonly Dictionary<string, string> RevealedBy = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Ids.Characters.Maelle] = Ids.StoryEvents.Act3Alicia,
            [Ids.Characters.Curator] = Ids.StoryEvents.Act3CellCrack,
        };

        /// <summary>The name a character shows in this game.</summary>
        public static string CharacterName(GameContext ctx, string characterId)
        {
            var row = Data.Characters[characterId];
            if (ctx.Spoilers != SpoilerMode.Finished) return row.SafeName;
            if (RevealedBy.TryGetValue(characterId, out var ev) && !StoryEvents.HasFired(ctx, ev)) return row.SafeName;
            return row.FinishedName;
        }

        /// <summary>The title Root shows for a seat (do.set-seat-title), or null for a seat playing plain Root.</summary>
        public static string SeatTitle(GameContext ctx, SeatId seat)
        {
            var ch = ctx.CharacterOf(seat);
            if (ch == null) return null;
            if (ch == Ids.Characters.Gustave && ctx.Act >= 2 && ctx.Options.FaithfulGustaveActive(ctx.Spoilers)) return FaithfulGustaveTitle;
            return CharacterName(ctx, ch);
        }
    }
}
