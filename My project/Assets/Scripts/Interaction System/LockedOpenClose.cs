using System.Collections;
using FS_Atmo;
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

    public bool Interact() => InteractInternal(bypassLock: false);

    bool InteractInternal(bool bypassLock)
    {
        if (!bypassLock && Time.time < _nextInteractTime)
        {
            InteractionFeedback.Fail();
            return false;
        }

        if (!bypassLock && isLocked)
        {
            InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
            if (selected != null && requiredKey != null && selected == requiredKey)
                return false;

            InteractionFeedback.Fail(lockedMessage, 2.5f);
            PlayLockedFeedback();
            return false;
        }

        return ToggleOpenClose();
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

    bool ToggleOpenClose()
    {
        if (animator == null)
        {
            Debug.LogWarning($"{nameof(LockedOpenClose)} on {name}: no Animator assigned.");
            objectOpen = !objectOpen;
            return true;
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

            return true;
        }

        float openAmount = GetOpenAmount();
        if (!ShouldAllowToggle(openAmount))
        {
            InteractionFeedback.Fail();
            return false;
        }

        if (objectOpen)
        {
            animator.CrossFade(_closeHash, crossFadeDuration, 0, openAmount);
            objectOpen = false;
            PlayCloseSfx();
        }
        else
        {
            animator.CrossFade(_openHash, crossFadeDuration, 0, 1f - openAmount);
            objectOpen = true;
            PlayOpenSfx();
        }

        _nextInteractTime = Time.time + crossFadeDuration + 0.05f;
        return true;
    }

    bool ShouldAllowToggle(float openAmount)
    {
        bool reversing =
            (objectOpen && openAmount < 1f - minToggleProgress) ||
            (!objectOpen && openAmount > minToggleProgress);

        if (reversing)
            return true;

        if (animator != null && animator.IsInTransition(0))
            return false;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.loop)
            return true;

        return openAmount <= minToggleProgress || openAmount >= 1f - minToggleProgress;
    }

    float GetOpenAmount()
    {
        if (animator == null)
            return objectOpen ? 1f : 0f;

        if (animator.IsInTransition(0))
        {
            float current = StateToOpenAmount(animator.GetCurrentAnimatorStateInfo(0));
            float next = StateToOpenAmount(animator.GetNextAnimatorStateInfo(0));
            float blend = animator.GetAnimatorTransitionInfo(0).normalizedTime;
            return Mathf.Lerp(current, next, blend);
        }

        return StateToOpenAmount(animator.GetCurrentAnimatorStateInfo(0));
    }

    float StateToOpenAmount(AnimatorStateInfo info)
    {
        if (info.shortNameHash == _openHash)
            return GetClampedProgress(info);
        if (info.shortNameHash == _closeHash)
            return 1f - GetClampedProgress(info);
        return objectOpen ? 1f : 0f;
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

    public static bool IsUnlockedDoor(IInteractable interactable)
    {
        if (interactable is SimpleOpenClose door)
            return !door.isLocked;
        return IsUnlockedOpenable(interactable);
    }
}
