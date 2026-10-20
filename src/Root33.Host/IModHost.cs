using System.Collections.Generic;
using Root33.Core;

namespace Root33.Host
{
    /// <summary>
    /// What a host must supply so Root33.Host can show the mod the same way everywhere. The hosts are the text tabletop
    /// (src/Root33.Sim, SimHost over a SimGame) and the in-game plugin (src/Root33.Plugin, RootHost over Root's live game).
    /// Everything else comes from Root33.Core: the board (<see cref="IBoardView"/>) and the game
    /// (<see cref="Root33.Core.Engine.GameContext"/>).
    ///
    /// Seats are the core's <see cref="SeatId"/> values (0-based). A host names them for players as it likes in
    /// <see cref="Who"/>; <see cref="ModText.SeatLabel"/> is the standard form ("seat 2 (The Paintress)").
    /// </summary>
    public interface IModHost
    {
        /// <summary>Seats at the table (2-4).</summary>
        int SeatCount { get; }

        /// <summary>Every seat in turn order, starting with the first player.</summary>
        IReadOnlyList<SeatId> TurnOrder();

        /// <summary>The character id the seat plays (characters.json), or null for a seat playing plain Root.</summary>
        string? CharacterOf(SeatId seat);

        /// <summary>The title Root 33 gave the seat with do.set-seat-title, or null when it has none
        /// (<see cref="ModText.SeatTitle(IModHost, SeatId)"/> then uses the character's name).</summary>
        string? TitleOf(SeatId seat);

        /// <summary>How the host names a seat in running text (banners, bubbles, the Goal Tracker).</summary>
        string Who(SeatId seat);

        /// <summary>The seat's VP (undefined while it has an active dominance card, as read.vp).</summary>
        int Vp(SeatId seat);

        /// <summary>Whether the seat has activated a dominance card.</summary>
        bool HasActiveDominance(SeatId seat);

        /// <summary>Whether the seat (a Vagabond) is in a coalition.</summary>
        bool InCoalition(SeatId seat);

        /// <summary>The round the host shows (bubbles and story records are stamped with it).</summary>
        int Round { get; }

        /// <summary>The spoiler mode the host shows names in (characters.json safeName or finishedName).</summary>
        SpoilerMode Spoilers { get; }

        /// <summary>A clearing as players read it. <see cref="ModText.ClearingName"/> is the standard form on Autumn.</summary>
        string ClearingName(ClearingId c);

        /// <summary>The countdown banners up now (do.show-banner, not yet do.clear-banner): seat and win condition id.</summary>
        IReadOnlyList<KeyValuePair<SeatId, string>> Banners();
    }
}
