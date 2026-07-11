using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Atmo
{
    public class SimpleOpenClose : MonoBehaviour, IItemReceiver, IInteractable
    {
        [Header("State Names (Animator)")]
        [SerializeField] private string openStateName  = "Open";
        [SerializeField] private string closeStateName = "Close";
        [Tooltip("Animator penceresindeki Trigger parametresinin adı")]
        [SerializeField] private string lockedTriggerName = "Locked";

        [Header("Blending")]
        [SerializeField] private float crossFadeDuration = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float minToggleProgress = 0.25f;

        [Header("Lock Settings (Player Only)")]
        public bool isLocked = false;
        [SerializeField] private InventoryItem requiredKey;
        [Tooltip("Preferred: a Loc key (e.g. gameplay.locked) so the message is translated. Takes priority over Locked Message below.")]
        [SerializeField] private string lockedMessageKey;
        [TextArea]
        [Tooltip("Fallback raw text used only if Locked Message Key is empty. Not localized - prefer the key above.")]
        [SerializeField] private string lockedMessage = "The door is locked.";
        
        [Tooltip("Kilitli animasyonu kaç saniye sürüyor? Bu süre boyunca tekrar tıklanamaz.")]
        [SerializeField] private float lockedAnimationDuration = 1.0f; // YENİ: Spam engelleyici süre

        [Header("Audio SFX")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [Tooltip("Optional: plays first when opening; open sound follows after a short delay.")]
        [SerializeField] private AudioClip handleOpenSound;
        [SerializeField] private float openSoundDelayAfterHandle = 0.1f;
        [SerializeField] private AudioClip unlockSound; 
        [SerializeField] private AudioClip lockedSound; 

        [Header("Runtime Setup")]
        public bool objectOpen;
        public bool objectOpenAdditional;
        public GameObject animateAdditional;

        private Animator myAnimator;
        private Animator additionalAnimator;
        private bool hasAdditional = false;
        private int openHash, closeHash, lockedHash;
        
        // YENİ: Spam koruması için zamanlayıcı
        private float nextInteractTime = 0f;
        private Coroutine _openSfxRoutine;

        void Awake()
        {
            myAnimator = GetComponent<Animator>();
            if (myAnimator == null) myAnimator = GetComponentInParent<Animator>();

            openHash   = Animator.StringToHash(openStateName);
            closeHash  = Animator.StringToHash(closeStateName);
            lockedHash = Animator.StringToHash(lockedTriggerName);

            if (animateAdditional != null && animateAdditional.GetComponent<SimpleOpenClose>())
            {
                additionalAnimator = animateAdditional.GetComponent<Animator>();
                hasAdditional = additionalAnimator != null;
                objectOpenAdditional = animateAdditional.GetComponent<SimpleOpenClose>().objectOpen;
            }
        }


        public bool Interact() => ObjectClicked(bypassLock: false);

        public bool ObjectClicked() => ObjectClicked(bypassLock: false);

        public bool ObjectClicked(bool bypassLock)
        {
            if (!bypassLock && Time.time < nextInteractTime && !IsReversingToggle(myAnimator))
            {
                InteractionFeedback.Fail();
                return false;
            }

            // --- KİLİTLİ DURUM KONTROLÜ (Burası aynı kalıyor) ---
            if (!bypassLock && !objectOpen && isLocked)
            {
                InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
                if (selectedItem != null && requiredKey != null && selectedItem == requiredKey)
                    return false;
                if (Time.time < nextInteractTime)
                {
                    InteractionFeedback.Fail();
                    return false;
                }

                string message;
                if (!string.IsNullOrWhiteSpace(lockedMessageKey))
                    message = Loc.Get(lockedMessageKey);
                else if (!string.IsNullOrWhiteSpace(lockedMessage))
                    message = lockedMessage;
                else
                    message = Loc.Get("gameplay.locked");
                InteractionFeedback.Fail(message, 2.5f);

                Debug.Log("❌ Kapı kilitli! (Animasyon Tetikleniyor)");
                myAnimator.SetTrigger(lockedHash);
                if (hasAdditional) additionalAnimator.SetTrigger(lockedHash);
                nextInteractTime = Time.time + lockedAnimationDuration;
                return false;
            }

            float openAmount = GetOpenAmount(myAnimator);
            if (!ShouldAllowToggle(openAmount))
            {
                InteractionFeedback.Fail();
                return false;
            }

            // --- TEK KAPIYSA ---
            if (!hasAdditional)
            {
                ToggleSelf(openAmount);
                return true;
            }

            // --- ÇİFT KAPIYSA ---
            float openAmountAdd = GetOpenAmount(additionalAnimator);
            ToggleSelf(openAmount);
            ToggleAdditional(openAmountAdd);
            return true;
        }

        public bool ReceiveItem(InventoryItem item)
        {
            if (objectOpen) return false;
            if (!isLocked) return false;

            if (item == null || requiredKey == null || item != requiredKey)
            {
                if (Time.time < nextInteractTime) return false;

                if (item != null && item.TryBreakOnWrongUse(transform.position))
                {
                    InteractionFeedback.Fail();
                    myAnimator.SetTrigger(lockedHash);
                    nextInteractTime = Time.time + lockedAnimationDuration;
                    return false;
                }

                InteractionFeedback.Fail(Loc.Get("gameplay.wrong_item"), 2.5f);
                myAnimator.SetTrigger(lockedHash);
                nextInteractTime = Time.time + lockedAnimationDuration;
                return false;
            }

            var inventory = InventoryManager.Instance.GetInventory();
            if (!inventory.Contains(item))
            {
                InteractionFeedback.Fail(Loc.Format("gameplay.dont_have_item", item.GetLocalizedName()), 2.5f);
                return false;
            }

            // ✅ KİLİT AÇILDI
            isLocked = false;
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.item_unlocked", item.GetLocalizedName()),
                2.5f, HudManager.Instance.SuccessColor);
            Debug.Log($"✅ {item.itemName} ile kilit açıldı.");

            if (item.consumeOnUse)
            {
                InventoryManager.Instance.RemoveItem(item);
                InventoryUI.Instance?.RefreshUI();
            }

            AudioClip unlockSfx = unlockSound != null ? unlockSound : item.useSuccessSound;
            PlaySound(unlockSfx);
            return true;
        }

        // --- Helper Methods ---
        
        // PUBLIC: Animation Event için
        public void PlaySound(AudioClip clip)
        {
            if (SoundManager.Instance != null && clip != null)
                SoundManager.Instance.PlaySFXSafe(clip, transform.position);
        }

        public void PlayLockedSound()
        {
            if (SoundManager.Instance != null && lockedSound != null)
                SoundManager.Instance.PlaySFXAt(lockedSound, transform.position);
        }

        public void SetLocked(bool locked) => isLocked = locked;
        public void SetRequiredKey(InventoryItem key) => requiredKey = key;

        private void ToggleSelf(float openAmount)
        {
            openAmount = Mathf.Clamp01(openAmount);

            if (objectOpen)
            {
                myAnimator.CrossFade(closeHash, crossFadeDuration, 0, openAmount);
                objectOpen = false;
                PlayCloseSfx(transform.position);
            }
            else
            {
                myAnimator.CrossFade(openHash, crossFadeDuration, 0, 1f - openAmount);
                objectOpen = true;
                PlayOpenSfxSequence(transform.position, includeAdditionalLeaf: hasAdditional);
            }

            nextInteractTime = Time.time + crossFadeDuration + 0.05f;
        }

        private void ToggleAdditional(float openAmount)
        {
            var other = animateAdditional.GetComponent<SimpleOpenClose>();
            if (other == null) return;

            openAmount = Mathf.Clamp01(openAmount);

            if (objectOpenAdditional)
            {
                additionalAnimator.CrossFade(closeHash, crossFadeDuration, 0, openAmount);
                objectOpenAdditional = false;
                other.objectOpen = false;
                other.objectOpenAdditional = false;

                PlayCloseSfx(animateAdditional.transform.position);
            }
            else
            {
                additionalAnimator.CrossFade(openHash, crossFadeDuration, 0, 1f - openAmount);
                objectOpenAdditional = true;
                other.objectOpen = true;
                other.objectOpenAdditional = true;
                // Open SFX (handle → open) is played once from ToggleSelf for double doors.
            }
        }

        bool IsReversingToggle(Animator animator)
        {
            float openAmount = GetOpenAmount(animator);
            return (objectOpen && openAmount < 1f - minToggleProgress) ||
                   (!objectOpen && openAmount > minToggleProgress);
        }

        bool ShouldAllowToggle(float openAmount)
        {
            if (IsReversingToggle(myAnimator))
                return true;

            if (myAnimator.IsInTransition(0))
                return false;

            var info = myAnimator.GetCurrentAnimatorStateInfo(0);
            if (info.loop)
                return true;

            return openAmount <= minToggleProgress || openAmount >= 1f - minToggleProgress;
        }

        float GetOpenAmount(Animator animator)
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
            if (info.shortNameHash == openHash)
                return GetClampedProgress(info);
            if (info.shortNameHash == closeHash)
                return 1f - GetClampedProgress(info);
            return objectOpen ? 1f : 0f;
        }

        void PlayCloseSfx(Vector3 position)
        {
            if (SoundManager.Instance != null && closeSound != null)
                SoundManager.Instance.PlaySFXSafe(closeSound, position);
        }

        void PlayOpenSfxSequence(Vector3 primaryPosition, bool includeAdditionalLeaf)
        {
            if (_openSfxRoutine != null)
                StopCoroutine(_openSfxRoutine);

            Vector3? additionalPosition = includeAdditionalLeaf && animateAdditional != null
                ? animateAdditional.transform.position
                : null;

            _openSfxRoutine = StartCoroutine(OpenSfxSequenceRoutine(primaryPosition, additionalPosition));
        }

        IEnumerator OpenSfxSequenceRoutine(Vector3 primaryPosition, Vector3? additionalPosition)
        {
            if (SoundManager.Instance == null)
                yield break;

            if (handleOpenSound != null)
                SoundManager.Instance.PlaySFXAt(handleOpenSound, primaryPosition,.2f);

            if (openSound != null)
            {
                if (handleOpenSound != null)
                    yield return new WaitForSeconds(openSoundDelayAfterHandle);

                SoundManager.Instance.PlaySFXSafe(openSound, primaryPosition);

                if (additionalPosition.HasValue)
                    SoundManager.Instance.PlaySFXSafe(openSound, additionalPosition.Value);
            }

            _openSfxRoutine = null;
        }

        private static float GetClampedProgress(AnimatorStateInfo info)
        {
            float t = info.normalizedTime;
            return info.loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);
        }
    }
}