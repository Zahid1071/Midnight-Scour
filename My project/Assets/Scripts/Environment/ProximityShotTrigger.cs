using UnityEngine;

/// <summary>
/// Plays a focus shot once, the first time the player comes within range of a point (e.g. the grate with the key under
/// it) while free to move and not chased. Dropped for good once the object it shows is gone (e.g. the key was taken).
/// </summary>
public class ProximityShotTrigger : MonoBehaviour
{
    [SerializeField] HideOptionsShot shot;
    [Tooltip("Range is measured from here, along the floor. Defaults to this object.")]
    [SerializeField] Transform center;
    [SerializeField] float radius = 1f;
    [Tooltip("Optional: once this is destroyed or turned off the shot never plays.")]
    [SerializeField] GameObject requires;

    CinemachineMouseLook _player;
    bool _hasRequirement;
    bool _played;

    void Awake()
    {
        if (center == null)
            center = transform;
        _hasRequirement = requires != null;
    }

    void Update()
    {
        if (_played || shot == null)
            return;
        if (_hasRequirement && (requires == null || !requires.activeInHierarchy))
        {
            _played = true;
            return;
        }
        if (_player == null)
            _player = FindFirstObjectByType<CinemachineMouseLook>();
        if (_player == null)
            return;

        Vector3 offset = _player.transform.position - center.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > radius * radius || PlayerControlGate.IsInputBlocked || !shot.isActiveAndEnabled || Chased())
            return;

        _played = true;
        shot.StartCoroutine(shot.Play());
    }

    static bool Chased()
    {
        foreach (NPCController npc in FindObjectsByType<NPCController>(FindObjectsSortMode.None))
            if (npc != null && (npc.currentState == NPCController.AIState.Chase || npc.currentState == NPCController.AIState.Capture))
                return true;
        return false;
    }
}
