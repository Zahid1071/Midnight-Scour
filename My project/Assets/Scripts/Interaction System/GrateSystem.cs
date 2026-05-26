using UnityEngine;

public class GrateSystem : MonoBehaviour, IInteractable
{
    const int DefaultLayer = 0;

    [SerializeField] GrateBolt[] bolts;
    [SerializeField] Animator animator;
    [SerializeField] string removeTrigger = "Remove";
    [Tooltip("Object whose layer is toggled (grate mesh/collider). Defaults to this GameObject. Does not change child bolt layers.")]
    [SerializeField] GameObject layerTarget;

    [Header("Audio")]
    [SerializeField] AudioClip boltRemoveSound;

    static int _doorLayer = -1;
    static int DoorLayer
    {
        get
        {
            if (_doorLayer < 0)
            {
                _doorLayer = LayerMask.NameToLayer("Door");
                if (_doorLayer < 0)
                    _doorLayer = 9;
            }
            return _doorLayer;
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

    public void NotifyBoltRemoved(Vector3 boltWorldPosition)
    {
        if (_isRemoved)
            return;

        if (boltRemoveSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(boltRemoveSound, boltWorldPosition);

        _removedCount++;
        if (AllBoltsRemoved)
            SetGrateLayer(DoorLayer);
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
            InteractionFeedback.Fail("The grate is still bolted down.", 3f);
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
