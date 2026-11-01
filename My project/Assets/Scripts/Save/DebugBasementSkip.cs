#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Dev shortcut: Ctrl+Numpad1 loads the game with the basement already done, standing just outside it as the butcher
/// window shot is about to play. Every time the real "left the basement" checkpoint is written (see
/// <see cref="CheckpointManager"/>), a copy is kept as the snapshot; the shortcut puts that copy back as the
/// checkpoint and restores it, from any scene. Play through the basement once to create it.
/// Editor and development builds only.
/// </summary>
public class DebugBasementSkip : MonoBehaviour
{
    const string SnapshotFileName = "debug_basement_done.json";

    static DebugBasementSkip _instance;

    static string SnapshotPath => Path.Combine(Application.persistentDataPath, SnapshotFileName);

    ButcherWindowShot _windowShot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (_instance != null)
            return;
        var go = new GameObject("Debug Basement Skip");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DebugBasementSkip>();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookWindowShot();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnhookWindowShot();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookWindowShot();

    void HookWindowShot()
    {
        UnhookWindowShot();
        _windowShot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
        if (_windowShot != null)
            _windowShot.Triggered += OnLeftBasement;
    }

    void UnhookWindowShot()
    {
        if (_windowShot != null)
            _windowShot.Triggered -= OnLeftBasement;
        _windowShot = null;
    }

    void OnLeftBasement() => StartCoroutine(KeepSnapshot());

    // The checkpoint manager writes its save from the same event; wait a frame so it has, whatever the handler order.
    IEnumerator KeepSnapshot()
    {
        yield return null;
        if (!Checkpoint.Exists)
            yield break;
        try
        {
            File.Copy(Checkpoint.FilePath, SnapshotPath, true);
            Debug.Log($"[DebugBasementSkip] Saved basement-done snapshot to {SnapshotPath}.");
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[DebugBasementSkip] Could not save the snapshot: {e.Message}");
        }
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.ctrlKey.isPressed || !kb.numpad1Key.wasPressedThisFrame)
            return;
        LoadSnapshot();
    }

    static void LoadSnapshot()
    {
        if (!File.Exists(SnapshotPath))
        {
            Debug.LogWarning("[DebugBasementSkip] No basement-done snapshot yet. Play out of the basement once (until the butcher window shot) to create it.");
            return;
        }

        try
        {
            File.Copy(SnapshotPath, Checkpoint.FilePath, true);
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[DebugBasementSkip] Could not restore the snapshot: {e.Message}");
            return;
        }

        string scene = Checkpoint.SavedScene;
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning("[DebugBasementSkip] The snapshot is unreadable or from an older save version. Play out of the basement again to refresh it.");
            return;
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;
        PlayerControlGate.Clear();
        LockpickExtraLife.Clear();
        InventoryManager.ResetForNewGame();
        GameplayHintManager.ResetAllHints();
        CaptureContinue.ClearSpawnCache();
        CaptureContext.Clear();

        Checkpoint.RequestRestore();
        Debug.Log("[DebugBasementSkip] Skipping to the end of the basement.");
        LoadingScreenController.LoadScene(scene);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Midnight Scour/Debug/Delete Basement-Done Snapshot")]
    static void DebugDeleteSnapshot()
    {
        if (File.Exists(SnapshotPath))
            File.Delete(SnapshotPath);
        Debug.Log("[DebugBasementSkip] Snapshot deleted.");
    }
#endif
}
#endif
