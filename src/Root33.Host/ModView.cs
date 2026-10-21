using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Root33.Core;
using Root33.Core.Characters.Gustave;
using Root33.Core.Characters.Paintress;
using Root33.Core.Characters.Sciel;
using Root33.Core.Engine;
using Root33.Core.Sheets;

namespace Root33.Host
{
    /// <summary>One Goal Tracker row: the seat, its themed win and how far it is, and its character's public tracker or gauge.</summary>
    public sealed class GoalRow
    {
        public GoalRow(SeatId seat, string who, string goal) { Seat = seat; Who = who; Goal = goal; }
        public SeatId Seat { get; }
        public string Who { get; }
        /// <summary>"The Family Portrait (countdown): locked until Act II, needs 10+ VP, 2 steps missing".</summary>
        public string Goal { get; }
        /// <summary>The character's tracker ("Arm 2/4 · flags: ..."), or empty for a character without one.</summary>
        public string Tracker { get; internal set; } = "";
    }

    /// <summary>
    /// The mod's panel for one viewer, part by part, so a host can draw each part where it likes (the plugin's overlay) or
    /// print it whole (<see cref="ToText"/>, the tabletop's view). Parts the view did not reach because a read failed are
    /// null; <see cref="Failure"/> then holds the exception.
    /// </summary>
    public sealed class ModPanel
    {
        /// <summary>"ROOT 33 · Act I: The Landing · Monolith Number 33: 33 minus the top score. ..." (<see cref="ModView.Header"/>).</summary>
        public string? Header { get; internal set; }
        /// <summary>The act banner's line (what unlocks, when the next act begins).</summary>
        public string? ActLine { get; internal set; }
        /// <summary>The countdown banners up now, by seat.</summary>
        public List<string>? Banners { get; internal set; }
        /// <summary>The public Goal Tracker, one row per seated character in turn order.</summary>
        public List<GoalRow>? Goals { get; internal set; }
        /// <summary>The last story events as players read them ("Act I: The Landing: ..."), never the design notes.</summary>
        public List<string>? Story { get; internal set; }
        /// <summary>"SPEECH BUBBLES SINCE YOUR LAST TURN" for a seat, "LATEST SPEECH BUBBLES" for a spectator.</summary>
        public string BubblesTitle { get; internal set; } = "";
        /// <summary>The bubbles that viewer should see ("r3 seat 2 (The Paintress): "...""), or null if not reached.</summary>
        public List<string>? Bubbles { get; internal set; }
        /// <summary>The exception that stopped the view (for example a board read the plugin has not mapped yet), or null.</summary>
        public Exception? Failure { get; internal set; }

        /// <summary>The whole panel as text, part by part; a failure adds "(Root 33 view unavailable: ...)" where it stopped.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            if (Header != null) sb.AppendLine(Header);
            if (ActLine != null) sb.AppendLine("  " + ActLine);
            foreach (var b in Banners ?? new List<string>()) sb.AppendLine("  " + b);
            if (Goals != null)
            {
                sb.AppendLine("GOAL TRACKER (public)");
                foreach (var g in Goals)
                {
                    sb.AppendLine($"  {g.Who} · {g.Goal}");
                    if (g.Tracker.Length > 0) sb.AppendLine("      " + g.Tracker);
                }
            }
            if (Story != null && Story.Count > 0)
            {
                sb.AppendLine("STORY SO FAR");
                foreach (var s in Story) sb.AppendLine("  " + s);
            }
            if (Bubbles != null && Bubbles.Count > 0)
            {
                sb.AppendLine(BubblesTitle);
                foreach (var b in Bubbles) sb.AppendLine("  " + b);
            }
            if (Failure != null) sb.AppendLine($"(Root 33 view unavailable: {Failure.Message})");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// What the mod shows a viewer, from the core's public state only: the act banner and Monolith Number, the countdown
    /// banners, the public Goal Tracker with each character's tracker or gauge (overlays.json: ui.gustave.road/arm,
    /// ui.paintress.portrait-tracker, ui.sciel.gauges, ui.verso.rank, ui.maelle.family/stance, ui.esquie.rock-ring/rocks),
    /// the story so far, and the speech bubbles the viewer has not seen. The same in the tabletop and in Root.
    /// </summary>
    public sealed class ModView
    {
        /// <summary>Story events the panel lists (the latest).</summary>
        public const int StoryShown = 6;
        /// <summary>Bubbles a spectator sees (the latest).</summary>
        public const int SpectatorBubbles = 12;

        private readonly GameContext _ctx;
        private readonly IModHost _host;
        private readonly ModFeed _feed;

        public ModView(GameContext ctx, IModHost host, ModFeed feed)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        }

        /// <summary>The panel for a seat (bubbles since its last turn), or for a spectator when <paramref name="viewer"/> is null
        /// (the latest bubbles). Empty text in a plain Root game.</summary>
        public string Text(SeatId? viewer) => _ctx.E33Mode ? Panel(viewer).ToText() : "";

        /// <summary>The panel part by part. Never throws: a failed read stops it and is kept in <see cref="ModPanel.Failure"/>.</summary>
        public ModPanel Panel(SeatId? viewer)
        {
            var p = new ModPanel();
            try { Fill(p, viewer); }
            catch (Exception e) { p.Failure = e; }
            return p;
        }

        private IBoardView Board => _ctx.Board;

        private void Fill(ModPanel p, SeatId? viewer)
        {
            var spoilers = _host.Spoilers;
            p.Header = Header(_ctx.Act, _ctx.MonolithNumber, spoilers);
            p.ActLine = ModText.ActLine(_ctx.Act, spoilers);
            p.Banners = new List<string>();
            foreach (var kv in _host.Banners().OrderBy(k => k.Key.Value))
                p.Banners.Add(ModText.BannerAnnounced(_host, kv.Key, kv.Value));

            p.Goals = new List<GoalRow>();
            foreach (var s in _host.TurnOrder())
            {
                var ch = _ctx.CharacterOf(s);
                if (ch == null) continue;
                var row = new GoalRow(s, _host.Who(s), GoalLine(s));
                p.Goals.Add(row);
                row.Tracker = Tracker(s, ch);
            }

            p.Story = _feed.LatestStory(StoryShown).Select(e => $"{ModText.ActTitle(e.Act, spoilers)}: {ModText.StoryText(e.EventId, spoilers)}").ToList();

            var bubbles = viewer is SeatId v ? _feed.SinceLastTurn(v) : _feed.Latest(SpectatorBubbles);
            p.BubblesTitle = viewer.HasValue ? "SPEECH BUBBLES SINCE YOUR LAST TURN" : "LATEST SPEECH BUBBLES";
            p.Bubbles = new List<string>();
            foreach (var b in bubbles) p.Bubbles.Add($"r{b.Round} {_host.Who(new SeatId(b.Seat))}: \"{ModText.BarkText(b.BarkId)}\"");
        }

        /// <summary>The panel's first line: the act and the Monolith Number with what it means. The number has no rule of its
        /// own; it is 33 minus the top score (GameContext.MonolithNumber), a clock that reaches 3 when someone has 30 VP.</summary>
        public static string Header(int act, int monolithNumber, SpoilerMode spoilers) =>
            $"ROOT 33 · {ModText.ActTitle(act, spoilers)} · Monolith Number {monolithNumber}: {Tuning.MonolithNumberStart} minus the top score. " +
            $"It falls as the leader scores and reaches {Tuning.MonolithNumberStart - Tuning.RootWinVp} when someone has {Tuning.RootWinVp} VP.";

        /// <summary>One Goal Tracker row: the win's title and how far the seat is (gates, then the themed test's missing steps).</summary>
        public string GoalLine(SeatId seat)
        {
            var win = _ctx.WinOf(seat);
            if (win == null || !Data.WinConditions.TryGetValue(win.WinId, out var row)) return "no themed win";
            var parts = new List<string>();
            if (_ctx.Act < row.ActMin) parts.Add($"locked until Act {ModText.Roman(row.ActMin)}");
            if (row.VpMin > 0 && _host.Vp(seat) < row.VpMin) parts.Add($"needs {row.VpMin}+ VP");
            if (row.OffWithDominance && _host.HasActiveDominance(seat)) parts.Add("off while a dominance card is active");
            if (row.OffInCoalition && _host.InCoalition(seat)) parts.Add("off while in a coalition");
            if (win is IGoalProgress progress)
            {
                var missing = progress.UnitsMissing(_ctx, seat);
                parts.Add(missing switch { 0 => "themed test met", 1 => "ONE STEP AWAY", _ => $"{missing} steps missing" });
            }
            if (_host.Banners().Any(kv => kv.Key == seat)) parts.Add("COUNTDOWN ANNOUNCED");
            return $"{ModText.WinTitle(row.Id, _host.Spoilers)} ({(row.Kind == "quest" ? "quest" : "countdown")}): {string.Join(", ", parts)}";
        }

        private string Where(ClearingId c) => _host.ClearingName(c);

        private static string Suits(IEnumerable<Suit> suits)
        {
            var l = suits.Where(s => s != Suit.Bird).OrderBy(s => s).Select(s => s.ToString().ToLowerInvariant()).ToList();
            return l.Count == 0 ? "none" : string.Join(", ", l);
        }

        /// <summary>The character's own public tracker or gauge, or empty for a character without one.</summary>
        public string Tracker(SeatId me, string characterId)
        {
            var st = _ctx.State;
            var monolith = _ctx.Role(Ids.MapRoles.Monolith);
            var lumiere = _ctx.Role(Ids.MapRoles.Lumiere);
            switch (characterId)
            {
                case "gustave":
                {
                    var chain = GustaveChain.Chain(Board, st, me, lumiere);
                    var beach = GustaveChain.ChainBeachhead(Board, me, chain, monolith);
                    var flags = st.GustaveFlags.OrderBy(c => c.Value).Select(c => "c" + c.Value + (Board.Ruler(c) == me ? "" : " (faded)"));
                    return $"Arm {st.GustaveArm}/{Tuning.GustaveArmMax}{(st.GustaveOverchargeArmed ? ", Overcharge armed" : "")} · " +
                           $"flags: {(st.GustaveFlags.Count == 0 ? "none" : string.Join(", ", flags))} · " +
                           $"Chain from Lumière: {(chain.Count == 0 ? "none" : string.Join(", ", chain.OrderBy(c => c.Value).Select(c => "c" + c.Value)))} " +
                           $"covering {Suits(GustaveChain.ChainSuits(Board, chain))} · Beachhead: {(beach is ClearingId b ? Where(b) : "not reached")}";
                }
                case "paintress":
                {
                    var portraits = _host.Spoilers == SpoilerMode.Finished
                        ? PaintressPortraits.LabelledPortraits(Board, st, me, monolith).Select(kv => $"{kv.Key} at {Where(kv.Value)}")
                        : PaintressPortraits.LivingPortraits(Board, st, me, monolith).Select(Where);
                    var missing = PaintressPortraits.MissingSuits(Board, st, me, monolith);
                    return $"Portraits {3 - missing.Count}/3: {string.Join("; ", portraits.DefaultIfEmpty("none"))} · missing: {Suits(missing)} · " +
                           $"holds the Monolith with an easel: {(PaintressPortraits.HoldsMonolith(Board, me, monolith) ? "yes" : "no")}";
                }
                case "sciel":
                {
                    var sanct = ScielBoard.Sanctuaries(Board, me);
                    return $"Sun: {sanct.Count}/5 Sanctuaries ({Suits(ScielBoard.SuitsCovered(Board, sanct))}) · Moon: {ScielBoard.Aquafarms(Board, me)} Aquafarm(s) on the map";
                }
                case "verso":
                {
                    var rank = "DCBAS"[Math.Max(0, Math.Min(4, st.VersoRank))];
                    var inMono = monolith.HasValue && Board.PawnClearing(me) == monolith.Value;
                    return $"Perfection rank {rank} · pawn in the Monolith: {(inMono ? "yes" : "no")}";
                }
                case "maelle":
                {
                    var rels = _host.TurnOrder().Where(o => o != me && !_ctx.IsVagabond(o))
                        .Select(o => $"{_host.Who(o)} {Board.RelationshipOf(me, o)}{(st.MaelleFamily != null && st.MaelleFamily.Contains(o) ? " (her chosen family)" : "")}");
                    var stance = string.IsNullOrEmpty(st.MaelleStance) ? "" : $" · stance {st.MaelleStance}";
                    return $"family: {string.Join("; ", rels.DefaultIfEmpty("no non-Vagabond faction"))}{stance}";
                }
                case "esquie":
                {
                    var names = new[] { "Florrie", "Dorrie", "Soarrie", "Urrie" };
                    var rocks = st.EsquieRocks ?? Array.Empty<RockState>();
                    var home = rocks.Count(r => r != null && r.Status == RockStatus.Held);
                    var parts = new List<string>();
                    for (var i = 0; i < rocks.Length && i < names.Length; i++) parts.Add($"{names[i]} {RockText(rocks[i], i)}");
                    return $"rocks home {home}/4 · {string.Join("; ", parts)}";
                }
                default:
                    return "";
            }
        }

        private string RockText(RockState? r, int i)
        {
            if (r == null) return "unknown";
            var at = r.Clearing is ClearingId c ? Where(c) : "?";
            return r.Status switch
            {
                RockStatus.NotSpawned => i switch { 1 or 2 => "appears in Act II", 3 => "appears in Act III", _ => "not on the map" },
                RockStatus.Hidden => $"hidden in the ruin of {at}",
                RockStatus.Guarded => $"at {at}, guarded by {r.GuardsLeft} {(r.HostSeat is SeatId h ? _host.Who(h) : "host")} warrior{(r.GuardsLeft == 1 ? "" : "s")}",
                RockStatus.Loose => $"loose at {at} (Esquie picks it up after any of his actions there)",
                RockStatus.HeldByRuler => $"held by whoever rules {at}",
                RockStatus.WithVerso => "on Verso's card",
                RockStatus.Held => "home",
                _ => r.Status,
            };
        }
    }
}
