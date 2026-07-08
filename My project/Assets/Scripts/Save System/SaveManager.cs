using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton that owns all save / load logic.
/// Persists across scenes via DontDestroyOnLoad.
/// Auto-saves on application quit and scene unload when
/// <see cref="GameSettingsManager.IsAutoSavePersistenceEnabled"/> is true.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    private const string FileName = "savegame.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    // Tag used to locate the player in any scene
    [SerializeField] private string playerTag = "Player";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    // ───── Auto-save hooks ─────

    void OnApplicationQuit()
    {
        if (!ShouldRunAutoSaveHooks()) return;
        if (!IsMainMenu())
            SaveGame();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (!ShouldRunAutoSaveHooks()) return;

        // Don't save when leaving the main menu or credits
        if (scene.name == "Main Menu" || scene.name == "CreditsScene")
            return;

        SaveGame();
    }

    private static bool ShouldRunAutoSaveHooks() =>
        GameSettingsManager.IsAutoSavePersistenceEnabled();

    // ───── Public API ─────

    /// <summary>Save current game state to disk.</summary>
    public void SaveGame()
    {
        SaveData data = GatherData();
        if (data == null) return;

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(FilePath, json);
        Debug.Log($"[SaveManager] Game saved → {FilePath}");
    }

    /// <summary>
    /// Load saved data, switch to the saved scene, then apply position/rotation.
    /// Call from the main menu or wherever you need full scene reload.
    /// </summary>
    public void LoadGame()
    {
        if (!HasSave()) return;

        SaveData data = ReadFromDisk();

        SceneManager.sceneLoaded += ApplyAfterLoad;
        // Async load behind the loading screen (also warms up shaders there)
        // instead of an instant load that stalls on first-time shader compilation.
        LoadingScreenController.LoadScene(data.sceneName);

        void ApplyAfterLoad(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= ApplyAfterLoad;
            ApplyData(data);
        }
    }

    /// <returns>True when a save file exists on disk.</returns>
    public bool HasSave() => File.Exists(FilePath);

    /// <summary>Delete the save file (used by New Game to start fresh).</summary>
    public void DeleteSave()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
            Debug.Log("[SaveManager] Save file deleted.");
        }
    }

    // ───── Data gathering ─────

    private SaveData GatherData()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning("[SaveManager] No player found — skipping save.");
            return null;
        }

        SaveData data = new()
        {
            sceneName = SceneManager.GetActiveScene().name,
            score = 0 // replace with your own score variable when ready
        };

        data.SetPosition(player.transform.position);
        data.SetRotation(player.transform.rotation);

        // Persist inventory item names (if InventoryManager exists)
        if (InventoryManager.Instance != null)
        {
            data.inventory = new List<string>();
            foreach (var item in InventoryManager.Instance.GetInventory())
                data.inventory.Add(item.itemName);
        }

        return data;
    }

    // ───── Data application ─────

    private void ApplyData(SaveData data)
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning("[SaveManager] No player found after scene load.");
            return;
        }

        // Disable CharacterController temporarily so we can teleport
        var cc = player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;

        player.transform.position = data.GetPosition();
        player.transform.rotation = data.GetRotation();

        if (cc) cc.enabled = true;

        Debug.Log($"[SaveManager] Game loaded — scene: {data.sceneName}");
    }

    // ───── Disk I/O ─────

    private SaveData ReadFromDisk()
    {
        string json = File.ReadAllText(FilePath);
        return JsonUtility.FromJson<SaveData>(json);
    }

    // ───── Utilities ─────

    private bool IsMainMenu()
    {
        string scene = SceneManager.GetActiveScene().name;
        return scene == "Main Menu" || scene == "CreditsScene";
    }
}
