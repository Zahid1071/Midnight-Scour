using System.Collections;
using UnityEngine;

/// <summary>
/// Locked container (toolbox, chest, etc.): unlock with a key item (LMB), then toggle open/close (E).
/// Supports animator triggers (Open / Close / Locked) or state cross-fades like doors.
/// </summary>
public class LockedOpenClose : MonoBehaviour, IInteractable, IItemReceiver
{
    [Header("Animator")]
    [SerializeField] Animator animator;
    [SerializeField] bool useTriggers = true;
    [SerializeField] string openTrigger = "Open";
    [SerializeField] string closeTrigger = "Close";
    [SerializeField] string lockedTrigger = "Locked";
    [SerializeField] string openStateName = "Open";
    [SerializeField] string closeStateName = "Close";
    [SerializeField] float crossFadeDuration = 0.1f;
    [Range(0f, 1f)]
    [SerializeField] float minToggleProgress = 0.25f;

    [Header("Lock")]
    public bool isLocked = true;
    [SerializeField] InventoryItem requiredKey;
    [SerializeField] float lockedFeedbackCooldown = 1f;
    [SerializeField] string lockedMessage = "It's locked.";
    [SerializeField] string wrongItemMessage = "That won't work here.";

    [Header("Audio")]
    [SerializeField] AudioClip openSound;
    [SerializeField] AudioClip closeSound;
    [SerializeField] AudioClip unlockSound;
    [SerializeField] AudioClip lockedSound;

    [Header("Runtime")]
    public bool objectOpen;

    int _openHash;
    int _closeHash;
    int _openTriggerHash;
    int _closeTriggerHash;
    int _lockedTriggerHash;
    float _nextInteractTime;

    void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _openHash = Animator.StringToHash(openStateName);
        _closeHash = Animator.StringToHash(closeStateName);
        _openTriggerHash = Animator.StringToHash(openTrigger);
        _closeTriggerHash = Animator.StringToHash(closeTrigger);
        _lockedTriggerHash = Animator.StringToHash(lockedTrigger);
    }

    public void Interact() => InteractInternal(bypassLock: false);

    void InteractInternal(bool bypassLock)
    {
        if (!bypassLock && Time.time < _nextInteractTime)
            return;

        if (!bypassLock && isLocked)
        {
            InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
            if (selected != null && requiredKey != null && selected == requiredKey)
                return;

            InteractionFeedback.Fail(lockedMessage, 2.5f);
            PlayLockedFeedback();
            return;
        }

        ToggleOpenClose();
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (objectOpen || !isLocked)
            return false;

        if (item == null || requiredKey == null || item != requiredKey)
        {
            if (Time.time < _nextInteractTime)
                return false;

            if (item != null && item.TryBreakOnWrongUse(transform.position))
            {
                InteractionFeedback.Fail();
                PlayLockedFeedback();
                return false;
            }

            InteractionFeedback.Fail(wrongItemMessage, 2.5f);
            PlayLockedFeedback();
            return false;
        }

        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail("You do not have that item.", 2.5f);
            return false;
        }

        isLocked = false;
        _nextInteractTime = Time.time + lockedFeedbackCooldown;
        HudManager.Instance?.ShowSubtitle($"{item.itemName} unlocked it.", 2.5f, HudManager.Instance.SuccessColor);

        if (item.consumeOnUse)
        {
            InventoryManager.Instance.RemoveItem(item);
            InventoryUI.Instance?.RefreshUI();
        }

        AudioClip sfx = unlockSound != null ? unlockSound : item.useSuccessSound;
        InteractionFeedback.PlaySound(sfx, transform.position);
        return true;
    }

    void ToggleOpenClose()
    {
        if (animator == null)
        {
            Debug.LogWarning($"{nameof(LockedOpenClose)} on {name}: no Animator assigned.");
            objectOpen = !objectOpen;
            return;
        }

        if (!animator.enabled)
            animator.enabled = true;

        if (useTriggers)
        {
            if (objectOpen)
            {
                animator.SetTrigger(_closeTriggerHash);
                objectOpen = false;
                PlayCloseSfx();
            }
            else
            {
                animator.SetTrigger(_openTriggerHash);
                objectOpen = true;
                PlayOpenSfx();
            }

            return;
        }

        var info = animator.GetCurrentAnimatorStateInfo(0);
        float t = 1f;
        if (info.shortNameHash == _openHash || info.shortNameHash == _closeHash)
            t = GetClampedProgress(info);

        if (t < minToggleProgress && !info.loop && t != 1f)
            return;

        if (objectOpen)
        {
            animator.CrossFade(_closeHash, crossFadeDuration, 0, 1f - t);
            objectOpen = false;
            PlayCloseSfx();
        }
        else
        {
            animator.CrossFade(_openHash, crossFadeDuration, 0, 1f - t);
            objectOpen = true;
            PlayOpenSfx();
        }
    }

    void PlayLockedFeedback()
    {
        _nextInteractTime = Time.time + lockedFeedbackCooldown;
        if (animator != null && animator.enabled)
            animator.SetTrigger(_lockedTriggerHash);
        if (lockedSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(lockedSound, transform.position);
    }

    void PlayOpenSfx()
    {
        if (openSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(openSound, transform.position);
    }

    void PlayCloseSfx()
    {
        if (closeSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(closeSound, transform.position);
    }

    static float GetClampedProgress(AnimatorStateInfo info)
    {
        float t = info.normalizedTime;
        return info.loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);
    }

    public static bool IsLockedShut(IInteractable interactable) =>
        interactable is LockedOpenClose loc && !loc.objectOpen && loc.isLocked;

    public static bool IsUnlockedOpenable(IInteractable interactable) =>
        interactable is LockedOpenClose loc && !loc.isLocked;
}
