using UnityEngine;
using Unity.Cinemachine;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;

    public CinemachineCamera jumpscareCam;
    [SerializeField] private GameObject wishlistBanner;

    private GameObject player;

    void Awake()
    {
        Instance = this;
        player = GameObject.FindWithTag("Player");

        if (wishlistBanner == null)
        {
            var found = GameObject.Find("Wishlist Banner");
            if (found != null)
                wishlistBanner = found;
        }

        if (wishlistBanner != null)
            wishlistBanner.SetActive(false);
    }

    public void TriggerJumpscare(Transform jumpscarePoint, Transform npcFace)
    {
        if (npcFace != null)
        {
            var lookDirector = npcFace.GetComponent<POILookDirector>()
                ?? npcFace.GetComponentInChildren<POILookDirector>()
                ?? npcFace.root.GetComponentInChildren<POILookDirector>();
            lookDirector?.CancelHoldAndClearLook();
        }

        jumpscareCam.transform.position = jumpscarePoint.position;
        jumpscareCam.Priority = 50;

        if (wishlistBanner != null)
            wishlistBanner.SetActive(true);
    }

    public void EndJumpscare()
    {
        jumpscareCam.Priority = 0;
    }
}
