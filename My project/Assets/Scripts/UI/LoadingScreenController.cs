/*
 * =============================================================================
 *                      LOADING SCREEN CONTROLLER
 *                    (Professional Scene Transition)
 * =============================================================================
 * 
 * PURPOSE:
 * Provides a professional loading screen with:
 * - Smooth fade transitions
 * - Progress bar
 * - Random gameplay tips
 * - Atmospheric visuals for horror games
 * 
 * USAGE:
 * ------
 * Instead of: SceneManager.LoadScene("Main Scene");
 * Use:        LoadingScreenController.LoadScene("Main Scene");
 * 
 * SETUP:
 * ------
 * 1. Create a Canvas with a loading screen UI (set to DontDestroyOnLoad)
 * 2. Attach this script
 * 3. Assign UI references
 * 4. Add gameplay tips to the tips array
 * 
 * =============================================================================
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class LoadingScreenController : MonoBehaviour
{
    // =========================================================================
    // SINGLETON
    // =========================================================================
    
    private static LoadingScreenController _instance;
    public static LoadingScreenController Instance => _instance;

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("UI References")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI tipText;
    [SerializeField] private TextMeshProUGUI loadingStatusText;
    
    [Header("Optional Visuals")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite[] backgroundSprites; // Random backgrounds
    [SerializeField] private GameObject loadingSpinner;
    
    [Header("Animation Settings")]
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float minimumLoadTime = 1.5f; // Minimum time to show tips
    
    private static readonly string[] GameplayTipKeys =
    {
        LocalizationTables.GameplayKeys.TipHide,
        LocalizationTables.GameplayKeys.TipExplore,
        LocalizationTables.GameplayKeys.TipSound,
        LocalizationTables.GameplayKeys.TipFlashlight,
        LocalizationTables.GameplayKeys.TipKeys,
        LocalizationTables.GameplayKeys.TipRun,
        LocalizationTables.GameplayKeys.TipNotes,
        LocalizationTables.GameplayKeys.TipDark
    };

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    
    private bool _isLoading = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        // Singleton with persistence
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Hide on start
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Loads a scene with a professional loading screen.
    /// </summary>
    public static void LoadScene(string sceneName)
    {
        if (Instance == null)
        {
            // Fallback to direct load if no instance
            Debug.LogWarning("[LoadingScreenController] No instance found, loading directly.");
            SceneManager.LoadScene(sceneName);
            return;
        }
        
        Instance.StartCoroutine(Instance.LoadSceneAsync(sceneName));
    }
    
    /// <summary>
    /// Loads a scene by build index.
    /// </summary>
    public static void LoadScene(int sceneIndex)
    {
        if (Instance == null)
        {
            SceneManager.LoadScene(sceneIndex);
            return;
        }
        
        Instance.StartCoroutine(Instance.LoadSceneByIndexAsync(sceneIndex));
    }

    // =========================================================================
    // LOADING COROUTINES
    // =========================================================================
    
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        if (_isLoading) yield break;
        _isLoading = true;
        
        // Setup loading screen
        SetupLoadingScreen();
        
        // Fade in
        yield return StartCoroutine(FadeIn());
        
        // Start async loading
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;
        
        float elapsedTime = 0f;
        
        // Update progress while loading
        while (!asyncLoad.isDone)
        {
            elapsedTime += Time.unscaledDeltaTime;
            
            // Progress goes from 0 to 0.9 during loading
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            UpdateProgress(progress);
            
            // When loading is complete (0.9) and minimum time has passed
            if (asyncLoad.progress >= 0.9f && elapsedTime >= minimumLoadTime)
            {
                UpdateProgress(1f);
                yield return new WaitForSecondsRealtime(0.3f); // Brief pause at 100%
                
                // Activate scene
                asyncLoad.allowSceneActivation = true;
            }
            
            yield return null;
        }
        
        // Wait a frame for scene to initialize
        yield return null;
        
        // Fade out
        yield return StartCoroutine(FadeOut());
        
        _isLoading = false;
    }
    
    private IEnumerator LoadSceneByIndexAsync(int sceneIndex)
    {
        if (_isLoading) yield break;
        _isLoading = true;
        
        SetupLoadingScreen();
        yield return StartCoroutine(FadeIn());
        
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneIndex);
        asyncLoad.allowSceneActivation = false;
        
        float elapsedTime = 0f;
        
        while (!asyncLoad.isDone)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            UpdateProgress(progress);
            
            if (asyncLoad.progress >= 0.9f && elapsedTime >= minimumLoadTime)
            {
                UpdateProgress(1f);
                yield return new WaitForSecondsRealtime(0.3f);
                asyncLoad.allowSceneActivation = true;
            }
            
            yield return null;
        }
        
        yield return null;
        yield return StartCoroutine(FadeOut());
        
        _isLoading = false;
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================
    
    private void SetupLoadingScreen()
    {
        // Show panel
        if (loadingPanel != null)
            loadingPanel.SetActive(true);
        
        // Reset progress
        UpdateProgress(0f);
        
        // Set random background
        if (backgroundImage != null && backgroundSprites != null && backgroundSprites.Length > 0)
        {
            backgroundImage.sprite = backgroundSprites[Random.Range(0, backgroundSprites.Length)];
        }
        
        // Set random tip
        if (tipText != null && GameplayTipKeys.Length > 0)
        {
            string tipKey = GameplayTipKeys[Random.Range(0, GameplayTipKeys.Length)];
            tipText.text = LocalizationService.GetGameplay(tipKey);
        }
        
        // Set loading status
        if (loadingStatusText != null)
            loadingStatusText.text = LocalizationService.GetMainMenu(LocalizationTables.MainMenuKeys.LoadingStatus);
    }
    
    private void UpdateProgress(float progress)
    {
        if (progressBar != null)
            progressBar.value = progress;
        
        if (progressText != null)
            progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
    }

    // =========================================================================
    // ANIMATION
    // =========================================================================
    
    private IEnumerator FadeIn()
    {
        if (canvasGroup == null)
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(true);
            yield break;
        }
        
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true;
        
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
            yield return null;
        }
        
        canvasGroup.alpha = 1f;
    }
    
    private IEnumerator FadeOut()
    {
        if (canvasGroup == null)
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(false);
            yield break;
        }
        
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
        
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
    }
}


