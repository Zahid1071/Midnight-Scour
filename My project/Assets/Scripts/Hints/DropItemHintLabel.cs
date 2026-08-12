using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// Localizes the drop-item hint panel authored under Gameplay Hint Box.
/// Expects a manually built hierarchy: Hint Row / Prefix Text, G Key, Suffix Text.
/// hint.drop_item uses {0} as the badge slot; controls.key.drop fills the badge label.
/// </summary>
[DisallowMultipleComponent]
public class DropItemHintLabel : MonoBehaviour
{
    const string MessageKey = "hint.drop_item";
    const string KeyKey = "controls.key.drop";
    const string Placeholder = "{0}";

    [SerializeField] RectTransform hintRow;
    [SerializeField] TextMeshProUGUI prefixText;
    [SerializeField] TextMeshProUGUI suffixText;
    [SerializeField] TextMeshProUGUI keyLabel;

    Coroutine _layoutRoutine;

    void Awake()
    {
        if (hintRow == null)
            hintRow = transform.Find("Hint Row") as RectTransform;
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        if (LocaleManager.IsReady)
            Refresh();
        else
            LocaleManager.WhenReady(Refresh);
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        StopLayoutRoutine();
    }

    void OnLocaleChanged(UnityEngine.Localization.Locale _) => Refresh();

    public void Refresh()
    {
        if (prefixText == null || suffixText == null)
            return;

        SplitLocalizedMessage(Loc.Get(MessageKey), out string prefix, out string suffix);

        SetSegment(prefixText, prefix);
        SetSegment(suffixText, suffix);

        if (keyLabel != null)
            UIFont.SetText(keyLabel, Loc.Get(KeyKey));

        RebuildLayout();
        ScheduleLayoutRebuild();
    }

    void SetSegment(TextMeshProUGUI label, string text)
    {
        bool visible = !string.IsNullOrEmpty(text);
        label.gameObject.SetActive(visible);
        if (!visible)
            return;

        UIFont.SetText(label, text);
    }

    void RebuildLayout()
    {
        if (hintRow == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(hintRow);
    }

    void ScheduleLayoutRebuild()
    {
        if (!isActiveAndEnabled)
            return;

        StopLayoutRoutine();
        _layoutRoutine = StartCoroutine(RebuildLayoutNextFrame());
    }

    IEnumerator RebuildLayoutNextFrame()
    {
        yield return null;
        RebuildLayout();
        _layoutRoutine = null;
    }

    void StopLayoutRoutine()
    {
        if (_layoutRoutine == null)
            return;

        StopCoroutine(_layoutRoutine);
        _layoutRoutine = null;
    }

    static void SplitLocalizedMessage(string template, out string prefix, out string suffix)
    {
        if (string.IsNullOrEmpty(template))
        {
            prefix = string.Empty;
            suffix = string.Empty;
            return;
        }

        int index = template.IndexOf(Placeholder, System.StringComparison.Ordinal);
        if (index < 0)
        {
            prefix = template;
            suffix = string.Empty;
            return;
        }

        prefix = template.Substring(0, index);
        suffix = template.Substring(index + Placeholder.Length);
    }
}
