using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core.Sheets;

namespace Root33.Core.Engine
{
    /// <summary>
    /// Bark selection rules from sheets/barks.json and sheets/bark_triggers.json (sys.speech-bubbles, sys.spoiler-mode).
    /// Pure functions: which lines a character may say for a trigger in a spoiler mode and act.
    /// </summary>
    public static class BarkRules
    {
        public const string TierSafe = "safe";
        public const string TierAct2 = "act2";
        public const string TierFinished = "finished";

        /// <summary>bark_triggers notes: safe lines play in both modes, act2 lines once the mod's Act II has begun (either
        /// mode), finished lines only in finished mode.</summary>
        public static bool TierAllows(string tier, SpoilerMode mode, int act) =>
            tier == TierSafe
            || (tier == TierAct2 && act >= 2)
            || (tier == TierFinished && mode == SpoilerMode.Finished);

        /// <summary>
        /// The acts a trigger can fire in, as its bark_triggers description states them (the sheet has no act column):
        /// trigger.act2-start "Act II ... begins", trigger.act3-start "Act III ... begins", trigger.canvas-truth "Act III only"
        /// (where the finished 'mother' lines live, so they fire only in the mod's Act III), trigger.decoy-found
        /// "During Act I", trigger.soarrie-handoff "(Act II or later)". Every other trigger fires in any act.
        /// </summary>
        private static readonly Dictionary<string, (int Min, int Max)> ActWindows = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            [Ids.BarkTriggers.Act2Start] = (2, 2),
            [Ids.BarkTriggers.Act3Start] = (3, 3),
            [Ids.BarkTriggers.CanvasTruth] = (3, 3),
            [Ids.BarkTriggers.DecoyFound] = (1, 1),
            [Ids.BarkTriggers.SoarrieHandoff] = (2, 3),
        };

        /// <summary>The first and last act a trigger may fire in.</summary>
        public static (int Min, int Max) ActWindow(string triggerId) =>
            ActWindows.TryGetValue(triggerId, out var w) ? w : (1, 3);

        public static bool TriggerAllowsAct(string triggerId, int act)
        {
            var w = ActWindow(triggerId);
            return act >= w.Min && act <= w.Max;
        }

        /// <summary>trigger.goal-move: "Bot seats only".</summary>
        public static bool BotsOnly(string triggerId) => triggerId == Ids.BarkTriggers.GoalMove;

        /// <summary>
        /// The lines <paramref name="characterId"/> may say for <paramref name="triggerId"/>, in sheet order. A finished line
        /// plays only in finished mode; in safe mode its safe sibling plays in its place, and in finished mode the safe sibling
        /// of an eligible finished line is skipped (trigger.canvas-truth, trigger.act3-start). Empty when the trigger cannot fire
        /// in this act.
        /// </summary>
        public static IReadOnlyList<BarkDef> Lines(string characterId, string triggerId, SpoilerMode mode, int act)
        {
            var result = new List<BarkDef>();
            if (!TriggerAllowsAct(triggerId, act)) return result;
            var rows = Data.Barks.Values.Where(b => b.Character == characterId && b.Trigger == triggerId).ToList();
            var replacedBySibling = new HashSet<string>(StringComparer.Ordinal);
            if (mode == SpoilerMode.Finished)
                foreach (var r in rows)
                    if (r.Tier == TierFinished && !string.IsNullOrEmpty(r.SafeSibling)) replacedBySibling.Add(r.SafeSibling);

            foreach (var r in rows)
            {
                BarkDef line = null;
                if (r.Tier == TierFinished)
                {
                    if (mode == SpoilerMode.Finished) line = r;
                    else if (!string.IsNullOrEmpty(r.SafeSibling) && Data.Barks.TryGetValue(r.SafeSibling, out var sibling)
                             && TierAllows(sibling.Tier, mode, act))
                        line = sibling;
                }
                else if (TierAllows(r.Tier, mode, act) && !replacedBySibling.Contains(r.Id)) line = r;
                if (line != null && result.All(x => x.Id != line.Id)) result.Add(line);
            }
            return result;
        }

        /// <summary>The cooldown of a trigger in the speaker's own turns (0 = none).</summary>
        public static int CooldownTurns(string triggerId) =>
            Data.BarkTriggers.TryGetValue(triggerId, out var t) ? t.CooldownTurns : 0;
    }

    /// <summary>One line that was shown.</summary>
    public readonly struct BarkRecord
    {
        public readonly SeatId Seat;
        public readonly string BarkId;
        public readonly string TriggerId;
        public BarkRecord(SeatId seat, string barkId, string triggerId) { Seat = seat; BarkId = barkId; TriggerId = triggerId; }
        public override string ToString() => Seat + ":" + BarkId;
    }

    /// <summary>
    /// The speech-bubble director of one game (sys.speech-bubbles). It picks a line through <see cref="BarkRules"/>, applies
    /// the trigger's cooldown (counted in the speaking seat's own completed turns: after a line from a trigger, that seat does
    /// not speak from the same trigger again until that many of its turns have passed) and shows it with do.show-bubble.
    /// Among several allowed lines it takes the one used least recently (sheet order on ties).
    /// <para>Cooldowns are not part of <see cref="ModState"/> (state.json has no row for them), so a loaded game starts with
    /// every cooldown cleared.</para>
    /// </summary>
    public sealed class BarkDirector
    {
        private readonly GameContext _ctx;
        private readonly Dictionary<SeatId, int> _turns = new Dictionary<SeatId, int>();
        private readonly Dictionary<(SeatId, string), int> _lastByTrigger = new Dictionary<(SeatId, string), int>();
        private readonly Dictionary<SeatId, int> _lastAny = new Dictionary<SeatId, int>();
        private readonly Dictionary<string, int> _lineUsed = new Dictionary<string, int>(StringComparer.Ordinal);

        public BarkDirector(GameContext ctx) { _ctx = ctx; }

        /// <summary>Every line shown this game, in order.</summary>
        public List<BarkRecord> Said { get; } = new List<BarkRecord>();

        /// <summary>A position in <see cref="Said"/>, to ask later whether a seat spoke since.</summary>
        public int Mark => Said.Count;

        public bool SpokeSince(SeatId seat, int mark)
        {
            for (var i = Math.Max(0, mark); i < Said.Count; i++)
                if (Said[i].Seat == seat) return true;
            return false;
        }

        public void Reset()
        {
            _turns.Clear();
            _lastByTrigger.Clear();
            _lastAny.Clear();
            _lineUsed.Clear();
            Said.Clear();
        }

        /// <summary>The seat's own completed turns this game.</summary>
        public int OwnTurns(SeatId seat) => _turns.TryGetValue(seat, out var n) ? n : 0;

        /// <summary>hook.turn-end of a seat: one more of its own turns has passed.</summary>
        public void TurnEnded(SeatId seat) => _turns[seat] = OwnTurns(seat) + 1;

        /// <summary>Own turns since the seat last said anything (since the game started when it never spoke).</summary>
        public int SilentTurns(SeatId seat) => OwnTurns(seat) - (_lastAny.TryGetValue(seat, out var t) ? t : 0);

        public bool OnCooldown(SeatId seat, string triggerId)
        {
            var cd = BarkRules.CooldownTurns(triggerId);
            return cd > 0 && _lastByTrigger.TryGetValue((seat, triggerId), out var at) && OwnTurns(seat) - at < cd;
        }

        /// <summary>The line a seat would say for a trigger now, or null (no character, bots-only trigger for a human, cooldown,
        /// trigger.idle before the seat has been silent for the trigger's cooldown, or no line allowed by tier and act).</summary>
        public BarkDef Pick(SeatId seat, string triggerId)
        {
            var character = _ctx.CharacterOf(seat);
            if (character == null) return null;
            if (BarkRules.BotsOnly(triggerId) && !_ctx.Board.IsBot(seat)) return null;
            if (triggerId == Ids.BarkTriggers.Idle && SilentTurns(seat) < BarkRules.CooldownTurns(triggerId)) return null;
            if (OnCooldown(seat, triggerId)) return null;
            var lines = BarkRules.Lines(character, triggerId, _ctx.Spoilers, _ctx.Act);
            if (lines.Count == 0) return null;
            return lines.OrderBy(l => _lineUsed.TryGetValue(l.Id, out var u) ? u : -1).First();
        }

        /// <summary>Shows a line for the seat if one is allowed; returns its bark id or null.</summary>
        public string Say(SeatId seat, string triggerId)
        {
            var line = Pick(seat, triggerId);
            if (line == null) return null;
            _ctx.Actions.ShowBubble(seat, line.Id);
            _lineUsed[line.Id] = Said.Count;
            _lastByTrigger[(seat, triggerId)] = OwnTurns(seat);
            _lastAny[seat] = OwnTurns(seat);
            Said.Add(new BarkRecord(seat, line.Id, triggerId));
            return line.Id;
        }

        /// <summary>Each seat in order may say one line.</summary>
        public void SayEach(IEnumerable<SeatId> seats, string triggerId)
        {
            foreach (var s in seats.ToList()) Say(s, triggerId);
        }

        /// <summary>"One seated character may say": among the seats that have a line, the one silent longest (ties in the
        /// given order) says it. Returns the bark id or null.</summary>
        public string SayOne(IEnumerable<SeatId> seats, string triggerId)
        {
            var pick = seats.Select((s, i) => (seat: s, index: i))
                .Where(x => Pick(x.seat, triggerId) != null)
                .OrderByDescending(x => SilentTurns(x.seat)).ThenBy(x => x.index)
                .Select(x => (SeatId?)x.seat).FirstOrDefault();
            return pick.HasValue ? Say(pick.Value, triggerId) : null;
        }
    }
}
