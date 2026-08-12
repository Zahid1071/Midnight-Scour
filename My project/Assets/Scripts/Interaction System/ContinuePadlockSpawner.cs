using Unity.Cinemachine;
using UnityEngine;
using FS_Atmo;

/// <summary>
/// Spawns a fresh PadlockWithChains on each lockpick Continue.
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

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Destroy the previous Continue padlock (if any) and spawn a fresh locked one.</summary>
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
}
