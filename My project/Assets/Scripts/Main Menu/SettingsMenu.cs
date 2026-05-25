/*
 * =============================================================================
 *                          SETTINGS MENU (Main Menu UI)
 * =============================================================================
 *
 * THIN UI ADAPTER. All persistence, application to Unity systems, and the
 * single source of truth for setting VALUES live in <see cref="GameSettingsManager"/>.
 * Brightness is applied to the URP Volume by <see cref="BrightnessApplier"/>
 * (a tiny scene-bound component that subscribes to the manager's event).
 *
 * This script's job is only:
 *   - Read manager state -> populate UI widgets (sliders/selectors)
 *   - Forward UI changes -> manager (live preview)
 *   - On Save: tell manager to persist + apply
 *   - Manage the four-panel navigation (Display / Audio / Controls / Gameplay)
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Panels")]
    [SerializeField] private GameObject displayPanel;
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject controlsPanel;
    [SerializeField] private GameObject gameplayPanel;

    [Header("Sliders (0-1 normalized)")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Slider masterVolSlider;
    [SerializeField] private Slider musicVolSlider;
    [SerializeField] private Slider sfxVolSlider;
    [SerializeField] private Slider brightnessSlider;

    [Header("Selectors (SettingsSelector components)")]
    [SerializeField] private SettingsSelector resolutionSelector;
    [SerializeField] private SettingsSelector qualitySelector;
    [SerializeField] private SettingsSelector fullscreenSelector;

    [Tooltip("Optional. If empty, resolved at runtime from a child named Row_AutoSave.")]
    [SerializeField] private SettingsSelector autoSaveSelector;

    [Tooltip("Optional. If empty, resolved at runtime from a child named Row_Language.")]
    [SerializeField] private SettingsSelector languageSelector;

    // =========================================================================
    // BACKWARD COMPATIBILITY
    // =========================================================================

    /// <summary>
    /// Legacy static accessor. The single source of truth is now
    /// <see cref="GameSettingsManager.MouseSensitivity"/> (normalized 0-1).
    /// </summary>
    public static float MouseSensitivity =>
        GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.MouseSensitivity
            : GameSettingsManager.DefaultSettings.mouseSensitivity;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================

    /// <summary>
    /// Snapshot of the manager's settings taken when editing begins.
    /// Used by <see cref="CancelEditing"/> to revert any live preview changes
    /// the user made while panning sliders without pressing Save.
    /// </summary>
    private GameSettingsManager.SettingsData _appliedSnapshot;

    /// <summary>
    /// In-progress edit buffer reflecting current UI state.
    /// Pushed to <see cref="GameSettingsManager"/> when <see cref="ApplyAndSave"/>
    /// is called.
    /// </summary>
    private GameSettingsManager.SettingsData _pending;

    private bool _listenersRegistered;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        if (autoSaveSelector == null)
            autoSaveSelector = FindSelectorOnRow("Row_AutoSave");

        if (languageSelector == null)
            languageSelector = FindSelectorOnRow("Row_Language");
    }

    private SettingsSelector FindSelectorOnRow(string rowName)
    {
        foreach (var tr in GetComponentsInChildren<Transform>(true))
        {
            if (tr.name != rowName) continue;
            return tr.GetComponentInChildren<SettingsSelector>(true);
        }
        return null;
    }

    private void Start()
    {
        ConfigureLanguageSelector();
        LoadSettings();
        HideAllPanels();
        if (displayPanel) displayPanel.SetActive(true);
    }

    private void OnEnable()
    {
        RegisterListeners();
        LocalizationService.LocaleChanged += OnLocaleChanged;
    }

    private void OnDisable()
    {
        UnregisterListeners();
        LocalizationService.LocaleChanged -= OnLocaleChanged;

        if (languageSelector != null)
            languageSelector.OnIndexChanged -= OnLanguageIndexChanged;
    }

    private void OnLocaleChanged() => MainMenuTextLocalizer.RefreshAllInstances();

    // =========================================================================
    // PUBLIC API (called by UI buttons in Canvas.prefab and by MainMenuUI)
    // =========================================================================

    /// <summary>
    /// Take a snapshot of the currently applied settings and seed the UI from it.
    /// Call this when opening the settings panel so Cancel can revert reliably.
    /// </summary>
    public void BeginEditing()
    {
        if (!EnsureManager()) return;

        ConfigureLanguageSelector();

        _appliedSnapshot = GameSettingsManager.Instance.CurrentSettings;
        _pending = _appliedSnapshot;
        SyncUIFrom(_pending);
        MainMenuTextLocalizer.EnsureOnCanvas(this)?.RebuildAndRefresh();
        Debug.Log("[Settings] BeginEditing — snapshot taken.");
    }

    /// <summary>
    /// Discard pending changes and restore the snapshot. The manager re-applies
    /// to Unity systems and fires events so any live preview (camera sensitivity,
    /// brightness, audio) snaps back.
    /// </summary>
    public void CancelEditing()
    {
        if (!EnsureManager()) return;

        GameSettingsManager.Instance.SetSettings(_appliedSnapshot);
        GameSettingsManager.Instance.ApplyAllSettings();
        _pending = _appliedSnapshot;
        SyncUIFrom(_appliedSnapshot);
        Debug.Log("[Settings] CancelEditing — reverted to applied snapshot.");
    }

    /// <summary>
    /// Read UI -> push to manager -> apply to Unity systems -> persist.
    /// </summary>
    public void ApplyAndSave()
    {
        if (!EnsureManager()) return;

        ReadUIInto(ref _pending);
        GameSettingsManager.Instance.SetSettings(_pending);
        GameSettingsManager.Instance.ApplyAllSettings();
        GameSettingsManager.Instance.SaveSettings();
        _appliedSnapshot = GameSettingsManager.Instance.CurrentSettings;
        MainMenuTextLocalizer.RefreshAllInstances();
        Debug.Log("[Settings] ApplyAndSave — settings persisted.");
    }

    /// <summary>
    /// Convenience: persist current manager state without re-reading UI.
    /// (UI button can call this directly for "save without close" flows.)
    /// </summary>
    public void SaveSettings()
    {
        if (!EnsureManager()) return;
        GameSettingsManager.Instance.SaveSettings();
    }

    /// <summary>
    /// Read settings from the manager and refresh the UI to match.
    /// The manager already loaded from PlayerPrefs in its Awake; this method
    /// just mirrors that state into the UI widgets.
    /// </summary>
    public void LoadSettings()
    {
        if (!EnsureManager()) return;

        _appliedSnapshot = GameSettingsManager.Instance.CurrentSettings;
        _pending = _appliedSnapshot;
        SyncUIFrom(_appliedSnapshot);
        Debug.Log($"[Settings] LoadSettings — Sens: {_appliedSnapshot.mouseSensitivity:F2}, " +
                  $"Brightness: {_appliedSnapshot.brightness:F2}");
    }

    /// <summary>
    /// Reset the *pending* edit to defaults and refresh UI. NOT saved until
    /// the user presses Apply.
    /// </summary>
    public void RestoreDefaults()
    {
        if (!EnsureManager()) return;

        _pending = GameSettingsManager.DefaultSettings;
        // Pick a sensible runtime default for indexes that depend on hardware.
        var resolutions = GameSettingsManager.Instance.AvailableResolutions;
        _pending.resolutionIndex = Mathf.Max(0, resolutions != null ? resolutions.Length - 1 : 0);
        _pending.qualityIndex = Mathf.Min(GameSettingsManager.DefaultSettings.qualityIndex,
                                           QualitySettings.names.Length - 1);

        // Push to manager so the live preview (camera sensitivity, audio, brightness)
        // also snaps to defaults. Persistence still requires Apply.
        GameSettingsManager.Instance.SetSettings(_pending);
        GameSettingsManager.Instance.ApplyAllSettings();
        SyncUIFrom(_pending);
        Debug.Log("[Settings] RestoreDefaults (not saved yet).");
    }

    // =========================================================================
    // PANEL NAVIGATION
    // =========================================================================

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

    // =========================================================================
    // LIVE-PREVIEW LISTENERS
    // =========================================================================

    private void RegisterListeners()
    {
        if (_listenersRegistered) return;
        _listenersRegistered = true;

        if (masterVolSlider)        masterVolSlider.onValueChanged.AddListener(OnMasterVolChanged);
        if (musicVolSlider)         musicVolSlider.onValueChanged.AddListener(OnMusicVolChanged);
        if (sfxVolSlider)           sfxVolSlider.onValueChanged.AddListener(OnSFXVolChanged);
        if (mouseSensitivitySlider) mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensChanged);
        if (brightnessSlider)       brightnessSlider.onValueChanged.AddListener(OnBrightnessChanged);
    }

    private void UnregisterListeners()
    {
        if (!_listenersRegistered) return;
        _listenersRegistered = false;

        if (masterVolSlider)        masterVolSlider.onValueChanged.RemoveListener(OnMasterVolChanged);
        if (musicVolSlider)         musicVolSlider.onValueChanged.RemoveListener(OnMusicVolChanged);
        if (sfxVolSlider)           sfxVolSlider.onValueChanged.RemoveListener(OnSFXVolChanged);
        if (mouseSensitivitySlider) mouseSensitivitySlider.onValueChanged.RemoveListener(OnMouseSensChanged);
        if (brightnessSlider)       brightnessSlider.onValueChanged.RemoveListener(OnBrightnessChanged);
    }

    private void OnMasterVolChanged(float v)
    {
        _pending.masterVolume = Mathf.Clamp01(v);
        if (GameSettingsManager.Instance != null)
        {
            GameSettingsManager.Instance.MasterVolume = _pending.masterVolume; // setter applies
        }
    }

    private void OnMusicVolChanged(float v)
    {
        _pending.musicVolume = Mathf.Clamp01(v);
        if (GameSettingsManager.Instance != null)
            GameSettingsManager.Instance.MusicVolume = _pending.musicVolume;
    }

    private void OnSFXVolChanged(float v)
    {
        _pending.sfxVolume = Mathf.Clamp01(v);
        if (GameSettingsManager.Instance != null)
            GameSettingsManager.Instance.SFXVolume = _pending.sfxVolume;
    }

    private void OnMouseSensChanged(float v)
    {
        _pending.mouseSensitivity = Mathf.Clamp01(v);
        if (GameSettingsManager.Instance != null)
            GameSettingsManager.Instance.MouseSensitivity = _pending.mouseSensitivity;
    }

    private void OnBrightnessChanged(float v)
    {
        _pending.brightness = Mathf.Clamp01(v);
        if (GameSettingsManager.Instance != null)
            GameSettingsManager.Instance.Brightness = _pending.brightness;
    }

    // =========================================================================
    // UI <-> DATA SYNC
    // =========================================================================

    private void SyncUIFrom(in GameSettingsManager.SettingsData s)
    {
        // Sliders use SetValueWithoutNotify so mirroring data into the UI
        // doesn't fire our live-preview callbacks (would otherwise loop).
        if (mouseSensitivitySlider) mouseSensitivitySlider.SetValueWithoutNotify(s.mouseSensitivity);
        if (masterVolSlider)        masterVolSlider.SetValueWithoutNotify(s.masterVolume);
        if (musicVolSlider)         musicVolSlider.SetValueWithoutNotify(s.musicVolume);
        if (sfxVolSlider)           sfxVolSlider.SetValueWithoutNotify(s.sfxVolume);
        if (brightnessSlider)       brightnessSlider.SetValueWithoutNotify(s.brightness);

        if (resolutionSelector)
        {
            int maxIdx = 0;
            if (GameSettingsManager.Instance != null && GameSettingsManager.Instance.AvailableResolutions != null)
                maxIdx = Mathf.Max(0, GameSettingsManager.Instance.AvailableResolutions.Length - 1);
            resolutionSelector.SetIndex(Mathf.Clamp(s.resolutionIndex, 0, maxIdx));
        }

        if (qualitySelector)
            qualitySelector.SetIndex(Mathf.Clamp(s.qualityIndex, 0, Mathf.Max(0, QualitySettings.names.Length - 1)));

        if (fullscreenSelector)
            fullscreenSelector.SetIndex(Mathf.Clamp(s.displayModeIndex, 0, 2));

        if (autoSaveSelector)
            autoSaveSelector.SetIndex(s.autoSaveEnabled ? 0 : 1);

        if (languageSelector)
            languageSelector.SetIndex(Mathf.Clamp(s.languageIndex, 0, LocalizationTables.SupportedLocaleCodes.Length - 1));
    }

    private void ReadUIInto(ref GameSettingsManager.SettingsData s)
    {
        s.mouseSensitivity = mouseSensitivitySlider ? mouseSensitivitySlider.value : s.mouseSensitivity;
        s.masterVolume     = masterVolSlider        ? masterVolSlider.value        : s.masterVolume;
        s.musicVolume      = musicVolSlider         ? musicVolSlider.value         : s.musicVolume;
        s.sfxVolume        = sfxVolSlider           ? sfxVolSlider.value           : s.sfxVolume;
        s.brightness       = brightnessSlider       ? brightnessSlider.value       : s.brightness;

        if (resolutionSelector) s.resolutionIndex   = resolutionSelector.GetCurrentIndex();
        if (qualitySelector)    s.qualityIndex      = qualitySelector.GetCurrentIndex();
        if (fullscreenSelector) s.displayModeIndex  = fullscreenSelector.GetCurrentIndex();

        if (autoSaveSelector) s.autoSaveEnabled = autoSaveSelector.GetCurrentIndex() == 0;

        if (languageSelector)
            s.languageIndex = languageSelector.GetCurrentIndex();
    }

    private void ConfigureLanguageSelector()
    {
        if (languageSelector == null)
            languageSelector = FindSelectorOnRow("Row_Language");

        if (languageSelector != null)
        {
            languageSelector.OnIndexChanged -= OnLanguageIndexChanged;
            languageSelector.ConfigureAsLanguageSelector();
            languageSelector.OnIndexChanged += OnLanguageIndexChanged;
        }

        foreach (var langUi in GetComponentsInChildren<LanguageSettingsUI>(true))
            langUi.Configure();
    }

    private void OnLanguageIndexChanged(int index)
    {
        _pending.languageIndex = Mathf.Clamp(
            index, 0, LocalizationTables.SupportedLocaleCodes.Length - 1);
    }

    // =========================================================================
    // GUARDS
    // =========================================================================

    private static bool EnsureManager()
    {
        if (GameSettingsManager.Instance != null) return true;
        Debug.LogWarning("[SettingsMenu] GameSettingsManager.Instance is null. " +
                         "Drop the GameSettingsManager prefab into your scene " +
                         "(or rely on the auto-bootstrap)");
        return false;
    }
}
