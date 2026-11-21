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
        [Tooltip("If set, using the required key starts this minigame instead of unlocking instantly.")]
        [SerializeField] private ScrewdriverLockBreakMinigame lockBreakMinigame;
        [Tooltip("If off, a door whose lock was broken stays shut until the player opens it.")]
        [SerializeField] private bool openAfterLockBreak = true;
        [Tooltip("Preferred: a Loc key (e.g. gameplay.locked) so the message is translated. Takes priority over Locked Message below.")]
        [SerializeField] private string lockedMessageKey;
        [TextArea]
        [Tooltip("Fallback raw text used only if Locked Message Key is empty. Not localized - prefer the key above.")]
        [SerializeField] private string lockedMessage = "The door is locked.";
        
        [Tooltip("Kilitli animasyonu kaç saniye sürüyor? Bu süre boyunca tekrar tıklanamaz.")]
        [SerializeField] private float lockedAnimationDuration = 1.0f; // YENİ: Spam engelleyici süre

        /// <summary>After lockpick Continue: door stays locked, screwdriver break is disabled, chained message shows.</summary>
        bool _chainedAfterContinue;

        [Header("Audio SFX")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [Tooltip("Optional: plays first when opening; open sound follows after a short delay.")]
        [SerializeField] private AudioClip handleOpenSound;
        [SerializeField] private float openSoundDelayAfterHandle = 0.1f;
        [SerializeField] private AudioClip unlockSound; 
        [SerializeField] private AudioClip lockedSound;

        /// <summary>Fired when the open door SFX actually plays (after optional handle delay).</summary>
        public event System.Action OnOpenSoundPlayed; 

        [Header("Runtime Setup")]
        public bool objectOpen;
        public bool objectOpenAdditional;
        public GameObject animateAdditional;

        [Header("Obstruction (DoorPlayerObstructionTrigger sets these)")]
        public bool playerBlockingOpen;
        public bool playerBlockingClose;

        private Animator myAnimator;
        private Animator additionalAnimator;
        private bool hasAdditional = false;
        private int openHash, closeHash, lockedHash;
        
        private float nextInteractTime = 0f;
        private Coroutine _openSfxRoutine;

        bool _moving;       // mid open/close we started
        bool _movingOpen;   // true = opening, false = closing
        bool _reverting;

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

        void Update()
        {
            if (_reverting)
            {
                float a = GetOpenAmount(myAnimator);
                bool done = objectOpen ? a >= 0.98f : a <= 0.02f;
                if (!myAnimator.IsInTransition(0) && done)
                    _reverting = false;
                return;
            }

            if (!_moving)
                return;

            if (_movingOpen && playerBlockingOpen)
            {
                StopMotionBecauseBlocked(wasOpening: true);
                return;
            }
            if (!_movingOpen && playerBlockingClose)
            {
                StopMotionBecauseBlocked(wasOpening: false);
                return;
            }

            float amount = GetOpenAmount(myAnimator);
            bool finished = _movingOpen ? amount >= 0.98f : amount <= 0.02f;
            if (!myAnimator.IsInTransition(0) && finished)
                _moving = false;
        }

        /// <summary>
        /// If barely moved (≈0), cancel to rest without inventing motion.
        /// If already partway, cross-fade back to the previous rest state.
        /// </summary>
        void StopMotionBecauseBlocked(bool wasOpening)
        {
            float amount = Mathf.Clamp01(GetOpenAmount(myAnimator));
            _moving = false;

            InteractionFeedback.Fail(Loc.Get("gameplay.blocking_way"), 2.5f);

            if (wasOpening)
            {
                // Trying to open, blocked → end closed.
                objectOpen = false;
                if (amount <= 0.05f)
                {
                    // Never really left closed — stay at 0, do not push a partial open then fade.
                    myAnimator.Play(closeHash, 0, 0f);
                    if (hasAdditional && additionalAnimator != null)
                        additionalAnimator.Play(closeHash, 0, 0f);
                    SyncAdditionalFlags(open: false);
                    _reverting = false;
                }
                else
                {
                    _reverting = true;
                    myAnimator.CrossFade(closeHash, crossFadeDuration, 0, amount);
                    if (hasAdditional && additionalAnimator != null)
                    {
                        float add = Mathf.Clamp01(GetOpenAmount(additionalAnimator));
                        additionalAnimator.CrossFade(closeHash, crossFadeDuration, 0, add);
                    }
                    SyncAdditionalFlags(open: false);
                }
            }
            else
            {
                // Trying to close, blocked → end open.
                objectOpen = true;
                if (amount >= 0.95f)
                {
                    myAnimator.Play(openHash, 0, 1f);
                    if (hasAdditional && additionalAnimator != null)
                        additionalAnimator.Play(openHash, 0, 1f);
                    SyncAdditionalFlags(open: true);
                    _reverting = false;
                }
                else
                {
                    _reverting = true;
                    myAnimator.CrossFade(openHash, crossFadeDuration, 0, amount);
                    if (hasAdditional && additionalAnimator != null)
                    {
                        float add = Mathf.Clamp01(GetOpenAmount(additionalAnimator));
                        additionalAnimator.CrossFade(openHash, crossFadeDuration, 0, add);
                    }
                    SyncAdditionalFlags(open: true);
                }
            }

            nextInteractTime = Time.time + 0.15f;
        }

        void SyncAdditionalFlags(bool open)
        {
            objectOpenAdditional = open;
            if (!hasAdditional || animateAdditional == null)
                return;
            var other = animateAdditional.GetComponent<SimpleOpenClose>();
            if (other == null)
                return;
            other.objectOpen = open;
            other.objectOpenAdditional = open;
        }


        /// <summary>Off = the player can open this but not close it (scripted sequences). AI and scripts still can.</summary>
        public bool PlayerCanClose { get; set; } = true;

        /// <summary>Off = the player can close this but not open it (scripted "stuck" door). AI and scripts still can.</summary>
        public bool PlayerCanOpen { get; set; } = true;

        [Tooltip("Loc key shown when PlayerCanOpen/Close blocks the door. Empty = gameplay.stuck.")]
        [SerializeField] string stuckMessageKey = "gameplay.stuck";

        public bool Interact()
        {
            if (objectOpen && !PlayerCanClose)
            {
                InteractionFeedback.Fail(StuckMessage(), 2f);
                return false;
            }
            if (!objectOpen && !PlayerCanOpen)
            {
                InteractionFeedback.Fail(StuckMessage(), 2f);
                return false;
            }
            return ObjectClicked(bypassLock: false);
        }

        string StuckMessage()
        {
            string key = string.IsNullOrWhiteSpace(stuckMessageKey) ? "gameplay.stuck" : stuckMessageKey;
            return Loc.Get(key);
        }

        /// <summary>Slam shut, re-lock for screwdriver, and clear break progress. E says stuck; item use still breaks.</summary>
        public void StickShutForScrewdriver(string messageKey = "gameplay.screwdriver_stuck")
        {
            if (objectOpen)
                ForceClose();
            isLocked = true;
            _chainedAfterContinue = false;
            PlayerCanOpen = false;
            if (!string.IsNullOrWhiteSpace(messageKey))
                stuckMessageKey = messageKey;
            if (lockBreakMinigame != null)
                lockBreakMinigame.ResetProgress();
        }

        void ClearStuckOpenBlock()
        {
            PlayerCanOpen = true;
            PlayerCanClose = true;
            stuckMessageKey = "gameplay.stuck";
        }

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

                string message = !string.IsNullOrWhiteSpace(lockedMessageKey)
                    ? Loc.Get(lockedMessageKey)
                    : Loc.Get("gameplay.locked");
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

            // Already standing in the path — do not start the animation at all (stay at rest 0 / open).
            bool wantOpen = !objectOpen;
            if (wantOpen && playerBlockingOpen)
            {
                InteractionFeedback.Fail(Loc.Get("gameplay.blocking_way"), 2.5f);
                return false;
            }
            if (!wantOpen && playerBlockingClose)
            {
                InteractionFeedback.Fail(Loc.Get("gameplay.blocking_way"), 2.5f);
                return false;
            }

            // Butcher just opened this door — don't let the player slam it shut in his face.
            if (!bypassLock && !wantOpen)
            {
                var doorAi = GetComponentInParent<DoorController>();
                if (doorAi != null && doorAi.IsPlayerCloseBlocked)
                {
                    InteractionFeedback.Fail(Loc.Get("gameplay.wait_moment"), 2f);
                    return false;
                }
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

            if (lockBreakMinigame != null && !_chainedAfterContinue)
            {
                return lockBreakMinigame.TryBegin(
                    item,
                    onSuccess: CompleteLockBreak,
                    onCancel: null);
            }

            // Chained after Continue — screwdriver (or any key) won't break it; pick the padlock.
            if (_chainedAfterContinue)
            {
                if (Time.time < nextInteractTime)
                    return false;

                string chained = !string.IsNullOrWhiteSpace(lockedMessageKey)
                    ? Loc.Get(lockedMessageKey)
                    : Loc.Get("gameplay.door_chained");
                InteractionFeedback.Fail(chained, 2.5f);
                myAnimator.SetTrigger(lockedHash);
                if (hasAdditional) additionalAnimator.SetTrigger(lockedHash);
                nextInteractTime = Time.time + lockedAnimationDuration;
                return false;
            }

            CompleteUnlockFromMinigame(item);
            return true;
        }

        void CompleteUnlockFromMinigame(InventoryItem item)
        {
            if (item == null)
                return;

            // ✅ KİLİT AÇILDI
            isLocked = false;
            ClearStuckOpenBlock();
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.item_unlocked", item.GetLocalizedName()),
                2.5f, HudManager.Instance.SuccessColor);

            if (item.consumeOnUse)
            {
                InventoryManager.Instance.RemoveItem(item);
                InventoryUI.Instance?.RefreshUI();
            }

            AudioClip unlockSfx = unlockSound != null ? unlockSound : item.useSuccessSound;
            PlaySound(unlockSfx);

            // Open immediately after a successful lock break (ignore player-in-path block).
            ForceOpenAfterUnlock();
        }

        /// <summary>Screwdriver minigame finished: the minigame already played its break sound.</summary>
        void CompleteLockBreak(InventoryItem item)
        {
            if (item == null)
                return;

            isLocked = false;
            ClearStuckOpenBlock();
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.item_unlocked", item.GetLocalizedName()),
                2.5f, HudManager.Instance.SuccessColor);

            if (item.consumeOnUse)
            {
                InventoryManager.Instance.RemoveItem(item);
                InventoryUI.Instance?.RefreshUI();
            }

            if (openAfterLockBreak)
                ForceOpenAfterUnlock();
        }

        /// <summary>Scripted unlock during a chase deadline — no subtitle / consume.</summary>
        public void ForceUnlockOpenSilent()
        {
            isLocked = false;
            ClearStuckOpenBlock();
            ForceOpenAfterUnlock();
        }

        /// <summary>Re-opens the screwdriver break with the required key still in inventory (escape look-back resume).</summary>
        public bool TryResumeScrewdriverBreak()
        {
            if (!isLocked || requiredKey == null || lockBreakMinigame == null)
                return false;
            var inventory = InventoryManager.Instance?.GetInventory();
            if (inventory == null || !inventory.Contains(requiredKey))
                return false;
            return ReceiveItem(requiredKey);
        }

        /// <summary>Opens if closed, skipping obstruction / interact cooldowns used by normal clicks.</summary>
        public void ForceOpenAfterUnlock()
        {
            if (objectOpen)
                return;

            float openAmount = GetOpenAmount(myAnimator);
            if (!hasAdditional)
            {
                ToggleSelf(openAmount);
                return;
            }

            float openAmountAdd = GetOpenAmount(additionalAnimator);
            ToggleSelf(openAmount);
            ToggleAdditional(openAmountAdd);
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

        /// <summary>
        /// Lockpick Continue: door stays locked, screwdriver break is off, chained subtitle key is used.
        /// </summary>
        public void ApplyContinueChainedLock(string messageKey = "gameplay.door_chained")
        {
            isLocked = true;
            _chainedAfterContinue = true;
            if (!string.IsNullOrWhiteSpace(messageKey))
                lockedMessageKey = messageKey;
        }

        /// <summary>Unlock without subtitle / unlock SFX / opening (padlock already handled feedback).</summary>
        public void UnlockSilently()
        {
            isLocked = false;
            _chainedAfterContinue = false;
        }

        /// <summary>Snap shut immediately — used after lockpick Continue day-reset.</summary>
        public void ForceCloseInstant() => SnapOpenState(false);

        /// <summary>Closes with the normal close animation and SFX if currently open.</summary>
        public void ForceClose()
        {
            if (!objectOpen)
                return;
            float openAmount = GetOpenAmount(myAnimator);
            if (!hasAdditional)
            {
                ToggleSelf(openAmount);
                return;
            }
            float openAmountAdd = GetOpenAmount(additionalAnimator);
            ToggleSelf(openAmount);
            ToggleAdditional(openAmountAdd);
        }

        /// <summary>Snap fully open or shut with no sound (checkpoint load, Continue reset).</summary>
        public void SnapOpenState(bool open)
        {
            _moving = false;
            _reverting = false;
            objectOpen = open;

            int hash = open ? openHash : closeHash;
            float time = open ? 1f : 0f;
            if (myAnimator != null)
                myAnimator.Play(hash, 0, time);

            if (hasAdditional && additionalAnimator != null)
                additionalAnimator.Play(hash, 0, time);

            SyncAdditionalFlags(open);
        }

        private void ToggleSelf(float openAmount)
        {
            openAmount = Mathf.Clamp01(openAmount);
            _reverting = false;

            if (objectOpen)
            {
                myAnimator.CrossFade(closeHash, crossFadeDuration, 0, openAmount);
                objectOpen = false;
                _moving = true;
                _movingOpen = false;
                PlayCloseSfx(transform.position);
            }
            else
            {
                myAnimator.CrossFade(openHash, crossFadeDuration, 0, openAmount);
                objectOpen = true;
                _moving = true;
                _movingOpen = true;
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
                additionalAnimator.CrossFade(openHash, crossFadeDuration, 0, openAmount);
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

        // Close plays the open clip in reverse (speed -1), so both states' normalized time is the open amount.
        float StateToOpenAmount(AnimatorStateInfo info)
        {
            if (info.shortNameHash == openHash || info.shortNameHash == closeHash)
                return GetClampedProgress(info);
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
            {
                OnOpenSoundPlayed?.Invoke();
                _openSfxRoutine = null;
                yield break;
            }

            if (handleOpenSound != null)
                SoundManager.Instance.PlaySFXAt(handleOpenSound, primaryPosition,.7f);

            if (openSound != null)
            {
                if (handleOpenSound != null)
                    yield return new WaitForSeconds(openSoundDelayAfterHandle);

                SoundManager.Instance.PlaySFXAt(openSound, primaryPosition, 1f);

                if (additionalPosition.HasValue)
                    SoundManager.Instance.PlaySFXSafe(openSound, additionalPosition.Value);

                OnOpenSoundPlayed?.Invoke();
            }
            else
            {
                // No open clip — fire at the same timing open would have played (after handle delay).
                if (handleOpenSound != null)
                    yield return new WaitForSeconds(openSoundDelayAfterHandle);
                OnOpenSoundPlayed?.Invoke();
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