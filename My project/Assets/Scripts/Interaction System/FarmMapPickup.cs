using UnityEngine;

/// <summary>The hand-drawn farm map pinned in the cellar. Taking it opens the map; M brings it back up later.</summary>
[RequireComponent(typeof(Collider))]
public class FarmMapPickup : MonoBehaviour, IInteractable
{
    [SerializeField] AudioClip pickupSFX;
    [Tooltip("Subtitle shown when the player first puts the map away.")]
    [SerializeField] string closedSubtitleKey = "map.pickup";

    public bool Interact()
    {
        var view = FarmMapView.Instance;
        if (view == null)
        {
            Debug.LogWarning("[FarmMapPickup] FarmMapView not found in scene.");
            return false;
        }
        if (FarmMapView.IsBlockingPlayerInput || ReadPanelUI.IsBlockingPlayerInput)
            return false;

        InteractionFeedback.PlaySound(pickupSFX, transform.position);
        gameObject.SetActive(false);
        view.Collect(closedSubtitleKey);
        return true;
    }
}
