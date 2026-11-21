using StarterAssets;
using UnityEngine;

/// <summary>
/// One-time tutorial popup the first time the player comes within range of the farmhouse,
/// teaching that open windows can be climbed through.
/// </summary>
[DisallowMultipleComponent]
public class FarmhouseWindowClimbTutorial : MonoBehaviour
{
    const string DefaultPopupId = "Farmhouse Windows";

    [Tooltip("Range is measured from here along the floor. Defaults to this object.")]
    [SerializeField] Transform center;
    [SerializeField] float radius = 30f;
    [SerializeField] string popupId = DefaultPopupId;
    [SerializeField] string titleKey = "tutorial.window_climb.title";
    [SerializeField] string bodyKey = "tutorial.window_climb.body";

    FirstPersonController _player;
    bool _played;

    void Awake()
    {
        if (center == null)
            center = transform;
    }

    void Update()
    {
        if (_played)
            return;

        if (_player == null)
            _player = FindFirstObjectByType<FirstPersonController>(FindObjectsInactive.Include);
        if (_player == null || center == null)
            return;

        Vector3 offset = _player.transform.position - center.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > radius * radius || PlayerControlGate.IsInputBlocked || Chased())
            return;

        // ShowOnce no-ops (and returns false) if this id was already seen / restored from a checkpoint.
        _played = true;
        TutorialPopupUI.ShowOnce(popupId, titleKey, bodyKey);
    }

    static bool Chased()
    {
        foreach (NPCController npc in FindObjectsByType<NPCController>(FindObjectsSortMode.None))
        {
            if (npc != null && (npc.currentState == NPCController.AIState.Chase
                                || npc.currentState == NPCController.AIState.Capture))
                return true;
        }
        return false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 c = (center != null ? center : transform).position;
        c.y = transform.position.y;
        Gizmos.color = new Color(0.4f, 0.75f, 1f, 0.35f);
        Gizmos.DrawWireSphere(c, radius);
    }
#endif
}
