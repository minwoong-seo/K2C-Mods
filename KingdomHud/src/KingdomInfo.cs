using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomHud;

/// <summary>Bank and calendar facts read from the game's own state.</summary>
internal static class KingdomInfo
{
    public static Kingdom GetKingdom()
    {
        if (Managers.InstExists && Managers.Inst != null && Managers.Inst.kingdom != null)
            return Managers.Inst.kingdom;
        return Object.FindObjectOfType<Kingdom>();
    }

    /// <summary>True while actually playing a kingdom (not on the title screen or in a loading transition).</summary>
    public static bool InKingdom()
    {
        var game = Managers.InstExists && Managers.Inst != null ? Managers.Inst.game : null;
        return game != null && game.InPlayableState;
    }

    /// <summary>
    /// The banker's stash and the interest it will add at the next dawn. Mirrors Banker.HandleOnDayStart:
    /// interest = min(maxInterest, ceil(stash * dailyInterest)). fullAt is the stash that earns the maximum.
    /// </summary>
    public static bool TryGetBank(out int stash, out int interest, out int maxInterest, out int fullAt)
    {
        stash = interest = maxInterest = fullAt = 0;
        var banker = Object.FindObjectOfType<Banker>();
        if (banker == null || !banker.isActiveAndEnabled)
            return false;

        stash = banker._stashedCoins;
        maxInterest = banker.maxInterest;
        interest = Math.Min(maxInterest, (int)Math.Ceiling(stash * (double)banker.dailyInterest));
        fullAt = banker.dailyInterest > 0f ? (int)Math.Floor((maxInterest - 1) / (double)banker.dailyInterest + 1e-6) + 1 : 0;
        return true;
    }

    /// <summary>Days until the next blood moon (boss day): 0 = tonight. Null if none is scheduled soon.</summary>
    public static int? DaysUntilBloodMoon(int lookAhead = 60)
    {
        var director = Managers.InstExists && Managers.Inst != null ? Managers.Inst.director : null;
        if (director == null)
            return null;

        try
        {
            var today = director.TotalDaysInReign;
            for (var n = 0; n <= lookAhead; n++)
            {
                if (director.GetDayTypeForDay(today + n) == Day.DayType.bossDay)
                    return n;
            }
        }
        catch (Exception)
        {
            // Some islands have no day cycle data (e.g. while it's being set up); just show nothing.
        }
        return null;
    }

    /// <summary>
    /// Farmer slots from the farms: each built farmhouse allows CurrentMaxFarmlands() plots (its maxFarmlands, plus the
    /// farmer statue's bonus once active), and each plot is worked by one farmer. Stables are farmhouses too; skipped.
    /// </summary>
    public static int FarmSlots()
    {
        var slots = 0;
        foreach (var obj in Object.FindObjectsByType(Il2CppInterop.Runtime.Il2CppType.Of<Farmhouse>(), FindObjectsSortMode.None))
        {
            var farm = obj.TryCast<Farmhouse>();
            if (farm != null && farm.isActiveAndEnabled && !farm.isStable)
                slots += farm.CurrentMaxFarmlands();
        }
        return slots;
    }

    /// <summary>
    /// Villagers waiting in cottages (CitizenHousePayable). Each refills one villager every _cooldownOfSpawning seconds
    /// up to its citizen slots; paying it takes one. nextIn is the soonest refill, or -1 if all are full, and cooldown
    /// the full refill time of that cottage (for a progress bar).
    /// </summary>
    public static bool TryGetCottages(out int ready, out int max, out float nextIn, out float cooldown)
    {
        ready = max = 0;
        nextIn = -1f;
        cooldown = 0f;
        foreach (var obj in Object.FindObjectsByType(Il2CppInterop.Runtime.Il2CppType.Of<CitizenHousePayable>(), FindObjectsSortMode.None))
        {
            var house = obj.TryCast<CitizenHousePayable>();
            if (house == null || !house.isActiveAndEnabled || house.citizens == null)
                continue;
            var slots = house.citizens.Count;
            if (slots <= 0)
                continue;
            var available = house._numberOfAvailableCitizens;
            ready += available;
            max += slots;
            if (available < slots)
            {
                var left = Mathf.Max(0f, house._cooldownOfSpawning - house._timePassedSinceLastSpawn);
                if (nextIn < 0f || left < nextIn)
                {
                    nextIn = left;
                    cooldown = house._cooldownOfSpawning;
                }
            }
        }
        return max > 0;
    }

    /// <summary>Local co-op with the screen currently split (top/bottom by default, or side by side).</summary>
    public static bool Split
    {
        get
        {
            var kingdom = GetKingdom();
            var p2 = kingdom != null ? kingdom.playerTwo : null;
            return p2 != null && p2.isActiveAndEnabled && CameraMarshaller.InstExists && CameraMarshaller.Inst != null
                   && CameraMarshaller.Inst.camerasCurrentlySplit;
        }
    }

    /// <summary>
    /// The part of the screen showing a player, in pixels from the top-left corner; the whole screen if not split.
    /// Split screen: their camera's viewport if the game splits that way, else their half (top/bottom, or left/right
    /// when the split has been switched to side by side).
    /// </summary>
    public static (float left, float top, float width, float height) Region(int index)
    {
        float w = Screen.width, h = Screen.height;
        if (!Split)
            return (0f, 0f, w, h);

        try
        {
            var camera = CameraMarshaller.Inst.GetCameraForPlayerID(index)?._camera;
            if (camera != null)
            {
                var r = camera.rect;
                if (r.width < 0.98f || r.height < 0.98f)
                    return (r.x * w, (1f - r.y - r.height) * h, r.width * w, r.height * h);
            }
        }
        catch (Exception)
        {
            // Fall back to the halves below.
        }

        var first = index == (Plugin.Player1OnTop.Value ? 0 : 1);
        bool sideBySide;
        try { sideBySide = !CameraMarshaller.Inst.IsSplitOrientationHorizontal(); }
        catch (Exception) { sideBySide = false; }
        if (sideBySide)
            return first ? (0f, 0f, w / 2f, h) : (w / 2f, 0f, w / 2f, h);
        return first ? (0f, 0f, w, h / 2f) : (0f, h / 2f, w, h / 2f);
    }

    public static bool IsNight()
    {
        var director = Managers.InstExists && Managers.Inst != null ? Managers.Inst.director : null;
        return director != null && director.IsNight;
    }
}
