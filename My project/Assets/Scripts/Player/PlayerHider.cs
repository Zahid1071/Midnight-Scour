using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHider : MonoBehaviour
{
    public bool isHidden { get; private set; }
    public HidingSpot CurrentHidingSpot => currentHidingSpot;

    private HidingSpot currentHidingSpot;
    
    [Header("Visuals")]
    public GameObject visualRoot; 
    
    [Header("Components to Disable")]
    public MonoBehaviour[] scriptsToDisable;

    private void Update()
    {
        if (!isHidden || currentHidingSpot == null || PlayerControlGate.IsInputBlocked)
            return;

        if (Keyboard.current == null)
            return;

        bool exitHide = Keyboard.current.eKey.wasPressedThisFrame
            || Keyboard.current.spaceKey.wasPressedThisFrame;
        if (exitHide)
            currentHidingSpot.RevealPlayer(gameObject);
    }

    public bool IsHidingIn(HidingSpot spot) => isHidden && currentHidingSpot == spot;

    // Sadece Input/Hareket scriptlerini kapatır
    public void DisableControlsOnly()
    {
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = false;
    }

    // Sadece Input/Hareket scriptlerini açar
    public void EnableControls()
    {
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = true;
    }

    // Görselliği aç/kapat
    public void SetVisuals(bool isActive)
    {
        if (visualRoot != null) visualRoot.SetActive(isActive);
    }

    // Durum güncellemesi
    public void SetHiddenState(bool hidden, HidingSpot spot)
    {
        isHidden = hidden;
        currentHidingSpot = spot;
    }
}