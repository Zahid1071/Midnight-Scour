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
 * =============================================================================
 */

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class LoadingScreenController : MonoBehaviour
{
    private static LoadingScreenController _instance;
    public static LoadingScreenController Instance => _instance;

    [Header("UI References")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI tipText;
    [SerializeField] private TextMeshProUGUI loadingStatusText;

    [Header("Optional Visuals")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite[] backgroundSprites;
    [SerializeField] private GameObject loadingSpinner;

    [Header("Animation Settings")]
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float minimumLoadTime = 1.5f;

    [Header("Gameplay Tips")]
    static readonly string[] TipKeys =
    {
        "loading.tip.hiding",
        "loading.tip.explore",
        "loading.tip.quiet",
        "loading.tip.flashlight",
        "loading.tip.doors",
        "loading.tip.running",
        "loading.tip.notes",
        "loading.tip.dark_corners"
    };

    private bool _isLoading;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (loadingPanel != null)
            loadingPanel.SetActive(false);
    }

    public static void LoadScene(string sceneName)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[LoadingScreenController] No instance found, loading directly.");
            SceneManager.LoadScene(sceneName);
            return;
        }

        Instance.StartCoroutine(Instance.LoadSceneAsync(sceneName));
    }

    public static void LoadScene(int sceneIndex)
    {
        if (Instance == null)
        {
            SceneManager.LoadScene(sceneIndex);
            return;
        }

        Instance.StartCoroutine(Instance.LoadSceneByIndexAsync(sceneIndex));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        if (_isLoading) yield break;
        _isLoading = true;

        SetupLoadingScreen();
        yield return StartCoroutine(FadeIn());

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
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

    private void SetupLoadingScreen()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(true);

        UpdateProgress(0f);

        if (backgroundImage != null && backgroundSprites != null && backgroundSprites.Length > 0)
            backgroundImage.sprite = backgroundSprites[Random.Range(0, backgroundSprites.Length)];

        if (tipText != null && TipKeys.Length > 0)
            UIFont.SetText(tipText, Loc.Get(TipKeys[Random.Range(0, TipKeys.Length)]));

        if (loadingStatusText != null)
            UIFont.SetText(loadingStatusText, Loc.Get("loading.status"));
    }

    private void UpdateProgress(float progress)
    {
        if (progressBar != null)
            progressBar.value = progress;

        if (progressText != null)
            UIFont.SetText(progressText, $"{Mathf.RoundToInt(progress * 100)}%");
    }

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
