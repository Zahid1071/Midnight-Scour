using System.Collections;
using FS_Atmo;
using UnityEngine;

/// <summary>
/// After the bar is taken: turning away re-sticks the already-shut corridor door (no second slam SFX).
/// Screwdriver break #2 — look back on the 3rd jam breakthrough (DoorBreakScare), then a short window for the
/// final breakthrough. Only a successful final break opens the door and starts the chase — the timer never
/// auto-opens the door.
/// </summary>
[DisallowMultipleComponent]
public class BasementBarEscapeTrap : MonoBehaviour
{
    enum Phase { Idle, Armed, Stuck, Breaking, FinalRush, Chase }

    [SerializeField] SimpleOpenClose door;
    [SerializeField] ScrewdriverLockBreakMinigame lockMinigame;
    [SerializeField] DoorBreakScare doorScare;
    [SerializeField] CorpseVanishEvent corpseEvent;
    [SerializeField] WallStanderChase wallStanderChase;

    [Header("Re-stick")]
    [Tooltip("Optional delay before re-sticking the already-shut door for the second screwdriver break.")]
    [SerializeField] float bangDelay;

    [Header("Final breakthrough")]
    [Tooltip("Seconds after the look back before he starts chasing if the door is still shut. Never auto-opens the door.")]
    [SerializeField] float finalBreakSeconds = 8f;

    [Header("Chase lights")]
    [SerializeField] LightSwitch[] chaseFlickerSwitches;
    [SerializeField] GameObject[] chaseFlickerLamps;
    [SerializeField] float flickerOnMin = 0.04f;
    [SerializeField] float flickerOnMax = 0.12f;
    [SerializeField] float flickerOffMin = 0.03f;
    [SerializeField] float flickerOffMax = 0.1f;

    Phase _phase;
    Coroutine _deadline;
    Coroutine _lampFlicker;
    bool _chaseLightsOn;
    Light[] _cachedChaseLights;
    bool _chaseLightsCached;

    void OnEnable()
    {
        if (wallStanderChase != null)
            wallStanderChase.WouldStartChase += OnWouldStartChase;
        if (doorScare != null)
            doorScare.LookBackFinished += OnLookBackFinished;
        if (lockMinigame != null)
            lockMinigame.BreakSucceeded += OnBreakSucceeded;
        if (wallStanderChase != null)
            wallStanderChase.ShutOut += OnShutOut;
    }

    void OnDisable()
    {
        if (wallStanderChase != null)
            wallStanderChase.WouldStartChase -= OnWouldStartChase;
        if (doorScare != null)
            doorScare.LookBackFinished -= OnLookBackFinished;
        if (lockMinigame != null)
            lockMinigame.BreakSucceeded -= OnBreakSucceeded;
        if (wallStanderChase != null)
            wallStanderChase.ShutOut -= OnShutOut;
        StopDeadline();
        StopChaseLights(leaveOn: true);
    }

    void Update()
    {
        if (_phase != Phase.Idle || corpseEvent == null || !corpseEvent.BarTaken)
            return;
        _phase = Phase.Armed;
    }

    void OnWouldStartChase()
    {
        if (_phase != Phase.Armed)
            return;
        _phase = Phase.Stuck;
        StartCoroutine(BangShutRoutine());
    }

    IEnumerator BangShutRoutine()
    {
        if (bangDelay > 0f)
            yield return new WaitForSeconds(bangDelay);

        // Door already banged shut on the return walk — only re-stick for the second break, no slam SFX.
        if (door != null)
            door.StickShutForScrewdriver();

        if (doorScare != null)
            doorScare.ArmEscapeLookBack();

        _phase = Phase.Breaking;
    }

    void OnLookBackFinished()
    {
        if (_phase != Phase.Breaking || doorScare == null || !doorScare.EscapeLookBackDone)
            return;
        _phase = Phase.FinalRush;
        // Warm roar / chase music + cache flicker lights during the final breakthrough window so the scream doesn't hitch.
        wallStanderChase?.WarmChaseAudio();
        EnsureChaseLightsCached();
        StopDeadline();
        _deadline = StartCoroutine(FinalRushDeadline());
    }

    IEnumerator FinalRushDeadline()
    {
        // Do not auto-reopen the minigame — player keeps facing where the look-back left them.
        float end = Time.time + Mathf.Max(0.5f, finalBreakSeconds);
        while (Time.time < end)
        {
            if (_phase == Phase.Chase)
                yield break;
            // They finished the lock themselves.
            if (door != null && !door.isLocked)
            {
                BeginChase();
                yield break;
            }
            yield return null;
        }

        if (_phase == Phase.Chase)
            yield break;

        // Time's up: he comes. Door stays shut unless they already broke it.
        if (lockMinigame != null && lockMinigame.IsRunning)
            lockMinigame.ForceExit(playCancelSfx: false);

        BeginChase();
        _deadline = null;
    }

    void OnBreakSucceeded()
    {
        // Chase only after look-back, and only when the player actually finishes the lock.
        if (_phase != Phase.FinalRush)
            return;
        BeginChase();
    }

    void BeginChase()
    {
        if (_phase == Phase.Chase)
            return;
        _phase = Phase.Chase;
        StopDeadline();
        StartChaseLights();
        if (wallStanderChase != null)
            wallStanderChase.BeginChaseNow();
    }

    void OnShutOut() => StopChaseLights(leaveOn: true);

    void StopDeadline()
    {
        if (_deadline == null)
            return;
        StopCoroutine(_deadline);
        _deadline = null;
    }

    void StartChaseLights()
    {
        if (_chaseLightsOn)
            return;
        _chaseLightsOn = true;
        if (chaseFlickerSwitches != null)
        {
            foreach (LightSwitch sw in chaseFlickerSwitches)
            {
                if (sw == null)
                    continue;
                sw.StartConstantFlicker(flickerOnMin, flickerOnMax, flickerOffMin, flickerOffMax);
            }
        }
        StopLampFlicker();
        if (chaseFlickerLamps != null && chaseFlickerLamps.Length > 0)
            _lampFlicker = StartCoroutine(ChaseLampFlickerRoutine());
    }

    void StopChaseLights(bool leaveOn)
    {
        if (!_chaseLightsOn && _lampFlicker == null)
            return;
        _chaseLightsOn = false;
        if (chaseFlickerSwitches != null)
        {
            foreach (LightSwitch sw in chaseFlickerSwitches)
                sw?.StopFlicker(leaveOn);
        }
        StopLampFlicker(leaveOn);
    }

    void StopLampFlicker(bool leaveOn = true)
    {
        if (_lampFlicker != null)
        {
            StopCoroutine(_lampFlicker);
            _lampFlicker = null;
        }
        SetChaseLampsLit(leaveOn);
    }

    IEnumerator ChaseLampFlickerRoutine()
    {
        while (true)
        {
            SetChaseLampsLit(true);
            yield return new WaitForSeconds(Random.Range(flickerOnMin, flickerOnMax));
            SetChaseLampsLit(false);
            yield return new WaitForSeconds(Random.Range(flickerOffMin, flickerOffMax));
        }
    }

    void SetChaseLampsLit(bool lit)
    {
        EnsureChaseLightsCached();
        if (_cachedChaseLights == null)
            return;
        for (int i = 0; i < _cachedChaseLights.Length; i++)
        {
            Light l = _cachedChaseLights[i];
            if (l != null)
                l.enabled = lit;
        }
    }

    void EnsureChaseLightsCached()
    {
        if (_chaseLightsCached)
            return;
        _chaseLightsCached = true;
        if (chaseFlickerLamps == null || chaseFlickerLamps.Length == 0)
        {
            _cachedChaseLights = System.Array.Empty<Light>();
            return;
        }

        var list = new System.Collections.Generic.List<Light>(8);
        foreach (GameObject lamp in chaseFlickerLamps)
        {
            if (lamp == null)
                continue;
            foreach (Light l in lamp.GetComponentsInChildren<Light>(true))
            {
                if (l == null)
                    continue;
                if (l.GetComponentInParent<Unity.Cinemachine.CinemachineCamera>() != null)
                    continue;
                list.Add(l);
            }
        }
        _cachedChaseLights = list.ToArray();
    }
}
