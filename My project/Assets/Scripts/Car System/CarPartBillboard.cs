using UnityEngine;

/// <summary>
/// Faces the camera, drives outline/tint, and fades by a hard distance gate.
/// Optionally requires the car hood to be open before any UI is shown (including fuel).
/// Within range + hood open = fully on (fades in). Otherwise fully off (fades out).
/// </summary>
[DisallowMultipleComponent]
public class CarPartBillboard : MonoBehaviour
{
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField] CarPartSlot slot;
    [SerializeField] Transform billboardRoot;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] bool faceCamera = true;
    [SerializeField] bool hideWhenInstalled = false;

    [Header("Hood gate")]
    [Tooltip("If set, billboard stays fully off until this hood panel is open.")]
    [SerializeField] CarHingePanel requireOpenHood;

    [Header("Distance fade (on/off gate)")]
    [Tooltip("Fully on when the viewer is at or closer than this distance (meters).")]
    [SerializeField] float visibleDistance = 3f;
    [Tooltip("Seconds to fade between fully hidden and fully visible when crossing the gate.")]
    [SerializeField] float fadeDuration = 0.35f;
    [SerializeField] bool usePlayerTag = true;
    [SerializeField] string playerTag = "Player";

    [Header("Colors")]
    [SerializeField] Color missingOutline = new Color(0.92f, 0.18f, 0.16f, 1f);
    [SerializeField] Color missingGlow = new Color(0.92f, 0.18f, 0.16f, 0.45f);
    [SerializeField] Color missingTint = Color.white;
    [SerializeField] Color installedOutline = new Color(0.35f, 0.85f, 0.4f, 1f);
    [SerializeField] Color installedGlow = new Color(0.35f, 0.85f, 0.4f, 0.35f);
    [SerializeField] Color installedTint = new Color(0.85f, 1f, 0.88f, 1f);

    Camera _cam;
    Transform _viewer;
    Material _matInstance;
    bool _lastInstalled;
    float _fade;
    float _appliedFade = -1f;

    void Awake()
    {
        if (slot == null)
            slot = GetComponentInChildren<CarPartSlot>(true);
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (billboardRoot == null)
            billboardRoot = spriteRenderer != null ? spriteRenderer.transform : transform;

        if (requireOpenHood == null)
        {
            var car = transform.root;
            var hinges = car.GetComponentsInChildren<CarHingePanel>(true);
            for (int i = 0; i < hinges.Length; i++)
            {
                if (hinges[i].Kind == CarHingePanel.PanelKind.Hood)
                {
                    requireOpenHood = hinges[i];
                    break;
                }
            }
        }

        EnsureMaterialInstance();
        _fade = ShouldShow() ? 1f : 0f;
        ApplyVisuals(force: true);
    }

    void OnEnable()
    {
        if (requireOpenHood != null)
            requireOpenHood.OnToggled += HandleHoodToggled;
    }

    void OnDisable()
    {
        if (requireOpenHood != null)
            requireOpenHood.OnToggled -= HandleHoodToggled;
    }

    void OnDestroy()
    {
        if (_matInstance != null)
            Destroy(_matInstance);
    }

    void HandleHoodToggled(CarHingePanel panel, bool open)
    {
        if (!open)
        {
            // Closing the hood: kill UI immediately (don't wait for the panel animation).
            _fade = 0f;
            ApplyVisuals(force: true);
            return;
        }

        // Opening: allow normal fade-in on subsequent LateUpdates.
        _appliedFade = -1f;
    }

    void LateUpdate()
    {
        float target = ShouldShow() ? 1f : 0f;
        // Closing / out of range still fades, but hood-close already snapped to 0 above.
        float step = fadeDuration <= 0.0001f ? 1f : Time.deltaTime / fadeDuration;
        _fade = Mathf.MoveTowards(_fade, target, step);

        ApplyVisuals(force: false);

        if (!faceCamera || billboardRoot == null || !billboardRoot.gameObject.activeInHierarchy)
            return;
        if (_fade <= 0.001f)
            return;

        if (_cam == null)
            _cam = Camera.main;
        if (_cam == null)
            return;

        Vector3 toCam = billboardRoot.position - _cam.transform.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;
        billboardRoot.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    bool ShouldShow()
    {
        if (requireOpenHood != null && !requireOpenHood.IsOpen)
            return false;
        return IsViewerInRange();
    }

    bool IsViewerInRange()
    {
        var viewer = ResolveViewer();
        if (viewer == null || billboardRoot == null)
            return true;
        return Vector3.Distance(viewer.position, billboardRoot.position) <= visibleDistance;
    }

    void EnsureMaterialInstance()
    {
        if (spriteRenderer == null || _matInstance != null)
            return;
        if (spriteRenderer.sharedMaterial == null)
            return;
        _matInstance = new Material(spriteRenderer.sharedMaterial);
        spriteRenderer.material = _matInstance;
    }

    Transform ResolveViewer()
    {
        if (_viewer != null)
            return _viewer;

        if (usePlayerTag)
        {
            var player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                _viewer = player.transform;
                return _viewer;
            }
        }

        if (_cam == null)
            _cam = Camera.main;
        if (_cam != null)
            _viewer = _cam.transform;
        return _viewer;
    }

    void ApplyVisuals(bool force)
    {
        bool installed = slot != null && slot.IsInstalled();

        if (!force && installed == _lastInstalled && Mathf.Abs(_fade - _appliedFade) < 0.005f)
            return;

        _lastInstalled = installed;
        _appliedFade = _fade;

        if (hideWhenInstalled && installed)
        {
            if (spriteRenderer != null)
                spriteRenderer.gameObject.SetActive(false);
            return;
        }

        if (spriteRenderer != null && !spriteRenderer.gameObject.activeSelf)
            spriteRenderer.gameObject.SetActive(true);

        EnsureMaterialInstance();
        if (_matInstance == null || spriteRenderer == null)
            return;

        Color outline = installed ? installedOutline : missingOutline;
        Color glow = installed ? installedGlow : missingGlow;
        Color tint = installed ? installedTint : missingTint;

        outline.a *= _fade;
        glow.a *= _fade;
        tint.a *= _fade;

        _matInstance.SetColor(OutlineColorId, outline);
        _matInstance.SetColor(GlowColorId, glow);
        _matInstance.SetColor(ColorId, tint);
        spriteRenderer.color = tint;
        spriteRenderer.enabled = _fade > 0.01f;
    }

#if UNITY_EDITOR
    public void EditorBind(CarPartSlot partSlot, Transform root, SpriteRenderer renderer)
    {
        slot = partSlot;
        billboardRoot = root;
        spriteRenderer = renderer;
    }

    public void EditorSetRequireOpenHood(CarHingePanel hood)
    {
        requireOpenHood = hood;
    }
#endif
}
