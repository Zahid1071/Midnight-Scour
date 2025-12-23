using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Slider masterVolSlider;
    [SerializeField] private Slider musicVolSlider;
    [SerializeField] private Slider sfxVolSlider;

    [Header("Audio")]
    [SerializeField] private AudioMixer mixer; // assign your Audio Mixer
    [SerializeField] private string masterParam = "MasterVol";
    [SerializeField] private string musicParam  = "MusicVol";
    [SerializeField] private string sfxParam    = "SFXVol";

    // cached list to map dropdown index -> real resolution
    private Resolution[] supportedRes;

    // last saved (applied) settings kept in memory
    private SettingsData applied;

    // currently edited values (not applied until Apply)
    private SettingsData pending;

    [Serializable]
    private struct SettingsData
    {
        public bool fullscreen;
        public int resolutionIndex; // index inside our supportedRes
        public int qualityIndex;
        public float mouseSensitivity; // 0..1 slider (map however you like in-game)
        public float masterLinear;     // 0..1 (converted to dB on apply)
        public float musicLinear;      // 0..1
        public float sfxLinear;        // 0..1
    }

    void Awake()
    {
        PopulateResolutions();
        PopulateQualities();
        LoadAppliedFromPrefs();
        // don’t push to UI here; we do that in BeginEditing(), when the panel opens
    }

    public void BeginEditing()
    {
        // copy applied -> pending, then reflect on UI
        pending = applied;
        SyncUIFrom(pending);
    }

    public void CancelEditing()
    {
        // throw away pending, restore UI to applied so next open is clean
        SyncUIFrom(applied);
    }

    public void ApplyAndSave()
    {
        // read latest values from UI into pending
        ReadUIInto(ref pending);

        // apply to system
        ApplyToSystem(pending);

        // persist
        SaveToPrefs(pending);

        // new baseline
        applied = pending;
    }

    // ---------- internals ----------

    private void PopulateResolutions()
    {
        supportedRes = Screen.resolutions
            .Where(r => r.refreshRateRatio.value >= 59.5) // keep >~60Hz
            .Distinct()
            .OrderBy(r => r.width)
            .ThenBy(r => r.height)
            .ToArray();

        resolutionDropdown.ClearOptions();
        var options = supportedRes.Select(r => $"{r.width}x{r.height}").ToList();
        resolutionDropdown.AddOptions(options);
    }

    private void PopulateQualities()
    {
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(QualitySettings.names.ToList());
    }

    private void LoadAppliedFromPrefs()
    {
        // fallbacks to current system values
        int resIndex = Mathf.Clamp(PlayerPrefs.GetInt("resIndex",
            Array.FindIndex(supportedRes, r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height)),
            0, Mathf.Max(0, supportedRes.Length - 1));

        applied = new SettingsData
        {
            fullscreen      = PlayerPrefs.GetInt("fullscreen", Screen.fullScreen ? 1 : 0) == 1,
            resolutionIndex = resIndex < 0 ? Mathf.Max(0, supportedRes.Length - 1) : resIndex,
            qualityIndex    = Mathf.Clamp(PlayerPrefs.GetInt("qualityIndex", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1),
            mouseSensitivity= PlayerPrefs.GetFloat("mouseSensitivity", 0.5f),
            masterLinear    = PlayerPrefs.GetFloat("masterVol", 0.8f),
            musicLinear     = PlayerPrefs.GetFloat("musicVol", 0.8f),
            sfxLinear       = PlayerPrefs.GetFloat("sfxVol", 0.8f),
        };

        // also make sure the actual runtime matches the saved values at boot
        ApplyToSystem(applied);
    }

    private void SaveToPrefs(in SettingsData s)
    {
        PlayerPrefs.SetInt("fullscreen", s.fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("resIndex", s.resolutionIndex);
        PlayerPrefs.SetInt("qualityIndex", s.qualityIndex);
        PlayerPrefs.SetFloat("mouseSensitivity", s.mouseSensitivity);
        PlayerPrefs.SetFloat("masterVol", s.masterLinear);
        PlayerPrefs.SetFloat("musicVol", s.musicLinear);
        PlayerPrefs.SetFloat("sfxVol", s.sfxLinear);
        PlayerPrefs.Save();
    }

    private void SyncUIFrom(in SettingsData s)
    {
        fullscreenToggle.SetIsOnWithoutNotify(s.fullscreen);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Clamp(s.resolutionIndex, 0, Mathf.Max(0, supportedRes.Length - 1)));
        qualityDropdown.SetValueWithoutNotify(s.qualityIndex);
        mouseSensitivitySlider.SetValueWithoutNotify(s.mouseSensitivity);
        masterVolSlider.SetValueWithoutNotify(s.masterLinear);
        musicVolSlider.SetValueWithoutNotify(s.musicLinear);
        sfxVolSlider.SetValueWithoutNotify(s.sfxLinear);
    }

    private void ReadUIInto(ref SettingsData s)
    {
        s.fullscreen       = fullscreenToggle.isOn;
        s.resolutionIndex  = Mathf.Clamp(resolutionDropdown.value, 0, Mathf.Max(0, supportedRes.Length - 1));
        s.qualityIndex     = qualityDropdown.value;
        s.mouseSensitivity = mouseSensitivitySlider.value;
        s.masterLinear     = masterVolSlider.value;
        s.musicLinear      = musicVolSlider.value;
        s.sfxLinear        = sfxVolSlider.value;
    }

    private void ApplyToSystem(in SettingsData s)
    {
        // screen
        if (supportedRes.Length > 0)
        {
            var r = supportedRes[Mathf.Clamp(s.resolutionIndex, 0, supportedRes.Length - 1)];
            Screen.SetResolution(r.width, r.height, s.fullscreen);

        }
        else
        {
            Screen.fullScreen = s.fullscreen;
        }

        // quality
        QualitySettings.SetQualityLevel(s.qualityIndex, true);

        // audio mixer
        if (mixer)
        {
            mixer.SetFloat(masterParam, Linear01ToDb(s.masterLinear));
            mixer.SetFloat(musicParam,  Linear01ToDb(s.musicLinear));
            mixer.SetFloat(sfxParam,    Linear01ToDb(s.sfxLinear));
        }

        // mouse sens is just saved; your player script should read:
        // float sens = PlayerPrefs.GetFloat("mouseSensitivity", 0.5f);
    }

    public void RestoreDefaults()
    {
        // Hardcoded defaults (same as your LoadAppliedFromPrefs fallback values)
        pending = new SettingsData
        {
            fullscreen      = true,
            resolutionIndex = Mathf.Max(0, supportedRes.Length - 1), // highest available
            qualityIndex    = QualitySettings.GetQualityLevel(), // current quality level
            mouseSensitivity= 0.5f,
            masterLinear    = 0.8f,
            musicLinear     = 0.8f,
            sfxLinear       = 0.8f
        };

        // Apply defaults to the UI so player sees the change immediately
        SyncUIFrom(pending);
    }


    private static float Linear01ToDb(float v)
    {
        // 0 -> -80 dB (silent), 1 -> 0 dB
        return v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;
    }
}
