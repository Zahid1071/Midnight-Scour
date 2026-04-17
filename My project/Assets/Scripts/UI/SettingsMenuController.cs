/*
 * =============================================================================
 *                      SETTINGS MENU CONTROLLER
 *                    (Resident Evil Style UI)
 * =============================================================================
 * 
 * PURPOSE:
 * Controls the Resident Evil-style settings menu UI with category tabs.
 * This is a self-contained prefab that can be used in both Main Menu and
 * Pause Menu contexts.
 * 
 * USAGE:
 * ------
 * 1. Drag the SettingsMenuRoot prefab into your scene
 * 2. Assign references in the inspector
 * 3. Call Open() to show the menu, Close() to hide it
 * 
 * PUBLIC METHODS:
 * ---------------
 * - Open()           : Show the settings panel and load current values
 * - Close()          : Hide the settings panel (discards unsaved changes)
 * - ApplySettings()  : Save and apply current UI values
 * - ResetToDefaults(): Reset all settings to defaults
 * 
 * CATEGORY TABS (Turkish):
 * ------------------------
 * - Görüntü    (Display)
 * - Ses        (Audio)  
 * - Kontroller (Controls)
 * - Oynanış    (Gameplay)
 * 
 * =============================================================================
 */

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

public class SettingsMenuController : MonoBehaviour
{
    // =========================================================================
    // EVENTS
    // =========================================================================
    
    /// <summary>
    /// Fired when the Back/Geri button is pressed.
    /// Parent menus should subscribe to this to know when to show themselves.
    /// </summary>
    public UnityEvent OnBackPressed;

    // =========================================================================
    // INSPECTOR REFERENCES - Root & Animation
    // =========================================================================
    
    [Header("Root Panel")]
    [Tooltip("The root GameObject of the entire settings menu")]
    [SerializeField] private GameObject settingsRoot;
    
    [Tooltip("CanvasGroup for fade animations")]
    [SerializeField] private CanvasGroup canvasGroup;
    
    [Header("Animation")]
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.2f;

    // =========================================================================
    // CATEGORY TABS
    // =========================================================================
    
    [Header("Category Buttons (Left Column)")]
    [SerializeField] private Button categoryDisplayBtn;   // Görüntü
    [SerializeField] private Button categoryAudioBtn;     // Ses
    [SerializeField] private Button categoryControlsBtn;  // Kontroller
    [SerializeField] private Button categoryGameplayBtn;  // Oynanış
    
    [Header("Category Panels (Right Column)")]
    [SerializeField] private GameObject displayPanel;     // Görüntü options
    [SerializeField] private GameObject audioPanel;       // Ses options
    [SerializeField] private GameObject controlsPanel;    // Kontroller options
    [SerializeField] private GameObject gameplayPanel;    // Oynanış options
    
   

    // =========================================================================
    // DISPLAY SETTINGS UI (Görüntü)
    // =========================================================================
    
    [Header("Display Settings - Görüntü")]
    [SerializeField] private Toggle fullscreenToggle;           // Tam Ekran
    [SerializeField] private SettingsSpinner displayModeSpinner;  // Ekran Modu
    [SerializeField] private SettingsSpinner resolutionSpinner;   // Çözünürlük
    [SerializeField] private SettingsSpinner qualitySpinner;      // Görsel Kalite

    // =========================================================================
    // AUDIO SETTINGS UI (Ses)
    // =========================================================================
    
    [Header("Audio Settings - Ses")]
    [SerializeField] private Slider masterVolumeSlider;    // Ana Ses
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private Slider musicVolumeSlider;     // Müzik Sesi
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private Slider sfxVolumeSlider;       // Efekt Sesi
    [SerializeField] private TextMeshProUGUI sfxVolumeText;

    // =========================================================================
    // CONTROLS SETTINGS UI (Kontroller)
    // =========================================================================
    
    [Header("Controls Settings - Kontroller")]
    [SerializeField] private Slider mouseSensitivitySlider;  // Fare Hassasiyeti
    [SerializeField] private TextMeshProUGUI mouseSensitivityText;
    [SerializeField] private Toggle invertYToggle;           // Ters Y Ekseni

    // =========================================================================
    // GAMEPLAY SETTINGS UI (Oynanış)
    // =========================================================================
    
    [Header("Gameplay Settings - Oynanış")]
    [SerializeField] private SettingsSpinner difficultySpinner;  // Zorluk
    [SerializeField] private Toggle subtitlesToggle;             // Altyazılar

    // =========================================================================
    // FOOTER BUTTONS
    // =========================================================================
    
    [Header("Footer Buttons")]
    [SerializeField] private Button applyButton;     // Uygula
    [SerializeField] private Button defaultsButton;  // Varsayılan
    [SerializeField] private Button backButton;      // Geri

    // =========================================================================
    // AUDIO FEEDBACK
    // =========================================================================
    
    [Header("UI Audio (Optional)")]
    [SerializeField] private AudioSource uiAudioSource;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioClip hoverSound;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    
    private int _currentCategory = 0; // 0=Display, 1=Audio, 2=Controls, 3=Gameplay
    private Button[] _categoryButtons;
    private GameObject[] _categoryPanels;
    private Coroutine _fadeCoroutine;
    private GameSettingsManager.SettingsData _pendingSettings;
    private bool _isOpen = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        // Cache arrays for easier iteration
        _categoryButtons = new Button[] { categoryDisplayBtn, categoryAudioBtn, categoryControlsBtn, categoryGameplayBtn };
        _categoryPanels = new GameObject[] { displayPanel, audioPanel, controlsPanel, gameplayPanel };
        
        // Setup button listeners
        SetupButtonListeners();
        SetupSliderListeners();
        
        // Initialize spinners
        PopulateSpinners();
        
        // Ensure closed on start
        if (settingsRoot != null)
            settingsRoot.SetActive(false);

        SelectCategory(0);
    }


    private void SetupButtonListeners()
    {
        // Category buttons
        if (categoryDisplayBtn) categoryDisplayBtn.onClick.AddListener(() => SelectCategory(0));
        if (categoryAudioBtn) categoryAudioBtn.onClick.AddListener(() => SelectCategory(1));
        if (categoryControlsBtn) categoryControlsBtn.onClick.AddListener(() => SelectCategory(2));
        if (categoryGameplayBtn) categoryGameplayBtn.onClick.AddListener(() => SelectCategory(3));
        
        // Footer buttons
        if (applyButton) applyButton.onClick.AddListener(ApplySettings);
        if (defaultsButton) defaultsButton.onClick.AddListener(ResetToDefaults);
        if (backButton) backButton.onClick.AddListener(OnBackClicked);
    }
    
    private void SetupSliderListeners()
    {
        // Audio sliders - update text display in real-time
        if (masterVolumeSlider)
        {
            masterVolumeSlider.onValueChanged.AddListener(v => UpdateVolumeText(masterVolumeText, v));
        }
        if (musicVolumeSlider)
        {
            musicVolumeSlider.onValueChanged.AddListener(v => UpdateVolumeText(musicVolumeText, v));
        }
        if (sfxVolumeSlider)
        {
            sfxVolumeSlider.onValueChanged.AddListener(v => UpdateVolumeText(sfxVolumeText, v));
        }
        
        // Mouse sensitivity slider
        if (mouseSensitivitySlider)
        {
            mouseSensitivitySlider.onValueChanged.AddListener(v => UpdateSensitivityText(mouseSensitivityText, v));
        }
    }

    private void PopulateSpinners()
    {
        // Display Mode spinner
        if (displayModeSpinner != null)
        {
            displayModeSpinner.SetOptions(new System.Collections.Generic.List<string>
            {
                "Tam Ekran",      // Fullscreen
                "Pencereli",      // Windowed
                "Kenarlıksız"     // Borderless
            });
        }
        
        // Quality spinner
        if (qualitySpinner != null)
        {
            // Turkish quality names
            qualitySpinner.SetOptions(new System.Collections.Generic.List<string>
            {
                "Düşük",   // Low
                "Orta",    // Medium
                "Yüksek",  // High
                "Ultra"    // Ultra
            });
        }
        
        // Difficulty spinner
        if (difficultySpinner != null)
        {
            difficultySpinner.SetOptions(new System.Collections.Generic.List<string>
            {
                "Kolay",   // Easy
                "Normal",  // Normal
                "Zor"      // Hard
            });
        }
        
        // Resolution spinner will be populated when menu opens (from GameSettingsManager)
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Opens the settings menu, loading current values from GameSettingsManager.
    /// </summary>
    public void Open()
    {
        if (_isOpen) return;
        
        // Ensure GameSettingsManager exists
        if (GameSettingsManager.Instance == null)
        {
            Debug.LogError("[SettingsMenuController] GameSettingsManager.Instance is null! Cannot open settings.");
            return;
        }
        
        _isOpen = true;
        
        // Populate resolution spinner with current available resolutions
        PopulateResolutionSpinner();
        
        // Load current settings into pending
        _pendingSettings = GameSettingsManager.Instance.CurrentSettings;
        
        // Update all UI elements
        SyncUIFromSettings();
        
        // Select first category
        SelectCategory(0);
        
        // Show and animate
        if (settingsRoot != null)
            settingsRoot.SetActive(true);
        
        if (canvasGroup != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeIn());
        }
        
        // Ensure cursor is visible
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        PlayClickSound();
    }
    
    /// <summary>
    /// Closes the settings menu. Unsaved changes are discarded.
    /// </summary>
    public void Close()
    {
        if (!_isOpen) return;
        
        _isOpen = false;
        
        if (canvasGroup != null)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeOut());
        }
        else if (settingsRoot != null)
        {
            settingsRoot.SetActive(false);
        }
    }
    
    /// <summary>
    /// Applies current UI values to GameSettingsManager and saves to PlayerPrefs.
    /// </summary>
    public void ApplySettings()
    {
        ReadUIIntoSettings();
        
        if (GameSettingsManager.Instance != null)
        {
            GameSettingsManager.Instance.SetSettings(_pendingSettings);
            GameSettingsManager.Instance.ApplyAllSettings();
            GameSettingsManager.Instance.SaveSettings();
        }
        
        PlayClickSound();
        Debug.Log("[SettingsMenuController] Settings applied and saved");
    }
    
    /// <summary>
    /// Resets all settings to defaults and updates the UI.
    /// Does NOT close the menu - user must click Apply to save.
    /// </summary>
    public void ResetToDefaults()
    {
        if (GameSettingsManager.Instance != null)
        {
            // Get default settings
            _pendingSettings = GameSettingsManager.DefaultSettings;
            _pendingSettings.resolutionIndex = Mathf.Max(0, GameSettingsManager.Instance.AvailableResolutions.Length - 1);
            _pendingSettings.qualityIndex = Mathf.Min(GameSettingsManager.DefaultSettings.qualityIndex, QualitySettings.names.Length - 1);
            
            // Update UI
            SyncUIFromSettings();
        }
        
        PlayClickSound();
        Debug.Log("[SettingsMenuController] Reset to defaults (not saved yet)");
    }

    // =========================================================================
    // CATEGORY SWITCHING
    // =========================================================================
    
    private void SelectCategory(int index)
    {
        _currentCategory = index;
        
        // Update panels visibility
        for (int i = 0; i < _categoryPanels.Length; i++)
        {
            if (_categoryPanels[i] != null)
                _categoryPanels[i].SetActive(i == index);
        }
        
        
        
        PlayClickSound();
    }
    
    

    // =========================================================================
    // UI SYNC METHODS
    // =========================================================================
    
    private void PopulateResolutionSpinner()
    {
        if (resolutionSpinner == null || GameSettingsManager.Instance == null) return;
        
        var resStrings = GameSettingsManager.Instance.GetResolutionStrings();
        resolutionSpinner.SetOptions(new System.Collections.Generic.List<string>(resStrings));
    }
    
    private void SyncUIFromSettings()
    {
        // Display
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(_pendingSettings.fullscreen);
        
        if (displayModeSpinner != null)
            displayModeSpinner.SetValueWithoutNotify(_pendingSettings.displayModeIndex);
        
        if (resolutionSpinner != null && GameSettingsManager.Instance != null)
        {
            int maxIdx = GameSettingsManager.Instance.AvailableResolutions.Length - 1;
            resolutionSpinner.SetValueWithoutNotify(Mathf.Clamp(_pendingSettings.resolutionIndex, 0, Mathf.Max(0, maxIdx)));
        }
        
        if (qualitySpinner != null)
            qualitySpinner.SetValueWithoutNotify(Mathf.Clamp(_pendingSettings.qualityIndex, 0, Mathf.Max(0, qualitySpinner.OptionCount - 1)));
        
        // Audio
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(_pendingSettings.masterVolume);
            UpdateVolumeText(masterVolumeText, _pendingSettings.masterVolume);
        }
        
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(_pendingSettings.musicVolume);
            UpdateVolumeText(musicVolumeText, _pendingSettings.musicVolume);
        }
        
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(_pendingSettings.sfxVolume);
            UpdateVolumeText(sfxVolumeText, _pendingSettings.sfxVolume);
        }
        
        // Controls
        if (mouseSensitivitySlider != null)
        {
            // Map 0.1-10.0 range to 0-1 slider
            float normalizedSens = Mathf.InverseLerp(0.1f, 10f, _pendingSettings.mouseSensitivity);
            mouseSensitivitySlider.SetValueWithoutNotify(normalizedSens);
            UpdateSensitivityText(mouseSensitivityText, normalizedSens);
        }
        
        if (invertYToggle != null)
            invertYToggle.SetIsOnWithoutNotify(_pendingSettings.invertYAxis);
        
        // Gameplay
        if (difficultySpinner != null)
            difficultySpinner.SetValueWithoutNotify(_pendingSettings.difficultyIndex);
        
        if (subtitlesToggle != null)
            subtitlesToggle.SetIsOnWithoutNotify(_pendingSettings.subtitlesEnabled);
    }
    
    private void ReadUIIntoSettings()
    {
        // Display
        if (fullscreenToggle != null)
            _pendingSettings.fullscreen = fullscreenToggle.isOn;
        
        if (displayModeSpinner != null)
            _pendingSettings.displayModeIndex = displayModeSpinner.Value;
        
        if (resolutionSpinner != null)
            _pendingSettings.resolutionIndex = resolutionSpinner.Value;
        
        if (qualitySpinner != null)
            _pendingSettings.qualityIndex = qualitySpinner.Value;
        
        // Audio
        if (masterVolumeSlider != null)
            _pendingSettings.masterVolume = masterVolumeSlider.value;
        
        if (musicVolumeSlider != null)
            _pendingSettings.musicVolume = musicVolumeSlider.value;
        
        if (sfxVolumeSlider != null)
            _pendingSettings.sfxVolume = sfxVolumeSlider.value;
        
        // Controls
        if (mouseSensitivitySlider != null)
        {
            // Map 0-1 slider back to 0.1-10.0 range
            _pendingSettings.mouseSensitivity = Mathf.Lerp(0.1f, 10f, mouseSensitivitySlider.value);
        }
        
        if (invertYToggle != null)
            _pendingSettings.invertYAxis = invertYToggle.isOn;
        
        // Gameplay
        if (difficultySpinner != null)
            _pendingSettings.difficultyIndex = difficultySpinner.Value;
        
        if (subtitlesToggle != null)
            _pendingSettings.subtitlesEnabled = subtitlesToggle.isOn;
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================
    
    private void UpdateVolumeText(TextMeshProUGUI text, float value)
    {
        if (text != null)
            text.text = $"{Mathf.RoundToInt(value * 100)}%";
    }
    
    private void UpdateSensitivityText(TextMeshProUGUI text, float normalizedValue)
    {
        if (text != null)
        {
            float actualValue = Mathf.Lerp(0.1f, 10f, normalizedValue);
            text.text = actualValue.ToString("F1");
        }
    }
    
    private void OnBackClicked()
    {
        PlayClickSound();
        Close();
        OnBackPressed?.Invoke();
    }
    
    private void PlayClickSound()
    {
        if (uiAudioSource != null && clickSound != null)
            uiAudioSource.PlayOneShot(clickSound);
        else if (SoundManager.Instance != null)
            SoundManager.Instance.PlayUIClick();
    }

    // =========================================================================
    // ANIMATION COROUTINES
    // =========================================================================
    
    private IEnumerator FadeIn()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
    }
    
    private IEnumerator FadeOut()
    {
        canvasGroup.interactable = false;
        
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;
        
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
            yield return null;
        }
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        
        if (settingsRoot != null)
            settingsRoot.SetActive(false);
    }
    
    // =========================================================================
    // PUBLIC STATE CHECK
    // =========================================================================
    
    public bool IsOpen => _isOpen;
}

