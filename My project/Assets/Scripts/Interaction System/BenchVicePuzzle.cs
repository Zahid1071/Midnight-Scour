using System.Collections;
using UnityEngine;

/// <summary>
/// Bench vise: insert bar (LMB) → bar won't turn (E) → strike with hammer (LMB) → wrench can be picked up.
/// Hammer strikes only succeed during the thunder mask; strikes in the wider pre/post window alert the butcher.
/// </summary>
public class BenchVicePuzzle : MonoBehaviour, IInteractable, IItemReceiver
{
    public static BenchVicePuzzle Instance { get; private set; }

    [SerializeField] InventoryItem barItem;
    [SerializeField] InventoryItem hammerItem;
    [SerializeField] GameObject barVisual;
    [SerializeField] Transform dialTransform;
    [SerializeField] Animator animator;
    [SerializeField] string strikeAnimatorTrigger = "Strike";
    [Header("Dial strike (used when no Animator)")]
    [SerializeField] float dialStrikeDegrees = 45f;
    [SerializeField] float dialStrikeDuration = 0.35f;
    [SerializeField] AudioClip strikeSound;
    [Header("NPC Suspicion")]
    [Tooltip("Footstep-suspicion added to NPCs when a mistimed hammer strike lands in the thunder window.")]
    [Min(0f)]
    [SerializeField] float npcSuspicionOnStrike = 300f;
    [SerializeField] float strikeSfxVolume = 1f;

    [Header("Post-completion")]
    [Tooltip("Objects moved off the Interactable layer once the wrench is released (defaults to this + bar visual).")]
    [SerializeField] GameObject[] disableInteractLayers;

    bool _barInserted;
    bool _wrenchReleased;
    bool _isAnimating;
    int _mistimedStrikeCount;

    public bool IsWrenchReleased => _wrenchReleased;
    public bool BarInserted => _barInserted;

    void Awake()
    {
        Instance = this;
        if (barVisual != null)
            barVisual.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool Interact()
    {
        if (_wrenchReleased)
            return false;

        ShowContextualHint(InventoryUI.Instance?.GetSelectedItem());
        return false;
    }

    public string GetContextualHintMessage(InventoryItem held)
    {
        if (_wrenchReleased)
            return null;

        if (!_barInserted)
            return Loc.Get("gameplay.need_bar");

        if (held == hammerItem)
            return Loc.Get("gameplay.use_hammer_bar");

        return Loc.Get("gameplay.bar_stuck_rusty");
    }

    void ShowContextualHint(InventoryItem held)
    {
        string message = GetContextualHintMessage(held);
        if (!string.IsNullOrWhiteSpace(message))
            InteractionFeedback.Fail(message, 3f);
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (_isAnimating)
            return false;

        if (item == null)
            return false;

        if (!_barInserted && item == barItem)
            return TryInsertBar(item);

        if (_barInserted && !_wrenchReleased && item == hammerItem)
            return TryStrikeWithHammer(item);

        ShowContextualHint(item);
        return false;
    }

    bool TryInsertBar(InventoryItem item)
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.dont_have_item"), 2.5f);
            return false;
        }

        if (item.consumeOnUse)
            InventoryManager.Instance.RemoveItem(item);

        _barInserted = true;
        if (barVisual != null)
            barVisual.SetActive(true);

        AudioClip sfx = item.useSuccessSound;
        if (sfx != null)
            InteractionFeedback.PlaySound(sfx, transform.position);
        InteractionUIManager.Instance?.PlaySuccessShake();
        return true;
    }

    bool TryStrikeWithHammer(InventoryItem item)
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.dont_have_item"), 2.5f);
            return false;
        }

        // Outside the 3s-pre / 3s-post thunder window: refuse the strike entirely.
        if (!ThunderManager.IsViseHammerWindowOpen)
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.hammer_too_loud_now"), 3f);
            return false;
        }

        bool perfectTiming = ThunderManager.ArePlayerNoisesMasked;
        bool earlyMiss = !perfectTiming && ThunderManager.IsViseHammerEarly;
        StartCoroutine(StrikeRoutine(item, perfectTiming, earlyMiss));
        return true;
    }

    IEnumerator StrikeRoutine(InventoryItem hammer, bool perfectTiming, bool earlyMiss)
    {
        _isAnimating = true;

        Vector3 noisePos = transform.position;

        if (!perfectTiming)
        {
            // Same path as vase/flush: suspicion → search/chase.
            WorldNoiseAlert.Notify(noisePos, npcSuspicionOnStrike);
        }

        AudioClip hitSfx = strikeSound != null ? strikeSound : hammer.useSuccessSound;
        if (hitSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(hitSfx, noisePos, strikeSfxVolume);

        if (perfectTiming)
            InteractionUIManager.Instance?.PlaySuccessShake();
        else
            InteractionUIManager.Instance?.PlayFailShake();

        // Dial/rotate anim only on a successful timed hit — misses still play SFX / noise / subtitles.
        if (perfectTiming)
        {
            if (animator != null)
            {
                if (!animator.enabled)
                    animator.enabled = true;
                animator.SetTrigger(strikeAnimatorTrigger);
                yield return new WaitForSeconds(dialStrikeDuration);
            }
            else if (dialTransform != null)
            {
                Quaternion start = dialTransform.localRotation;
                Quaternion end = start * Quaternion.Euler(0f, 0f, dialStrikeDegrees);
                float t = 0f;
                while (t < dialStrikeDuration)
                {
                    t += Time.deltaTime;
                    float p = Mathf.SmoothStep(0f, 1f, t / dialStrikeDuration);
                    dialTransform.localRotation = Quaternion.Slerp(start, end, p);
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(dialStrikeDuration);
            }

            _wrenchReleased = true;
            ClearInteractableLayers();
            ThunderManager.Instance?.NotifyHammerUsedSuccessfully();

            if (hammer.consumeOnUse)
                InventoryManager.Instance?.RemoveItem(hammer);

            var hud = HudManager.Instance;
            if (hud != null)
                hud.ShowSubtitle(Loc.Get("gameplay.hammer_perfect_timing"), 3.5f, hud.SuccessColor);
        }
        else
        {
            _mistimedStrikeCount++;
            ShowMissTimingSubtitle(earlyMiss);
            // First two misses: normal noise only. Third miss: guaranteed catch hunt.
            if (_mistimedStrikeCount >= 3)
                ForceButcherHuntPlayer();
        }

        _isAnimating = false;
    }

    static void ShowMissTimingSubtitle(bool early)
    {
        var hud = HudManager.Instance;
        if (hud == null)
            return;

        string key = early ? "gameplay.hammer_hit_too_early" : "gameplay.hammer_hit_too_late";
        string fallback = early
            ? "I hit it too early, I'll get caught"
            : "I hit it too late, I'll get caught";
        string msg = Loc.Get(key);
        if (string.IsNullOrEmpty(msg) || msg == key)
            msg = fallback;

        SoundManager.Instance?.PlayActionFail();
        InteractionUIManager.Instance?.PlayFailShake();
        hud.ShowSubtitle(msg, 4.5f, hud.ErrorColor, HudManager.PriorityHigh);
    }

    static void ForceButcherHuntPlayer()
    {
        var npcs = Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null)
                npcs[i].ForceHuntPlayerForViseMiss();
        }
    }

    void ClearInteractableLayers()
    {
        const int defaultLayer = 0;

        if (disableInteractLayers != null && disableInteractLayers.Length > 0)
        {
            for (int i = 0; i < disableInteractLayers.Length; i++)
            {
                if (disableInteractLayers[i] != null)
                    disableInteractLayers[i].layer = defaultLayer;
            }
            return;
        }

        gameObject.layer = defaultLayer;
        if (barVisual != null)
            barVisual.layer = defaultLayer;
    }
}
