using System;
using Root33.Core;
#if !NOUNITY && !ROOT_MONO
using Il2CppInterop.Runtime;
using tuber.canis.data.winConditions;
#endif

namespace Root33.Plugin.Adapter
{
    /// <summary>
    /// do.declare-winner on Root's own extra win condition: each player has TuberPlayerEntity.altWinCondition, which Root
    /// tests with its score and dominance wins. The seat gets an AdHocWinCondition that is always met, joined to any condition
    /// it already had with WinConditionUtils.Or, so Root ends the game with that seat's win at its next winner check.
    /// </summary>
    internal static class RootWins
    {
#if !NOUNITY && !ROOT_MONO
        public static void Declare(SeatId seat)
        {
            var player = RootGame.Player(seat);
            var check = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Canis.context.Context, bool>>(
                new Func<Canis.context.Context, bool>(_ => true));
            IWinCondition won = new AdHocWinCondition(check).Cast<IWinCondition>();
            var before = player.altWinCondition;
            player.altWinCondition = before == null ? won : WinConditionUtils.Or(before, won).Cast<IWinCondition>();
        }
#else
        public static void Declare(SeatId seat) =>
            throw new NotSupportedException("Root 33: do.declare-winner needs Root (BepInEx 6 IL2CPP build)");
#endif
    }
}
