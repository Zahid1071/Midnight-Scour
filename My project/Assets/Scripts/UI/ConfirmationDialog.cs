/*
 * =============================================================================
 *                      CONFIRMATION DIALOG
 *                    (Professional UI Component)
 * =============================================================================
 * 
 * PURPOSE:
 * Generic confirmation popup for dangerous actions like:
 * - Quitting the game
 * - Returning to main menu (losing progress)
 * - Resetting settings to defaults
 * - Deleting files / irreversible actions
 * 
 * USAGE:
 * ------
 * ConfirmationDialog.Show(
 *     "Oyundan çıkmak istediğinize emin misiniz?",
 *     onConfirm: () => Application.Quit(),
 *     onCancel: () => Debug.Log("Cancelled")
 * );
 * 
 * =============================================================================
 */

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ConfirmationDialog : MonoBehaviour
{
    // =========================================================================
    // SINGLETON
    // =========================================================================
    
    private static ConfirmationDialog _instance;
    public static ConfirmationDialog Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance
                _instance = FindFirstObjectByType<ConfirmationDialog>();
                
                // If not found, create from prefab or dynamically
                if (_instance == null)
                {
                    Debug.LogWarning("[ConfirmationDialog] No instance found in scene!");
                }
            }
            return _instance;
        }
    }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("UI References")]
    [SerializeField] private GameObject dialogPanel;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private TextMeshProUGUI cancelButtonText;
    
    [Header("Animation")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.1f;
    
    [Header("Default Texts")]
    [SerializeField] private string defaultTitleKey = "ui.confirm.title";
    [SerializeField] private string defaultConfirmKey = "ui.confirm.yes";
    [SerializeField] private string defaultCancelKey = "ui.confirm.no";

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    
    private Action _onConfirm;
    private Action _onCancel;
    private bool _isShowing = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        // Setup button listeners
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmClicked);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
        
        // Hide on start
        if (dialogPanel != null)
            dialogPanel.SetActive(false);
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Shows a confirmation dialog with custom message and callbacks.
    /// </summary>
    public static void Show(string message, Action onConfirm, Action onCancel = null, 
                           string title = null, string confirmText = null, string cancelText = null)
    {
        if (Instance == null) return;
        Instance.ShowInternal(message, onConfirm, onCancel, title, confirmText, cancelText);
    }
    
    /// <summary>
    /// Shows a simple "Are you sure?" dialog.
    /// </summary>
    public static void ShowQuitConfirmation(Action onConfirm)
    {
        Show(
            Loc.Get("ui.confirm.quit_message"),
            onConfirm,
            null,
            Loc.Get("ui.confirm.quit_title"),
            Loc.Get("ui.confirm.quit_confirm"),
            Loc.Get("ui.confirm.no"));
    }

    public static void ShowReturnToMenuConfirmation(Action onConfirm)
    {
        Show(
            Loc.Get("ui.confirm.menu_message"),
            onConfirm,
            null,
            Loc.Get("ui.confirm.menu_title"),
            Loc.Get("ui.confirm.menu_confirm"),
            Loc.Get("ui.confirm.no"));
    }
    
    /// <summary>
    /// Hides the dialog without triggering callbacks.
    /// </summary>
    public static void Hide()
    {
        if (Instance != null)
            Instance.HideInternal();
    }

    // =========================================================================
    // INTERNAL METHODS
    // =========================================================================
    
    private void ShowInternal(string message, Action onConfirm, Action onCancel,
                              string title, string confirmText, string cancelText)
    {
        if (_isShowing) return;
        
        _isShowing = true;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        
        // Set texts
        if (titleText != null)
            UIFont.SetText(titleText, title ?? Loc.Get(defaultTitleKey));
        if (messageText != null)
            UIFont.SetText(messageText, message);
        if (confirmButtonText != null)
            UIFont.SetText(confirmButtonText, confirmText ?? Loc.Get(defaultConfirmKey));
        if (cancelButtonText != null)
            UIFont.SetText(cancelButtonText, cancelText ?? Loc.Get(defaultCancelKey));
        
        // Show panel
        if (dialogPanel != null)
            dialogPanel.SetActive(true);
        
        // Animate
        if (canvasGroup != null)
            StartCoroutine(FadeIn());
        
        // Play sound
        PlayUIClick();
    }
    
    private void HideInternal()
    {
        if (!_isShowing) return;
        
        _isShowing = false;
        _onConfirm = null;
        _onCancel = null;
        
        if (canvasGroup != null)
            StartCoroutine(FadeOut());
        else if (dialogPanel != null)
            dialogPanel.SetActive(false);
    }
    
    private void OnConfirmClicked()
    {
        PlayUIClick();
        var callback = _onConfirm;
        HideInternal();
        callback?.Invoke();
    }
    
    private void OnCancelClicked()
    {
        PlayUIClick();
        var callback = _onCancel;
        HideInternal();
        callback?.Invoke();
    }
    
    private void PlayUIClick()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayUIClick();
    }

    // =========================================================================
    // ANIMATION
    // =========================================================================
    
    private System.Collections.IEnumerator FadeIn()
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
    
    private System.Collections.IEnumerator FadeOut()
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
        
        if (dialogPanel != null)
            dialogPanel.SetActive(false);
    }
}


