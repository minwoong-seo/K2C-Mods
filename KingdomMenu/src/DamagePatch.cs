using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomMenu;

/// <summary>
/// Every hit in the game goes through Damageable.ReceiveDamage(int, GameObject, DamageSource, Vector2)
/// (the 3-argument overload forwards to it), and the int is the hit points removed. Arrows pass their archer as
/// the damager, knights/pikemen pass themselves, so the damage can be scaled here by who dealt it and who took it.
/// </summary>
[HarmonyPatch(typeof(Damageable), nameof(Damageable.ReceiveDamage), new[] { typeof(int), typeof(GameObject), typeof(DamageSource), typeof(Vector2) })]
internal static class DamagePatch
{
    public static float ArcherMultiplier = 1f;
    public static float SoldierMultiplier = 1f;
    public static float WallDamageMultiplier = 1f;

    private static readonly System.Random Rng = new();
    private static bool _loggedError;

    private static void Prefix(Damageable __instance, ref int __0, GameObject __1)
    {
        if (__0 <= 0 || (ArcherMultiplier == 1f && SoldierMultiplier == 1f && WallDamageMultiplier == 1f))
            return;

        try
        {
            var multiplier = 1f;
            if (__1 != null)
            {
                if (ArcherMultiplier != 1f && __1.GetComponent<Archer>() != null)
                    multiplier *= ArcherMultiplier;
                else if (SoldierMultiplier != 1f && IsSoldier(__1))
                    multiplier *= SoldierMultiplier;
            }

            if (WallDamageMultiplier != 1f && __instance != null && __instance.GetComponentInParent<Wall>() != null)
                multiplier *= WallDamageMultiplier;

            if (multiplier != 1f)
                __0 = RoundRandom(__0 * multiplier);
        }
        catch (Exception e)
        {
            if (!_loggedError)
            {
                Plugin.Logger.LogError($"Damage upgrade failed: {e}");
                _loggedError = true;
            }
        }
    }

    private static bool IsSoldier(GameObject go) =>
        go.GetComponent<Knight>() != null || go.GetComponent<Pikeman>() != null ||
        go.GetComponent<Berserker>() != null || go.GetComponent<Ninja>() != null;

    /// <summary>Hit points are small integers, so fractions are kept on average: 1.5 is 1 or 2 with equal odds.</summary>
    private static int RoundRandom(float value)
    {
        var whole = (int)Math.Floor(value);
        return Rng.NextDouble() < value - whole ? whole + 1 : whole;
    }
}
