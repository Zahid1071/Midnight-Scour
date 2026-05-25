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
        [SerializeField] private float crossFadeDuration = 0.10f;
        [Range(0f, 1f)]
        [SerializeField] private float minToggleProgress = 0.25f;

        [Header("Lock Settings (Player Only)")]
        public bool isLocked = false;
        [SerializeField] private InventoryItem requiredKey;
        
        [Tooltip("Kilitli animasyonu kaç saniye sürüyor? Bu süre boyunca tekrar tıklanamaz.")]
        [SerializeField] private float lockedAnimationDuration = 1.0f; // YENİ: Spam engelleyici süre

        [Header("Audio SFX")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
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


        public void Interact() => ObjectClicked(bypassLock: false);

        public void ObjectClicked() => ObjectClicked(bypassLock: false);

        public void ObjectClicked(bool bypassLock)
        {
            if (!bypassLock && Time.time < nextInteractTime)
                return;

            // --- KİLİTLİ DURUM KONTROLÜ (Burası aynı kalıyor) ---
            if (!bypassLock && !objectOpen && isLocked)
            {
                InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
                if (selectedItem != null && requiredKey != null && selectedItem == requiredKey) return; 
                if (Time.time < nextInteractTime) return;

                var hud = HudManager.Instance;
                if (hud != null)
                    hud.ShowSubtitle("The door is locked.", 2.5f, hud.WarningColor);

                InteractionUIManager.Instance?.PlayFailShake();

                Debug.Log("❌ Kapı kilitli! (Animasyon Tetikleniyor)");
                myAnimator.SetTrigger(lockedHash);
                if (hasAdditional) additionalAnimator.SetTrigger(lockedHash);
                nextInteractTime = Time.time + lockedAnimationDuration;
                return;
            }

            // ============================================================
            // 🔥 YENİ MANTIK: Sadece Open/Close ise Blending Yap 🔥
            // ============================================================
            
            var info = myAnimator.GetCurrentAnimatorStateInfo(0);
            
            // Varsayılan değer: 1f 
            // (ToggleSelf içinde 1-t yapıldığı için sonuç 0 olur, yani BAŞTAN başlar)
            float t = 1f; 

            // Eğer şu an oynayan şey AÇILMA veya KAPANMA ise, kaldığımız yeri hesapla
            if (info.shortNameHash == openHash || info.shortNameHash == closeHash)
            {
                t = GetClampedProgress(info);
            }

            // Min toggle süresi kontrolü (Sadece loop olmayan ve hareket halindeki animasyonlar için)
            if (t < minToggleProgress && !info.loop && t != 1f) return;

            // --- TEK KAPIYSA ---
            if (!hasAdditional)
            {
                ToggleSelf(t);
                return;
            }

            // --- ÇİFT KAPIYSA ---
            var addInfo = additionalAnimator.GetCurrentAnimatorStateInfo(0);
            float tAdd = 1f; // İkinci kapı için de varsayılan "Sıfırdan Başla"

            // İkinci kapı hareket halindeyse hesapla
            if (addInfo.shortNameHash == openHash || addInfo.shortNameHash == closeHash)
            {
                tAdd = GetClampedProgress(addInfo);
            }

            ToggleSelf(t);
            ToggleAdditional(tAdd);
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
                    InteractionUIManager.Instance?.PlayFailShake();
                    myAnimator.SetTrigger(lockedHash);
                    nextInteractTime = Time.time + lockedAnimationDuration;
                    return false;
                }

                HudManager.Instance?.ShowSubtitle("This item cannot be used here.", 2.5f);
                myAnimator.SetTrigger(lockedHash);
                nextInteractTime = Time.time + lockedAnimationDuration;
                return false;
            }

            var inventory = InventoryManager.Instance.GetInventory();
            if (!inventory.Contains(item))
            {
                HudManager.Instance?.ShowSubtitle("You do not have that item.", 2.5f);
                return false;
            }

            // ✅ KİLİT AÇILDI
            isLocked = false;
            nextInteractTime = Time.time + lockedAnimationDuration;
            HudManager.Instance?.ShowSubtitle($"{item.itemName} unlocked it.", 2.5f, HudManager.Instance.SuccessColor);
            Debug.Log($"✅ {item.itemName} ile kilit açıldı.");

            if (item.consumeOnUse)
            {
                InventoryManager.Instance.RemoveItem(item);
                InventoryUI.Instance?.RefreshUI();
            }

            PlaySound(unlockSound);
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

        private void ToggleSelf(float currentT)
        {
            if (objectOpen)
            {
                float mapped = 1f - currentT;
                myAnimator.CrossFade(closeHash, crossFadeDuration, 0, mapped);
                objectOpen = false;
                PlaySound(closeSound);
            }
            else
            {
                float mapped = 1f - currentT;
                myAnimator.CrossFade(openHash, crossFadeDuration, 0, mapped);
                objectOpen = true;
                PlaySound(openSound);
            }
        }

        private void ToggleAdditional(float currentT)
        {
            var other = animateAdditional.GetComponent<SimpleOpenClose>();
            if (other == null) return;

            if (objectOpenAdditional)
            {
                float mapped = 1f - currentT;
                additionalAnimator.CrossFade(closeHash, crossFadeDuration, 0, mapped);
                objectOpenAdditional = false;
                other.objectOpen = false;
                other.objectOpenAdditional = false;
                
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFXSafe(closeSound, animateAdditional.transform.position);
            }
            else
            {
                float mapped = 1f - currentT;
                additionalAnimator.CrossFade(openHash, crossFadeDuration, 0, mapped);
                objectOpenAdditional = true;
                other.objectOpen = true;
                other.objectOpenAdditional = true;
                
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFXSafe(openSound, animateAdditional.transform.position);
            }
        }

        private static float GetClampedProgress(AnimatorStateInfo info)
        {
            float t = info.normalizedTime;
            return info.loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);
        }
    }
}