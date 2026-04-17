/*
 * =============================================================================
 *                          SETTINGS SPINNER
 *                    (Resident Evil Style Selector)
 * =============================================================================
 * 
 * PURPOSE:
 * A spinner-style UI component for selecting options with left/right arrows.
 * Replaces dropdowns with a cleaner, more game-like interface.
 * 
 * VISUAL LAYOUT:
 * ┌────────────────────────────────────────┐
 * │   <   │      Current Value      │   >  │
 * └────────────────────────────────────────┘
 * 
 * USAGE:
 * ------
 * 1. Create UI structure: Parent -> LeftButton, ValueText, RightButton
 * 2. Add this script to the parent
 * 3. Assign references in inspector
 * 4. Set options via SetOptions() or inspector
 * 
 * =============================================================================
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

public class SettingsSpinner : MonoBehaviour
{
    // =========================================================================
    // EVENTS
    // =========================================================================
    
    /// <summary>
    /// Called when the selected index changes.
    /// </summary>
    [Serializable]
    public class ValueChangedEvent : UnityEvent<int> { }
    
    public ValueChangedEvent OnValueChanged;

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("UI References")]
    [Tooltip("Button to go to previous option")]
    [SerializeField] private Button leftButton;
    
    [Tooltip("Button to go to next option")]
    [SerializeField] private Button rightButton;
    
    [Tooltip("Text displaying the current value")]
    [SerializeField] private TextMeshProUGUI valueText;
    
    [Header("Options")]
    [Tooltip("List of available options")]
    [SerializeField] private List<string> options = new List<string>();
    
    [Tooltip("Currently selected index")]
    [SerializeField] private int currentIndex = 0;
    
    [Header("Behavior")]
    [Tooltip("If true, wraps around from last to first and vice versa")]
    [SerializeField] private bool wrapAround = true;
    
    [Tooltip("If true, disables buttons at min/max when not wrapping")]
    [SerializeField] private bool disableButtonsAtLimits = true;
    
    [Header("Audio")]
    [Tooltip("Play click sound on value change")]
    [SerializeField] private bool playSound = true;

    // =========================================================================
    // PROPERTIES
    // =========================================================================
    
    /// <summary>
    /// Gets the currently selected index.
    /// </summary>
    public int Value => currentIndex;
    
    /// <summary>
    /// Gets the currently selected option text.
    /// </summary>
    public string CurrentOption => (options != null && options.Count > 0 && currentIndex >= 0 && currentIndex < options.Count) 
        ? options[currentIndex] 
        : "";
    
    /// <summary>
    /// Gets the number of options.
    /// </summary>
    public int OptionCount => options?.Count ?? 0;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        SetupButtons();
    }
    
    private void OnEnable()
    {
        RefreshDisplay();
    }
    
    private void SetupButtons()
    {
        if (leftButton != null)
        {
            leftButton.onClick.RemoveAllListeners();
            leftButton.onClick.AddListener(OnLeftClicked);
        }
        
        if (rightButton != null)
        {
            rightButton.onClick.RemoveAllListeners();
            rightButton.onClick.AddListener(OnRightClicked);
        }
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Sets the list of options and optionally the initial index.
    /// </summary>
    public void SetOptions(List<string> newOptions, int initialIndex = 0)
    {
        options = newOptions ?? new List<string>();
        currentIndex = Mathf.Clamp(initialIndex, 0, Mathf.Max(0, options.Count - 1));
        RefreshDisplay();
    }
    
    /// <summary>
    /// Sets the list of options from a string array.
    /// </summary>
    public void SetOptions(string[] newOptions, int initialIndex = 0)
    {
        SetOptions(new List<string>(newOptions), initialIndex);
    }
    
    /// <summary>
    /// Adds a single option to the list.
    /// </summary>
    public void AddOption(string option)
    {
        if (options == null)
            options = new List<string>();
        options.Add(option);
        RefreshDisplay();
    }
    
    /// <summary>
    /// Clears all options.
    /// </summary>
    public void ClearOptions()
    {
        options?.Clear();
        currentIndex = 0;
        RefreshDisplay();
    }
    
    /// <summary>
    /// Sets the current value/index without triggering OnValueChanged.
    /// </summary>
    public void SetValueWithoutNotify(int index)
    {
        if (options == null || options.Count == 0)
        {
            currentIndex = 0;
        }
        else
        {
            currentIndex = Mathf.Clamp(index, 0, options.Count - 1);
        }
        RefreshDisplay();
    }
    
    /// <summary>
    /// Sets the current value/index and triggers OnValueChanged.
    /// </summary>
    public void SetValue(int index)
    {
        if (options == null || options.Count == 0)
        {
            currentIndex = 0;
            RefreshDisplay();
            return;
        }
        
        int newIndex = Mathf.Clamp(index, 0, options.Count - 1);
        if (newIndex != currentIndex)
        {
            currentIndex = newIndex;
            RefreshDisplay();
            OnValueChanged?.Invoke(currentIndex);
        }
    }
    
    /// <summary>
    /// Goes to next option.
    /// </summary>
    public void Next()
    {
        if (options == null || options.Count <= 1) return;
        
        int newIndex = currentIndex + 1;
        
        if (wrapAround)
        {
            newIndex = newIndex % options.Count;
        }
        else
        {
            newIndex = Mathf.Min(newIndex, options.Count - 1);
        }
        
        if (newIndex != currentIndex)
        {
            currentIndex = newIndex;
            RefreshDisplay();
            PlayClickSound();
            OnValueChanged?.Invoke(currentIndex);
        }
    }
    
    /// <summary>
    /// Goes to previous option.
    /// </summary>
    public void Previous()
    {
        if (options == null || options.Count <= 1) return;
        
        int newIndex = currentIndex - 1;
        
        if (wrapAround)
        {
            if (newIndex < 0)
                newIndex = options.Count - 1;
        }
        else
        {
            newIndex = Mathf.Max(newIndex, 0);
        }
        
        if (newIndex != currentIndex)
        {
            currentIndex = newIndex;
            RefreshDisplay();
            PlayClickSound();
            OnValueChanged?.Invoke(currentIndex);
        }
    }

    // =========================================================================
    // PRIVATE METHODS
    // =========================================================================
    
    private void OnLeftClicked()
    {
        Previous();
    }
    
    private void OnRightClicked()
    {
        Next();
    }
    
    private void RefreshDisplay()
    {
        // Update value text
        if (valueText != null)
        {
            if (options != null && options.Count > 0 && currentIndex >= 0 && currentIndex < options.Count)
            {
                valueText.text = options[currentIndex];
            }
            else
            {
                valueText.text = "-";
            }
        }
        
        // Update button states
        if (!wrapAround && disableButtonsAtLimits)
        {
            if (leftButton != null)
                leftButton.interactable = currentIndex > 0;
            
            if (rightButton != null)
                rightButton.interactable = options != null && currentIndex < options.Count - 1;
        }
        else
        {
            if (leftButton != null)
                leftButton.interactable = options != null && options.Count > 1;
            
            if (rightButton != null)
                rightButton.interactable = options != null && options.Count > 1;
        }
    }
    
    private void PlayClickSound()
    {
        if (!playSound) return;
        
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUIClick();
        }
    }

    // =========================================================================
    // EDITOR VALIDATION
    // =========================================================================
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (options != null && options.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, options.Count - 1);
        }
        else
        {
            currentIndex = 0;
        }
    }
#endif
}

