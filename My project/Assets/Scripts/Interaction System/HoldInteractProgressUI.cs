using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space circular fill shown while the player holds to complete an interaction.
/// Created at runtime under the HUD canvas; no scene wiring required.
/// </summary>
public class HoldInteractProgressUI : MonoBehaviour
{
    public static HoldInteractProgressUI Instance { get; private set; }

    [SerializeField] float ringSize = 72f;
    [SerializeField] float ringThickness = 8f;
    [SerializeField] Color trackColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] Color fillColor = new Color(0.92f, 0.92f, 0.9f, 0.95f);
    [Tooltip("Centered on the crosshair so the ring replaces the hand icon instead of sitting under it.")]
    [SerializeField] Vector2 screenOffset = Vector2.zero;

    RectTransform _root;
    Image _track;
    Image _fill;
    Sprite _circleSprite;
    Texture2D _circleTex;
    bool _visible;

    public static HoldInteractProgressUI Ensure()
    {
        if (Instance != null)
            return Instance;

        Canvas canvas = null;
        if (HudManager.Instance != null)
            canvas = HudManager.Instance.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return null;

        var go = new GameObject("HoldInteractProgressUI", typeof(RectTransform), typeof(HoldInteractProgressUI));
        go.transform.SetParent(canvas.transform, false);
        var ui = go.GetComponent<HoldInteractProgressUI>();
        ui.Build(canvas);
        return ui;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (_circleTex != null)
            Destroy(_circleTex);
    }

    void Build(Canvas canvas)
    {
        _circleSprite = CreateCircleSprite();

        _root = gameObject.GetComponent<RectTransform>();
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = screenOffset;
        _root.sizeDelta = new Vector2(ringSize, ringSize);

        _track = CreateRingChild("Track", trackColor, fillAmount: 1f);
        _fill = CreateRingChild("Fill", fillColor, fillAmount: 0f);
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Radial360;
        _fill.fillOrigin = (int)Image.Origin360.Top;
        _fill.fillClockwise = true;

        // Hollow look: shrink fill/track via a simple radial mask by using filled images only.
        // Track is a full dim circle; fill draws on top as a radial wipe.
        SetVisible(false);
    }

    Image CreateRingChild(string name, Color color, float fillAmount)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_root, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.sprite = _circleSprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.type = Image.Type.Simple;
        img.fillAmount = fillAmount;
        return img;
    }

    Sprite CreateCircleSprite()
    {
        const int size = 64;
        _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _circleTex.wrapMode = TextureWrapMode.Clamp;
        _circleTex.filterMode = FilterMode.Bilinear;

        float outer = (size - 1) * 0.5f;
        float inner = Mathf.Max(0f, outer - ringThickness * (size / ringSize));
        Vector2 center = new Vector2(outer, outer);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float a = 0f;
                if (dist <= outer && dist >= inner)
                {
                    // Soft edge
                    float outerEdge = Mathf.Clamp01(outer - dist);
                    float innerEdge = Mathf.Clamp01(dist - inner);
                    a = Mathf.Clamp01(Mathf.Min(outerEdge, innerEdge) * 2f);
                }
                else if (dist < inner)
                {
                    a = 0f;
                }

                _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        _circleTex.Apply(false, false);
        return Sprite.Create(_circleTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    public void SetProgress(float t)
    {
        EnsureBuilt();
        if (_fill != null)
            _fill.fillAmount = Mathf.Clamp01(t);
    }

    public void SetVisible(bool visible)
    {
        EnsureBuilt();
        _visible = visible;
        if (_root != null)
            _root.gameObject.SetActive(visible);
        if (!visible && _fill != null)
            _fill.fillAmount = 0f;
    }

    public bool IsVisible => _visible;

    void EnsureBuilt()
    {
        if (_root != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && HudManager.Instance != null)
            canvas = HudManager.Instance.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
            Build(canvas);
    }
}
