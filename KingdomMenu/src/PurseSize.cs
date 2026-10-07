using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// The Bigger purse upgrade. The purse is a little physics bag (<see cref="CurrencyBag"/>): its coins are physics bodies
/// that pile up inside a polygon collider, and one that rolls over the rim falls out of the purse (see
/// <see cref="CoinMagnet"/>). This makes the bag itself bigger, walls, rim, lid, drawing and the "fell out" trigger
/// alike, while the coins keep their size, so more of them fit (capacity grows with the bag's area).
///
/// The bag's parts are scaled about the bottom of its cavity in the coins' own frame (the coin container), so the floor
/// never moves under the coins; the container and the parts are then shifted down together so the top of the drawing
/// stays where the game put it on screen. New coins are dropped in from the same relative height above the bigger bag.
/// The bag grows smoothly, and only shrinks (after a reset, or on a save without the upgrade) once the coins in it fit
/// the smaller bag, so changing size can never spill coins.
/// </summary>
internal static class PurseSize
{
    /// <summary>About how many coins the game's own purse holds before spilling (measured from its shape).</summary>
    private const int BaseCapacity = 25;
    private const float GrowPerSecond = 1f;
    private const float ShrinkPerSecond = 0.25f;
    /// <summary>How far (bag units) below the smaller bag's rim every coin must be before it shrinks.</summary>
    private const float ShrinkMargin = 0.15f;

    private sealed class BagState
    {
        public readonly List<(Transform Part, Vector3 Position, Vector3 Scale)> Parts = new();
        public Transform Container;
        public Vector3 ContainerPosition;
        /// <summary>Bottom of the cavity, rim and top of the drawing, in the bag's own (unscaled) units.</summary>
        public float Bottom, Rim, Top;
        public float Scale = 1f;
    }

    private static readonly Dictionary<IntPtr, BagState> Bags = new();

    public static float ScaleFor(int level) => 1f + 0.25f * level;

    /// <summary>Roughly how many coins the purse holds at a level (capacity grows with the bag's area).</summary>
    public static int Capacity(int level)
    {
        var s = ScaleFor(level);
        return (int)Math.Round(BaseCapacity * s * s);
    }

    /// <summary>Grow or shrink each monarch's purse toward their level (every frame, on game time).</summary>
    public static void Tick()
    {
        if (!GameState.Playing())
            return;
        for (var i = 0; i < Coop.MaxPlayers; i++)
        {
            var monarch = Coop.Monarch(i);
            var bag = monarch != null ? monarch.GetCurrencyBag() : null;
            if (bag == null)
                continue;
            var state = StateOf(bag);
            if (state != null)
                Step(bag, state, ScaleFor(UpgradeStore.Level(Upgrades.BiggerPurse, i)), Time.deltaTime);
        }
    }

    /// <summary>
    /// A coin's height in the game's original bag shape (bag units), from its position in the coin container, so
    /// "nearly full" means the same at any purse size.
    /// </summary>
    public static float ShapeHeight(CurrencyBag bag, float containerY)
    {
        if (bag == null || !Bags.TryGetValue(bag.Pointer, out var state) || state == null || state.Scale == 1f)
            return containerY;
        return state.Bottom + (containerY - state.Bottom) / state.Scale;
    }

    /// <summary>A coin was just dropped into this bag at the game's spawn height: lift it to the same spot above the bigger bag.</summary>
    public static void PlaceNewCoin(CurrencyBag bag)
    {
        if (bag == null || !Bags.TryGetValue(bag.Pointer, out var state) || state == null || state.Scale == 1f)
            return;
        var coins = bag._currencyObjects;
        if (coins == null || coins.Count == 0)
            return;
        var coin = coins[coins.Count - 1];
        if (coin == null)
            return;
        var t = coin.transform;
        var p = t.localPosition;
        t.localPosition = new Vector3(p.x, state.Bottom + state.Scale * (p.y - state.Bottom), p.z);
    }

    private static void Step(CurrencyBag bag, BagState state, float target, float deltaTime)
    {
        if (Math.Abs(state.Scale - target) < 0.0001f)
            return;

        var coins = bag._currencyObjects;
        var empty = coins == null || coins.Count == 0;
        float next;
        if (empty)
            next = target; // nothing inside to disturb
        else if (target > state.Scale)
            next = Math.Min(target, state.Scale + GrowPerSecond * deltaTime);
        else
        {
            next = Math.Max(target, state.Scale - ShrinkPerSecond * deltaTime);
            if (!Fits(state, coins, next))
                return; // wait until enough coins have been spent
        }
        Apply(state, next);
    }

    /// <summary>Whether every coin would sit below the rim of the bag at this size (with a margin).</summary>
    private static bool Fits(BagState state, Il2CppSystem.Collections.Generic.List<BagCurrency> coins, float scale)
    {
        var rim = state.Bottom + scale * (state.Rim - state.Bottom) - ShrinkMargin;
        for (var i = 0; i < coins.Count; i++)
        {
            var coin = coins[i];
            if (coin != null && coin.isActiveAndEnabled && coin.transform.localPosition.y > rim)
                return false;
        }
        return true;
    }

    private static void Apply(BagState state, float scale)
    {
        // Parts scale about the cavity's bottom (in the container's frame); everything then moves down by dy so the top
        // of the drawing stays put.
        var dy = (1f - scale) * (state.Top - state.Bottom);
        foreach (var (part, position, size) in state.Parts)
        {
            if (part == null)
                continue;
            part.localPosition = new Vector3(scale * position.x, state.Bottom + scale * (position.y - state.Bottom) + dy, position.z);
            part.localScale = new Vector3(scale * size.x, scale * size.y, size.z);
        }
        if (state.Container != null)
            state.Container.localPosition = state.ContainerPosition + new Vector3(0f, dy, 0f);
        state.Scale = scale;
    }

    /// <summary>The bag's original layout, taken the first time it's seen (before any scaling).</summary>
    private static BagState StateOf(CurrencyBag bag)
    {
        // (A bag rebuilt by the game may reuse a destroyed one's address: then its container is gone and it's re-read.)
        if (Bags.TryGetValue(bag.Pointer, out var known) && (known == null || known.Container != null))
            return known;

        var container = bag._container;
        PolygonCollider2D walls = null;
        foreach (var poly in bag.GetComponentsInChildren<PolygonCollider2D>(true))
        {
            if (!poly.isTrigger && (container == null || !poly.transform.IsChildOf(container)))
            {
                walls = poly;
                break;
            }
        }
        if (container == null || walls == null)
        {
            Bags[bag.Pointer] = null; // not a bag we understand; leave it alone
            Plugin.Logger.LogWarning($"Bigger purse: '{bag.name}' has no coin container or walls; it keeps its size.");
            return null;
        }

        var state = new BagState { Container = container, ContainerPosition = container.localPosition };
        var bagTransform = bag.transform;
        for (var i = 0; i < bagTransform.childCount; i++)
        {
            var child = bagTransform.GetChild(i);
            if (child.Pointer != container.Pointer)
                state.Parts.Add((child, child.localPosition, child.localScale));
        }

        // Rim: the highest point of the walls. Bottom: the lowest point near the middle (the floor of the cavity).
        var path = walls.GetPath(0);
        var offset = walls.transform.localPosition;
        float rim = float.MinValue, bottom = float.MaxValue;
        for (var i = 0; i < path.Length; i++)
        {
            var y = path[i].y + offset.y;
            rim = Math.Max(rim, y);
            if (Math.Abs(path[i].x) < 0.3f)
                bottom = Math.Min(bottom, y);
        }
        state.Rim = rim;
        state.Bottom = bottom < rim ? bottom : rim - 1.1f;

        var front = bag._front;
        state.Top = front != null && front.sprite != null
            ? front.transform.localPosition.y + front.sprite.bounds.max.y * front.transform.localScale.y
            : state.Rim + 0.2f;

        Bags[bag.Pointer] = state;
        Plugin.Logger.LogInfo($"Bigger purse: '{bag.name}' cavity {state.Bottom:0.###}..{state.Rim:0.###}, top {state.Top:0.###}, {state.Parts.Count} parts.");
        return state;
    }
}

/// <summary>New coins drop into the purse from a fixed height; in a bigger purse they start from above its rim instead.</summary>
[HarmonyPatch(typeof(CurrencyBag), nameof(CurrencyBag.SpawnCurrency))]
internal static class PurseSpawnPatch
{
    private static void Postfix(CurrencyBag __instance) => PurseSize.PlaceNewCoin(__instance);
}


