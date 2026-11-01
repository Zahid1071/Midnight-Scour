using System.Collections;
using FS_Atmo;
using UnityEngine;
using UnityEngine.UI;

public class InteractionUIManager : MonoBehaviour
{
    public static InteractionUIManager Instance { get; private set; }

    private enum CrosshairState
    {
        Default,
        Interact,
        Door,
        OutOfReach
    }

    [Header("Detection Settings")]
    [SerializeField] private Transform cameraTransform;

    [Header("Crosshair")]
    [Tooltip("Parent under HUD (e.g. Crosshair). Children are resolved by name if images are unset.")]
    [SerializeField] private RectTransform crosshairRoot;
    [SerializeField] private Image defaultCrosshairImage;
    [SerializeField] private Image interactCrosshairImage;
    [SerializeField] private Image doorCrosshairImage;
    [SerializeField] private Image outOfReachCrosshairImage;
    [Tooltip("Small dot that stays on while no other crosshair or blocking UI is showing.")]
    [SerializeField] private Image dotCrosshairImage;

    [Header("Button Prompt")]
    [SerializeField] private Image buttonPromptImage;
    [SerializeField] private Sprite buttonPromptSprite;

    [Header("Interact feedback")]
    [SerializeField] private float shakeDuration = 0.22f;
    [SerializeField] private float failShakeStrength = 6f;
    [SerializeField] private float successScalePeak = 1.22f;

    [Header("Crosshair Fade")]
    [Tooltip("Fade the default crosshair (nothing targeted) out instead of showing it.")]
    [SerializeField] private bool fadeDefaultCrosshair = true;
    [Min(0.01f)]
    [SerializeField] private float defaultFadeDuration = 0.35f;
    [Tooltip("Fade-in of the interact / door / out-of-reach crosshair when it appears after nothing was targeted.")]
    [Min(0.01f)]
    [SerializeField] private float targetFadeInDuration = 0.2f;

    private GameObject lastDetectedObject;
    private PlayerHider _cachedPlayerHider;
    private CrosshairState _crosshairState = CrosshairState.Default;
    private Coroutine shakeRoutine;
    private Vector2 crosshairRestPos;
    private Vector3 crosshairRestScale = Vector3.one;
    private static int doorLayer = -1;
    private float _defaultBaseAlpha = 1f;
    private float _defaultAlpha;
    private float _targetAlpha = 1f;
    private readonly float[] _stateBaseAlpha = { 1f, 1f, 1f, 1f };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveCrosshairReferences();

        if (crosshairRoot != null)
        {
            crosshairRestPos = crosshairRoot.anchoredPosition;
            crosshairRestScale = crosshairRoot.localScale;
        }

        if (doorLayer < 0)
            doorLayer = LayerMask.NameToLayer("Door");

        if (defaultCrosshairImage != null)
            _defaultBaseAlpha = defaultCrosshairImage.color.a;
        for (int i = 0; i < _stateBaseAlpha.Length; i++)
        {
            Image image = GetCrosshairImage((CrosshairState)i);
            if (image != null)
                _stateBaseAlpha[i] = image.color.a;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void PlayFailShake()
    {
        SoundManager.Instance?.PlayActionFail();

        if (crosshairRoot == null)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(FailShakeRoutine());
    }

    public void PlaySuccessShake()
    {
        if (crosshairRoot == null)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(SuccessScaleShakeRoutine());
    }

    private void Start()
    {
        ApplyCrosshairState(CrosshairState.Default);

        if (buttonPromptImage)
            buttonPromptImage.enabled = false;
    }

    private void ResolveCrosshairReferences()
    {
        if (crosshairRoot == null)
        {
            if (defaultCrosshairImage != null)
                crosshairRoot = defaultCrosshairImage.transform.parent as RectTransform;
            else
            {
                var crosshairGo = GameObject.Find("Crosshair");
                if (crosshairGo != null)
                    crosshairRoot = crosshairGo.GetComponent<RectTransform>();
            }
        }

        if (crosshairRoot == null)
            return;

        if (defaultCrosshairImage == null)
            defaultCrosshairImage = crosshairRoot.Find("CrosshairDefault")?.GetComponent<Image>();
        if (interactCrosshairImage == null)
            interactCrosshairImage = crosshairRoot.Find("CrosshairInteract")?.GetComponent<Image>();
        if (doorCrosshairImage == null)
            doorCrosshairImage = crosshairRoot.Find("CrosshairDoor")?.GetComponent<Image>();
        if (outOfReachCrosshairImage == null)
            outOfReachCrosshairImage = crosshairRoot.Find("CrosshairOutOfReach")?.GetComponent<Image>();
        if (dotCrosshairImage == null)
            dotCrosshairImage = crosshairRoot.Find("CrosshairDot")?.GetComponent<Image>();
    }

    private void LateUpdate()
    {
        UpdateCrosshairFade();
        UpdateCrosshairDot();
    }

    private void UpdateCrosshairDot()
    {
        if (dotCrosshairImage == null)
            return;

        bool show = _crosshairState == CrosshairState.Default
            && !PlayerControlGate.IsInputBlocked
            && (_cachedPlayerHider == null || (!_cachedPlayerHider.isHidden && !_cachedPlayerHider.IsHidingEngaged));
        SetCrosshairActive(dotCrosshairImage, show);
    }

    private void Update()
    {
        if (PlayerControlGate.IsInputBlocked)
        {
            ClearDetection(true);
            return;
        }

        if (TryShowHiddenExitPrompt())
            return;

        DetectInteractable();
    }

    private bool TryShowHiddenExitPrompt()
    {
        if (PlayerControlGate.IsInputBlocked)
            return false;

        if (_cachedPlayerHider == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                _cachedPlayerHider = player.GetComponent<PlayerHider>();
        }

        if (_cachedPlayerHider == null || !_cachedPlayerHider.isHidden)
            return false;

        // Exit hide is still E/Space on PlayerHider — don't show center interact UI while hidden.
        ClearDetection(true);
        return true;
    }

    private void DetectInteractable()
    {
        var interactor = PlayerInteractor.Instance;
        if (interactor == null)
        {
            ClearDetection(true);
            return;
        }

        if (PlayerControlGate.IsInputBlocked)
        {
            ClearDetection(true);
            return;
        }

        if (!interactor.TryRaycastForHint(out RaycastHit hit, out IInteractable interactable))
        {
            ClearDetection(true);
            return;
        }

        GameObject hitObject = hit.collider.gameObject;

        if (hitObject != lastDetectedObject)
        {
            ClearDetection(false);
            lastDetectedObject = hitObject;
        }

        float actionRange = interactor.GetActionRangeForHeldItem();
        if (interactor.IsWithinActionRange(hit, actionRange))
            ShowInRangeUI(interactable, hit.collider);
        else
            ShowOutOfReachUI();
    }

    private void ShowInRangeUI(IInteractable interactable, Collider collider)
    {
        bool isDoor = IsDoorTarget(interactable, collider);
        ApplyCrosshairState(isDoor ? CrosshairState.Door : CrosshairState.Interact);

        if (buttonPromptImage && buttonPromptSprite)
        {
            buttonPromptImage.sprite = buttonPromptSprite;
            buttonPromptImage.enabled = true;
        }
    }

    private void ShowOutOfReachUI()
    {
        ApplyCrosshairState(CrosshairState.OutOfReach);

        if (buttonPromptImage)
            buttonPromptImage.enabled = false;
    }

    private void ClearDetection(bool resetUI)
    {
        if (resetUI)
            ApplyCrosshairState(CrosshairState.Default);

        if (buttonPromptImage)
            buttonPromptImage.enabled = false;

        lastDetectedObject = null;
    }

    private void ApplyCrosshairState(CrosshairState state)
    {
        // Looking away from a target: show the default crosshair at full strength, then fade it.
        if (state == CrosshairState.Default && _crosshairState != CrosshairState.Default)
            _defaultAlpha = 1f;

        // Targeting something after nothing: fade its crosshair in from wherever the default faded to.
        if (state != CrosshairState.Default && _crosshairState == CrosshairState.Default)
            _targetAlpha = fadeDefaultCrosshair ? _defaultAlpha : 1f;

        if (state != _crosshairState)
            SetImageAlpha(GetCrosshairImage(_crosshairState), _stateBaseAlpha[(int)_crosshairState]);

        _crosshairState = state;

        SetCrosshairActive(defaultCrosshairImage, state == CrosshairState.Default);
        SetCrosshairActive(interactCrosshairImage, state == CrosshairState.Interact);
        SetCrosshairActive(doorCrosshairImage, state == CrosshairState.Door);
        SetCrosshairActive(outOfReachCrosshairImage, state == CrosshairState.OutOfReach);
    }

    private void UpdateCrosshairFade()
    {
        float target = fadeDefaultCrosshair ? 0f : 1f;
        _defaultAlpha = Mathf.MoveTowards(_defaultAlpha, target, Time.unscaledDeltaTime / defaultFadeDuration);
        SetImageAlpha(defaultCrosshairImage, _defaultBaseAlpha * _defaultAlpha);

        if (_crosshairState != CrosshairState.Default)
        {
            _targetAlpha = Mathf.MoveTowards(_targetAlpha, 1f, Time.unscaledDeltaTime / targetFadeInDuration);
            SetImageAlpha(GetCrosshairImage(_crosshairState), _stateBaseAlpha[(int)_crosshairState] * _targetAlpha);
        }
    }

    private Image GetCrosshairImage(CrosshairState state) => state switch
    {
        CrosshairState.Default => defaultCrosshairImage,
        CrosshairState.Interact => interactCrosshairImage,
        CrosshairState.Door => doorCrosshairImage,
        CrosshairState.OutOfReach => outOfReachCrosshairImage,
        _ => null
    };

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    private static bool IsDoorTarget(IInteractable interactable, Collider collider)
    {
        if (interactable is SimpleOpenClose)
            return true;

        return collider != null && doorLayer >= 0 && collider.gameObject.layer == doorLayer;
    }

    private static void SetCrosshairActive(Image image, bool active)
    {
        if (image == null)
            return;

        image.gameObject.SetActive(active);
    }

    private void OnDisable()
    {
        StopCrosshairShake();
        ClearDetection(true);
    }

    private void StopCrosshairShake()
    {
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        if (crosshairRoot == null)
            return;

        crosshairRoot.anchoredPosition = crosshairRestPos;
        crosshairRoot.localScale = crosshairRestScale;
    }

    private IEnumerator FailShakeRoutine()
    {
        crosshairRoot.anchoredPosition = crosshairRestPos;
        crosshairRoot.localScale = crosshairRestScale;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - (elapsed / shakeDuration);
            float offset = failShakeStrength * t;
            float x = Random.Range(-offset, offset);
            float y = Random.Range(-offset, offset);
            crosshairRoot.anchoredPosition = crosshairRestPos + new Vector2(x, y);
            yield return null;
        }

        crosshairRoot.anchoredPosition = crosshairRestPos;
        shakeRoutine = null;
    }

    private IEnumerator SuccessScaleShakeRoutine()
    {
        crosshairRoot.anchoredPosition = crosshairRestPos;

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / shakeDuration;
            float pulse = Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(1f, successScalePeak, pulse);
            crosshairRoot.localScale = crosshairRestScale * scale;
            yield return null;
        }

        crosshairRoot.localScale = crosshairRestScale;
        shakeRoutine = null;
    }

    private void OnDrawGizmosSelected()
    {
        Transform rayOrigin = GetRayOrigin();
        if (rayOrigin == null || PlayerInteractor.Instance == null)
            return;

        var interactor = PlayerInteractor.Instance;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(rayOrigin.position, rayOrigin.forward * interactor.HintRange);

        Gizmos.color = Color.green;
        Gizmos.DrawRay(rayOrigin.position, rayOrigin.forward * interactor.GetActionRangeForHeldItem());
    }

    private Transform GetRayOrigin()
    {
        if (PlayerInteractor.Instance != null && PlayerInteractor.Instance.CameraTransform != null)
            return PlayerInteractor.Instance.CameraTransform;
        return cameraTransform;
    }
}
