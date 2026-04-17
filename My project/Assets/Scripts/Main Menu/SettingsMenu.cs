using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject displayPanel;
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Slider masterVolSlider;
    [SerializeField] private Slider musicVolSlider;
    [SerializeField] private Slider sfxVolSlider;
    [SerializeField] private Slider brightnessSlider;

    [Header("Selectors (SettingsSelector components)")]
    [SerializeField] private SettingsSelector resolutionSelector;
    [SerializeField] private SettingsSelector qualitySelector;
    [SerializeField] private SettingsSelector fullscreenSelector;

    [Header("Audio")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private string masterParam = "Master";
    [SerializeField] private string musicParam  = "Music";
    [SerializeField] private string sfxParam    = "SFX";

    [Header("Brightness")]
    [Tooltip("Assign the Global Volume GameObject from the scene. If left empty, will auto-find one.")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float minExposure = -2f;
    [SerializeField] private float maxExposure =  2f;

    public static float MouseSensitivity { get; private set; } = 0.5f;

    private Resolution[] supportedRes;
    private string[] qualityNames;
    private SettingsData applied;
    private SettingsData pending;
    private bool listenersRegistered;
    private ColorAdjustments colorAdjustments;

    [Serializable]
    private struct SettingsData
    {
        public int fullscreenMode; // 0=Fullscreen, 1=Windowed, 2=Exclusive Fullscreen
        public int resolutionIndex;
        public int qualityIndex;
        public float mouseSensitivity;
        public float masterLinear;
        public float musicLinear;
        public float sfxLinear;
        public float brightness; // 0..1 slider, mapped to minExposure..maxExposure
    }

    void Awake()
    {
        CacheResolutions();
        qualityNames = QualitySettings.names;
        CacheColorAdjustments();
    }

    void Start()
    {
        LoadSettings();
        HideAllPanels();
        if (displayPanel) displayPanel.SetActive(true);
    }

    void OnEnable()
    {
        RegisterListeners();
    }

    void OnDisable()
    {
        UnregisterListeners();
    }

    private void CacheColorAdjustments()
    {
        if (globalVolume == null)
        {
            var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var v in volumes)
            {
                if (v.profile != null && v.profile.TryGet(out ColorAdjustments _))
                {
                    globalVolume = v;
                    Debug.Log($"[Settings] Found Volume WITH ColorAdjustments on: {v.gameObject.name}");
                    break;
                }
            }

            if (globalVolume == null && volumes.Length > 0)
            {
                globalVolume = volumes[0];
                Debug.Log($"[Settings] Using first Volume (no ColorAdjustments yet): {globalVolume.gameObject.name}");
            }
        }

        if (globalVolume == null)
        {
            Debug.LogError("[Settings] No Volume found in scene! Brightness won't work.");
            return;
        }

        if (globalVolume.profile == null)
        {
            globalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Debug.Log("[Settings] Created new runtime VolumeProfile for brightness.");
        }

        if (globalVolume.profile.TryGet(out ColorAdjustments ca))
        {
            colorAdjustments = ca;
        }
        else
        {
            colorAdjustments = globalVolume.profile.Add<ColorAdjustments>();
            colorAdjustments.postExposure.overrideState = true;
            Debug.Log("[Settings] Added ColorAdjustments to Volume at runtime.");
        }

        Debug.Log($"[Settings] Brightness ready on '{globalVolume.gameObject.name}', postExposure: {colorAdjustments.postExposure.value}");
    }

    private void RegisterListeners()
    {
        if (listenersRegistered) return;
        listenersRegistered = true;

        if (masterVolSlider)        masterVolSlider.onValueChanged.AddListener(OnMasterVolChanged);
        if (musicVolSlider)         musicVolSlider.onValueChanged.AddListener(OnMusicVolChanged);
        if (sfxVolSlider)           sfxVolSlider.onValueChanged.AddListener(OnSFXVolChanged);
        if (mouseSensitivitySlider) mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensChanged);
        if (brightnessSlider)       brightnessSlider.onValueChanged.AddListener(OnBrightnessChanged);
    }

    private void UnregisterListeners()
    {
        if (!listenersRegistered) return;
        listenersRegistered = false;

        if (masterVolSlider)        masterVolSlider.onValueChanged.RemoveListener(OnMasterVolChanged);
        if (musicVolSlider)         musicVolSlider.onValueChanged.RemoveListener(OnMusicVolChanged);
        if (sfxVolSlider)           sfxVolSlider.onValueChanged.RemoveListener(OnSFXVolChanged);
        if (mouseSensitivitySlider) mouseSensitivitySlider.onValueChanged.RemoveListener(OnMouseSensChanged);
        if (brightnessSlider)       brightnessSlider.onValueChanged.RemoveListener(OnBrightnessChanged);
    }

    // --- Instant-apply callbacks ---

    private void OnMasterVolChanged(float value)
    {
        pending.masterLinear = value;
        Debug.Log($"[Settings] Master Vol: {value:F2} -> {Linear01ToDb(value):F1} dB");
        if (mixer) mixer.SetFloat(masterParam, Linear01ToDb(value));
    }

    private void OnMusicVolChanged(float value)
    {
        pending.musicLinear = value;
        Debug.Log($"[Settings] Music Vol: {value:F2} -> {Linear01ToDb(value):F1} dB");
        if (mixer) mixer.SetFloat(musicParam, Linear01ToDb(value));
    }

    private void OnSFXVolChanged(float value)
    {
        pending.sfxLinear = value;
        Debug.Log($"[Settings] SFX Vol: {value:F2} -> {Linear01ToDb(value):F1} dB");
        if (mixer) mixer.SetFloat(sfxParam, Linear01ToDb(value));
    }

    private void OnMouseSensChanged(float value)
    {
        pending.mouseSensitivity = value;
        MouseSensitivity = value;
        Debug.Log($"[Settings] Mouse Sensitivity -> {value:F2}");
    }

    private void OnBrightnessChanged(float value)
    {
        pending.brightness = value;
        Debug.Log($"[Settings] OnBrightnessChanged called! slider: {value:F2}, colorAdjustments null: {colorAdjustments == null}");
        ApplyBrightness(value);
    }

    // --- Public API ---

    public void BeginEditing()
    {
        pending = applied;
        SyncUIFrom(pending);
        Debug.Log("[Settings] BeginEditing called.");
    }

    public void CancelEditing()
    {
        ApplyToSystem(applied);
        SyncUIFrom(applied);
        Debug.Log("[Settings] CancelEditing - reverted to applied.");
    }

    public void ApplyAndSave()
    {
        ReadUIInto(ref pending);
        Debug.Log($"[Settings] Applying - Res: {pending.resolutionIndex}, Quality: {pending.qualityIndex}, " +
                  $"FullscreenMode: {pending.fullscreenMode}, Brightness: {pending.brightness:F2}");
        ApplyResolution(pending);
        MouseSensitivity = pending.mouseSensitivity;
        applied = pending;
        SaveSettings();
        Debug.Log("[Settings] Saved to PlayerPrefs.");
    }

    public void SaveSettings()
    {
        SaveToPrefs(applied);
    }

    public void LoadSettings()
    {
        LoadAppliedFromPrefs();
        Debug.Log($"[Settings] Loaded - Res: {applied.resolutionIndex}, Quality: {applied.qualityIndex}, " +
                  $"FullscreenMode: {applied.fullscreenMode}, Master: {applied.masterLinear}, " +
                  $"Music: {applied.musicLinear}, SFX: {applied.sfxLinear}, Brightness: {applied.brightness:F2}");
        MouseSensitivity = applied.mouseSensitivity;
        ApplyToSystem(applied);
        SyncUIFrom(applied);
    }

    public void RestoreDefaults()
    {
        pending = new SettingsData
        {
            fullscreenMode   = 0, // Fullscreen
            resolutionIndex  = Mathf.Max(0, supportedRes.Length - 1),
            qualityIndex     = qualityNames.Length - 1,
            mouseSensitivity = 0.5f,
            masterLinear     = 0.5f,
            musicLinear      = 0.5f,
            sfxLinear        = 0.5f,
            brightness       = 0.5f
        };

        MouseSensitivity = pending.mouseSensitivity;
        SyncUIFrom(pending);
        ApplyInstantSettings(pending);
        Debug.Log("[Settings] Defaults restored.");
    }

    // --- Panel navigation ---

    public void ShowDisplay()
    {
        HideAllPanels();
        if (displayPanel) displayPanel.SetActive(true);
    }

    public void ShowAudio()
    {
        HideAllPanels();
        if (audioPanel) audioPanel.SetActive(true);
    }

    public void ShowControls()
    {
        HideAllPanels();
        if (controlsPanel) controlsPanel.SetActive(true);
    }

    public void ShowGameplay()
    {
        HideAllPanels();
        if (gameplayPanel) gameplayPanel.SetActive(true);
    }

    private void HideAllPanels()
    {
        if (displayPanel)  displayPanel.SetActive(false);
        if (audioPanel)    audioPanel.SetActive(false);
        if (controlsPanel) controlsPanel.SetActive(false);
        if (gameplayPanel) gameplayPanel.SetActive(false);
    }

    // --- Internals ---

    private void CacheResolutions()
    {
        supportedRes = Screen.resolutions
            .Where(r => r.refreshRateRatio.value >= 59.5)
            .Distinct()
            .OrderBy(r => r.width)
            .ThenBy(r => r.height)
            .ToArray();
    }

    private void LoadAppliedFromPrefs()
    {
        int defaultResIndex = Array.FindIndex(supportedRes,
            r => r.width == Screen.currentResolution.width &&
                 r.height == Screen.currentResolution.height);

        int resIndex = PlayerPrefs.GetInt("resIndex", defaultResIndex);
        resIndex = Mathf.Clamp(resIndex, 0, Mathf.Max(0, supportedRes.Length - 1));
        if (resIndex < 0) resIndex = Mathf.Max(0, supportedRes.Length - 1);

        applied = new SettingsData
        {
            fullscreenMode   = PlayerPrefs.GetInt("fullscreenMode", 0),
            resolutionIndex  = resIndex,
            qualityIndex     = Mathf.Clamp(
                PlayerPrefs.GetInt("qualityIndex", QualitySettings.GetQualityLevel()),
                0, qualityNames.Length - 1),
            mouseSensitivity = PlayerPrefs.GetFloat("mouseSensitivity", 0.5f),
            masterLinear     = PlayerPrefs.GetFloat("masterVol", 0.5f),
            musicLinear      = PlayerPrefs.GetFloat("musicVol", 0.5f),
            sfxLinear        = PlayerPrefs.GetFloat("sfxVol", 0.5f),
            brightness       = PlayerPrefs.GetFloat("brightness", 0.5f),
        };
    }

    private void SaveToPrefs(in SettingsData s)
    {
        PlayerPrefs.SetInt("fullscreenMode", s.fullscreenMode);
        PlayerPrefs.SetInt("resIndex", s.resolutionIndex);
        PlayerPrefs.SetInt("qualityIndex", s.qualityIndex);
        PlayerPrefs.SetFloat("mouseSensitivity", s.mouseSensitivity);
        PlayerPrefs.SetFloat("masterVol", s.masterLinear);
        PlayerPrefs.SetFloat("musicVol", s.musicLinear);
        PlayerPrefs.SetFloat("sfxVol", s.sfxLinear);
        PlayerPrefs.SetFloat("brightness", s.brightness);
        PlayerPrefs.Save();
    }

    private void SyncUIFrom(in SettingsData s)
    {
        if (mouseSensitivitySlider) mouseSensitivitySlider.SetValueWithoutNotify(s.mouseSensitivity);
        if (masterVolSlider)        masterVolSlider.SetValueWithoutNotify(s.masterLinear);
        if (musicVolSlider)         musicVolSlider.SetValueWithoutNotify(s.musicLinear);
        if (sfxVolSlider)           sfxVolSlider.SetValueWithoutNotify(s.sfxLinear);
        if (brightnessSlider)       brightnessSlider.SetValueWithoutNotify(s.brightness);

        if (resolutionSelector)
            resolutionSelector.SetIndex(Mathf.Clamp(s.resolutionIndex, 0, Mathf.Max(0, supportedRes.Length - 1)));

        if (qualitySelector)
            qualitySelector.SetIndex(Mathf.Clamp(s.qualityIndex, 0, qualityNames.Length - 1));

        if (fullscreenSelector)
            fullscreenSelector.SetIndex(Mathf.Clamp(s.fullscreenMode, 0, 2));
    }

    private void ReadUIInto(ref SettingsData s)
    {
        s.mouseSensitivity = mouseSensitivitySlider ? mouseSensitivitySlider.value : s.mouseSensitivity;
        s.masterLinear     = masterVolSlider ? masterVolSlider.value : s.masterLinear;
        s.musicLinear      = musicVolSlider  ? musicVolSlider.value  : s.musicLinear;
        s.sfxLinear        = sfxVolSlider    ? sfxVolSlider.value    : s.sfxLinear;
        s.brightness       = brightnessSlider ? brightnessSlider.value : s.brightness;

        if (resolutionSelector)
            s.resolutionIndex = resolutionSelector.GetCurrentIndex();
        if (qualitySelector)
            s.qualityIndex = qualitySelector.GetCurrentIndex();
        if (fullscreenSelector)
            s.fullscreenMode = fullscreenSelector.GetCurrentIndex();
    }

    private void ApplyToSystem(in SettingsData s)
    {
        ApplyResolution(s);
        ApplyInstantSettings(s);
    }

    private void ApplyInstantSettings(in SettingsData s)
    {
        ApplyFullscreen(s.fullscreenMode);
        QualitySettings.SetQualityLevel(s.qualityIndex, true);
        ApplyBrightness(s.brightness);

        if (mixer)
        {
            mixer.SetFloat(masterParam, Linear01ToDb(s.masterLinear));
            mixer.SetFloat(musicParam,  Linear01ToDb(s.musicLinear));
            mixer.SetFloat(sfxParam,    Linear01ToDb(s.sfxLinear));
        }
    }

    private static readonly FullScreenMode[] FullscreenModes =
    {
        FullScreenMode.FullScreenWindow,       // 0 = Fullscreen
        FullScreenMode.Windowed,               // 1 = Windowed
        FullScreenMode.ExclusiveFullScreen     // 2 = Exclusive Fullscreen
    };

    private void ApplyFullscreen(int modeIndex)
    {
        var mode = FullscreenModes[Mathf.Clamp(modeIndex, 0, FullscreenModes.Length - 1)];
        Screen.fullScreenMode = mode;
        Debug.Log($"[Settings] Fullscreen mode -> {mode}");
    }

    private void ApplyBrightness(float linear01)
    {
        float targetExposure = Mathf.Lerp(minExposure, maxExposure, linear01);

        if (colorAdjustments == null)
        {
            Debug.LogError($"[Settings] ApplyBrightness FAILED - colorAdjustments is NULL! (slider: {linear01:F2}, target: {targetExposure:F2})");
            return;
        }

        colorAdjustments.postExposure.overrideState = true;
        colorAdjustments.postExposure.value = targetExposure;
        Debug.Log($"[Settings] ApplyBrightness OK - slider: {linear01:F2}, postExposure set to: {colorAdjustments.postExposure.value:F2}");
    }

    private void ApplyResolution(in SettingsData s)
    {
        if (supportedRes.Length == 0) return;

        var r = supportedRes[Mathf.Clamp(s.resolutionIndex, 0, supportedRes.Length - 1)];
        var mode = FullscreenModes[Mathf.Clamp(s.fullscreenMode, 0, FullscreenModes.Length - 1)];
        Screen.SetResolution(r.width, r.height, mode);
    }

    private static float Linear01ToDb(float v)
    {
        return v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;
    }
}
