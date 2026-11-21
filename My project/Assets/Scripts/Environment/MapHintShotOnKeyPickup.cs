using System.Collections;
using UnityEngine;

/// <summary>
/// When the basement key is picked up and the farm map has not been collected yet, plays a
/// <see cref="HideOptionsShot"/> that frames the map paper (same focus-zoom style as the grate key shot).
/// </summary>
[DisallowMultipleComponent]
public class MapHintShotOnKeyPickup : MonoBehaviour
{
    [SerializeField] InventoryItem basementKey;
    [SerializeField] HideOptionsShot shot;
    [SerializeField] FarmMapPickup mapPickup;
    [Tooltip("Brief pause after the pickup subtitle/SFX before the zoom starts.")]
    [SerializeField] float startDelay = 0.35f;

    bool _played;
    bool _hadKey;

    void Awake()
    {
        if (shot == null)
            shot = GetComponent<HideOptionsShot>();
        if (mapPickup == null)
            mapPickup = FindFirstObjectByType<FarmMapPickup>(FindObjectsInactive.Include);
        if (basementKey == null)
        {
            foreach (var item in FindObjectsByType<InteractableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (item == null || item.itemData == null)
                    continue;
                if (item.itemData.itemName != null
                    && item.itemData.itemName.IndexOf("Basement Key", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    basementKey = item.itemData;
                    break;
                }
            }
        }

        _hadKey = InventoryManager.Instance != null && basementKey != null
                  && InventoryManager.Instance.HasItem(basementKey);
    }

    void OnEnable() => InventoryManager.OnInventoryChanged += OnInventoryChanged;

    void OnDisable() => InventoryManager.OnInventoryChanged -= OnInventoryChanged;

    void OnInventoryChanged()
    {
        if (_played || basementKey == null || shot == null)
            return;
        if (Checkpoint.IsRestoring)
            return;

        bool hasKey = InventoryManager.Instance != null && InventoryManager.Instance.HasItem(basementKey);
        bool justPicked = hasKey && !_hadKey;
        _hadKey = hasKey;
        if (!justPicked)
            return;

        if (FarmMapView.Instance != null && FarmMapView.Instance.HasMap)
            return;
        if (mapPickup == null || !mapPickup.isActiveAndEnabled)
            return;

        _played = true;
        StartCoroutine(PlayAfterDelay());
    }

    IEnumerator PlayAfterDelay()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        if (FarmMapView.Instance != null && FarmMapView.Instance.HasMap)
            yield break;
        if (mapPickup == null || !mapPickup.isActiveAndEnabled || shot == null)
            yield break;

        if (PlayerControlGate.IsInputBlocked)
        {
            float wait = 2f;
            while (wait > 0f && PlayerControlGate.IsInputBlocked)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            if (PlayerControlGate.IsInputBlocked)
                yield break;
        }

        yield return shot.Play();
    }
}
