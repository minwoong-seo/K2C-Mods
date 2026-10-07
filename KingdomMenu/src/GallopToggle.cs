using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// The Gallop toggle upgrade: one tap of the gallop key starts galloping and the next tap stops it, instead of holding
/// the key down.
///
/// Player.UpdateActionState gets the gallop key every frame as "pressed this frame" (startSprint), "released this frame"
/// (stopSprint), held, and double-tapped. While walking, a press starts a gallop (Player.TryToGallop); while galloping,
/// a release ends it, and holding isn't checked at all. So the toggle lets the first press through, hides the release
/// while it's on, and turns the next press into a release. When the gallop ends by itself (you stop or turn, or the
/// steed tires), the toggle goes off with it, so the next tap starts a new gallop rather than "stopping" one that's
/// already over. Held and double-tap are left alone: holding the key while standing is what triggers some steeds'
/// abilities.
/// </summary>
internal static class GallopToggle
{
    internal struct State
    {
        public bool On;
        public float Since;
    }

    /// <summary>A press starts the gallop on that frame or soon after; until then it isn't "over".</summary>
    internal const float StartGrace = 0.3f;

    private static readonly Dictionary<IntPtr, State> States = new();

    /// <summary>Per-save flag set while a player has switched their bought toggle off (UpgradesPage's switch).</summary>
    public static string OffFlag(int player) => Upgrades.GallopToggle.Key(player) + "_off";

    public static bool Enabled(int player) =>
        UpgradeStore.Level(Upgrades.GallopToggle, player) > 0 && !UpgradeStore.Flag(OffFlag(player));

    public static void Before(Player player, ref bool startSprint, ref bool stopSprint)
    {
        if (player == null)
            return;
        var p1 = Coop.Monarch(0);
        var p2 = Coop.Monarch(1);
        var index = p1 != null && p1.Pointer == player.Pointer ? 0 : p2 != null && p2.Pointer == player.Pointer ? 1 : -1;
        if (index < 0 || !Enabled(index))
        {
            States.Remove(player.Pointer);
            return;
        }

        States.TryGetValue(player.Pointer, out var state);
        Decide(ref state, ref startSprint, ref stopSprint, player.actionState == Player.ActionState.Run, Time.time);
        States[player.Pointer] = state;
    }

    /// <summary>The toggle itself, kept apart from the game so it can be checked on its own.</summary>
    internal static void Decide(ref State state, ref bool start, ref bool stop, bool galloping, float now)
    {
        // The gallop ended by itself (stopped, turned, tired steed): the toggle goes off with it.
        if (state.On && !galloping && now - state.Since > StartGrace)
            state.On = false;

        if (start)
        {
            if (state.On)
            {
                // Second tap: stop, as if the key had been let go.
                state.On = false;
                start = false;
                stop = true;
            }
            else
            {
                // First tap: the game starts the gallop on the press, as usual.
                state.On = true;
                state.Since = now;
            }
        }
        else if (stop && state.On)
        {
            stop = false; // letting go of the key doesn't stop it
        }
    }
}

/// <summary>Turns the gallop key into a toggle for players who bought (and switched on) the Gallop toggle upgrade.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.UpdateActionState))]
internal static class GallopTogglePatch
{
    private static void Prefix(Player __instance, ref bool startSprint, ref bool stopSprint) =>
        GallopToggle.Before(__instance, ref startSprint, ref stopSprint);
}
