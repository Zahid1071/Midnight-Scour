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
 *     GameSettingsManager.Instance.MouseSensitivity  // Get/set mouse sensitivity (0.1-10.0)
 *     GameSettingsManager.Instance.InvertYAxis       // Get/set Y-axis inversion
 *     GameSettingsManager.Instance.MasterVolume      // Get/set master volume (0.0-1.0 linear)
 *     GameSettingsManager.Instance.Difficulty        // Get current difficulty (0=Easy, 1=Normal, 2=Hard)
 * 
 * The manager automatically applies settings to Unity systems when changed.
 * Settings persist across sessions via PlayerPrefs.
 * 
 * INTEGRATION POINTS:
 * -------------------
 * - SettingsMenuController: Opens/closes settings UI, reads/writes via this manager
 * - PauseMenuController: Uses this manager indirectly through SettingsMenuController
 * - Player scripts: Read MouseSensitivity, InvertYAxis for camera controls
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
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("Audio")]
    [Tooltip("Assign the main AudioMixer asset")]
    [SerializeField] private AudioMixer audioMixer;
    
    [Header("Exposed Parameter Names (must match AudioMixer)")]
    [SerializeField] private string masterVolumeParam = "Master";
    [SerializeField] private string musicVolumeParam = "Music";
    [SerializeField] private string sfxVolumeParam = "SFX";

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
        public float mouseSensitivity;    // 0.1 - 10.0
        public bool invertYAxis;
        
        // Gameplay
        public int difficultyIndex;       // 0=Easy, 1=Normal, 2=Hard
        public bool subtitlesEnabled;
    }

    // =========================================================================
    // DEFAULT VALUES
    // =========================================================================
    
    public static readonly SettingsData DefaultSettings = new SettingsData
    {
        fullscreen = true,
        displayModeIndex = 0,
        resolutionIndex = -1, // Will be set to max resolution at runtime
        qualityIndex = 2,     // High
        
        masterVolume = 0.8f,
        musicVolume = 0.8f,
        sfxVolume = 0.8f,
        
        mouseSensitivity = 1.5f,
        invertYAxis = false,
        
        difficultyIndex = 1, // Normal
        subtitlesEnabled = true
    };

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
    /// Mouse sensitivity (0.1 to 10.0).
    /// Player scripts should multiply their base sensitivity by this value.
    /// </summary>
    public float MouseSensitivity
    {
        get => _currentSettings.mouseSensitivity;
        set { _currentSettings.mouseSensitivity = Mathf.Clamp(value, 0.1f, 10f); }
    }
    
    /// <summary>
    /// If true, Y-axis mouse input should be inverted.
    /// </summary>
    public bool InvertYAxis
    {
        get => _currentSettings.invertYAxis;
        set => _currentSettings.invertYAxis = value;
    }

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
        
        // Initialize resolutions
        CacheAvailableResolutions();
        
        // Load saved settings or defaults
        LoadSettings();
        
        // Apply all settings at startup
        ApplyAllSettings();
    }

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
            int resIdx = Mathf.Clamp(_currentSettings.resolutionIndex, 0, _availableResolutions.Length - 1);
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
        PlayerPrefs.SetFloat("mouseSensitivity", _currentSettings.mouseSensitivity);
        
        PlayerPrefs.SetFloat("settings_mouseSensitivity", _currentSettings.mouseSensitivity);
        PlayerPrefs.SetInt("settings_invertY", _currentSettings.invertYAxis ? 1 : 0);
        
        PlayerPrefs.SetInt("settings_difficulty", _currentSettings.difficultyIndex);
        PlayerPrefs.SetInt("settings_subtitles", _currentSettings.subtitlesEnabled ? 1 : 0);
        
        PlayerPrefs.Save();
        
        Debug.Log("[GameSettingsManager] Settings saved to PlayerPrefs");
    }
    
    /// <summary>
    /// Load settings from PlayerPrefs, using defaults for missing values.
    /// </summary>
    public void LoadSettings()
    {
        // Determine default resolution index (highest available)
        int defaultResIndex = Mathf.Max(0, _availableResolutions.Length - 1);
        
        // Find current resolution in the list for better default
        int currentResIndex = Array.FindIndex(_availableResolutions, 
            r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height);
        if (currentResIndex < 0) currentResIndex = defaultResIndex;
        
        _currentSettings = new SettingsData
        {
            fullscreen = PlayerPrefs.GetInt("settings_fullscreen", DefaultSettings.fullscreen ? 1 : 0) == 1,
            displayModeIndex = PlayerPrefs.GetInt("settings_displayMode", DefaultSettings.displayModeIndex),
            resolutionIndex = PlayerPrefs.GetInt("settings_resolutionIndex", currentResIndex),
            qualityIndex = PlayerPrefs.GetInt("settings_qualityIndex", QualitySettings.GetQualityLevel()),
            
            // Try new keys first, fall back to legacy keys
            masterVolume = PlayerPrefs.GetFloat("settings_masterVolume", 
                PlayerPrefs.GetFloat("masterVol", DefaultSettings.masterVolume)),
            musicVolume = PlayerPrefs.GetFloat("settings_musicVolume", 
                PlayerPrefs.GetFloat("musicVol", DefaultSettings.musicVolume)),
            sfxVolume = PlayerPrefs.GetFloat("settings_sfxVolume", 
                PlayerPrefs.GetFloat("sfxVol", DefaultSettings.sfxVolume)),
            
            mouseSensitivity = PlayerPrefs.GetFloat("settings_mouseSensitivity", 
                PlayerPrefs.GetFloat("mouseSensitivity", DefaultSettings.mouseSensitivity)),
            invertYAxis = PlayerPrefs.GetInt("settings_invertY", DefaultSettings.invertYAxis ? 1 : 0) == 1,
            
            difficultyIndex = PlayerPrefs.GetInt("settings_difficulty", DefaultSettings.difficultyIndex),
            subtitlesEnabled = PlayerPrefs.GetInt("settings_subtitles", DefaultSettings.subtitlesEnabled ? 1 : 0) == 1
        };
        
        // Clamp values to valid ranges
        _currentSettings.resolutionIndex = Mathf.Clamp(_currentSettings.resolutionIndex, 0, Mathf.Max(0, _availableResolutions.Length - 1));
        _currentSettings.qualityIndex = Mathf.Clamp(_currentSettings.qualityIndex, 0, QualitySettings.names.Length - 1);
    }
    
    /// <summary>
    /// Reset all settings to defaults, apply, and save.
    /// </summary>
    public void ResetToDefaults()
    {
        int defaultResIndex = Mathf.Max(0, _availableResolutions.Length - 1);
        
        _currentSettings = DefaultSettings;
        _currentSettings.resolutionIndex = defaultResIndex;
        _currentSettings.qualityIndex = Mathf.Min(DefaultSettings.qualityIndex, QualitySettings.names.Length - 1);
        
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
        _currentSettings = newSettings;
        
        // Clamp values
        _currentSettings.resolutionIndex = Mathf.Clamp(_currentSettings.resolutionIndex, 0, Mathf.Max(0, _availableResolutions.Length - 1));
        _currentSettings.qualityIndex = Mathf.Clamp(_currentSettings.qualityIndex, 0, QualitySettings.names.Length - 1);
        _currentSettings.displayModeIndex = Mathf.Clamp(_currentSettings.displayModeIndex, 0, 2);
        _currentSettings.difficultyIndex = Mathf.Clamp(_currentSettings.difficultyIndex, 0, 2);
        _currentSettings.mouseSensitivity = Mathf.Clamp(_currentSettings.mouseSensitivity, 0.1f, 10f);
        _currentSettings.masterVolume = Mathf.Clamp01(_currentSettings.masterVolume);
        _currentSettings.musicVolume = Mathf.Clamp01(_currentSettings.musicVolume);
        _currentSettings.sfxVolume = Mathf.Clamp01(_currentSettings.sfxVolume);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================
    
    private void CacheAvailableResolutions()
    {
        // Get unique resolutions, sorted by size
        _availableResolutions = Screen.resolutions
            .Where(r => r.refreshRateRatio.value >= 59.5) // 60Hz and above
            .GroupBy(r => new { r.width, r.height })
            .Select(g => g.First())
            .OrderBy(r => r.width)
            .ThenBy(r => r.height)
            .ToArray();
        
        if (_availableResolutions.Length == 0)
        {
            // Fallback: use current resolution
            _availableResolutions = new Resolution[] { Screen.currentResolution };
        }
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

