using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain data container for everything we persist to disk.
/// Keep this class free of MonoBehaviour / ScriptableObject logic.
/// </summary>
[System.Serializable]
public class SaveData
{
    public string sceneName;

    // Player transform (stored as floats because Vector3/Quaternion aren't JSON-friendly)
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ, rotW;

    public int score;

    public List<string> inventory = new();

    // --- helpers ---

    public Vector3 GetPosition() => new(posX, posY, posZ);
    public Quaternion GetRotation() => new(rotX, rotY, rotZ, rotW);

    public void SetPosition(Vector3 v)
    {
        posX = v.x;
        posY = v.y;
        posZ = v.z;
    }

    public void SetRotation(Quaternion q)
    {
        rotX = q.x;
        rotY = q.y;
        rotZ = q.z;
        rotW = q.w;
    }
}
