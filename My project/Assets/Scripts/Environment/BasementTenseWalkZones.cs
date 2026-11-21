using FS_Atmo;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Turns on <see cref="FirstPersonController.TensionWalk"/> for:
/// 1. Opening — from start until the first cage-room corridor door is broken open.
/// 2. Return for the bar — after the note arms the wall stander, once the player crosses
///    Door Cage Room from the basement-main prepoint into the cage-room prepoint, until the bar is taken.
/// 3. Small crawl-scare house — while inside the house zone, until the crawler is triggered.
/// Everywhere else uses normal <see cref="FirstPersonController.MoveSpeed"/>.
/// </summary>
[DisallowMultipleComponent]
public class BasementTenseWalkZones : MonoBehaviour
{
    [Tooltip("First screwdriver door out of the cage room. Tension walk stays on until this lock is broken.")]
    [SerializeField] SimpleOpenClose firstDoor;
    [Tooltip("Basement-main side prepoint on Door Cage Room (RoomGraph / DoorController PrePoint).")]
    [SerializeField] Transform returnDoorBasementPrePoint;
    [Tooltip("Cage-room side prepoint on Door Cage Room.")]
    [SerializeField] Transform returnDoorCagePrePoint;
    [SerializeField] CorpseVanishEvent corpseEvent;
    [SerializeField] CorpseCrawlScare crawlScare;
    [SerializeField] CorridorTension corridorTension;

    FirstPersonController _fpc;
    ScrewdriverLockBreakMinigame _firstDoorBreak;
    bool _applied;
    bool _returnTensionLatched;
    bool _seenBasementSideOfReturnDoor;
    bool _firstDoorBroken;

    void Awake()
    {
        if (corpseEvent == null)
            corpseEvent = FindFirstObjectByType<CorpseVanishEvent>(FindObjectsInactive.Include);
        if (crawlScare == null)
            crawlScare = FindFirstObjectByType<CorpseCrawlScare>(FindObjectsInactive.Include);
        if (corridorTension == null)
            corridorTension = FindFirstObjectByType<CorridorTension>(FindObjectsInactive.Include);
        ResolveFirstDoor();
        ResolveReturnDoorPrePoints();
        HookFirstDoor();
        RefreshFirstDoorBroken();
    }

    void OnEnable() => HookFirstDoor();

    void OnDisable()
    {
        UnhookFirstDoor();
        ClearTension();
    }

    void Update()
    {
        if (_fpc == null)
        {
            _fpc = FindFirstObjectByType<FirstPersonController>(FindObjectsInactive.Include);
            if (_fpc == null)
                return;
        }

        RefreshFirstDoorBroken();

        Vector3 pos = _fpc.transform.position;
        bool tense = WantOpeningTension() || WantReturnBarTension(pos) || WantCrawlHouseTension(pos);
        if (tense)
        {
            _fpc.TensionWalk = true;
            _applied = true;
        }
        else if (_applied)
        {
            ClearTension();
        }
    }

    bool WantOpeningTension() => !_firstDoorBroken;

    bool WantReturnBarTension(Vector3 pos)
    {
        if (corpseEvent == null || corpseEvent.BarTaken || !corpseEvent.ReturnArmed)
        {
            _returnTensionLatched = false;
            _seenBasementSideOfReturnDoor = false;
            return false;
        }

        // Checkpoint mid-return: reveal already played, still need the bar.
        if (!_returnTensionLatched && corpseEvent.WallRevealPlayed && !corpseEvent.BarTaken)
            _returnTensionLatched = true;

        if (!_returnTensionLatched && CrossedReturnDoorIntoCage(pos))
            _returnTensionLatched = true;

        return _returnTensionLatched;
    }

    /// <summary>
    /// True once the player has been on the basement-main prepoint side after the note, then steps onto the
    /// cage-room prepoint side of Door Cage Room.
    /// </summary>
    bool CrossedReturnDoorIntoCage(Vector3 pos)
    {
        if (returnDoorBasementPrePoint == null || returnDoorCagePrePoint == null)
            ResolveReturnDoorPrePoints();
        if (returnDoorBasementPrePoint == null || returnDoorCagePrePoint == null)
            return false;

        Vector3 basement = returnDoorBasementPrePoint.position;
        Vector3 cage = returnDoorCagePrePoint.position;
        Vector3 mid = (basement + cage) * 0.5f;
        Vector3 through = cage - basement;
        through.y = 0f;
        if (through.sqrMagnitude < 0.0001f)
            return false;
        through.Normalize();

        Vector3 offset = pos - mid;
        offset.y = 0f;
        float side = Vector3.Dot(offset, through);

        // Negative = basement-main side, positive = cage-room side.
        if (side < 0f)
            _seenBasementSideOfReturnDoor = true;

        return _seenBasementSideOfReturnDoor && side >= 0f;
    }

    bool WantCrawlHouseTension(Vector3 pos)
    {
        if (crawlScare == null || crawlScare.Started)
            return false;
        return crawlScare.IsPlayerInsideHouse(pos);
    }

    void ResolveFirstDoor()
    {
        if (firstDoor != null)
            return;

        foreach (var door in FindObjectsByType<SimpleOpenClose>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (door == null)
                continue;
            Transform t = door.transform;
            if (t.name != "Door_mesh_A_L")
                continue;
            Transform parent = t.parent;
            if (parent == null || parent.name != "Door Cage Room")
                continue;
            firstDoor = door;
            return;
        }
    }

    void ResolveReturnDoorPrePoints()
    {
        if (returnDoorBasementPrePoint != null && returnDoorCagePrePoint != null)
            return;

        // Prefer the room-graph edge Basement_Main_Room ↔ Basement_Cage_Room (preA = main, preB = cage).
        RoomGraph graph = FindFirstObjectByType<RoomGraph>(FindObjectsInactive.Include);
        if (graph != null && graph.edges != null)
        {
            foreach (DoorEdge edge in graph.edges)
            {
                if (edge == null || edge.roomA == null || edge.roomB == null
                    || edge.prePointA == null || edge.prePointB == null)
                    continue;

                bool aMain = edge.roomA.name == "Basement_Main_Room";
                bool bCage = edge.roomB.name == "Basement_Cage_Room";
                bool aCage = edge.roomA.name == "Basement_Cage_Room";
                bool bMain = edge.roomB.name == "Basement_Main_Room";
                if (aMain && bCage)
                {
                    returnDoorBasementPrePoint = edge.prePointA;
                    returnDoorCagePrePoint = edge.prePointB;
                    return;
                }
                if (aCage && bMain)
                {
                    returnDoorBasementPrePoint = edge.prePointB;
                    returnDoorCagePrePoint = edge.prePointA;
                    return;
                }
            }
        }

        // Fallback: Door Cage Room children (PrePoint B = basement, PrePoint A = cage).
        Transform doorRoot = GameObject.Find("Door Cage Room")?.transform;
        if (doorRoot == null)
            return;
        Transform preA = doorRoot.Find("PrePoint A");
        Transform preB = doorRoot.Find("PrePoint B");
        if (preA != null && preB != null)
        {
            returnDoorBasementPrePoint = preB;
            returnDoorCagePrePoint = preA;
        }
    }

    void HookFirstDoor()
    {
        UnhookFirstDoor();
        if (firstDoor == null)
            return;

        _firstDoorBreak = firstDoor.GetComponent<ScrewdriverLockBreakMinigame>()
                          ?? firstDoor.GetComponentInChildren<ScrewdriverLockBreakMinigame>(true);
        if (_firstDoorBreak != null)
            _firstDoorBreak.BreakSucceeded += OnFirstDoorBroken;
    }

    void UnhookFirstDoor()
    {
        if (_firstDoorBreak != null)
            _firstDoorBreak.BreakSucceeded -= OnFirstDoorBroken;
        _firstDoorBreak = null;
    }

    void OnFirstDoorBroken() => _firstDoorBroken = true;

    void RefreshFirstDoorBroken()
    {
        if (_firstDoorBroken)
            return;

        if (firstDoor != null && !firstDoor.isLocked)
        {
            _firstDoorBroken = true;
            return;
        }

        // Checkpoint / skip past the opening beat.
        if (corridorTension != null && corridorTension.HasLeftCorridor)
            _firstDoorBroken = true;
    }

    void ClearTension()
    {
        if (_fpc != null && _applied)
            _fpc.TensionWalk = false;
        _applied = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (returnDoorBasementPrePoint == null || returnDoorCagePrePoint == null)
            return;
        Gizmos.color = new Color(0.85f, 0.55f, 0.2f, 0.9f);
        Gizmos.DrawSphere(returnDoorBasementPrePoint.position, 0.12f);
        Gizmos.DrawSphere(returnDoorCagePrePoint.position, 0.12f);
        Gizmos.DrawLine(returnDoorBasementPrePoint.position, returnDoorCagePrePoint.position);
    }
#endif
}
