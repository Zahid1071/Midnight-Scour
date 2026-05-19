using UnityEngine;

public class GrateSystem : MonoBehaviour, IInteractable
{
    const int DefaultLayer = 0;

    [SerializeField] GrateBolt[] bolts;
    [SerializeField] Animator animator;
    [SerializeField] string removeTrigger = "Remove";
    [Tooltip("Object whose layer is toggled (grate mesh/collider). Defaults to this GameObject. Does not change child bolt layers.")]
    [SerializeField] GameObject layerTarget;

    static int _interactableLayer = -1;
    static int InteractableLayer
    {
        get
        {
            if (_interactableLayer < 0)
            {
                _interactableLayer = LayerMask.NameToLayer("Interactable ");
                if (_interactableLayer < 0)
                    _interactableLayer = 6;
            }
            return _interactableLayer;
        }
    }

    int _removedCount;
    bool _isRemoved;

    public bool AllBoltsRemoved => bolts != null && bolts.Length > 0 && _removedCount >= bolts.Length;
    public bool IsRemoved => _isRemoved;

    void Awake()
    {
        if (layerTarget == null)
            layerTarget = gameObject;
        SetGrateLayer(DefaultLayer);
    }

    public void NotifyBoltRemoved()
    {
        if (_isRemoved)
            return;
        _removedCount++;
        if (AllBoltsRemoved)
            SetGrateLayer(InteractableLayer);
    }

    void SetGrateLayer(int layer)
    {
        if (layerTarget != null)
            layerTarget.layer = layer;
    }

    public void Interact()
    {
        if (_isRemoved)
            return;

        if (!AllBoltsRemoved)
        {
            HudManager.Instance?.ShowSubtitle("The grate is still bolted down.", 3f);
            return;
        }

        if (animator != null && !string.IsNullOrEmpty(removeTrigger))
            animator.SetTrigger(removeTrigger);
            
    }

    /// <summary>Call from removal animation event when the grate is fully open.</summary>
    public void OnRemovalComplete()
    {
        _isRemoved = true;
    }
}
