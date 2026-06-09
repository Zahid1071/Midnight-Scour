using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.SceneManagement;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;

    public CinemachineCamera jumpscareCam;

    [SerializeField] private GameObject wishlistBanner;
    [SerializeField] private string menuSceneName = "Main Menu";
    [SerializeField] private float endGameDelaySeconds = 4f;

    Coroutine _endGameRoutine;

    void Awake()
    {
        Instance = this;

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

        if (_endGameRoutine != null)
            StopCoroutine(_endGameRoutine);

        _endGameRoutine = StartCoroutine(EndGameAfterDelay());
    }

    IEnumerator EndGameAfterDelay()
    {
        yield return new WaitForSeconds(endGameDelaySeconds);

        EndJumpscare();
        LoadMenuScene();
        _endGameRoutine = null;
    }

    public void EndJumpscare()
    {
        if (jumpscareCam != null)
            jumpscareCam.Priority = 0;
    }

    void LoadMenuScene()
    {
        PlayerControlGate.Clear();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        SceneManager.LoadScene(menuSceneName);
    }
}
