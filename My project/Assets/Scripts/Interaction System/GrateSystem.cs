using UnityEngine;

public class GrateSystem : MonoBehaviour, IInteractable
{
    const int DefaultLayer = 0;

    [SerializeField] GrateBolt[] bolts;
    [SerializeField] Animator animator;
    [SerializeField] string removeTrigger = "Remove";
    [Tooltip("Animator state name for the grate open/close clip (used for reversing mid-way).")]
    [SerializeField] string moveStateName = "Remove";
    [Tooltip("Seconds to fully open/close (used for manual scrubbing). If unsure, set to your clip length.")]
    [Min(0.01f)]
    [SerializeField] float moveDuration = 1.0f;
    [Tooltip("Object whose layer is toggled (grate mesh/collider). Defaults to this GameObject. Does not change child bolt layers.")]
    [SerializeField] GameObject layerTarget;

    [Header("Audio")]
    [SerializeField] AudioClip boltRemoveSound;
    [SerializeField] AudioClip grateMoveSound;
    [Min(0f)]
    [SerializeField] float grateMoveSfxCooldown = 0.12f;

    [Header("NPC Suspicion")]
    [Tooltip("Footstep-suspicion value added to NPCs when the grate is opened (same mechanism as BodyBagDrop).")]
    [Min(0f)]
    [SerializeField] float npcSuspicionOnOpen = 220f;

    static int _doorLayer = -1;
    static int DoorLayer
    {
        get
        {
            if (_doorLayer < 0)
            {
                _doorLayer = LayerMask.NameToLayer("Door");
                if (_doorLayer < 0)
                    _doorLayer = 9;
            }
            return _doorLayer;
        }
    }

    int _removedCount;
    bool _isOpen;
    bool _isAnimating;
    bool _targetOpen;
    float _t;
    float _lastMoveSfxTime;
    int _moveStateHash;

    public bool AllBoltsRemoved => bolts != null && bolts.Length > 0 && _removedCount >= bolts.Length;
    public bool IsRemoved => _isOpen;

    void Awake()
    {
        if (layerTarget == null)
            layerTarget = gameObject;
        SetGrateLayer(DefaultLayer);
        _moveStateHash = !string.IsNullOrEmpty(moveStateName) ? Animator.StringToHash(moveStateName) : 0;

        if (animator != null)
            animator.speed = 0f;
    }

    public void NotifyBoltRemoved(Vector3 boltWorldPosition)
    {
        if (_isOpen)
            return;

        if (boltRemoveSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(boltRemoveSound, boltWorldPosition);

        _removedCount++;
        if (AllBoltsRemoved)
            SetGrateLayer(DoorLayer);
    }

    void SetGrateLayer(int layer)
    {
        if (layerTarget != null)
            layerTarget.layer = layer;
    }

    public bool Interact()
    {
        if (!AllBoltsRemoved)
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.grate_bolted"), 3f);
            return false;
        }

        ToggleOpenClose();
        return true;
    }

    void ToggleOpenClose()
    {
        if (animator == null)
            return;

        bool nextTarget = _isAnimating ? !_targetOpen : !_isOpen;
        bool openingNow = nextTarget && !_isOpen;
        _targetOpen = nextTarget;

        // Ensure we're in the correct state once, then keep animator paused and scrub time ourselves.
        if (_moveStateHash != 0 && !_isAnimating)
        {
            if (!string.IsNullOrEmpty(removeTrigger))
                animator.SetTrigger(removeTrigger);

            _t = _isOpen ? 1f : 0f;
            ApplyScrub(_t);
        }

        _isAnimating = true;

        PlayMoveSfx();
        if (openingNow)
            NotifyNPCsOfOpenSuspicion();
    }

    void Update()
    {
        if (!_isAnimating || animator == null)
            return;

        float step = Time.deltaTime / Mathf.Max(0.01f, moveDuration);
        float targetT = _targetOpen ? 1f : 0f;
        _t = Mathf.MoveTowards(_t, targetT, step);
        ApplyScrub(_t);

        if (Mathf.Approximately(_t, targetT))
        {
            _isAnimating = false;
            _isOpen = _targetOpen;
        }
    }

    void ApplyScrub(float normalizedTime)
    {
        if (_moveStateHash == 0)
            return;

        animator.speed = 0f;
        animator.Play(_moveStateHash, 0, Mathf.Clamp01(normalizedTime));
        animator.Update(0f);
    }

    // Kept to satisfy existing animation events (prevents console error).
    public void OnRemovalComplete() { }

    void PlayMoveSfx()
    {
        if (grateMoveSound == null || SoundManager.Instance == null)
            return;

        if (grateMoveSfxCooldown > 0f && Time.time - _lastMoveSfxTime < grateMoveSfxCooldown)
            return;

        _lastMoveSfxTime = Time.time;
        // Use time-based cooldown here (instead of PlaySFXSafe distance check) so open/close always play.
        SoundManager.Instance.PlaySFXAt(grateMoveSound, transform.position);
    }

    void NotifyNPCsOfOpenSuspicion()
    {
        if (npcSuspicionOnOpen <= 0f)
            return;

#if UNITY_2023_1_OR_NEWER
        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
#else
        foreach (var perception in Object.FindObjectsOfType<PerceptionController>())
#endif
        {
            if (perception != null)
                perception.HearLoudWorldNoise(transform.position, npcSuspicionOnOpen);
        }
    }
}
