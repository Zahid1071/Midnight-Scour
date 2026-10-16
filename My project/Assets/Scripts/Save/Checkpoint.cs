using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The single auto-save slot. Written when a checkpoint is reached (see <see cref="CheckpointManager"/>), kept on disk
/// so Continue on the main menu picks the run back up, and loaded again after every capture once it exists.
/// Starting the gameplay scene without a pending restore (New Game, Restart before any checkpoint) deletes it.
/// </summary>
public static class Checkpoint
{
    public const int Version = 2;
    const string FileName = "checkpoint.json";

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>The next gameplay scene load restores the saved checkpoint instead of starting fresh.</summary>
    public static bool RestorePending { get; private set; }

    /// <summary>A readable save of the current version is on disk.</summary>
    public static bool Exists => TryRead(out _);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => RestorePending = false;

    public static void RequestRestore() => RestorePending = Exists;

    public static void CancelRestore() => RestorePending = false;

    /// <summary>Scene the checkpoint was saved in, or null without a readable save.</summary>
    public static string SavedScene => TryRead(out CheckpointData data) ? data.scene : null;

    public static bool Write(CheckpointData data)
    {
        if (data == null)
            return false;
        data.version = Version;
        data.savedAtUtc = DateTime.UtcNow.ToString("o");
        string temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(temp, FilePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Checkpoint] Could not write {FilePath}: {e.Message}");
            return false;
        }
    }

    public static bool TryRead(out CheckpointData data)
    {
        data = null;
        if (!File.Exists(FilePath))
            return false;
        try
        {
            data = JsonUtility.FromJson<CheckpointData>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Checkpoint] Could not read {FilePath}: {e.Message}");
            data = null;
        }
        if (data != null && data.version == Version)
            return true;
        data = null;
        return false;
    }

    public static void Delete()
    {
        RestorePending = false;
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Checkpoint] Could not delete {FilePath}: {e.Message}");
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Midnight Scour/Debug/Delete Checkpoint")]
    static void DebugDelete()
    {
        Delete();
        Debug.Log("[Checkpoint] Deleted.");
    }

    [UnityEditor.MenuItem("Midnight Scour/Debug/Reveal Checkpoint File")]
    static void DebugReveal()
    {
        if (File.Exists(FilePath))
            UnityEditor.EditorUtility.RevealInFinder(FilePath);
        else
            Debug.Log($"[Checkpoint] No checkpoint at {FilePath}.");
    }
#endif
}

[Serializable]
public class CheckpointData
{
    public int version;
    public string id;
    public string scene;
    public string savedAtUtc;

    [Header("Player")]
    public Vector3 playerPosition;
    public float playerYaw;
    public float cameraPitch;

    [Header("Inventory")]
    public List<InventorySlotRecord> inventory = new List<InventorySlotRecord>();
    public int selectedSlot = -1;
    public bool lighterLit;
    public int lighterMisfires;
    public bool lighterStruckBefore;

    [Header("World")]
    public List<string> takenItems = new List<string>();
    public List<ActiveRecord> itemActive = new List<ActiveRecord>();
    public List<DroppedItemRecord> droppedItems = new List<DroppedItemRecord>();
    public List<DoorRecord> doors = new List<DoorRecord>();
    public List<ActiveRecord> lightSwitches = new List<ActiveRecord>();
    public List<ActiveRecord> candles = new List<ActiveRecord>();
    public List<string> openPadlocks = new List<string>();

    [Header("Story")]
    public bool wallStanderRevealed;
    public bool barTaken;
    public bool corridorLeft;
    public bool returnMusicPlaying;

    [Header("Hints")]
    public List<string> hintsShown = new List<string>();
    public List<string> tutorialsShown = new List<string>();
}

[Serializable]
public class InventorySlotRecord
{
    public string item;
    public int count;
}

[Serializable]
public class ActiveRecord
{
    public string id;
    public bool on;
}

[Serializable]
public class DroppedItemRecord
{
    public string item;
    public Vector3 position;
    public Quaternion rotation;
    public bool lit;
}

[Serializable]
public class DoorRecord
{
    public string id;
    public bool open;
    public bool locked;
    public bool aiLocked;
}
