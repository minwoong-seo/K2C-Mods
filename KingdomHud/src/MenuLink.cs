using System;
using System.Reflection;

namespace KingdomHud;

/// <summary>
/// The soldiers' wages and debt from Kingdom Menu (its Soldier wages hardship), read by reflection from its public
/// KingdomMenu.WagesInfo class, so the HUD works with or without Kingdom Menu installed.
/// </summary>
internal static class MenuLink
{
    internal struct Wages
    {
        public int PerDay;
        public int Debt;
        /// <summary>Days until this island's first wages (0 = being paid).</summary>
        public int StartsIn;
        /// <summary>Coins the last payday took, and how long ago it was in game seconds (-1 = none this session).</summary>
        public int LastPaid;
        public float PaidAgo;
    }

    private static bool _looked;
    private static FieldInfo _active, _perDay, _debt, _startsIn, _lastPaid, _lastPaydayTime;

    public static bool TryGetWages(out Wages wages)
    {
        wages = new Wages { PaidAgo = -1f };
        if (!_looked)
        {
            _looked = true;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "KingdomMenu")
                        continue;
                    var type = assembly.GetType("KingdomMenu.WagesInfo");
                    _active = type?.GetField("Active");
                    _perDay = type?.GetField("PerDay");
                    _debt = type?.GetField("Debt");
                    // Newer fields; an older Kingdom Menu just doesn't have them.
                    _startsIn = type?.GetField("StartsIn");
                    _lastPaid = type?.GetField("LastPaid");
                    _lastPaydayTime = type?.GetField("LastPaydayTime");
                    break;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"Couldn't read Kingdom Menu's wages: {e.Message}");
            }
        }

        if (_active == null || _perDay == null || _debt == null || !(bool)_active.GetValue(null))
            return false;
        wages.PerDay = (int)_perDay.GetValue(null);
        wages.Debt = (int)_debt.GetValue(null);
        wages.StartsIn = _startsIn != null ? (int)_startsIn.GetValue(null) : 0;
        wages.LastPaid = _lastPaid != null ? (int)_lastPaid.GetValue(null) : 0;
        var paidAt = _lastPaydayTime != null ? (float)_lastPaydayTime.GetValue(null) : -1f;
        wages.PaidAgo = paidAt >= 0f ? UnityEngine.Time.time - paidAt : -1f;
        return true;
    }
}
