/*
 * =============================================================================
 *                      UI BUTTON EFFECTS
 *                    (Professional Hover & Click Effects)
 * =============================================================================
 * 
 * PURPOSE:
 * Adds professional hover, click, and selection effects to UI buttons:
 * - Scale animation on hover
 * - Color tint changes
 * - Sound effects
 * - Smooth transitions
 * 
 * SETUP:
 * ------
 * 1. Attach to any Button GameObject
 * 2. Configure effect settings in inspector
 * 3. Optionally assign a different target transform for scaling
 * 
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Button))]
public class UIButtonEffects : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, 
                                IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    // =========================================================================
    // INSPECTOR SETTINGS
    // =========================================================================
    
    [Header("Scale Effects")]
    [SerializeField] private bool enableScaleEffect = true;
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float scaleSpeed = 10f;
    [SerializeField] private Transform scaleTarget; // If null, uses this transform
    
    [Header("Color Effects")]
    [SerializeField] private bool enableColorEffect = true;
    [SerializeField] private Image targetImage;
    [SerializeField] private TextMeshProUGUI targetText;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = new Color(1f, 0.9f, 0.7f, 1f); // Warm tint
    [SerializeField] private Color clickColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private float colorSpeed = 10f;
    
    [Header("Glow Effect")]
    [SerializeField] private bool enableGlowEffect = false;
    [SerializeField] private Image glowImage;
    [SerializeField] private float glowAlphaNormal = 0f;
    [SerializeField] private float glowAlphaHover = 0.5f;
    
    [Header("Audio")]
    [SerializeField] private bool playHoverSound = true;
    [SerializeField] private bool playClickSound = true;
    [SerializeField] private AudioClip customHoverSound;
    [SerializeField] private AudioClip customClickSound;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    
    private Button _button;
    private Vector3 _originalScale;
    private Vector3 _targetScale;
    private Color _targetColor;
    private float _targetGlowAlpha;
    private bool _isHovered = false;
    private bool _isPressed = false;
    private bool _isSelected = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        _button = GetComponent<Button>();
        
        // Set scale target
        if (scaleTarget == null)
            scaleTarget = transform;
        
        _originalScale = scaleTarget.localScale;
        _targetScale = _originalScale;
        
        // Auto-find target image if not assigned
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        
        // Auto-find text if not assigned
        if (targetText == null)
            targetText = GetComponentInChildren<TextMeshProUGUI>();
        
        // Initialize colors
        _targetColor = normalColor;
        _targetGlowAlpha = glowAlphaNormal;
        
        if (glowImage != null)
        {
            Color c = glowImage.color;
            c.a = glowAlphaNormal;
            glowImage.color = c;
        }
    }
    
    private void Update()
    {
        if (!_button.interactable)
        {
            // Reset to normal when disabled
            _targetScale = _originalScale;
            _targetColor = normalColor;
            _targetGlowAlpha = glowAlphaNormal;
        }
        
        // Smooth scale transition
        if (enableScaleEffect && scaleTarget != null)
        {
            scaleTarget.localScale = Vector3.Lerp(scaleTarget.localScale, _targetScale, Time.unscaledDeltaTime * scaleSpeed);
        }
        
        // Smooth color transition
        if (enableColorEffect)
        {
            if (targetImage != null)
                targetImage.color = Color.Lerp(targetImage.color, _targetColor, Time.unscaledDeltaTime * colorSpeed);
            if (targetText != null)
                targetText.color = Color.Lerp(targetText.color, _targetColor, Time.unscaledDeltaTime * colorSpeed);
        }
        
        // Smooth glow transition
        if (enableGlowEffect && glowImage != null)
        {
            Color c = glowImage.color;
            c.a = Mathf.Lerp(c.a, _targetGlowAlpha, Time.unscaledDeltaTime * colorSpeed);
            glowImage.color = c;
        }
    }

    // =========================================================================
    // POINTER EVENTS
    // =========================================================================
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        
        _isHovered = true;
        UpdateVisualState();
        
        if (playHoverSound)
            PlayHoverSound();
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        _isPressed = false;
        UpdateVisualState();
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_button.interactable) return;
        
        _isPressed = true;
        UpdateVisualState();
        
        if (playClickSound)
            PlayClickSound();
    }
    
    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        UpdateVisualState();
    }
    
    public void OnSelect(BaseEventData eventData)
    {
        if (!_button.interactable) return;
        
        _isSelected = true;
        UpdateVisualState();
        
        if (playHoverSound)
            PlayHoverSound();
    }
    
    public void OnDeselect(BaseEventData eventData)
    {
        _isSelected = false;
        UpdateVisualState();
    }

    // =========================================================================
    // VISUAL STATE
    // =========================================================================
    
    private void UpdateVisualState()
    {
        if (_isPressed)
        {
            _targetScale = _originalScale * clickScale;
            _targetColor = clickColor;
            _targetGlowAlpha = glowAlphaHover;
        }
        else if (_isHovered || _isSelected)
        {
            _targetScale = _originalScale * hoverScale;
            _targetColor = hoverColor;
            _targetGlowAlpha = glowAlphaHover;
        }
        else
        {
            _targetScale = _originalScale;
            _targetColor = normalColor;
            _targetGlowAlpha = glowAlphaNormal;
        }
    }

    // =========================================================================
    // AUDIO
    // =========================================================================
    
    private void PlayHoverSound()
    {
        if (customHoverSound != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUISound(customHoverSound);
        }
        else if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUIHover();
        }
    }
    
    private void PlayClickSound()
    {
        if (customClickSound != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUISound(customClickSound);
        }
        else if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUIClick();
        }
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Forces a visual refresh (useful after enabling/disabling button).
    /// </summary>
    public void RefreshVisuals()
    {
        _isHovered = false;
        _isPressed = false;
        _isSelected = false;
        UpdateVisualState();
    }
}


