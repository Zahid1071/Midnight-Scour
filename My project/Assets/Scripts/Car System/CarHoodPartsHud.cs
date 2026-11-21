using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// When the car hood is open, shows simple icons for still-missing install parts.
/// Uses each <see cref="CarPartSlot"/>'s required item icon.
/// </summary>
[DisallowMultipleComponent]
public class CarHoodPartsHud : MonoBehaviour
{
    [SerializeField] CarHingePanel hood;
    [SerializeField] CarPartSlot[] slots;
    [SerializeField] float iconSize = 56f;
    [SerializeField] float spacing = 14f;
    [SerializeField] Vector2 screenOffset = new Vector2(0f, 120f);
    [SerializeField] Color missingTint = Color.white;
    [SerializeField] Color installedTint = new Color(0.35f, 0.85f, 0.4f, 0.85f);

    Canvas _canvas;
    RectTransform _root;
    readonly List<Image> _icons = new List<Image>();
    readonly List<CarPartSlot> _boundSlots = new List<CarPartSlot>();
    bool _visible;

    void Awake()
    {
        if (hood == null)
            hood = GetComponentInChildren<CarHingePanel>(true);
        if (slots == null || slots.Length == 0)
            slots = GetComponentsInChildren<CarPartSlot>(true);
    }

    void OnEnable()
    {
        if (hood != null)
            hood.OnToggled += HandleHood;
    }

    void OnDisable()
    {
        if (hood != null)
            hood.OnToggled -= HandleHood;
        SetVisible(false);
    }

    void Start()
    {
        EnsureUi();
        SetVisible(hood != null && hood.IsOpen);
        Refresh();
    }

    void Update()
    {
        if (_visible)
            Refresh();
    }

    void HandleHood(CarHingePanel panel, bool open)
    {
        if (panel != hood)
            return;
        SetVisible(open);
        if (open)
            Refresh();
    }

    void EnsureUi()
    {
        if (_root != null)
            return;

        if (HudManager.Instance != null)
            _canvas = HudManager.Instance.GetComponentInParent<Canvas>();
        if (_canvas == null)
            _canvas = FindFirstObjectByType<Canvas>();
        if (_canvas == null)
            return;

        var go = new GameObject("CarHoodPartsHud", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        _root = go.GetComponent<RectTransform>();
        _root.anchorMin = new Vector2(0.5f, 0f);
        _root.anchorMax = new Vector2(0.5f, 0f);
        _root.pivot = new Vector2(0.5f, 0f);
        _root.anchoredPosition = screenOffset;

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = spacing;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _boundSlots.Clear();
        _icons.Clear();
        if (slots == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.requiredItem == null)
                continue;

            var iconGo = new GameObject(slot.requiredItem.name + "_Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(_root, false);
            var rt = iconGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            var img = iconGo.GetComponent<Image>();
            img.sprite = slot.requiredItem.icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = missingTint;

            _boundSlots.Add(slot);
            _icons.Add(img);
        }

        _root.gameObject.SetActive(false);
    }

    void Refresh()
    {
        EnsureUi();
        if (_root == null)
            return;

        bool anyMissing = false;
        for (int i = 0; i < _icons.Count; i++)
        {
            var slot = _boundSlots[i];
            bool installed = slot != null && slot.IsInstalled();
            _icons[i].enabled = true;
            _icons[i].color = installed ? installedTint : missingTint;
            // Dim installed; keep missing bright. Hide installed entirely for "shopping list" feel.
            _icons[i].gameObject.SetActive(!installed);
            if (!installed)
                anyMissing = true;
        }

        if (_visible && !anyMissing)
        {
            // All done — still show nothing once complete
        }
    }

    void SetVisible(bool visible)
    {
        _visible = visible;
        EnsureUi();
        if (_root != null)
            _root.gameObject.SetActive(visible);
    }
}
