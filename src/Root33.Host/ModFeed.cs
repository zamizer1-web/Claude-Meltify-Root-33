using System;
using System.Collections.Generic;
using System.Linq;
using Root33.Core;
using Root33.Core.Engine;

namespace Root33.Host
{
    /// <summary>One speech bubble a seat said (do.show-bubble), numbered in the order they were shown.</summary>
    public sealed class BubbleRecord
    {
        public int N { get; set; }
        /// <summary>The speaking seat (SeatId value).</summary>
        public int Seat { get; set; }
        public string BarkId { get; set; } = "";
        public int Round { get; set; }
    }

    /// <summary>One story event the core fired (GameContext.StoryLog), with the round it fired in.</summary>
    public sealed class StoryRecord
    {
        public string EventId { get; set; } = "";
        public int Act { get; set; }
        public int Round { get; set; }
    }

    /// <summary>
    /// What the mod has said so far, kept by the host because the core does not save it: the speech bubbles (the last
    /// <see cref="MaxBubbles"/>, with each seat's 'seen up to' mark set at the end of its own turn, so a seat sees the
    /// bubbles since its last turn), the story events in order, and the last act whose banner was shown.
    ///
    /// Plain settable properties so a host can store it as it likes (the tabletop serialises a subclass into its save).
    /// The read positions in the core's logs (<see cref="CatchUp"/>) are not part of it: after a load call
    /// <see cref="SkipSeen"/>.
    /// </summary>
    public class ModFeed
    {
        public const int MaxBubbles = 120;

        /// <summary>The bubbles kept for the views (the last <see cref="MaxBubbles"/>).</summary>
        public List<BubbleRecord> Bubbles { get; set; } = new List<BubbleRecord>();
        /// <summary>Bubbles shown this game (the number of the last one).</summary>
        public int BubbleCount { get; set; }
        /// <summary>For each seat (SeatId value), the last bubble number it has seen (set at the end of its turn).</summary>
        public Dictionary<int, int> BubbleMark { get; set; } = new Dictionary<int, int>();
        /// <summary>Every story event fired this game, in order.</summary>
        public List<StoryRecord> Story { get; set; } = new List<StoryRecord>();
        /// <summary>The last act whose banner was shown.</summary>
        public int LastAct { get; set; }

        private int _storySeen;
        private int _warningsSeen;

        /// <summary>do.show-bubble: records the bubble and returns it (the host shows or logs it).</summary>
        public BubbleRecord AddBubble(SeatId seat, string barkId, int round)
        {
            var b = new BubbleRecord { N = ++BubbleCount, Seat = seat.Value, BarkId = barkId, Round = round };
            Bubbles.Add(b);
            if (Bubbles.Count > MaxBubbles) Bubbles.RemoveAt(0);
            return b;
        }

        /// <summary>hook.turn-end of a seat: it has now seen every bubble so far.</summary>
        public void TurnEnded(SeatId seat) => BubbleMark[seat.Value] = BubbleCount;

        /// <summary>The bubbles a seat has not seen yet: every one since the end of its own last turn.</summary>
        public IReadOnlyList<BubbleRecord> SinceLastTurn(SeatId seat)
        {
            var mark = BubbleMark.TryGetValue(seat.Value, out var m) ? m : 0;
            return Bubbles.Where(b => b.N > mark).ToList();
        }

        /// <summary>The last <paramref name="count"/> bubbles (a spectator's view).</summary>
        public IReadOnlyList<BubbleRecord> Latest(int count) => Bubbles.Skip(Math.Max(0, Bubbles.Count - count)).ToList();

        /// <summary>The last <paramref name="count"/> story events.</summary>
        public IReadOnlyList<StoryRecord> LatestStory(int count) => Story.Skip(Math.Max(0, Story.Count - count)).ToList();

        /// <summary>
        /// After each hook: picks up what the core produced since the last call and hands each line to <paramref name="log"/>:
        /// every new story event (recorded with <paramref name="round"/>), the act banner once per new act (only after setup,
        /// and before the first story event of that act), and the core's notes for the player. Nothing in a plain Root game.
        /// </summary>
        public void CatchUp(GameContext ctx, int round, bool setupDone, Action<string> log)
        {
            if (!ctx.E33Mode) return;
            var story = ctx.StoryLog;
            for (; _storySeen < story.Count; _storySeen++)
            {
                var e = story[_storySeen];
                if (setupDone && ctx.Act > LastAct) ActBanner(ctx, log);
                Story.Add(new StoryRecord { EventId = e.EventId, Act = e.Act, Round = round });
                log(ModText.StoryLine(e.Act, e.EventId));
            }
            if (setupDone && ctx.Act > LastAct) ActBanner(ctx, log);
            for (; _warningsSeen < ctx.Warnings.Count; _warningsSeen++) log(ModText.NoteLine(ctx.Warnings[_warningsSeen]));
        }

        /// <summary>After a load: what the core logged while it reloaded is not news, so <see cref="CatchUp"/> starts after it.</summary>
        public void SkipSeen(GameContext ctx)
        {
            _storySeen = ctx.StoryLog.Count;
            _warningsSeen = ctx.Warnings.Count;
        }

        private void ActBanner(GameContext ctx, Action<string> log)
        {
            LastAct = ctx.Act;
            log(ModText.ActBannerLine(ctx.Act));
        }
    }
}
