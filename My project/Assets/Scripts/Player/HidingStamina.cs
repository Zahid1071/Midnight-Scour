using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drains while the player is hidden, refills after a short cooldown, and drives the stamina bar.
/// Plays an out-of-breath one-shot on exit when stamina is low, then restores the default breathing loop.
/// </summary>
[DisallowMultipleComponent]
public class HidingStamina : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] float maxHideSeconds = 20f;
    [SerializeField] float cooldownSeconds = 3f;
    [SerializeField] float regenSeconds = 8f;

    [Header("UI")]
    [SerializeField] GameObject staminaPanel;
    [SerializeField] Slider staminaSlider;
    [SerializeField] Image staminaFillImage;

    [Header("Bar colors")]
    [SerializeField] Color fullColor = new Color(0.45f, 0.95f, 0.5f, 1f);
    [SerializeField] Color emptyColor = new Color(0.95f, 0.28f, 0.22f, 1f);

    [Header("Bar shake")]
    [SerializeField] float maxBarShakeStrength = 7f;
    [SerializeField] float shakeStartStamina = 0.5f;

    [Header("Hiding camera noise")]
    [SerializeField] CinemachineBasicMultiChannelPerlin hidingCameraNoise;
    [SerializeField] float maxAmplitudeMultiplier = 3f;
    [SerializeField] float maxFrequencyMultiplier = 2.5f;
    [SerializeField] float noiseFadeSeconds = 1.5f;

    [Header("Out of breath")]
    [SerializeField] AudioSource breathingSource;
    [SerializeField] AudioClip outOfBreathClip;
    [SerializeField] float outOfBreathStaminaThreshold = 0.1f;

    [Header("Bar fade")]
    [SerializeField] float barFadeOutStartStamina = 0.15f;

    PlayerHider hider;
    AudioClip normalBreathingClip;
    float normalBreathingVolume;
    float normalBreathingPitch;
    float defaultAmplitudeGain;
    float defaultFrequencyGain;
    RectTransform barRect;
    Vector2 barRestPos;
    CanvasGroup panelCanvasGroup;
    Coroutine recoveryRoutine;
    Coroutine noiseFadeRoutine;
    float stamina = 1f;
    float cooldownRemaining;
    bool forcedExitPending;
    bool wasHidden;

    public bool CanEnterHide => stamina > 0f && cooldownRemaining <= 0f;
    public bool IsOnCooldown => cooldownRemaining > 0f;

    void Awake()
    {
        hider = GetComponent<PlayerHider>();

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

        if (breathingSource != null)
        {
            normalBreathingClip = breathingSource.clip;
            normalBreathingVolume = breathingSource.volume;
            normalBreathingPitch = breathingSource.pitch;
        }
    }

    void Start()
    {
        ResolveHidingCameraNoise();
        SetPanelVisible(false);
        RefreshUI();
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
        else if (hider == null || !hider.IsHidingEngaged)
            TickWhileNotHidden();

        RefreshUI();
    }

    void OnEnterHide()
    {
        CancelBreathRecovery();
        CancelNoiseFade();
    }

    void OnExitHide()
    {
        StartNoiseFadeOut();

        if (stamina < outOfBreathStaminaThreshold)
        {
            foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
                perception?.HearNoiseAt(transform.position, 300f);
        }

        if (breathingSource == null)
            return;

        CancelBreathRecovery();

        if (stamina < outOfBreathStaminaThreshold)
            recoveryRoutine = StartCoroutine(PlayOutOfBreathRoutine());
    }

    void CancelBreathRecovery()
    {
        if (recoveryRoutine != null)
        {
            StopCoroutine(recoveryRoutine);
            recoveryRoutine = null;
        }
    }

    void CancelNoiseFade()
    {
        if (noiseFadeRoutine != null)
        {
            StopCoroutine(noiseFadeRoutine);
            noiseFadeRoutine = null;
        }
    }

    IEnumerator PlayOutOfBreathRoutine()
    {
        breathingSource.Stop();

        if (outOfBreathClip != null)
        {
            breathingSource.clip = outOfBreathClip;
            breathingSource.loop = false;
            breathingSource.volume = normalBreathingVolume;
            breathingSource.pitch = 1f;
            breathingSource.Play();
            yield return new WaitForSeconds(outOfBreathClip.length);
        }

        RestoreDefaultBreathing();
        recoveryRoutine = null;
    }

    void RestoreDefaultBreathing()
    {
        if (breathingSource == null || normalBreathingClip == null)
            return;

        breathingSource.clip = normalBreathingClip;
        breathingSource.loop = true;
        breathingSource.volume = normalBreathingVolume;
        breathingSource.pitch = normalBreathingPitch;
        breathingSource.Play();
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

    void TickWhileHidden()
    {
        if (maxHideSeconds <= 0f)
            return;

        stamina -= Time.deltaTime / maxHideSeconds;

        if (stamina <= 0f && !forcedExitPending)
        {
            stamina = 0f;
            forcedExitPending = true;
            cooldownRemaining = cooldownSeconds;
            ForceExitHide();
        }
    }

    void TickWhileNotHidden()
    {
        forcedExitPending = false;

        if (cooldownRemaining > 0f)
        {
            cooldownRemaining -= Time.deltaTime;
            return;
        }

        if (stamina >= 1f || regenSeconds <= 0f)
            return;

        stamina = Mathf.Min(1f, stamina + Time.deltaTime / regenSeconds);
    }

    void ForceExitHide()
    {
        if (hider == null || !hider.isHidden || hider.CurrentHidingSpot == null)
            return;

        hider.CurrentHidingSpot.RevealPlayer(gameObject);
        HudManager.Instance?.ShowSubtitle("I have to get out of here!", 2.5f, HudManager.Instance.WarningColor);
    }

    void RefreshUI()
    {
        bool hidden = hider != null && hider.isHidden;
        SetPanelVisible(hidden);

        if (staminaSlider != null)
            staminaSlider.value = stamina;

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = stamina >= barFadeOutStartStamina
                ? 1f
                : Mathf.Clamp01(stamina / barFadeOutStartStamina);
        }

        if (staminaFillImage != null)
            staminaFillImage.color = Color.Lerp(emptyColor, fullColor, stamina);

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

        float stress = GetStaminaStress();
        hidingCameraNoise.AmplitudeGain = Mathf.Lerp(defaultAmplitudeGain, defaultAmplitudeGain * maxAmplitudeMultiplier, stress);
        hidingCameraNoise.FrequencyGain = Mathf.Lerp(defaultFrequencyGain, defaultFrequencyGain * maxFrequencyMultiplier, stress);
    }

    float GetStaminaStress()
    {
        if (stamina >= shakeStartStamina)
            return 0f;

        float t = (shakeStartStamina - stamina) / shakeStartStamina;
        return t * t;
    }

    float GetBarShakeStrength() => maxBarShakeStrength * GetStaminaStress();
}
