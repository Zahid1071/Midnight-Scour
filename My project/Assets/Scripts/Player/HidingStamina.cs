using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// While hidden, samples the microphone and drives the hide meter from loudness
/// (Fear to Fathom–style). Loud spikes / sustained noise send nearby NPCs to the
/// hide spot for a guaranteed capture (same path as being seen entering a hide).
/// </summary>
[DisallowMultipleComponent]
public class HidingStamina : MonoBehaviour
{
    [Header("Microphone")]
    [SerializeField] int sampleRate = 44100;
    [SerializeField] int sampleWindow = 256;
    [Tooltip("Mic level at or below this dB counts as silence.")]
    [SerializeField] float noiseFloorDb = -55f;
    [Tooltip("Mic level at or above this dB counts as fully loud.")]
    [SerializeField] float peakDb = -15f;
    [SerializeField, Range(0.1f, 5f)] float sensitivity = 1f;
    [SerializeField] float loudnessSmoothSpeed = 10f;

    [Header("Detection")]
    [Tooltip("Loudness above this starts building a sustained alert.")]
    [SerializeField, Range(0f, 1f)] float sustainedLoudnessThreshold = 0.45f;
    [SerializeField] float sustainedSecondsToAlert = 0.85f;
    [Tooltip("Instant alert when smoothed loudness crosses this.")]
    [SerializeField, Range(0f, 1f)] float spikeLoudnessThreshold = 0.75f;
    [SerializeField] float alertCooldownSeconds = 2.5f;

    [Header("UI")]
    [SerializeField] GameObject staminaPanel;
    [SerializeField] Slider staminaSlider;
    [SerializeField] Image staminaFillImage;

    [Header("Bar colors")]
    [SerializeField] Color fullColor = new Color(0.45f, 0.95f, 0.5f, 1f);
    [SerializeField] Color emptyColor = new Color(0.95f, 0.28f, 0.22f, 1f);

    [Header("Bar shake")]
    [SerializeField] float maxBarShakeStrength = 7f;
    [Tooltip("Shake / cam stress begins once loudness exceeds this.")]
    [SerializeField, Range(0f, 1f)] float shakeStartLoudness = 0.35f;

    [Header("Hiding camera noise")]
    [SerializeField] CinemachineBasicMultiChannelPerlin hidingCameraNoise;
    [SerializeField] float maxAmplitudeMultiplier = 3f;
    [SerializeField] float maxFrequencyMultiplier = 2.5f;
    [SerializeField] float noiseFadeSeconds = 1.5f;

    PlayerHider hider;
    float defaultAmplitudeGain;
    float defaultFrequencyGain;
    RectTransform barRect;
    Vector2 barRestPos;
    CanvasGroup panelCanvasGroup;
    Coroutine noiseFadeRoutine;

    string micDevice;
    AudioClip micClip;
    float[] sampleBuffer;
    float smoothedLoudness;
    float sustainedTimer;
    float alertCooldownRemaining;
    bool wasHidden;
    bool micUnavailableWarned;

    /// <summary>0 = silent, 1 = loud (smoothed). Also drives the bar fill.</summary>
    public float Loudness => smoothedLoudness;

    void Awake()
    {
        hider = GetComponent<PlayerHider>();
        sampleWindow = Mathf.Max(64, sampleWindow);
        sampleBuffer = new float[sampleWindow];

        if (staminaSlider != null)
        {
            staminaSlider.interactable = false;
            barRect = staminaSlider.GetComponent<RectTransform>();
            if (barRect != null)
                barRestPos = barRect.anchoredPosition;
        }

        if (staminaPanel != null)
        {
            panelCanvasGroup = staminaPanel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = staminaPanel.AddComponent<CanvasGroup>();
        }
    }

    void Start()
    {
        ResolveHidingCameraNoise();
        SetPanelVisible(false);
        RefreshUI();
    }

    void OnDestroy()
    {
        StopMicrophone();
    }

    void OnApplicationQuit()
    {
        StopMicrophone();
    }

    void ResolveHidingCameraNoise()
    {
        if (hidingCameraNoise == null && HidingCameraSystem.Instance != null)
            hidingCameraNoise = HidingCameraSystem.Instance.GetComponent<CinemachineBasicMultiChannelPerlin>();

        if (hidingCameraNoise == null)
            return;

        defaultAmplitudeGain = hidingCameraNoise.AmplitudeGain;
        defaultFrequencyGain = hidingCameraNoise.FrequencyGain;
    }

    void Update()
    {
        bool hidden = hider != null && hider.isHidden;

        if (hidden != wasHidden)
        {
            if (hidden)
                OnEnterHide();
            else
                OnExitHide();

            wasHidden = hidden;
        }

        if (hidden)
            TickWhileHidden();
        else
            smoothedLoudness = Mathf.MoveTowards(smoothedLoudness, 0f, Time.deltaTime * loudnessSmoothSpeed);

        if (alertCooldownRemaining > 0f)
            alertCooldownRemaining -= Time.deltaTime;

        RefreshUI();
    }

    void OnEnterHide()
    {
        CancelNoiseFade();
        sustainedTimer = 0f;
        smoothedLoudness = 0f;
        ResetMicAlertEscalation();
        StartMicrophone();
    }

    void OnExitHide()
    {
        StopMicrophone();
        StartNoiseFadeOut();
        ResetMicAlertEscalation();
    }

    void TickWhileHidden()
    {
        float raw = SampleMicrophoneLoudness();
        float sens = sensitivity;
        if (GameSettingsManager.Instance != null)
            sens *= GameSettingsManager.Instance.MicSensitivityMultiplier;
        float target = Mathf.Clamp01(raw * sens);
        float t = 1f - Mathf.Exp(-loudnessSmoothSpeed * Time.deltaTime);
        smoothedLoudness = Mathf.Lerp(smoothedLoudness, target, t);

        EvaluateAlerts();
    }

    void EvaluateAlerts()
    {
        if (alertCooldownRemaining > 0f)
            return;

        if (smoothedLoudness >= spikeLoudnessThreshold)
        {
            TriggerButcherHideCheck();
            sustainedTimer = 0f;
            return;
        }

        if (smoothedLoudness >= sustainedLoudnessThreshold)
        {
            sustainedTimer += Time.deltaTime;
            if (sustainedTimer >= sustainedSecondsToAlert)
            {
                TriggerButcherHideCheck();
                sustainedTimer = 0f;
            }
        }
        else
        {
            sustainedTimer = Mathf.Max(0f, sustainedTimer - Time.deltaTime * 2f);
        }
    }

    void TriggerButcherHideCheck()
    {
        alertCooldownRemaining = alertCooldownSeconds;

        HidingSpot spot = hider != null ? hider.CurrentHidingSpot : null;
        if (spot == null || spot.searchCheckpointOnly)
            return;

        Vector3 noisePos = spot.GetSearchPivot() != null
            ? spot.GetSearchPivot().position
            : spot.transform.position;

        // Only an NPC standing in the SAME ROOM as the hiding spot can hear the mic —
        // noise never carries through walls, other rooms, or floors. Ambush is exempt.
        RoomAnchor spotRoom = spot.GetComponentInParent<RoomAnchor>();
        if (spotRoom == null)
            return;

        // Same path as "NPC saw you enter hide": run to the spot, then capture on arrival.
        NPCController best = null;
        float bestDist = float.MaxValue;

        foreach (var npc in Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None))
        {
            if (npc == null)
                continue;
            if (npc.HearingDisabled)
                continue;
            if (npc.currentState == NPCController.AIState.Ambush
                || npc.currentState == NPCController.AIState.Capture
                || npc.currentState == NPCController.AIState.Dead
                || npc.currentState == NPCController.AIState.Scripted)
                continue;

            if (npc.GetCurrentRoom() != spotRoom)
                continue;

            float dist = Vector3.Distance(npc.transform.position, noisePos);
            if (dist < bestDist)
            {
                best = npc;
                bestDist = dist;
            }
        }

        if (best == null)
            return;

        if (best.perception != null)
            best.perception.RedirectLastKnownPosition(noisePos);

        best.BeginWitnessedHideCapture(spot);
    }

    void ResetMicAlertEscalation()
    {
        hider?.ClearMicCaptureOverride();
    }

    void StartMicrophone()
    {
        if (micClip != null)
            return;

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            if (!micUnavailableWarned)
            {
                micUnavailableWarned = true;
                Debug.LogWarning("HidingStamina: no microphone found — hide meter stays quiet.");
            }
            return;
        }

        micDevice = Microphone.devices[0];
        micClip = Microphone.Start(micDevice, true, 1, sampleRate);

        if (micClip == null)
        {
            micDevice = null;
            Debug.LogWarning("HidingStamina: failed to start microphone.");
        }
    }

    void StopMicrophone()
    {
        if (!string.IsNullOrEmpty(micDevice))
        {
            if (Microphone.IsRecording(micDevice))
                Microphone.End(micDevice);
        }

        micDevice = null;
        micClip = null;
    }

    float SampleMicrophoneLoudness()
    {
        if (micClip == null || string.IsNullOrEmpty(micDevice))
            return 0f;

        int micPos = Microphone.GetPosition(micDevice);
        if (micPos <= 0 || sampleBuffer == null || sampleBuffer.Length == 0)
            return 0f;

        int start = micPos - sampleBuffer.Length;
        if (start < 0)
            start += micClip.samples;

        try
        {
            micClip.GetData(sampleBuffer, start);
        }
        catch
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < sampleBuffer.Length; i++)
        {
            float s = sampleBuffer[i];
            sum += s * s;
        }

        float rms = Mathf.Sqrt(sum / sampleBuffer.Length);
        float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-7f));
        return Mathf.InverseLerp(noiseFloorDb, peakDb, db);
    }

    void CancelNoiseFade()
    {
        if (noiseFadeRoutine != null)
        {
            StopCoroutine(noiseFadeRoutine);
            noiseFadeRoutine = null;
        }
    }

    void StartNoiseFadeOut()
    {
        if (hidingCameraNoise == null)
            return;

        CancelNoiseFade();
        noiseFadeRoutine = StartCoroutine(FadeNoiseRoutine());
    }

    IEnumerator FadeNoiseRoutine()
    {
        float startAmp = hidingCameraNoise.AmplitudeGain;
        float startFreq = hidingCameraNoise.FrequencyGain;
        float duration = Mathf.Max(0.01f, noiseFadeSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / duration);
            hidingCameraNoise.AmplitudeGain = Mathf.Lerp(defaultAmplitudeGain, startAmp, t);
            hidingCameraNoise.FrequencyGain = Mathf.Lerp(defaultFrequencyGain, startFreq, t);
            yield return null;
        }

        hidingCameraNoise.AmplitudeGain = defaultAmplitudeGain;
        hidingCameraNoise.FrequencyGain = defaultFrequencyGain;
        noiseFadeRoutine = null;
    }

    void RefreshUI()
    {
        bool hidden = hider != null && hider.isHidden;
        SetPanelVisible(hidden);

        float loudness = smoothedLoudness;

        if (staminaSlider != null)
            staminaSlider.value = loudness;

        if (panelCanvasGroup != null)
            panelCanvasGroup.alpha = 1f;

        // Empty while quiet; fills toward red as noise rises.
        if (staminaFillImage != null)
            staminaFillImage.color = Color.Lerp(fullColor, emptyColor, loudness);

        ApplyBarShake(hidden);
        ApplyCameraNoise(hidden);
    }

    void SetPanelVisible(bool visible)
    {
        if (staminaPanel != null)
            staminaPanel.SetActive(visible);
    }

    void ApplyBarShake(bool hidden)
    {
        if (barRect == null)
            return;

        if (!hidden)
        {
            barRect.anchoredPosition = barRestPos;
            return;
        }

        float strength = GetBarShakeStrength();
        if (strength <= 0f)
        {
            barRect.anchoredPosition = barRestPos;
            return;
        }

        float x = Random.Range(-strength, strength);
        float y = Random.Range(-strength, strength);
        barRect.anchoredPosition = barRestPos + new Vector2(x, y);
    }

    void ApplyCameraNoise(bool hidden)
    {
        if (hidingCameraNoise == null || !hidden || noiseFadeRoutine != null)
            return;

        float stress = GetLoudnessStress();
        hidingCameraNoise.AmplitudeGain = Mathf.Lerp(defaultAmplitudeGain, defaultAmplitudeGain * maxAmplitudeMultiplier, stress);
        hidingCameraNoise.FrequencyGain = Mathf.Lerp(defaultFrequencyGain, defaultFrequencyGain * maxFrequencyMultiplier, stress);
    }

    float GetLoudnessStress()
    {
        if (smoothedLoudness <= shakeStartLoudness)
            return 0f;

        float t = (smoothedLoudness - shakeStartLoudness) / Mathf.Max(0.01f, 1f - shakeStartLoudness);
        return t * t;
    }

    float GetBarShakeStrength() => maxBarShakeStrength * GetLoudnessStress();
}
