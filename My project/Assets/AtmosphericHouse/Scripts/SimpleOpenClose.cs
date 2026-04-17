using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Atmo
{
    public class SimpleOpenClose : MonoBehaviour
    {
        [Header("State Names (Animator)")]
        [SerializeField] private string openStateName  = "Open";
        [SerializeField] private string closeStateName = "Close";

        [Header("Blending")]
        [Tooltip("Seconds to blend when (re)starting Open/Close.")]
        [SerializeField] private float crossFadeDuration = 0.10f;

        [Tooltip("Allow input when current state has reached this % progress (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float minToggleProgress = 0.25f; // 25%

        [Header("Initial/Runtime Flags")]
        public bool objectOpen;
        public bool objectOpenAdditional;

        [Header("Optional Paired Door (e.g., double door)")]
        public GameObject animateAdditional;

        private Animator myAnimator;
        private Animator additionalAnimator;
        private bool hasAdditional = false;

        private int openHash;
        private int closeHash;

        void Awake()
        {
            myAnimator = GetComponent<Animator>();
            if (myAnimator == null)
                myAnimator = GetComponentInParent<Animator>();

            openHash  = Animator.StringToHash(openStateName);
            closeHash = Animator.StringToHash(closeStateName);

            if (animateAdditional != null && animateAdditional.GetComponent<SimpleOpenClose>())
            {
                additionalAnimator = animateAdditional.GetComponent<Animator>();
                hasAdditional = additionalAnimator != null;
                objectOpenAdditional = animateAdditional.GetComponent<SimpleOpenClose>().objectOpen;
            }
            else
            {
                hasAdditional = false;
            }
        }

        void Start()
        {
            // Set initial pose instantly (no blend on boot to avoid a visible pop)
            if (objectOpen)
                myAnimator.Play(openHash, 0, 1f);
            else
                myAnimator.Play(closeHash, 0, 1f);

            if (hasAdditional)
            {
                if (objectOpenAdditional)
                    additionalAnimator.Play(openHash, 0, 1f);
                else
                    additionalAnimator.Play(closeHash, 0, 1f);
            }
        }

        /// <summary>
        /// Called by your player/use script.
        /// </summary>
        public void ObjectClicked()
        {
            // If currently transitioning, we can still accept input, but read from next/ current safely.
            var info = myAnimator.GetCurrentAnimatorStateInfo(0);
            float t = GetClampedProgress(info);

            // Only allow early re-toggle after minToggleProgress (25% by default)
            if (t < minToggleProgress && !info.loop) // for non-looping door clips
                return;

            if (!hasAdditional)
            {
                ToggleSelf(t);
                return;
            }

            // With paired door: try to keep their poses coherent and states aligned
            var addInfo = additionalAnimator.GetCurrentAnimatorStateInfo(0);
            float tAdd = GetClampedProgress(addInfo);

            ToggleSelf(t);
            ToggleAdditional(tAdd);
        }

        private void ToggleSelf(float currentT)
        {
            if (objectOpen)
            {
                // Going Open -> Close
                // Map pose so it continues from where it is:
                // if you authored Open (0->1) and Close (0->1) as time-mirrors,
                // use (1 - currentT) when entering the opposite clip.
                float mapped = 1f - currentT;
                myAnimator.CrossFade(closeHash, crossFadeDuration, 0, mapped);
                objectOpen = false;
            }
            else
            {
                // Going Close -> Open
                float mapped = 1f - currentT;
                myAnimator.CrossFade(openHash, crossFadeDuration, 0, mapped);
                objectOpen = true;
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
            }
            else
            {
                float mapped = 1f - currentT;
                additionalAnimator.CrossFade(openHash, crossFadeDuration, 0, mapped);
                objectOpenAdditional = true;
                other.objectOpen = true;
                other.objectOpenAdditional = true;
            }
        }

        private static float GetClampedProgress(AnimatorStateInfo info)
        {
            // For non-looping door clips, normalizedTime grows past 1 after it ends.
            // We want the fractional progress within 0..1.
            float t = info.normalizedTime;
            // If it’s a non-looping clip, clamp; if loop, take fractional part:
            return info.loop ? Mathf.Repeat(t, 1f) : Mathf.Clamp01(t);
        }
    }
}
