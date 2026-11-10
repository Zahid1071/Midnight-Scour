using System.Collections;
using UnityEngine;

public class CarEscapePoint : MonoBehaviour, IInteractable
{
    [Header("Demo")]
    [Tooltip("Demo build: the car door is just locked, and the part slots and part pickups are switched off.")]
    [SerializeField] bool repairDisabledForDemo = true;
    [SerializeField] AudioClip lockedSfx;
    [SerializeField, Range(0f, 1f)] float lockedVolume = 1f;
    [SerializeField] float lockedFeedbackCooldown = 1f;

    [Header("Locked → window scare")]
    [Tooltip("After the first locked feedback (once the leave-basement car shot has finished), wait this long then play the butcher-in-the-window beat.")]
    [SerializeField] float lockedWindowScareDelay = 2f;

    public bool RepairDisabledForDemo => repairDisabledForDemo;

    float _nextLockedTime;
    bool _windowScareStarted;

    public bool Interact()
    {
        if (repairDisabledForDemo)
            return PlayLocked();

        var slots = transform.root.GetComponentsInChildren<CarPartSlot>(true);
        if (slots == null || slots.Length == 0)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.escape_not_setup"), 3f);
            Debug.LogWarning("⚠️ No CarPartSlot components found under CarEscapePoint.");
            return false;
        }

        var missing = new System.Collections.Generic.List<string>();
        foreach (var slot in slots)
        {
            if (!slot.IsInstalled())
                missing.Add(slot.GetRequiredItemName());
        }

        if (missing.Count == 0)
        {
            var clip = SoundManager.Instance.sfxBank[2];
            if (!SoundManager.Instance.IsClipPlayingAt(clip, transform.position))
                SoundManager.Instance.PlaySFXAt(clip, transform.position, 0.7f);

            HudManager.Instance?.ShowSubtitle(
                Loc.Get("gameplay.all_parts_installed"), 4f, HudManager.Instance.SuccessColor);
            Debug.Log("✅ All parts installed! You escaped!");
            return true;
        }

        var failClip = SoundManager.Instance.sfxBank[1];
        if (!SoundManager.Instance.IsClipPlayingAt(failClip, transform.position))
            SoundManager.Instance.PlaySFXAt(failClip, transform.position);

        string list = string.Join(", ", missing);
        HudManager.Instance?.ShowSubtitle(Loc.Format("gameplay.still_missing", list), 4f);
        Debug.Log("❌ You still need some parts to escape.");
        return false;
    }

    bool PlayLocked()
    {
        if (Time.time < _nextLockedTime)
            return false;

        _nextLockedTime = Time.time + lockedFeedbackCooldown;
        var hud = HudManager.Instance;
        hud?.ShowSubtitle(Loc.Get("gameplay.locked"), 2.5f, hud.WarningColor);
        if (lockedSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(lockedSfx, transform.position, lockedVolume, 1f, -1f, transform);

        // Once out of the basement (car shot done): first locked click arms the window stare → car investigate.
        if (!_windowScareStarted && ButcherWindowShot.HasFinished)
        {
            _windowScareStarted = true;
            StartCoroutine(DelayedWindowScare());
        }

        return false;
    }

    IEnumerator DelayedWindowScare()
    {
        if (lockedWindowScareDelay > 0f)
            yield return new WaitForSeconds(lockedWindowScareDelay);

        ButcherWindowShot shot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
        if (shot == null)
        {
            Debug.LogWarning("[CarEscapePoint] Locked window scare: no ButcherWindowShot in the scene.", this);
            yield break;
        }

        // Investigate where the player actually stood when they tried the door — not the Outside_Room anchor.
        GameObject player = GameObject.FindWithTag("Player");
        Vector3 investigateAt = player != null ? player.transform.position : transform.position;
        if (!shot.TryPlayWindowCarInvestigate(investigateAt))
            Debug.LogWarning("[CarEscapePoint] Locked window scare could not start (already playing or finished).", this);
    }
}
