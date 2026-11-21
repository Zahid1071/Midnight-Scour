using System.Collections;
using FS_Atmo;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Return scare: walk into corridor → flicker → bang shut + stuck for screwdriver.
/// After the first screwdriver unlock: once the player enters the main basement (outside the corridor),
/// wait 1–2s, quietly close the door if still open (not locked), then 5–6s later play knock SFX.
/// </summary>
[DisallowMultipleComponent]
public class BasementReturnDoorTrap : MonoBehaviour
{
    enum State { Idle, HoldingOpen, WaitingBang, Stuck, Unlocked, CloseKnock, Done }

    [SerializeField] SimpleOpenClose door;
    [SerializeField] CorpseVanishEvent corpseEvent;
    [SerializeField] CorridorTension corridorTension;
    [Tooltip("Far door of the corridor; forward points out of the corridor (same as CorridorTension exit).")]
    [SerializeField] Transform corridorEntrance;
    [SerializeField] float corridorHalfWidth = 1.5f;
    [Tooltip("Metres into the corridor before the lights flicker.")]
    [SerializeField] float stepsInBeforeFlicker = 1.6f;
    [Tooltip("Seconds after the flicker before the door bangs shut.")]
    [SerializeField] float bangDelay = 1.4f;
    [Tooltip("Loud slam when the door shuts (Steam Audio + flat punch).")]
    [SerializeField] AudioClip bangClip;
    [SerializeField, Range(0f, 2f)] float bangVolume = 2f;
    [Tooltip("Flat (non-spatial) punch layered under the bang so it stays loud.")]
    [SerializeField, Range(0f, 1f)] float bangFlatLayer = 0.85f;
    [SerializeField] AudioMixerGroup mixerGroup;

    [Header("After first unlock → quiet close + knock")]
    [Tooltip("Seconds after entering the main basement before quietly closing (if still open).")]
    [SerializeField] Vector2 autoCloseDelay = new Vector2(1f, 2f);
    [Tooltip("Knock from the far side after the quiet close.")]
    [SerializeField] AudioClip leaveKnockClip;
    [SerializeField, Range(0f, 2f)] float leaveKnockVolume = 1.15f;
    [Tooltip("Seconds after the quiet close before the knock.")]
    [SerializeField] Vector2 leaveKnockDelay = new Vector2(5f, 6f);

    State _state;
    Vector3 _entryPos;
    Transform _player;
    Coroutine _closeKnockRoutine;
    bool _wasInCorridorAfterUnlock;

    void OnDisable()
    {
        if (_closeKnockRoutine != null)
        {
            StopCoroutine(_closeKnockRoutine);
            _closeKnockRoutine = null;
        }
    }

    void Update()
    {
        if (door == null || corpseEvent == null)
            return;
        if (_state == State.Done || _state == State.WaitingBang || _state == State.CloseKnock)
            return;
        if (corpseEvent.BarTaken)
            return;

        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null)
                return;
            _player = p.transform;
        }

        if (_state == State.Unlocked)
        {
            TickUnlocked();
            return;
        }

        if (_state == State.Stuck)
        {
            // First screwdriver unlock + door opened.
            if (!door.isLocked)
            {
                _state = State.Unlocked;
                _wasInCorridorAfterUnlock = InCorridor();
            }
            return;
        }

        if (!corpseEvent.ReturnArmed || corridorTension == null || !corridorTension.HasLeftCorridor)
            return;
        // Bang sequence only before the return lights-out finishes.
        if (corpseEvent.CurrentStage == CorpseVanishEvent.Stage.Done
            || corpseEvent.CurrentStage == CorpseVanishEvent.Stage.WaitingForLightsOut)
            return;

        if (_state == State.Idle)
        {
            if (!InCorridor())
                return;
            // Mute insects + basement bed the moment they step back into the corridor.
            SoundManager.Instance?.SetReturnScareAudioLock(true);
            door.PlayerCanClose = false;
            if (!door.objectOpen)
                door.ForceOpenAfterUnlock();
            _entryPos = _player.position;
            _state = State.HoldingOpen;
            return;
        }

        if (_state == State.HoldingOpen)
        {
            if (!InCorridor())
                return;
            Vector3 delta = _player.position - _entryPos;
            delta.y = 0f;
            if (delta.magnitude < stepsInBeforeFlicker)
                return;

            corpseEvent.NotifyReturnFlicker();
            _state = State.WaitingBang;
            StartCoroutine(BangShutRoutine());
        }
    }

    void TickUnlocked()
    {
        bool inCorridor = InCorridor();
        if (inCorridor)
        {
            _wasInCorridorAfterUnlock = true;
            return;
        }

        // Must have been in the corridor after unlock, then stepped into the main basement.
        if (!_wasInCorridorAfterUnlock)
            return;

        _state = State.CloseKnock;
        _closeKnockRoutine = StartCoroutine(QuietCloseThenKnockRoutine());
    }

    IEnumerator BangShutRoutine()
    {
        if (bangDelay > 0f)
            yield return new WaitForSeconds(bangDelay);

        Vector3 at = door != null ? door.transform.position : transform.position;
        if (door != null)
            door.StickShutForScrewdriver();

        if (bangClip != null)
        {
            SpatialAudio.PlayAt(bangClip, at, bangVolume, 5f, 35f, mixerGroup, transform, atmospheric: false);
            if (bangFlatLayer > 0f)
                SoundManager.Instance?.PlaySFX(bangClip, bangFlatLayer);
        }

        _state = State.Stuck;
    }

    IEnumerator QuietCloseThenKnockRoutine()
    {
        float closeWait = Random.Range(autoCloseDelay.x, autoCloseDelay.y);
        if (closeWait > 0f)
            yield return new WaitForSeconds(closeWait);

        if (corpseEvent != null && corpseEvent.BarTaken)
        {
            _closeKnockRoutine = null;
            _state = State.Done;
            yield break;
        }

        // Not locked — just close if still open.
        if (door != null && door.objectOpen)
            door.ForceClose();

        float knockWait = Random.Range(leaveKnockDelay.x, leaveKnockDelay.y);
        if (knockWait > 0f)
            yield return new WaitForSeconds(knockWait);

        if (leaveKnockClip != null && door != null && (corpseEvent == null || !corpseEvent.BarTaken))
        {
            Vector3 at = door.transform.position;
            if (corridorEntrance != null)
                at = door.transform.position + corridorEntrance.forward * 0.4f;
            SpatialAudio.PlayAt(leaveKnockClip, at, leaveKnockVolume, 1.5f, 22f, mixerGroup, transform,
                atmospheric: false);
            SoundManager.Instance?.PlaySFX(leaveKnockClip, Mathf.Clamp01(leaveKnockVolume * 0.55f));
        }

        _closeKnockRoutine = null;
        _state = State.Done;
    }

    bool InCorridor()
    {
        if (corridorEntrance == null || _player == null)
            return false;
        Vector3 offset = _player.position - corridorEntrance.position;
        return Vector3.Dot(offset, corridorEntrance.forward) < 0f
               && Mathf.Abs(Vector3.Dot(offset, corridorEntrance.right)) <= corridorHalfWidth;
    }

    /// <summary>Checkpoint / skip: quiet-close + knock already happened — never replay.</summary>
    public void MarkSequenceDone()
    {
        if (_closeKnockRoutine != null)
        {
            StopCoroutine(_closeKnockRoutine);
            _closeKnockRoutine = null;
        }
        _state = State.Done;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Dev shortcut: return trip door sequence already finished.</summary>
    public void DebugSkipReturnTripComplete()
    {
        MarkSequenceDone();
        if (door == null)
            return;
        if (door.isLocked)
            door.UnlockSilently();
        if (!door.objectOpen)
            door.SnapOpenState(true);
        DoorController ai = door.GetComponentInParent<DoorController>();
        if (ai != null)
            ai.SetLocked(false);
    }
#endif
}
