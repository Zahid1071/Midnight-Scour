using UnityEngine;

/// <summary>
/// Legacy hook — language selection is handled by <see cref="SettingsSelector"/> with
/// <see cref="SettingsSelector.SettingType.Language"/>. Kept for prefabs that still reference it.
/// </summary>
public class SettingsLanguageSpinner : MonoBehaviour
{
    [SerializeField] SettingsSelector selector;

    void Awake()
    {
        if (selector == null)
            selector = GetComponentInChildren<SettingsSelector>(true);
    }
}
