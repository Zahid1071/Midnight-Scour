using UnityEngine;

/// <summary>
/// Static candle lit by holding a burning lighter and pressing E on it. The flame object (lights + flame
/// particles, same rig as the world lighter) stays on once lit; the candle then stops being interactable.
/// </summary>
[DisallowMultipleComponent]
public class LightableCandle : MonoBehaviour, IInteractable, IItemReceiver
{
    [SerializeField] GameObject flame;
    [SerializeField] bool litOnStart;

    [Header("Feedback")]
    [SerializeField] AudioClip igniteSfx;
    [SerializeField, Range(0f, 1f)] float igniteVolume = 0.6f;
    [SerializeField] string needFlameKey = "gameplay.candle_need_flame";
    [SerializeField] string lighterNotLitKey = "gameplay.candle_light_lighter";

    public bool IsLit { get; private set; }

    void Awake()
    {
        SetLit(litOnStart);
    }

    public bool Interact() => TryLight();

    public bool ReceiveItem(InventoryItem item) => TryLight();

    bool TryLight()
    {
        if (IsLit)
            return false;

        HeldItemView view = HeldItemController.Instance != null ? HeldItemController.Instance.CurrentView : null;
        HeldLighter lighter = view != null ? view.GetComponent<HeldLighter>() : null;
        if (lighter == null)
        {
            InteractionFeedback.Fail(Loc.Get(needFlameKey), 2.5f);
            return false;
        }

        if (!lighter.IsLit)
        {
            InteractionFeedback.Fail(Loc.Get(lighterNotLitKey), 2.5f);
            return false;
        }

        SetLit(true);
        if (igniteSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(igniteSfx, flame != null ? flame.transform.position : transform.position, igniteVolume);
        return true;
    }

    public void SetLit(bool lit)
    {
        IsLit = lit;
        if (flame != null)
            flame.SetActive(lit);

        // A lit candle has nothing left to do; drop it from the interact raycast so the prompt goes away.
        foreach (var col in GetComponents<Collider>())
            col.enabled = !lit;
    }
}
