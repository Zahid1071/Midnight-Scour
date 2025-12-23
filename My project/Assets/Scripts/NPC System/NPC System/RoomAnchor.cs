using UnityEngine;

public class RoomAnchor : MonoBehaviour
{
     public string roomID;
     public string zoneID;

    void Awake()
    {
        roomID = name;
        zoneID = transform.parent != null ? transform.parent.name : string.Empty;
    }
}
