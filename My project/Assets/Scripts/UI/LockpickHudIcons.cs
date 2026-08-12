using UnityEngine;

/// <summary>
/// Deprecated top-left lockpick HUD. Lockpicks now live in the inventory bar.
/// Component kept on _UI (disabled) so missing-script errors are avoided.
/// </summary>
[DisallowMultipleComponent]
public class LockpickHudIcons : MonoBehaviour
{
    public static LockpickHudIcons Instance { get; private set; }

    [SerializeField] Sprite iconSprite;
    [SerializeField] float iconSize = 43f;
    [SerializeField] float spacing = 10f;
    [SerializeField] Vector2 screenPadding = new Vector2(28f, 28f);
    [SerializeField] float popDuration = 0.4f;
    [SerializeField] float popOvershoot = 1.18f;

    void Awake()
    {
        enabled = false;
        if (Instance == null)
            Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public int Count => 0;
    public bool HasAny => false;

    public void AddIcon() { }

    public bool TryConsumeOne() => false;

    public void Clear() { }
}
