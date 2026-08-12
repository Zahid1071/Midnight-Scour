using UnityEngine;

/// <summary>
/// Placeholder for future lockpick extra-life / progress retention (not active yet).
/// Prefs are cleared on new game so world pickup always respawns for now.
/// </summary>
public static class LockpickExtraLife
{
    public const string PrefsKey = "meta_lockpick_extra_life";

    /// <summary>Wipe any leftover prefs from earlier tests. Call on New Game / restart.</summary>
    public static void Clear()
    {
        if (!PlayerPrefs.HasKey(PrefsKey))
            return;

        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Midnight Scour/Debug/Clear Lockpick Extra Life")]
    static void DebugClear()
    {
        Clear();
        Debug.Log("[LockpickExtraLife] Cleared.");
    }
#endif
}
