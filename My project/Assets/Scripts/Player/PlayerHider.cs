using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHider : MonoBehaviour
{
    public bool isHidden { get; private set; }
    private HidingSpot currentHidingSpot;
    
    [Header("Visuals")]
    public GameObject visualRoot; 
    
    [Header("Components to Disable")]
    public MonoBehaviour[] scriptsToDisable;

    private void Update()
    {
        // Space tuşu ile çıkış
        if (isHidden && currentHidingSpot != null)
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                currentHidingSpot.RevealPlayer(gameObject);
                // currentHidingSpot = null; // Bunu RevealPlayer içindeki mantık halledecek
            }
        }
    }

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