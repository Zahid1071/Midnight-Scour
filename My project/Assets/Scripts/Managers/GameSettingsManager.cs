/*
 * =============================================================================
 *                          GAME SETTINGS MANAGER
 * =============================================================================
 * 
 * PURPOSE:
 * Central singleton responsible for storing, loading, saving, and applying
 * all game settings (graphics, audio, controls, gameplay).
 * 
 * USAGE FOR OTHER TEAM MEMBERS:
 * -----------------------------
 * Access the singleton instance anywhere in your code:
 * 
 *     GameSettingsManager.Instance.MouseSensitivity            // NORMALIZED 0.0-1.0 (slider value)
 *     GameSettingsManager.Instance.MouseSensitivityMultiplier  // Camera multiplier (camera scripts use this)
 *     GameSettingsManager.Instance.InvertYAxis                 // Get/set Y-axis inversion
 *     GameSettingsManager.Instance.MasterVolume                // Get/set master volume (0.0-1.0 linear)
 *     GameSettingsManager.Instance.Difficulty                  // Get current difficulty (0=Easy, 1=Normal, 2=Hard)
 *
 * Subscribe to live changes:
 *
 *     GameSettingsManager.OnMouseSensitivityChanged += newValue => { ... };
 *     GameSettingsManager.OnInvertYAxisChanged      += newValue => { ... };
 * 
 * The manager automatically applies settings to Unity systems when changed.
 * Settings persist across sessions via PlayerPrefs.
 * 
 * INTEGRATION POINTS:
 * -------------------
 * - SettingsMenuController: Opens/closes settings UI, reads/writes via this manager
 * - SettingsMenu (legacy main menu): Delegates mouseSensitivity to this manager
 * - PauseMenuController: Uses this manager indirectly through SettingsMenuController
 * - Player scripts: Read MouseSensitivityMultiplier, InvertYAxis for camera controls
 * - Audio systems: SoundManager already reads from PlayerPrefs (synced with this)
 * 
 * =============================================================================
 */

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

public class GameSettingsManager : MonoBehaviour
{
    // =========================================================================
    // SINGLETON
    // =========================================================================
    
    public static GameSettingsManager Instance { get; private set; }

    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>
    /// Single source-of-truth PlayerPrefs key for mouse sensitivity.
    /// Stored as a NORMALIZED 0-1 value (== slider value directly).
    /// </summary>
    public const string MOUSE_SENSITIVITY_KEY = "MouseSensitivity";

    /// <summary>
    /// PlayerPrefs key for hide-spot microphone sensitivity (normalized 0-1).
    /// </summary>
    public const string MIC_SENSITIVITY_KEY = "MicSensitivity";

    /// <summary>
    /// Single source-of-truth PlayerPrefs key for brightness.
    /// Stored as a NORMALIZED 0-1 value (== slider value directly).
    /// </summary>
    public const string BRIGHTNESS_KEY = "Brightness";

    /// <summary>PlayerPrefs flag set after migrating resolution indices to descending order.</summary>
    private const string RESOLUTION_INDEX_DESC_KEY = "settings_resolutionIndexDesc";

    /// <summary>Resolutions below this size are omitted from the settings list.</summary>
    private const int MIN_RESOLUTION_WIDTH = 1024;
    private const int MIN_RESOLUTION_HEIGHT = 768;

    /// <summary>
    /// Effective sensitivity multiplier range applied by camera scripts.
    /// MouseSensitivity (0..1) is mapped to [MIN, MAX] before being multiplied
    /// against the camera's baseSensitivity.
    /// </summary>
    public const float MIN_SENSITIVITY_MULTIPLIER = 0.25f;
    public const float MAX_SENSITIVITY_MULTIPLIER = 5f;

    /// <summary>
    /// Hide mic sensitivity multiplier range. Slider 0.5 maps to 1× (neutral).
    /// </summary>
    public const float MIN_MIC_SENSITIVITY_MULTIPLIER = 0.25f;
    public const float MAX_MIC_SENSITIVITY_MULTIPLIER = 3f;

    /// <summary>
    /// URP ColorAdjustments postExposure range that <see cref="Brightness"/>
    /// (0..1) is mapped to. Negative = darker, positive = brighter.
    /// Scene components (e.g. BrightnessApplier) consume <see cref="BrightnessExposure"/>.
    /// </summary>
    public const float MIN_BRIGHTNESS_EXPOSURE = -2f;
    public const float MAX_BRIGHTNESS_EXPOSURE =  1f;

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>
    /// Fired whenever MouseSensitivity changes. Subscribers get the new
    /// normalized [0,1] value. Useful for camera scripts that want to react
    /// instantly without polling.
    /// </summary>
    public static event Action<float> OnMouseSensitivityChanged;

    /// <summary>
    /// Fired whenever MicSensitivity changes. Subscribers get the new normalized [0,1] value.
    /// </summary>
    public static event Action<float> OnMicSensitivityChanged;

    /// <summary>
    /// Fired whenever InvertYAxis changes.
    /// </summary>
    public static event Action<bool> OnInvertYAxisChanged;

    /// <summary>
    /// Fired whenever Brightness changes. Subscribers get the new normalized
    /// [0,1] value. Scene components (e.g. <c>BrightnessApplier</c>) listen to
    /// this to update the URP Volume's ColorAdjustments postExposure live.
    /// </summary>
    public static event Action<float> OnBrightnessChanged;

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("Audio")]
    [Tooltip("Assign the main AudioMixer asset")]
    [SerializeField] private AudioMixer audioMixer;
    
    [Header("Exposed Parameter Names (must match AudioMixer)")]
    [SerializeField] private string masterVolumeParam = "Master";
    [SerializeField] private string musicVolumeParam = "Music";
    [SerializeField] private string sfxVolumeParam = "SFX";

    [Header("Defaults (Inspector / ScriptableObject)")]
    [Tooltip("Assign a Game Settings Defaults asset. If empty, loads Resources/GameSettingsDefaults.")]
    [SerializeField] private GameSettingsDefaults defaultProfile;

    // =========================================================================
    // SETTINGS DATA STRUCTURE
    // =========================================================================
    
    [Serializable]
    public struct SettingsData
    {
        // Graphics
        public bool fullscreen;
        public int displayModeIndex;      // 0=Fullscreen, 1=Windowed, 2=Borderless
        public int resolutionIndex;
        public int qualityIndex;
        
        // Audio (0.0 - 1.0 linear scale)
        public float masterVolume;
        public float musicVolume;
        public float sfxVolume;
        
        // Controls
        public float mouseSensitivity;    // NORMALIZED 0.0 - 1.0 (slider value directly)
        public float micSensitivity;      // NORMALIZED 0.0 - 1.0 (hide mic loudness gain)
        public bool invertYAxis;

        // Display (continued) — brightness is stored alongside graphics conceptually
        // but kept here for layout reasons. Range is normalized 0.0-1.0; mapped to
        // [MIN_BRIGHTNESS_EXPOSURE, MAX_BRIGHTNESS_EXPOSURE] when applied.
        public float brightness;

        // Gameplay
        public int difficultyIndex;       // 0=Easy, 1=Normal, 2=Hard
        public bool subtitlesEnabled;
    }

    // =========================================================================
    // DEFAULT VALUES (ScriptableObject + code fallback)
    // =========================================================================

    private const string DefaultProfileResourcesPath = "GameSettingsDefaults";

    private static GameSettingsDefaults _cachedDefaultProfile;

    /// <summary>
    /// Default settings from the assigned <see cref="GameSettingsDefaults"/> asset
    /// (Inspector or Resources). Edit via Assets → Create → Game → Settings Defaults.
    /// </summary>
    public static SettingsData DefaultSettings => GetDefaultSettings();

    /// <summary>Same as <see cref="DefaultSettings"/>; explicit name for call sites.</summary>
    public static SettingsData GetDefaultSettings() => ResolveDefaultProfile().defaults;

    private static GameSettingsDefaults ResolveDefaultProfile()
    {
        if (_cachedDefaultProfile != null)
            return _cachedDefaultProfile;

        if (Instance != null && Instance.defaultProfile != null)
            return _cachedDefaultProfile = Instance.defaultProfile;

        var fromResources = Resources.Load<GameSettingsDefaults>(DefaultProfileResourcesPath);
        if (fromResources != null)
            return _cachedDefaultProfile = fromResources;

        Debug.LogWarning(
            "[GameSettingsManager] No GameSettingsDefaults asset assigned or found in Resources. " +
            "Using built-in fallback. Create one via Assets → Create → Game → Settings Defaults.");

        return _cachedDefaultProfile = CreateBuiltInFallbackProfile();
    }

    private static GameSettingsDefaults CreateBuiltInFallbackProfile()
    {
        var fallback = ScriptableObject.CreateInstance<GameSettingsDefaults>();
        fallback.defaults = new SettingsData
        {
            fullscreen = true,
            displayModeIndex = 0,
            resolutionIndex = -1,
            qualityIndex = 2,
            masterVolume = 1f,
            musicVolume = 1f,
            sfxVolume = 1f,
            mouseSensitivity = 0.5f,
            micSensitivity = 0.5f,
            invertYAxis = false,
            brightness = 0.5f,
            difficultyIndex = 1,
            subtitlesEnabled = true
        };
        return fallback;
    }

    private void EnsureDefaultProfileAssigned()
    {
        if (defaultProfile != null)
            return;

        defaultProfile = Resources.Load<GameSettingsDefaults>(DefaultProfileResourcesPath);
        if (defaultProfile != null)
            _cachedDefaultProfile = defaultProfile;
    }

    // =========================================================================
    // RUNTIME STATE
    // =========================================================================
    
    private SettingsData _currentSettings;
    private Resolution[] _availableResolutions;

    // =========================================================================
    // PUBLIC PROPERTIES - Use these to read/modify settings
    // =========================================================================
    
    /// <summary>
    /// List of available screen resolutions (filtered and sorted).
    /// </summary>
    public Resolution[] AvailableResolutions => _availableResolutions;
    
    /// <summary>
    /// Current settings data (read-only snapshot).
    /// For modifications, use individual properties or SetSettings().
    /// </summary>
    public SettingsData CurrentSettings => _currentSettings;

    // --- Graphics Properties ---
    
    public bool Fullscreen
    {
        get => _currentSettings.fullscreen;
        set { _currentSettings.fullscreen = value; ApplyGraphicsSettings(); }
    }
    
    /// <summary>
    /// Display mode: 0=Tam Ekran (Fullscreen), 1=Pencereli (Windowed), 2=Kenarlıksız (Borderless)
    /// </summary>
    public int DisplayModeIndex
    {
        get => _currentSettings.displayModeIndex;
        set { _currentSettings.displayModeIndex = Mathf.Clamp(value, 0, 2); ApplyGraphicsSettings(); }
    }
    
    public int ResolutionIndex
    {
        get => _currentSettings.resolutionIndex;
        set { _currentSettings.resolutionIndex = Mathf.Clamp(value, 0, Mathf.Max(0, _availableResolutions.Length - 1)); ApplyGraphicsSettings(); }
    }

    /// <summary>
    /// Stored <see cref="ResolutionIndex"/> resolved for UI/apply (-1 = auto).
    /// </summary>
    public int EffectiveResolutionIndex => ResolveResolutionIndex(_currentSettings.resolutionIndex);
    
    /// <summary>
    /// Quality level index matching QualitySettings.names order.
    /// </summary>
    public int QualityIndex
    {
        get => _currentSettings.qualityIndex;
        set { _currentSettings.qualityIndex = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1); ApplyGraphicsSettings(); }
    }

    // --- Audio Properties ---
    
    /// <summary>
    /// Master volume (0.0 to 1.0 linear scale).
    /// </summary>
    public float MasterVolume
    {
        get => _currentSettings.masterVolume;
        set { _currentSettings.masterVolume = Mathf.Clamp01(value); ApplyAudioSettings(); }
    }
    
    /// <summary>
    /// Music volume (0.0 to 1.0 linear scale).
    /// </summary>
    public float MusicVolume
    {
        get => _currentSettings.musicVolume;
        set { _currentSettings.musicVolume = Mathf.Clamp01(value); ApplyAudioSettings(); }
    }
    
    /// <summary>
    /// SFX volume (0.0 to 1.0 linear scale).
    /// </summary>
    public float SFXVolume
    {
        get => _currentSettings.sfxVolume;
        set { _currentSettings.sfxVolume = Mathf.Clamp01(value); ApplyAudioSettings(); }
    }

    // --- Controls Properties ---
    
    /// <summary>
    /// Mouse sensitivity as a NORMALIZED [0, 1] value.
    /// This is the slider's value directly. Camera/player scripts should NOT
    /// multiply this raw value; use <see cref="MouseSensitivityMultiplier"/> instead,
    /// which maps 0..1 to a sensible camera multiplier range.
    /// 
    /// Setting this property fires <see cref="OnMouseSensitivityChanged"/> if the
    /// value changes, allowing live updates in gameplay scripts.
    /// </summary>
    public float MouseSensitivity
    {
        get => _currentSettings.mouseSensitivity;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_currentSettings.mouseSensitivity, clamped)) return;
            _currentSettings.mouseSensitivity = clamped;
            OnMouseSensitivityChanged?.Invoke(clamped);
        }
    }

    /// <summary>
    /// Effective camera-sensitivity multiplier derived from <see cref="MouseSensitivity"/>.
    /// Maps the normalized 0..1 slider value to the
    /// [<see cref="MIN_SENSITIVITY_MULTIPLIER"/>, <see cref="MAX_SENSITIVITY_MULTIPLIER"/>]
    /// range. Camera scripts should multiply their baseSensitivity by this value.
    /// </summary>
    public float MouseSensitivityMultiplier =>
        Mathf.Lerp(MIN_SENSITIVITY_MULTIPLIER, MAX_SENSITIVITY_MULTIPLIER, _currentSettings.mouseSensitivity);

    /// <summary>
    /// Hide microphone sensitivity as a NORMALIZED [0, 1] value (== slider value).
    /// Use <see cref="MicSensitivityMultiplier"/> in gameplay (0.5 → 1×).
    /// </summary>
    public float MicSensitivity
    {
        get => _currentSettings.micSensitivity;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_currentSettings.micSensitivity, clamped)) return;
            _currentSettings.micSensitivity = clamped;
            OnMicSensitivityChanged?.Invoke(clamped);
        }
    }

    /// <summary>
    /// Multiplier applied to hide-spot mic loudness. Slider 0.5 maps to 1×;
    /// low end → <see cref="MIN_MIC_SENSITIVITY_MULTIPLIER"/>, high end → max.
    /// </summary>
    public float MicSensitivityMultiplier
    {
        get
        {
            float t = _currentSettings.micSensitivity;
            if (t <= 0.5f)
                return Mathf.Lerp(MIN_MIC_SENSITIVITY_MULTIPLIER, 1f, t * 2f);
            return Mathf.Lerp(1f, MAX_MIC_SENSITIVITY_MULTIPLIER, (t - 0.5f) * 2f);
        }
    }

    /// <summary>
    /// If true, Y-axis mouse input should be inverted.
    /// Setting this fires <see cref="OnInvertYAxisChanged"/> when the value changes.
    /// </summary>
    public bool InvertYAxis
    {
        get => _currentSettings.invertYAxis;
        set
        {
            if (_currentSettings.invertYAxis == value) return;
            _currentSettings.invertYAxis = value;
            OnInvertYAxisChanged?.Invoke(value);
        }
    }

    // --- Display (Brightness) Properties ---

    /// <summary>
    /// Screen brightness as a NORMALIZED [0, 1] value (== slider value directly).
    /// Setting this fires <see cref="OnBrightnessChanged"/> when the value changes,
    /// allowing scene components (e.g. <c>BrightnessApplier</c>) to update the
    /// URP Volume's ColorAdjustments postExposure live without polling.
    /// </summary>
    public float Brightness
    {
        get => _currentSettings.brightness;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(_currentSettings.brightness, clamped)) return;
            _currentSettings.brightness = clamped;
            OnBrightnessChanged?.Invoke(clamped);
        }
    }

    /// <summary>
    /// Convenience helper: the URP ColorAdjustments postExposure value derived
    /// from <see cref="Brightness"/>. Maps 0..1 -> [MIN_BRIGHTNESS_EXPOSURE,
    /// MAX_BRIGHTNESS_EXPOSURE].
    /// </summary>
    public float BrightnessExposure =>
        Mathf.Lerp(MIN_BRIGHTNESS_EXPOSURE, MAX_BRIGHTNESS_EXPOSURE, _currentSettings.brightness);

    // --- Gameplay Properties ---
    
    /// <summary>
    /// Difficulty: 0=Kolay (Easy), 1=Normal, 2=Zor (Hard)
    /// </summary>
    public int DifficultyIndex
    {
        get => _currentSettings.difficultyIndex;
        set => _currentSettings.difficultyIndex = Mathf.Clamp(value, 0, 2);
    }
    
    public bool SubtitlesEnabled
    {
        get => _currentSettings.subtitlesEnabled;
        set => _currentSettings.subtitlesEnabled = value;
    }

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureDefaultProfileAssigned();
        if (defaultProfile != null)
            _cachedDefaultProfile = defaultProfile;
        
        // Initialize resolutions
        CacheAvailableResolutions();
        
        // Load saved settings or defaults
        LoadSettings();
        
        // Apply all settings at startup
        ApplyAllSettings();
    }

    private void Start()
    {
        // Re-apply audio once the audio system is fully up (Awake can be too early).
        ApplyAudioSettings();
    }

    /// <summary>
    /// Auto-bootstrap: ensures a GameSettingsManager exists at runtime even if
    /// no scene contains the prefab. Runs BEFORE the first scene's Awake calls,
    /// so consumers in MonoBehaviour.Awake can safely use <see cref="Instance"/>.
    /// 
    /// If a manually-placed prefab exists in the scene with proper inspector
    /// references (e.g. AudioMixer assigned), this auto-created instance will
    /// be destroyed by the singleton check in Awake() — the manually placed one
    /// wins. So sahnedeki prefab her zaman tercih edilir.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
//    private static void AutoBootstrap()
//     {
//         if (Instance != null) return;

//         // Try to find an existing instance that may have been created in a
//         // very early Awake (defensive — usually unnecessary).
//         var existing = FindFirstObjectByType<GameSettingsManager>();
//         if (existing != null) return;

//         var go = new GameObject("GameSettingsManager (auto)");
//         go.AddComponent<GameSettingsManager>();
//         // No DontDestroyOnLoad here — the component's Awake handles it.
//         Debug.Log("[GameSettingsManager] Auto-bootstrapped (no prefab in scene). " +
//                   "Drop the prefab into your first scene to assign an AudioMixer.");
//     } 

    // =========================================================================
    // PUBLIC API METHODS
    // =========================================================================
    
    /// <summary>
    /// Apply all graphics settings to Unity systems (Screen, Quality).
    /// Called automatically when properties change, but can be called manually.
    /// </summary>
    public void ApplyGraphicsSettings()
    {
        // Determine fullscreen mode
        FullScreenMode mode = _currentSettings.displayModeIndex switch
        {
            0 => FullScreenMode.ExclusiveFullScreen,
            1 => FullScreenMode.Windowed,
            2 => FullScreenMode.FullScreenWindow, // Borderless
            _ => FullScreenMode.ExclusiveFullScreen
        };
        
        // Apply resolution
        if (_availableResolutions.Length > 0)
        {
            int resIdx = ResolveResolutionIndex(_currentSettings.resolutionIndex);
            Resolution res = _availableResolutions[resIdx];
            Screen.SetResolution(res.width, res.height, mode);
        }
        else
        {
            Screen.fullScreenMode = mode;
        }
        
        // Apply quality level
        QualitySettings.SetQualityLevel(_currentSettings.qualityIndex, true);
    }
    
    /// <summary>
    /// Apply all audio settings to the AudioMixer.
    /// Called automatically when volume properties change.
    /// </summary>
    public void ApplyAudioSettings()
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("[GameSettingsManager] AudioMixer not assigned!");
            return;
        }
        
        audioMixer.SetFloat(masterVolumeParam, LinearToDecibel(_currentSettings.masterVolume));
        audioMixer.SetFloat(musicVolumeParam, LinearToDecibel(_currentSettings.musicVolume));
        audioMixer.SetFloat(sfxVolumeParam, LinearToDecibel(_currentSettings.sfxVolume));
    }
    
    /// <summary>
    /// Apply all settings (graphics + audio).
    /// </summary>
    public void ApplyAllSettings()
    {
        ApplyGraphicsSettings();
        ApplyAudioSettings();
    }
    
    /// <summary>
    /// Save current settings to PlayerPrefs.
    /// </summary>
    public void SaveSettings()
    {
        PlayerPrefs.SetInt("settings_fullscreen", _currentSettings.fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("settings_displayMode", _currentSettings.displayModeIndex);
        PlayerPrefs.SetInt("settings_resolutionIndex", _currentSettings.resolutionIndex);
        PlayerPrefs.SetInt("settings_qualityIndex", _currentSettings.qualityIndex);
        
        PlayerPrefs.SetFloat("settings_masterVolume", _currentSettings.masterVolume);
        PlayerPrefs.SetFloat("settings_musicVolume", _currentSettings.musicVolume);
        PlayerPrefs.SetFloat("settings_sfxVolume", _currentSettings.sfxVolume);
        
        // Also save with legacy keys for backward compatibility with SoundManager
        PlayerPrefs.SetFloat("masterVol", _currentSettings.masterVolume);
        PlayerPrefs.SetFloat("musicVol", _currentSettings.musicVolume);
        PlayerPrefs.SetFloat("sfxVol", _currentSettings.sfxVolume);

        // Mouse sensitivity: single source of truth key (normalized 0-1).
        // Also write the legacy key with the same normalized value so any
        // not-yet-migrated reader still gets a consistent value.
        PlayerPrefs.SetFloat(MOUSE_SENSITIVITY_KEY, _currentSettings.mouseSensitivity);
        PlayerPrefs.SetFloat("mouseSensitivity", _currentSettings.mouseSensitivity);
        PlayerPrefs.DeleteKey("settings_mouseSensitivity"); // remove stale [0.1..10] format
        PlayerPrefs.SetFloat(MIC_SENSITIVITY_KEY, _currentSettings.micSensitivity);
        PlayerPrefs.SetInt("settings_invertY", _currentSettings.invertYAxis ? 1 : 0);
        
        PlayerPrefs.SetInt("settings_difficulty", _currentSettings.difficultyIndex);
        PlayerPrefs.SetInt("settings_subtitles", _currentSettings.subtitlesEnabled ? 1 : 0);

        // Brightness: single source of truth key (normalized 0-1) plus legacy
        // mirror so the old "brightness" key stays in sync if anything still
        // reads it.
        PlayerPrefs.SetFloat(BRIGHTNESS_KEY, _currentSettings.brightness);
        PlayerPrefs.SetFloat("brightness", _currentSettings.brightness);

        PlayerPrefs.Save();
        
        Debug.Log("[GameSettingsManager] Settings saved to PlayerPrefs");
    }
    
    /// <summary>
    /// Load settings from PlayerPrefs, using defaults for missing values.
    /// </summary>
    public void LoadSettings()
    {
        _currentSettings = new SettingsData
        {
            fullscreen = PlayerPrefs.GetInt("settings_fullscreen", DefaultSettings.fullscreen ? 1 : 0) == 1,
            displayModeIndex = LoadDisplayModeFromPrefs(),
            resolutionIndex = PlayerPrefs.GetInt("settings_resolutionIndex",
                                PlayerPrefs.GetInt("resIndex", DefaultSettings.resolutionIndex)),
            qualityIndex = PlayerPrefs.GetInt("settings_qualityIndex",
                                PlayerPrefs.GetInt("qualityIndex", QualitySettings.GetQualityLevel())),
            
            // Try new keys first, fall back to legacy keys
            masterVolume = PlayerPrefs.GetFloat("settings_masterVolume", 
                PlayerPrefs.GetFloat("masterVol", DefaultSettings.masterVolume)),
            musicVolume = PlayerPrefs.GetFloat("settings_musicVolume", 
                PlayerPrefs.GetFloat("musicVol", DefaultSettings.musicVolume)),
            sfxVolume = PlayerPrefs.GetFloat("settings_sfxVolume", 
                PlayerPrefs.GetFloat("sfxVol", DefaultSettings.sfxVolume)),
            
            mouseSensitivity = LoadMouseSensitivityFromPrefs(),
            micSensitivity = Mathf.Clamp01(PlayerPrefs.GetFloat(MIC_SENSITIVITY_KEY, DefaultSettings.micSensitivity)),
            invertYAxis = PlayerPrefs.GetInt("settings_invertY", DefaultSettings.invertYAxis ? 1 : 0) == 1,

            brightness = LoadBrightnessFromPrefs(),

            difficultyIndex = PlayerPrefs.GetInt("settings_difficulty", DefaultSettings.difficultyIndex),
            subtitlesEnabled = PlayerPrefs.GetInt("settings_subtitles", DefaultSettings.subtitlesEnabled ? 1 : 0) == 1
        };
        
        MigrateResolutionIndexFromAscendingOrder(ref _currentSettings.resolutionIndex);

        // Clamp values to valid ranges (-1 means auto: prefer 1920x1080 / desktop default)
        _currentSettings.resolutionIndex = ResolveResolutionIndex(_currentSettings.resolutionIndex);
        _currentSettings.qualityIndex = Mathf.Clamp(_currentSettings.qualityIndex, 0, QualitySettings.names.Length - 1);
        _currentSettings.mouseSensitivity = Mathf.Clamp01(_currentSettings.mouseSensitivity);
        _currentSettings.micSensitivity = Mathf.Clamp01(_currentSettings.micSensitivity);
        _currentSettings.brightness = Mathf.Clamp01(_currentSettings.brightness);
    }

    /// <summary>
    /// Resolve display mode from PlayerPrefs with one-time legacy migration.
    /// Old <c>SettingsMenu</c> used key "fullscreenMode" with a different index
    /// scheme: 0=FullScreenWindow, 1=Windowed, 2=ExclusiveFullScreen.
    /// We remap that to the manager's canonical scheme:
    /// 0=Fullscreen(Exclusive), 1=Windowed, 2=Borderless(FullScreenWindow).
    /// </summary>
    private int LoadDisplayModeFromPrefs()
    {
        if (PlayerPrefs.HasKey("settings_displayMode"))
            return PlayerPrefs.GetInt("settings_displayMode", DefaultSettings.displayModeIndex);

        if (PlayerPrefs.HasKey("fullscreenMode"))
        {
            int legacy = PlayerPrefs.GetInt("fullscreenMode", 0);
            // legacy: 0=FullScreenWindow(Borderless), 1=Windowed, 2=ExclusiveFullScreen
            // canonical: 0=Exclusive, 1=Windowed, 2=Borderless
            return legacy switch
            {
                0 => 2, // Borderless
                1 => 1, // Windowed
                2 => 0, // Exclusive Fullscreen
                _ => DefaultSettings.displayModeIndex
            };
        }

        return DefaultSettings.displayModeIndex;
    }

    /// <summary>
    /// Resolve brightness from PlayerPrefs. Prefers the new "Brightness" key,
    /// falls back to the legacy lowercase "brightness" used by old SettingsMenu,
    /// otherwise uses the default. Always returns a clamped 0..1 value.
    /// </summary>
    private float LoadBrightnessFromPrefs()
    {
        if (PlayerPrefs.HasKey(BRIGHTNESS_KEY))
            return Mathf.Clamp01(PlayerPrefs.GetFloat(BRIGHTNESS_KEY, DefaultSettings.brightness));
        if (PlayerPrefs.HasKey("brightness"))
            return Mathf.Clamp01(PlayerPrefs.GetFloat("brightness", DefaultSettings.brightness));
        return DefaultSettings.brightness;
    }

    /// <summary>
    /// Resolve the mouse sensitivity from PlayerPrefs. Handles migration from
    /// older formats so the loaded value is ALWAYS a normalized [0, 1] float:
    ///   1) Preferred new key "MouseSensitivity" (normalized 0-1)
    ///   2) Legacy "settings_mouseSensitivity" (was 0.1..10) -> remap to 0..1
    ///   3) Legacy "mouseSensitivity":
    ///        - <= 1.0 : already normalized (legacy SettingsMenu wrote slider value)
    ///        - >  1.0 : came from 0.1..10 sensitivity range -> remap to 0..1
    ///   4) Default value
    /// </summary>
    private float LoadMouseSensitivityFromPrefs()
    {
        if (PlayerPrefs.HasKey(MOUSE_SENSITIVITY_KEY))
            return Mathf.Clamp01(PlayerPrefs.GetFloat(MOUSE_SENSITIVITY_KEY, DefaultSettings.mouseSensitivity));

        if (PlayerPrefs.HasKey("settings_mouseSensitivity"))
        {
            float legacy = PlayerPrefs.GetFloat("settings_mouseSensitivity", 1.5f);
            return Mathf.Clamp01(Mathf.InverseLerp(0.1f, 10f, legacy));
        }

        if (PlayerPrefs.HasKey("mouseSensitivity"))
        {
            float legacy = PlayerPrefs.GetFloat("mouseSensitivity", DefaultSettings.mouseSensitivity);
            if (legacy <= 1.0f) return Mathf.Clamp01(legacy);
            return Mathf.Clamp01(Mathf.InverseLerp(0.1f, 10f, legacy));
        }

        return DefaultSettings.mouseSensitivity;
    }

    /// <summary>
    /// Persist ONLY the mouse sensitivity to PlayerPrefs.
    /// Use this when a script (e.g. the legacy main-menu SettingsMenu) wants to
    /// save just sensitivity without overwriting other settings the manager owns.
    /// </summary>
    public void SaveMouseSensitivity()
    {
        PlayerPrefs.SetFloat(MOUSE_SENSITIVITY_KEY, _currentSettings.mouseSensitivity);
        PlayerPrefs.SetFloat("mouseSensitivity", _currentSettings.mouseSensitivity);
        PlayerPrefs.DeleteKey("settings_mouseSensitivity");
        PlayerPrefs.Save();
    }
    
    /// <summary>
    /// Reset all settings to defaults, apply, and save.
    /// </summary>
    public void ResetToDefaults()
    {
        var fresh = DefaultSettings;
        fresh.resolutionIndex = GetPreferredDefaultResolutionIndex();
        fresh.qualityIndex = Mathf.Min(DefaultSettings.qualityIndex, QualitySettings.names.Length - 1);

        // Use SetSettings so change events fire and consumers refresh.
        SetSettings(fresh);

        ApplyAllSettings();
        SaveSettings();
        
        Debug.Log("[GameSettingsManager] Settings reset to defaults");
    }
    
    /// <summary>
    /// Set all settings at once (useful for SettingsMenuController).
    /// Does NOT auto-apply or auto-save - call ApplyAllSettings() and SaveSettings() after.
    /// </summary>
    public void SetSettings(SettingsData newSettings)
    {
        float prevSensitivity = _currentSettings.mouseSensitivity;
        float prevMicSensitivity = _currentSettings.micSensitivity;
        bool prevInvertY = _currentSettings.invertYAxis;
        float prevBrightness = _currentSettings.brightness;

        _currentSettings = newSettings;
        
        // Clamp values (-1 = auto)
        _currentSettings.resolutionIndex = ResolveResolutionIndex(_currentSettings.resolutionIndex);
        _currentSettings.qualityIndex = Mathf.Clamp(_currentSettings.qualityIndex, 0, QualitySettings.names.Length - 1);
        _currentSettings.displayModeIndex = Mathf.Clamp(_currentSettings.displayModeIndex, 0, 2);
        _currentSettings.difficultyIndex = Mathf.Clamp(_currentSettings.difficultyIndex, 0, 2);
        _currentSettings.mouseSensitivity = Mathf.Clamp01(_currentSettings.mouseSensitivity);
        _currentSettings.micSensitivity = Mathf.Clamp01(_currentSettings.micSensitivity);
        _currentSettings.masterVolume = Mathf.Clamp01(_currentSettings.masterVolume);
        _currentSettings.musicVolume = Mathf.Clamp01(_currentSettings.musicVolume);
        _currentSettings.sfxVolume = Mathf.Clamp01(_currentSettings.sfxVolume);
        _currentSettings.brightness = Mathf.Clamp01(_currentSettings.brightness);

        // Fire change events so live consumers (camera, brightness applier, etc.)
        // react immediately.
        if (!Mathf.Approximately(prevSensitivity, _currentSettings.mouseSensitivity))
            OnMouseSensitivityChanged?.Invoke(_currentSettings.mouseSensitivity);
        if (!Mathf.Approximately(prevMicSensitivity, _currentSettings.micSensitivity))
            OnMicSensitivityChanged?.Invoke(_currentSettings.micSensitivity);
        if (prevInvertY != _currentSettings.invertYAxis)
            OnInvertYAxisChanged?.Invoke(_currentSettings.invertYAxis);
        if (!Mathf.Approximately(prevBrightness, _currentSettings.brightness))
            OnBrightnessChanged?.Invoke(_currentSettings.brightness);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================
    
    /// <summary>
    /// Builds the resolution list used by settings UI and <see cref="ApplyGraphicsSettings"/>.
    /// Index 0 = highest resolution; last index = lowest offered.
    /// </summary>
    public static Resolution[] BuildAvailableResolutions()
    {
        var resolutions = Screen.resolutions
            .Where(r => r.refreshRateRatio.value >= 59.5f) // 60Hz and above
            .Where(r => r.width >= MIN_RESOLUTION_WIDTH && r.height >= MIN_RESOLUTION_HEIGHT)
            .GroupBy(r => new { r.width, r.height })
            .Select(g => g.First())
            .OrderByDescending(r => r.width)
            .ThenByDescending(r => r.height)
            .ToArray();

        if (resolutions.Length > 0)
            return resolutions;

        // Fallback: keep current desktop resolution even if it fails the min-size filter.
        return new[] { Screen.currentResolution };
    }

    private void CacheAvailableResolutions()
    {
        _availableResolutions = BuildAvailableResolutions();
    }

    /// <summary>
    /// One-time migration: old builds stored index 0 = smallest, last = largest.
    /// Flip saved indices so existing player choices stay on the same resolution.
    /// </summary>
    private void MigrateResolutionIndexFromAscendingOrder(ref int index)
    {
        if (index < 0)
            return;

        if (PlayerPrefs.GetInt(RESOLUTION_INDEX_DESC_KEY, 0) == 1)
            return;

        if (_availableResolutions == null || _availableResolutions.Length <= 1)
            return;

        index = (_availableResolutions.Length - 1) - index;
        PlayerPrefs.SetInt(RESOLUTION_INDEX_DESC_KEY, 1);
        PlayerPrefs.SetInt("settings_resolutionIndex", index);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// -1 (or any invalid index) means auto: prefer 1920x1080, else current desktop res, else highest listed.
    /// </summary>
    int ResolveResolutionIndex(int index)
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0)
            return 0;

        if (index < 0)
            return GetPreferredDefaultResolutionIndex();

        return Mathf.Clamp(index, 0, _availableResolutions.Length - 1);
    }

    int GetPreferredDefaultResolutionIndex()
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0)
            return 0;

        for (int i = 0; i < _availableResolutions.Length; i++)
        {
            Resolution r = _availableResolutions[i];
            if (r.width == 1920 && r.height == 1080)
                return i;
        }

        int current = Array.FindIndex(
            _availableResolutions,
            r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height);
        if (current >= 0)
            return current;

        return 0;
    }
    
    /// <summary>
    /// Convert linear volume (0-1) to decibels (-80 to 0 dB).
    /// </summary>
    public static float LinearToDecibel(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
    }
    
    /// <summary>
    /// Convert decibels (-80 to 0) to linear volume (0-1).
    /// </summary>
    public static float DecibelToLinear(float dB)
    {
        return dB <= -79f ? 0f : Mathf.Pow(10f, dB / 20f);
    }
    
    /// <summary>
    /// Get quality level names for UI dropdowns.
    /// </summary>
    public string[] GetQualityNames()
    {
        return QualitySettings.names;
    }
    
    /// <summary>
    /// Get resolution strings for UI dropdowns.
    /// </summary>
    public string[] GetResolutionStrings()
    {
        return _availableResolutions.Select(r => $"{r.width}x{r.height}").ToArray();
    }
}

