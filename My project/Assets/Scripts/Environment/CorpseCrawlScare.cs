using System.Collections;
using UnityEngine;
using UnityEngine.Formats.Alembic.Importer;

/// <summary>
/// Small-house crawl scare: body + sheet share one clock. Idle holds the Blender frame 29
/// pose until <see cref="Play"/>; sheet stays visible for the full animation.
/// Body is scrubbed with <see cref="AnimationClip.SampleAnimation"/> because Animator.Play
/// at speed 0 advances state time without updating bones on this rig.
/// When the clip ends, the Mixamo ragdoll activates.
/// Entering the house music zone starts the tension bed; the jumpscare cuts it and plays
/// the start + sting one-shots.
/// </summary>
[DisallowMultipleComponent]
public class CorpseCrawlScare : MonoBehaviour
{
    [SerializeField] Animator animator;
    [Tooltip("FBX root that owns the Crawl_Rig hierarchy (usually the Animator object).")]
    [SerializeField] GameObject bodyRoot;
    [SerializeField] string crawlState = "Crawl Scare";
    [SerializeField] AnimationClip crawlClip;
    [Tooltip("Baked cloth sheet (Alembic); time range matches the crawl clip.")]
    [SerializeField] AlembicStreamPlayer sheet;
    [Tooltip("Optional root for the sheet (defaults to the Alembic player object). Kept active for the full scare.")]
    [SerializeField] GameObject sheetRoot;
    [SerializeField] CrawlScareRagdoll ragdoll;
    [Tooltip("Clip FPS used to map Blender frame numbers onto seconds.")]
    [SerializeField] float clipFps = 30f;
    [Tooltip("Blender frame held as the pre-scare idle pose.")]
    [SerializeField] int idleFrame = 29;
    [SerializeField] float speed = 1f;

    [Header("Audio")]
    [Tooltip("Played once when the crawl scare starts (e.g. zombie groan).")]
    [SerializeField] AudioClip startClip;
    [SerializeField, Range(0f, 1f)] float startClipVolume = 1f;
    [Tooltip("2D sting layered with the start clip when the scare begins.")]
    [SerializeField] AudioClip stingClip;
    [SerializeField, Range(0f, 1f)] float stingClipVolume = 1f;
    [Tooltip("Seconds after Play() before the sting SFX and camera kick.")]
    [SerializeField] float stingDelay = 0.2f;
    [Tooltip("Looping bed that replaces exploration music while inside the house zone.")]
    [SerializeField] AudioClip houseMusic;
    [SerializeField, Range(0f, 1f)] float houseMusicVolume = 0.25f;
    [Tooltip("Trigger volume for house music (on this object or a child). Auto-created if missing.")]
    [SerializeField] BoxCollider musicZone;

    float _time;
    bool _playing;
    bool _started;
    bool _ragdollStarted;
    bool _houseMusicArmed = true;

    public bool IsPlaying => _playing;
    public bool Started => _started;
    public bool RagdollActive => _ragdollStarted;
    public float NormalizedProgress =>
        !_started ? 0f : Mathf.Clamp01((_time - IdleTime) / Mathf.Max(0.05f, Length - IdleTime));

    /// <summary>True while the player is inside the house music / tension volume.</summary>
    public bool IsPlayerInsideHouse(Vector3 worldPos)
    {
        EnsureMusicZone();
        if (musicZone == null)
            return false;
        return musicZone.bounds.Contains(worldPos);
    }

    float Length => crawlClip != null ? crawlClip.length : sheet != null ? sheet.Duration : 0f;
    float IdleTime => FrameToTime(idleFrame);

    float FrameToTime(int frame) => Mathf.Max(0f, frame) / Mathf.Max(1f, clipFps);

    void Awake()
    {
        if (bodyRoot == null && animator != null)
            bodyRoot = animator.gameObject;

        if (ragdoll == null)
            ragdoll = GetComponent<CrawlScareRagdoll>();
        if (ragdoll != null && bodyRoot != null)
            ragdoll.SetBodyRoot(bodyRoot.transform);

        // Animator would overwrite SampleAnimation poses; keep it disabled and scrub the clip.
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.speed = 0f;
            animator.enabled = false;
        }

        if (sheetRoot == null && sheet != null)
            sheetRoot = sheet.gameObject;

        EnsureMusicZone();
        SoundManager.EnsureClipReady(houseMusic);
        SoundManager.EnsureClipReady(startClip);
        SoundManager.EnsureClipReady(stingClip);

        _time = IdleTime;
        Pose(_time);
        SetSheetActive(true);
    }

    void OnEnable()
    {
        if (!_started)
        {
            _time = IdleTime;
            Pose(_time);
            SetSheetActive(true);
        }
    }

    void OnDisable()
    {
        if (SoundManager.Instance != null && SoundManager.Instance.IsZoneMusicActive)
            StopHouseMusic(restoreGameplay: true);
    }

    public void Play()
    {
        if (_started)
            return;

        _started = true;
        _playing = true;
        _ragdollStarted = false;
        _houseMusicArmed = false;
        _time = IdleTime;
        Pose(_time);
        SetSheetActive(true);
        StopHouseMusic(restoreGameplay: false);
        PlayStartSfx();
        if (stingDelay <= 0f)
            PlaySting();
        else
            StartCoroutine(PlayStingDelayed(stingDelay));
    }

    IEnumerator PlayStingDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!_started)
            yield break;
        PlaySting();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other))
            return;
        if (_houseMusicArmed && !_started)
            StartHouseMusic();
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other))
            return;
        // Leaving the house restores exploration music whether or not the scare fired.
        StopHouseMusic(restoreGameplay: true);
    }

    void StartHouseMusic()
    {
        if (houseMusic == null || SoundManager.Instance == null)
            return;

        SoundManager.EnsureClipReady(houseMusic);
        SoundManager.Instance.SetZoneMusicActive(true, houseMusic, restoreGameplay: true, volume: houseMusicVolume);
    }

    void StopHouseMusic(bool restoreGameplay)
    {
        if (SoundManager.Instance == null)
            return;
        if (!SoundManager.Instance.IsZoneMusicActive && restoreGameplay)
            return;

        SoundManager.Instance.SetZoneMusicActive(false, null, restoreGameplay);
    }

    void PlayStartSfx()
    {
        if (startClip == null)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(startClip, transform.position, startClipVolume);
        else
            AudioSource.PlayClipAtPoint(startClip, transform.position, startClipVolume);
    }

    void PlaySting()
    {
        if (stingClip == null)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(stingClip, stingClipVolume);
        else
            AudioSource.PlayClipAtPoint(stingClip, transform.position, stingClipVolume);
        CameraKick.KickPlayer(1.6f, 16f, 0.45f);
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null)
            return false;
        if (other.CompareTag("Player"))
            return true;

        Transform t = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
        if (t.CompareTag("Player"))
            return true;
        return t.root != null && t.root.CompareTag("Player");
    }

    void EnsureMusicZone()
    {
        if (musicZone == null)
            musicZone = GetComponent<BoxCollider>();

        if (musicZone == null)
        {
            Transform existing = transform.Find("Music Zone");
            if (existing != null)
                musicZone = existing.GetComponent<BoxCollider>();
        }

        // Collider must live on this GameObject so OnTrigger* reaches this script.
        if (musicZone != null && musicZone.gameObject != gameObject)
        {
            Destroy(musicZone.gameObject);
            musicZone = null;
        }

        if (musicZone == null)
            musicZone = gameObject.AddComponent<BoxCollider>();

        musicZone.isTrigger = true;
        Vector3 worldCenter = new Vector3(74.98f, 2.2f, 39.16f);
        Vector3 worldSize = new Vector3(11.5f, 5.5f, 14.4f);
        musicZone.center = transform.InverseTransformPoint(worldCenter);
        Vector3 lossy = transform.lossyScale;
        musicZone.size = new Vector3(
            worldSize.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
            worldSize.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
            worldSize.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
    }

    void Update()
    {
        if (_ragdollStarted)
            return;

        if (!_playing)
        {
            if (!_started)
                Pose(IdleTime);
            return;
        }

        _time = Mathf.Min(_time + Time.deltaTime * speed, Length);
        Pose(_time);

        if (_time >= Length)
        {
            _playing = false;
            StartRagdoll();
        }
    }

    void StartRagdoll()
    {
        if (_ragdollStarted)
            return;
        _ragdollStarted = true;

        if (sheet != null && sheet.gameObject.activeInHierarchy)
        {
            sheet.CurrentTime = Length;
            sheet.UpdateImmediately(Length);
        }

        if (ragdoll != null)
            ragdoll.Activate();
    }

    void Pose(float time)
    {
        if (_ragdollStarted)
            return;

        if (crawlClip != null && bodyRoot != null)
            crawlClip.SampleAnimation(bodyRoot, Mathf.Clamp(time, 0f, crawlClip.length));

        if (sheet != null && sheet.gameObject.activeInHierarchy)
        {
            sheet.CurrentTime = time;
            sheet.UpdateImmediately(time);
        }
    }

    void SetSheetActive(bool active)
    {
        if (sheetRoot != null)
            sheetRoot.SetActive(active);
        else if (sheet != null)
            sheet.gameObject.SetActive(active);
    }
}
