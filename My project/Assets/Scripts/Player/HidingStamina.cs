using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// While hidden, samples the microphone and drives the hide meter from loudness
/// (Fear to Fathom–style). Loud spikes / sustained noise send nearby NPCs to the
/// hide spot for a guaranteed capture (same path as being seen entering a hide).
/// Scripted scenes can also show the bar out of hiding (SetOpenListening) and react to NoiseAlert.
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
    [Tooltip("Microphone icon shown beside the loudness bar. Leave empty to hide the icon.")]
    [SerializeField] Sprite microphoneIcon;
    [Tooltip("Optional legacy panel — disabled when runtime UI is created.")]
    [SerializeField] GameObject staminaPanel;

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
    GameObject runtimePanel;
    Slider staminaSlider;
    Image staminaFillImage;
    RectTransform barRect;
    Vector2 barRestPos;
    Coroutine noiseFadeRoutine;
    bool ownsRuntimeUi;
    UIFadeHelper _panelFade;

    string micDevice;
    AudioClip micClip;
    float[] sampleBuffer;
    float smoothedLoudness;
    float sustainedTimer;
    float alertCooldownRemaining;
    bool wasHidden;
    bool micUnavailableWarned;
    bool micWasOff;
    bool openListening;

    /// <summary>0 = silent, 1 = loud (smoothed). Also drives the bar fill.</summary>
    public float Loudness => smoothedLoudness;

    /// <summary>Raised on every loud spike / sustained noise, hidden or open listening.</summary>
    public event System.Action NoiseAlert;

    /// <summary>
    /// On: the bar shows and the mic is listened to out of hiding too (kept off while input is blocked).
    /// Hiding spot captures still only come from noise made while hidden.
    /// </summary>
    public void SetOpenListening(bool on)
    {
        if (on == openListening)
            return;
        openListening = on;
        sustainedTimer = 0f;
    }

    bool OpenListeningActive => openListening && !PlayerControlGate.IsInputBlocked;

    static bool MicDisabled => GameSettingsManager.Instance != null && GameSettingsManager.Instance.MicDisabled;

    /// <summary>The icon shown under the bar.</summary>
    public Sprite MicrophoneIcon => microphoneIcon;

    void Awake()
    {
        hider = GetComponent<PlayerHider>();
        sampleWindow = Mathf.Max(64, sampleWindow);
        sampleBuffer = new float[sampleWindow];

        // Prefer runtime bar (same style as lock-break). Hide any scene-built panel.
        if (staminaPanel != null)
            staminaPanel.SetActive(false);
    }

    void Start()
    {
        ResolveHidingCameraNoise();
        EnsureRuntimeUi();
        SetPanelVisible(false);
        RefreshUI();
        // Warm the mic once at load. Start/Stop on every hide enter causes a main-thread hitch
        // on the same frame as stealth visuals/layer — feels like "mesh disable lag".
        micWasOff = MicDisabled;
        StartMicrophone();
    }

    void OnDestroy()
    {
        StopMicrophone();
        if (wasHidden)
            GameplayHudVisibility.Instance?.SetHiddenDuringHiding(false);
        TeardownRuntimeUi();
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

        bool micOff = MicDisabled;
        if (micOff != micWasOff)
        {
            micWasOff = micOff;
            sustainedTimer = 0f;
            if (micOff)
                StopMicrophone();
            else
                StartMicrophone();
        }

        if (!micOff && (hidden || OpenListeningActive))
            TickListening();
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
        // Prefer warm mic from Start. Only start here if warm-up failed (no device yet, etc.).
        if (micClip == null)
            StartMicrophone();
        GameplayHudVisibility.Instance?.SetHiddenDuringHiding(true);
    }

    void OnExitHide()
    {
        // Keep mic recording so the next hide does not hitch on Microphone.Start.
        StartNoiseFadeOut();
        ResetMicAlertEscalation();
        // Always clear the hide latch. Capture already hides HUD via BeginCaptureUi /
        // CaptureUiActive / ReasonCaptured — skipping this left inventory dead forever
        // after a successful hide-grab (game-over → Continue never re-showed it).
        GameplayHudVisibility.Instance?.SetHiddenDuringHiding(false);
    }

    void TickListening()
    {
        float gainDb = 0f;
        float sens = sensitivity;
        if (GameSettingsManager.Instance != null)
        {
            gainDb = GameSettingsManager.Instance.MicGainDb;
            sens *= GameSettingsManager.Instance.MicSensitivityMultiplier;
        }
        float raw = SampleMicrophoneLoudness(gainDb);
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
        NoiseAlert?.Invoke();

        if (hider == null || !hider.isHidden)
            return;
        HidingSpot spot = hider != null ? hider.CurrentHidingSpot : null;
        if (spot == null || spot.searchCheckpointOnly)
            return;

        Vector3 noisePos = spot.GetSearchPivot() != null
            ? spot.GetSearchPivot().position
            : spot.transform.position;

        // Only an NPC standing in the SAME ROOM as the hiding spot can hear the mic —
        // noise never carries through walls, other rooms, or floors.
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
            if (ThunderManager.ArePlayerNoisesMasked)
                continue;
            // Ambush still hears same-room mic — death copy stays "too much noise".
            if (npc.currentState == NPCController.AIState.Capture
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

        // Mic noise always owns the death copy — even during ambush.
        CaptureContext.Set(CaptureReason.HideNoise);
        best.BeginWitnessedHideCapture(spot);
    }

    void ResetMicAlertEscalation()
    {
        hider?.ClearMicCaptureOverride();
    }

    void StartMicrophone()
    {
        if (micClip != null || MicDisabled)
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

    float SampleMicrophoneLoudness(float gainDb)
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
        float db = 20f * Mathf.Log10(Mathf.Max(rms, 1e-7f)) + gainDb;
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
        EnsureRuntimeUi();

        bool captured = PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured);
        bool hidden = hider != null && hider.isHidden && !captured;
        // Stays up behind a tutorial card, which may be explaining it.
        bool shown = !MicDisabled
                     && (hidden || (openListening && !captured
                                    && (!PlayerControlGate.IsInputBlocked || TutorialPopupUI.IsShowing)));
        SetPanelVisible(shown);

        float loudness = smoothedLoudness;

        if (staminaSlider != null)
            staminaSlider.value = loudness;

        // Empty while quiet; fills toward red as noise rises.
        if (staminaFillImage != null)
            staminaFillImage.color = Color.Lerp(fullColor, emptyColor, loudness);

        ApplyBarShake(shown);
        ApplyCameraNoise(hidden);
    }

    void SetPanelVisible(bool visible)
    {
        if (runtimePanel == null) return;
        if (_panelFade == null)
            _panelFade = new UIFadeHelper(runtimePanel);
        _panelFade.SetVisible(visible);
    }

    void EnsureRuntimeUi()
    {
        if (staminaSlider != null)
            return;

        Canvas canvas = null;
        if (HudManager.Instance != null)
            canvas = HudManager.Instance.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        ownsRuntimeUi = true;

        // Vertical bar, right-center of screen (mic icon below).
        runtimePanel = new GameObject("MicLoudnessPanel", typeof(RectTransform));
        runtimePanel.transform.SetParent(canvas.transform, false);
        var panelRt = runtimePanel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(1f, 0.5f);
        panelRt.anchorMax = new Vector2(1f, 0.5f);
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.anchoredPosition = new Vector2(-200f, 0f);
        panelRt.sizeDelta = new Vector2(40f, 540f);
        barRect = panelRt;
        barRestPos = panelRt.anchoredPosition;

        float iconSize = 36f;
        float gap = 30f;
        float barBottom = microphoneIcon != null ? iconSize + gap : 0f;

        if (microphoneIcon != null)
        {
            var iconGo = new GameObject("MicIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(runtimePanel.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0f);
            iconRt.anchorMax = new Vector2(0.5f, 0f);
            iconRt.pivot = new Vector2(0.5f, 0f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = microphoneIcon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.color = Color.white;
        }

        var barRoot = new GameObject("Bar", typeof(RectTransform));
        barRoot.transform.SetParent(runtimePanel.transform, false);
        var barRootRt = barRoot.GetComponent<RectTransform>();
        barRootRt.anchorMin = Vector2.zero;
        barRootRt.anchorMax = Vector2.one;
        barRootRt.offsetMin = new Vector2(6f, barBottom);
        barRootRt.offsetMax = new Vector2(-6f, 0f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(barRoot.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        bgImg.raycastTarget = false;

        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(barRoot.transform, false);
        var sliderRt = sliderGo.GetComponent<RectTransform>();
        sliderRt.anchorMin = Vector2.zero;
        sliderRt.anchorMax = Vector2.one;
        sliderRt.offsetMin = new Vector2(4f, 4f);
        sliderRt.offsetMax = new Vector2(-4f, -4f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = Vector2.zero;
        fillAreaRt.offsetMax = Vector2.zero;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(fillArea.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        staminaFillImage = fillGo.GetComponent<Image>();
        staminaFillImage.color = fullColor;
        staminaFillImage.raycastTarget = false;

        staminaSlider = sliderGo.GetComponent<Slider>();
        staminaSlider.interactable = false;
        staminaSlider.minValue = 0f;
        staminaSlider.maxValue = 1f;
        staminaSlider.wholeNumbers = false;
        staminaSlider.transition = Selectable.Transition.None;
        staminaSlider.fillRect = fillRt;
        staminaSlider.targetGraphic = staminaFillImage;
        staminaSlider.direction = Slider.Direction.BottomToTop;
        staminaSlider.value = 0f;
    }

    void TeardownRuntimeUi()
    {
        if (!ownsRuntimeUi)
            return;

        if (runtimePanel != null)
            Destroy(runtimePanel);

        runtimePanel = null;
        staminaSlider = null;
        staminaFillImage = null;
        barRect = null;
        ownsRuntimeUi = false;
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
