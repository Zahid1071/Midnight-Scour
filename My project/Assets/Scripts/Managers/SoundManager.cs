using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Mixer")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup masterGroup;
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioMixerGroup ambienceGroup;

    [Header("Exposed Param Names (match your mixer)")]
    [SerializeField] private string masterParam = "Master";
    [SerializeField] private string musicParam = "Music";
    [SerializeField] private string sfxParam = "SFX";

    [Header("Scene Routing")]
    [Tooltip("Scenes considered 'menus' will play menuMusic. Others use gameplay music + ambience.")]
    [SerializeField] private string[] menuSceneNames = new[] { "Main Menu", "Menu", "Title" };

    [Header("Music - Menus")]
    public AudioClip menuMusic;

    [Header("Music - Gameplay")]
    [Tooltip("Default exploration music for all non-captive layers.")]
    public AudioClip gameMusic;
    [Tooltip("Played while the player is on the captive layer (12). Falls back to gameMusic if unset.")]
    public AudioClip captiveLayerMusic;
    [Tooltip("Temporary override while any NPC is chasing. Clears back to layer music when chase ends.")]
    public AudioClip chaseMusic;
    [SerializeField] private int captiveLayerIndex = 12;
    [SerializeField] private float gameplayMusicFade = 0.5f;

    [Header("Ambience")]
    public AudioClip defaultAmbience;

    [Header("UI Clips")]
    public AudioClip uiClickClip;
    public AudioClip uiHoverClip; // optional
    [Tooltip("Played on failed interact (wrong item, out of reach, empty click, etc.).")]
    public AudioClip actionFailClip;

    [Header("SFX banks")]
    public AudioClip[] sfxBank;
    public AudioClip[] tensionSFXBank;

    [Header("World item SFX")]
    [Tooltip("Played once when a dropped item hits the ground (collision-based).")]
    public AudioClip itemDropLandClip;

    [Header("Runtime AudioSources (auto-created if null)")]
    [SerializeField] private AudioSource musicSourceA;
    [SerializeField] private AudioSource musicSourceB; // for crossfades
    [SerializeField] private AudioSource ambienceSource;
    [SerializeField] private AudioSource sfxSource;    // 2D one-shots

    [Header("3D SFX Pool")]
    [SerializeField] private int poolSize = 12;
    [SerializeField] private float threeDSpatialBlend = 1f;
    [SerializeField] private float sfxMinDistance = 1f;
    [SerializeField] private float sfxMaxDistance = 18f;

    [Header("3D World SFX Occlusion")]
    [SerializeField] private LayerMask occlusionLayers = ~0;
    [Range(0f, 1f)]
    [SerializeField] private float occludedVolumeMultiplier = 0.4f;
    [SerializeField] private float occludedLowpassCutoff = 1400f;
    [SerializeField] private float clearLowpassCutoff = 22000f;

    private readonly Queue<AudioSource> threeDPool = new();

    private Coroutine musicFadeRoutine;
    private bool usingA = true;

    Transform _player;
    int _lastPlayerLayer = -1;
    bool _playerOnCaptiveLayer;
    bool _chaseMusicActive;
    bool _inGameplayScene;
    bool _gameplayMusicReady;
    Coroutine _gameplayMusicRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureSources();
        SilenceMusicSources();
        Build3DPool();

        SceneManager.sceneLoaded += OnSceneLoaded;

        ApplyMixerFromPrefs();
    }

    void Update()
    {
        if (!_inGameplayScene || !_gameplayMusicReady || _chaseMusicActive)
            return;

        RefreshPlayerLayerMusic();
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void EnsureSources()
    {
        if (!musicSourceA) musicSourceA = CreateChildSource("Music A", musicGroup, loop: true);
        if (!musicSourceB) musicSourceB = CreateChildSource("Music B", musicGroup, loop: true);
        if (!ambienceSource) ambienceSource = CreateChildSource("Ambience", ambienceGroup, loop: true);
        if (!sfxSource) sfxSource = CreateChildSource("SFX 2D", sfxGroup, loop: false);

        // start with full volumes (actual loudness controlled by Mixer)
        musicSourceA.volume = 1f;
        musicSourceB.volume = 1f;
        ambienceSource.volume = 1f;
        sfxSource.volume = 1f;
    }

    void SilenceMusicSources()
    {
        StopMusic(0f);

        if (musicSourceA != null)
        {
            musicSourceA.playOnAwake = false;
            musicSourceA.Stop();
            musicSourceA.clip = null;
        }

        if (musicSourceB != null)
        {
            musicSourceB.playOnAwake = false;
            musicSourceB.Stop();
            musicSourceB.clip = null;
        }
    }

    private AudioSource CreateChildSource(string name, AudioMixerGroup group, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.outputAudioMixerGroup = group;
        return src;
    }

    private void Build3DPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"SFX3D_{i:D2}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.outputAudioMixerGroup = sfxGroup;
            src.spatialBlend = threeDSpatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = sfxMinDistance;
            src.maxDistance = sfxMaxDistance;

            if (src.GetComponent<AudioLowPassFilter>() == null)
                src.gameObject.AddComponent<AudioLowPassFilter>();

            go.SetActive(false);
            threeDPool.Enqueue(src);
        }
    }

    // ---------- PUBLIC API ----------

    /// <summary>NPC chase started — chase music overrides layer music until cleared.</summary>
    public void SetChaseMusicActive(bool active)
    {
        if (_chaseMusicActive == active)
            return;

        _chaseMusicActive = active;

        if (!_inGameplayScene)
            return;

        if (!active)
            RefreshPlayerLayerMusic(force: true);

        ApplyGameplayMusic();
    }

    public void PlayMusic(AudioClip clip, float fade = 0.5f)
    {
        if (clip == null) { StopMusic(fade); return; }
        CrossfadeTo(clip, fade);
    }

    public void StopMusic(float fade = 0.25f)
    {
        CrossfadeTo(null, fade);
    }

    public void PlayAmbience(AudioClip clip, bool loop = true)
    {
        if (clip == null) { StopAmbience(); return; }
        ambienceSource.loop = loop;
        if (ambienceSource.clip != clip)
        {
            ambienceSource.clip = clip;
        }
        if (!ambienceSource.isPlaying) ambienceSource.Play();
    }

    public void StopAmbience() => ambienceSource.Stop();

    public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume);
        sfxSource.pitch = 1f;
    }

    public void PlaySFXAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        PlaySFXAt(clip, position, volume, pitch, reverbZoneMix: -1f, ignoreOcclusionRoot: null);
    }

    public void PlaySFXAt(
        AudioClip clip,
        Vector3 position,
        float volume,
        float pitch,
        float reverbZoneMix,
        Transform ignoreOcclusionRoot)
    {
        if (!clip || threeDPool.Count == 0) return;

        WorldSfxOcclusion.Result occlusion = WorldSfxOcclusion.Evaluate(
            position,
            occlusionLayers,
            occludedVolumeMultiplier,
            occludedLowpassCutoff,
            clearLowpassCutoff,
            ignoreOcclusionRoot);

        var src = threeDPool.Dequeue();
        var go = src.gameObject;
        go.transform.position = position;
        go.SetActive(true);

        ApplyOcclusionToSource(src, occlusion, reverbZoneMix);

        src.pitch = pitch;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * occlusion.VolumeMultiplier);
        src.Play();
        StartCoroutine(Return3DWhenDone(src, clip.length / Mathf.Abs(pitch)));
    }

    public void PlaySFXSafe(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        if (!IsClipPlayingAt(clip, position))
            PlaySFXAt(clip, position, volume, pitch);
    }

    void ApplyOcclusionToSource(AudioSource src, WorldSfxOcclusion.Result occlusion, float reverbZoneMix)
    {
        if (reverbZoneMix >= 0f)
            src.reverbZoneMix = reverbZoneMix;

        var lowPass = src.GetComponent<AudioLowPassFilter>();
        if (lowPass == null)
            return;

        lowPass.enabled = occlusion.IsOccluded;
        lowPass.cutoffFrequency = occlusion.LowpassCutoff;
    }

    void ResetPooledSource(AudioSource src)
    {
        var lowPass = src.GetComponent<AudioLowPassFilter>();
        if (lowPass != null)
        {
            lowPass.enabled = false;
            lowPass.cutoffFrequency = clearLowpassCutoff;
        }

        src.reverbZoneMix = 1f;
        src.pitch = 1f;
    }

    public void ApplyMixerFromPrefs()
    {
        // values are stored 0..1 (linear) by your settings menu
        float master = PlayerPrefs.GetFloat("masterVol", 0.5f);
        float music = PlayerPrefs.GetFloat("musicVol", 0.5f);
        float sfx = PlayerPrefs.GetFloat("sfxVol", 0.5f);

        if (mixer)
        {
            mixer.SetFloat(masterParam, Linear01ToDb(master));
            mixer.SetFloat(musicParam, Linear01ToDb(music));
            mixer.SetFloat(sfxParam, Linear01ToDb(sfx));
        }
    }

    public void PlayUIClick()
    {
        if (uiClickClip != null)
            sfxSource.PlayOneShot(uiClickClip);
    }

    public void PlayUIHover()
    {
        if (uiHoverClip != null)
            sfxSource.PlayOneShot(uiHoverClip);
    }

    public void PlayActionFail(float volume = .25f)
    {
        if (actionFailClip != null && sfxSource != null)
            sfxSource.PlayOneShot(actionFailClip, volume);
    }

    /// <summary>
    /// Plays a custom UI sound clip.
    /// </summary>
    public void PlayUISound(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip);
    }


    // ---------- INTERNALS ----------

    private IEnumerator Return3DWhenDone(AudioSource src, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        src.Stop();
        src.clip = null;
        ResetPooledSource(src);
        src.gameObject.SetActive(false);
        threeDPool.Enqueue(src);
    }

    private void CrossfadeTo(AudioClip newClip, float duration)
    {
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(CrossfadeRoutine(newClip, duration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip, float duration)
    {
        var from = usingA ? musicSourceA : musicSourceB;
        var to = usingA ? musicSourceB : musicSourceA;
        usingA = !usingA;

        // prepare target
        if (newClip != null)
        {
            to.clip = newClip;
            to.volume = 0f;
            if (!to.isPlaying) to.Play();
        }

        float t = 0f;
        float fromStart = from.volume;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? t / duration : 1f;
            from.volume = Mathf.Lerp(fromStart, 0f, k);
            if (newClip != null) to.volume = Mathf.Lerp(0f, 1f, k);
            yield return null;
        }

        from.volume = 0f;
        if (from.isPlaying) from.Stop();
        if (newClip == null)
        {
            to.Stop();
            to.clip = null;
        }
        else
        {
            to.volume = 1f;
        }
    }

    private static float Linear01ToDb(float v) => (v <= 0.0001f) ? -80f : Mathf.Log10(Mathf.Clamp01(v)) * 20f;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleSceneAudio(scene);
    }

    void HandleSceneAudio(Scene scene)
    {
        if (_gameplayMusicRoutine != null)
        {
            StopCoroutine(_gameplayMusicRoutine);
            _gameplayMusicRoutine = null;
        }

        bool isMenu = IsMenuScene(scene.name);
        _inGameplayScene = !isMenu;
        _gameplayMusicReady = false;
        _player = null;
        _lastPlayerLayer = -1;
        _playerOnCaptiveLayer = false;
        _chaseMusicActive = false;

        SilenceMusicSources();

        if (isMenu)
        {
            PlayAmbience(null);
            PlayMusic(menuMusic);
            return;
        }

        _gameplayMusicRoutine = StartCoroutine(BeginGameplayAudioDeferred());
    }

    IEnumerator BeginGameplayAudioDeferred()
    {
        PlayAmbience(defaultAmbience, true);

        const float findPlayerTimeout = 3f;
        float elapsed = 0f;
        while (_player == null && elapsed < findPlayerTimeout)
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _player = playerGo.transform;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Let Cage / layer scripts settle before picking the track.
        yield return null;
        yield return null;

        RefreshPlayerLayerMusic(force: true);
        _gameplayMusicReady = true;
        _gameplayMusicRoutine = null;
    }

    void RefreshPlayerLayerMusic(bool force = false)
    {
        if (_player == null)
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _player = playerGo.transform;
        }

        if (_player == null)
            return;

        int layer = _player.gameObject.layer;
        bool captive = layer == captiveLayerIndex;

        if (!force && layer == _lastPlayerLayer && captive == _playerOnCaptiveLayer)
            return;

        _lastPlayerLayer = layer;
        _playerOnCaptiveLayer = captive;
        ApplyGameplayMusic();
    }

    void ApplyGameplayMusic()
    {
        if (!_inGameplayScene)
            return;

        AudioClip clip = ResolveGameplayMusicClip();
        if (clip == null)
            return;

        if (CurrentMusicClip == clip)
            return;

        PlayMusic(clip, gameplayMusicFade);
    }

    AudioClip ResolveGameplayMusicClip()
    {
        if (_chaseMusicActive && chaseMusic != null)
            return chaseMusic;

        if (_playerOnCaptiveLayer)
            return captiveLayerMusic != null ? captiveLayerMusic : gameMusic;

        return gameMusic;
    }

    private bool IsMenuScene(string sceneName)
    {
        for (int i = 0; i < menuSceneNames.Length; i++)
            if (sceneName == menuSceneNames[i]) return true;
        return false;
    }

    public AudioClip CurrentMusicClip
    {
        get
        {
            if (musicSourceA.isPlaying) return musicSourceA.clip;
            if (musicSourceB.isPlaying) return musicSourceB.clip;
            return null;
        }
    }
    

    // Returns true if 'clip' is currently playing on any pooled 3D source
    // close to 'position' (within maxDistance).
    public bool IsClipPlayingAt(AudioClip clip, Vector3 position, float maxDistance = 0.75f)
    {
        if (clip == null) return false;

        // Your 3D pool lives as children under SoundManager.
        // Iterate children (active ones will have src.isPlaying == true).
        for (int i = 0; i < transform.childCount; i++)
        {
            var t = transform.GetChild(i);
            var src = t.GetComponent<AudioSource>();
            if (src != null && src.isPlaying && src.clip == clip)
            {
                if (Vector3.Distance(t.position, position) <= maxDistance)
                    return true;
            }
        }
        return false;
    }

}
