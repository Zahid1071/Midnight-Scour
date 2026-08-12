using UnityEngine;
using UnityEngine.Localization.Settings;
using TMPro;

/// <summary>
/// Attach to the same object as a TMP label. Refreshes when the active locale changes.
/// Prefer Unity's Localize String Event for new UI if you wire it in the editor manually.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizedUILabel : MonoBehaviour
{
    [SerializeField] string entryKey;
    [SerializeField] string[] formatArgs;

    TextMeshProUGUI _label;

    void Awake()
    {
        _label = GetComponent<TextMeshProUGUI>();
        UIFont.Apply(_label);
        // Locale is applied BeforeSceneLoad — refresh here so the first paint is already correct.
        if (LocaleManager.IsReady)
            Refresh();
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        if (LocaleManager.IsReady)
            Refresh();
        else
            LocaleManager.WhenReady(Refresh);
    }

    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    void OnLocaleChanged(UnityEngine.Localization.Locale _) => Refresh();

    public void SetEntryKey(string key)
    {
        entryKey = key;
        if (isActiveAndEnabled)
            Refresh();
    }

    public void Refresh()
    {
        if (_label == null || string.IsNullOrWhiteSpace(entryKey))
            return;

        UIFont.SetText(_label, formatArgs != null && formatArgs.Length > 0
            ? Loc.Format(entryKey, formatArgs)
            : Loc.Get(entryKey));
    }
}
