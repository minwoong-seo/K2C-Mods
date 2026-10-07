using UnityEngine;

namespace KingdomMenu;

internal static class GameState
{
    /// <summary>
    /// True while actually playing a kingdom. The title screen also runs a live background kingdom
    /// (with real units and a player), so the presence of a Kingdom alone isn't enough.
    /// </summary>
    public static bool Playing()
    {
        var game = Game();
        return game != null && game.InPlayableState;
    }

    public static Game Game() => Managers.InstExists && Managers.Inst != null ? Managers.Inst.game : null;
}
