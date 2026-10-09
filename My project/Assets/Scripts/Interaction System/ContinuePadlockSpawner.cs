using Unity.Cinemachine;
using UnityEngine;
using FS_Atmo;

/// <summary>
/// Chains the cage door with a fresh PadlockWithChains at scene start and on each lockpick Continue.
/// Assign prefab, spawn pose, door, and lockpick camera in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class ContinuePadlockSpawner : MonoBehaviour
{
    public static ContinuePadlockSpawner Instance { get; private set; }

    [Header("Prefab")]
    [SerializeField] GameObject padlockPrefab;

    [Header("Placement")]
    [Tooltip("Defaults to this transform.")]
    [SerializeField] Transform spawnPoint;

    [Header("Wiring (applied to each spawn)")]
    [SerializeField] SimpleOpenClose doorToUnlock;
    [SerializeField] CinemachineCamera lockpickCamera;

    [Header("Start")]
    [Tooltip("Chain the cage door with a padlock as soon as the scene starts (New Game / Restart).")]
    [SerializeField] bool lockOnStart = true;

    GameObject _liveInstance;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        if (!lockOnStart || _liveInstance != null)
            return;

        SpawnFresh();
        ChainCageDoor(closeInstantly: false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Destroy the previous padlock (if any) and spawn a fresh locked one.</summary>
    public void SpawnFresh()
    {
        if (padlockPrefab == null)
        {
            Debug.LogError("[ContinuePadlockSpawner] Assign PadlockWithChains prefab.", this);
            return;
        }

        if (_liveInstance != null)
        {
            Destroy(_liveInstance);
            _liveInstance = null;
        }

        Transform anchor = spawnPoint != null ? spawnPoint : transform;
        _liveInstance = Instantiate(padlockPrefab, anchor.position, anchor.rotation);
        _liveInstance.name = padlockPrefab.name;

        var minigame = _liveInstance.GetComponentInChildren<PadlockLockpickMinigame>(true);
        if (minigame == null)
        {
            Debug.LogError("[ContinuePadlockSpawner] Prefab has no PadlockLockpickMinigame.", this);
            return;
        }

        minigame.ConfigureForContinue(doorToUnlock, lockpickCamera);
    }

    /// <summary>
    /// Lock the cage door so only the padlock opens it (screwdriver break and other keys are refused).
    /// </summary>
    public static void ChainCageDoor(bool closeInstantly)
    {
        var cage = FindFirstObjectByType<Cage>();
        if (cage == null || cage.cageDoor == null)
        {
            Debug.LogWarning("[ContinuePadlockSpawner] Cage / cageDoor missing — chained lock not applied.");
            return;
        }

        var doorController = cage.cageDoor.GetComponentInParent<DoorController>();
        if (closeInstantly)
        {
            if (doorController != null)
                doorController.ForceCloseInstant();
            else
                cage.cageDoor.ForceCloseInstant();
        }

        if (doorController != null)
            doorController.SetLocked(true);

        cage.cageDoor.ApplyContinueChainedLock("gameplay.door_chained");
    }
}
