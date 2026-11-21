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
    [Tooltip("Basement music. Plays after leaving the cage while still underground; silent outdoors.")]
    public AudioClip gameMusic;
    [Tooltip("Optional track while locked in the cage (captive layer 12). Leave empty for silence until escape.")]
    public AudioClip captiveLayerMusic;
    [Tooltip("Played while any NPC is chasing or searching. Overrides layer music until hunt ends.")]
    public AudioClip chaseMusic;
    [SerializeField] private int captiveLayerIndex = 12;
    [Tooltip("Stealth hiding layer — layer swaps here never change music.")]
    [SerializeField] private int hiddenLayerIndex = 7;
    [SerializeField] private float gameplayMusicFade = 0.5f;
    [Tooltip("NPC zone used for room votes (basement vs outside). Matches RoomAnchor parents.")]
    [SerializeField] string basementRoomZoneId = "Demo_Environment";

    [Header("Ambience")]
    public AudioClip defaultAmbience;
    [Tooltip("Extra outdoor layer (e.g. night crickets) played with the main outside bed.")]
    public AudioClip cricketAmbience;
    [Tooltip("Cricket layer volume relative to full outdoors (0.6 = 60%).")]
    [SerializeField, Range(0f, 1f)] private float cricketAmbienceVolume = 0.6f;
    [Tooltip("Outdoor ambience volume while there is a roof or ceiling above the camera.")]
    [SerializeField, Range(0f, 1f)] private float indoorAmbienceVolume = 0.12f;
    [Tooltip("Outdoor ambience low-pass cutoff (Hz) while indoors — heard through the walls.")]
    [SerializeField] private float indoorAmbienceCutoff = 800f;
    [SerializeField, Min(0.01f)] private float indoorAmbienceFade = 0.6f;
    [Tooltip("A roof within this height (m) above the camera counts as indoors.")]
    [SerializeField] private float roofCheckDistance = 15f;
    [Tooltip("Seconds to fade out basement bed when the return-scare audio lock engages.")]
    [SerializeField] private float returnScareMusicFadeOut = 2.5f;

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
    [SerializeField] private AudioSource cricketAmbienceSource;
    [SerializeField] private AudioSource sfxSource;    // 2D one-shots

    [Header("3D SFX Pool")]
    [SerializeField] private int poolSize = 20;
    [SerializeField] private float threeDSpatialBlend = 1f;
    [SerializeField] private float sfxMinDistance = 1f;
    [SerializeField] private float sfxMaxDistance = 18f;

    private readonly Queue<AudioSource> threeDPool = new();
    AudioSource _searchTensionSource;

    const int SearchTensionClipIndex = 2;

    private Coroutine musicFadeRoutine;
    private bool usingA = true;

    AudioLowPassFilter _ambienceLowPass;
    AudioLowPassFilter _cricketAmbienceLowPass;
    float _outdoorAmount = 1f;
    bool _underRoof;
    float _nextRoofCheck;

    const float OpenAirCutoff = 22000f;

    /// <summary>1 outdoors, 0 indoors (smoothed). Scale outdoor-only sounds such as thunder by it.</summary>
    public float OutdoorAmount => _outdoorAmount;

    Transform _player;
    int _lastPlayerLayer = -1;
    bool _playerOnCaptiveLayer;
    bool _chaseMusicActive;
    AudioClip _chaseMusicOverride;
    bool _zoneMusicActive;
    AudioClip _zoneMusicOverride;
    float _zoneMusicVolume = 1f;
    float _musicTargetVolume = 1f;
    bool _playerInBasement = true;
    bool _returnScareAudioLock;
    bool _wasPlayerHiddenForMusic;
    bool _inGameplayScene;
    bool _gameplayMusicReady;
    Coroutine _gameplayMusicRoutine;
    float _ambienceVolumeMul = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureSources();
        SilenceMusicSources();
        Build3DPool();

        SceneManager.sceneLoaded += OnSceneLoaded;

        SyncMixerVolumeFromSettings();
    }

    void Start()
    {
        SyncMixerVolumeFromSettings();
    }

    void Update()
    {
        UpdateIndoorAmbience();

        if (!_inGameplayScene || !_gameplayMusicReady)
            return;

        // Basement exit / re-entry can change the bed without a layer swap.
        if (!_chaseMusicActive && !_zoneMusicActive)
        {
            RefreshBasementMusicState();
            RefreshPlayerLayerMusic();
        }
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
        if (!cricketAmbienceSource) cricketAmbienceSource = CreateChildSource("Cricket Ambience", ambienceGroup, loop: true);
        if (!sfxSource) sfxSource = CreateChildSource("SFX 2D", sfxGroup, loop: false);

        // start with full volumes (actual loudness controlled by Mixer)
        musicSourceA.volume = 1f;
        musicSourceB.volume = 1f;
        ambienceSource.volume = 1f;
        cricketAmbienceSource.volume = 0f;
        sfxSource.volume = 1f;

        _ambienceLowPass = ambienceSource.GetComponent<AudioLowPassFilter>();
        if (!_ambienceLowPass) _ambienceLowPass = ambienceSource.gameObject.AddComponent<AudioLowPassFilter>();
        _ambienceLowPass.cutoffFrequency = OpenAirCutoff;

        _cricketAmbienceLowPass = cricketAmbienceSource.GetComponent<AudioLowPassFilter>();
        if (!_cricketAmbienceLowPass) _cricketAmbienceLowPass = cricketAmbienceSource.gameObject.AddComponent<AudioLowPassFilter>();
        _cricketAmbienceLowPass.cutoffFrequency = OpenAirCutoff;
    }

    // Muffles the outdoor ambience while a roof or ceiling is above the camera.
    void UpdateIndoorAmbience()
    {
        Camera cam = Camera.main;
        if (cam != null && Time.unscaledTime >= _nextRoofCheck)
        {
            _nextRoofCheck = Time.unscaledTime + 0.25f;
            // Starts above the camera so the player's own capsule (which it starts inside) is skipped.
            _underRoof = Physics.Raycast(cam.transform.position + Vector3.up * 0.3f, Vector3.up, roofCheckDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        _outdoorAmount = Mathf.MoveTowards(_outdoorAmount, _underRoof ? 0f : 1f, Time.unscaledDeltaTime / indoorAmbienceFade);
        float ambientTarget = Mathf.Lerp(indoorAmbienceVolume, 1f, _outdoorAmount) * _ambienceVolumeMul;
        ambienceSource.volume = Mathf.MoveTowards(ambienceSource.volume, ambientTarget, Time.unscaledDeltaTime * 1.5f);
        // Cutoff is blended on a log scale so the muffling fades evenly to the ear.
        float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(indoorAmbienceCutoff), Mathf.Log(OpenAirCutoff), _outdoorAmount));
        _ambienceLowPass.cutoffFrequency = cutoff;

        if (cricketAmbience != null)
        {
            if (cricketAmbienceSource == null || !cricketAmbienceSource.isPlaying)
                PlayCricketAmbience(true);

            if (cricketAmbienceSource != null)
            {
                float cricketTarget = ambientTarget * cricketAmbienceVolume;
                cricketAmbienceSource.volume = Mathf.MoveTowards(
                    cricketAmbienceSource.volume, cricketTarget, Time.unscaledDeltaTime * 1.5f);
                if (_cricketAmbienceLowPass != null)
                    _cricketAmbienceLowPass.cutoffFrequency = cutoff;
            }
        }

        TickReturnScareAudioLock();
    }

    /// <summary>
    /// Return trip (corridor / cage): mute insects ambience + basement bed until the player
    /// is above ground and chase music has ended.
    /// </summary>
    public void SetReturnScareAudioLock(bool locked)
    {
        if (_returnScareAudioLock == locked)
            return;
        _returnScareAudioLock = locked;
        _ambienceVolumeMul = locked ? 0f : 1f;
        if (locked)
        {
            // Fade basement bed out; chase/zone still win if active.
            if (!_chaseMusicActive && !_zoneMusicActive)
                StopMusic(returnScareMusicFadeOut);
        }
        else if (!_chaseMusicActive && !_zoneMusicActive)
            ApplyGameplayMusic();
    }

    public bool IsReturnScareAudioLocked => _returnScareAudioLock;

    void TickReturnScareAudioLock()
    {
        if (!_returnScareAudioLock || !_inGameplayScene)
            return;
        // Stay locked while still underground or while chase/search music is up.
        if (_playerInBasement || _chaseMusicActive)
            return;
        SetReturnScareAudioLock(false);
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
            go.SetActive(false);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.outputAudioMixerGroup = sfxGroup;
            // Interactive: HRTF + distance only. Reflections/occlusion on the pool made doors hum then hard-cut,
            // and nearby basement SFX sound choked.
            SpatialAudio.ConfigureInteractive(src, sfxMinDistance, sfxMaxDistance, AudioRolloffMode.Linear);
            src.spatialBlend = threeDSpatialBlend;
            threeDPool.Enqueue(src);
        }
    }

    // ---------- PUBLIC API ----------

    public bool IsChaseMusicActive => _chaseMusicActive;
    public bool IsZoneMusicActive => _zoneMusicActive;

    /// <summary>NPC chase/search started — tension music overrides layer music until cleared.</summary>
    public void SetChaseMusicActive(bool active) => SetChaseMusicActive(active, null);

    /// <summary>Same, with clip played instead of <see cref="chaseMusic"/> while active (null = chaseMusic).</summary>
    public void SetChaseMusicActive(bool active, AudioClip clip)
    {
        AudioClip overrideClip = active ? clip : null;
        if (_chaseMusicActive == active && _chaseMusicOverride == overrideClip)
            return;

        _chaseMusicActive = active;
        _chaseMusicOverride = overrideClip;

        if (!_inGameplayScene)
            return;

        if (active)
            EnsureClipReady(overrideClip != null ? overrideClip : chaseMusic);

        // While hidden, never fade out tension — resume layer music only after leaving the spot.
        if (!active && IsPlayerHiddenForMusic())
            return;

        if (!active)
            RefreshPlayerLayerMusic(force: true);

        ApplyGameplayMusic();
    }

    /// <summary>
    /// Location bed (e.g. small-house tension) that replaces exploration music until cleared.
    /// Chase music still wins while active. Pass restoreGameplay=false to cut to silence
    /// (jumpscare) and hold that silence until zone music is cleared with restore.
    /// </summary>
    public void SetZoneMusicActive(bool active, AudioClip clip = null, bool restoreGameplay = true, float volume = 1f)
    {
        if (!active && !restoreGameplay)
        {
            // Hold silence: stay in zone-music mode with a null clip so layer music can't restart.
            _zoneMusicActive = true;
            _zoneMusicOverride = null;
            _zoneMusicVolume = 1f;
            if (_inGameplayScene)
                StopMusic(0.12f);
            return;
        }

        AudioClip overrideClip = active ? clip : null;
        float targetVol = active ? Mathf.Clamp01(volume) : 1f;
        if (_zoneMusicActive == active && _zoneMusicOverride == overrideClip
            && (!active || Mathf.Approximately(_zoneMusicVolume, targetVol)))
            return;

        _zoneMusicActive = active;
        _zoneMusicOverride = overrideClip;
        _zoneMusicVolume = targetVol;

        if (!_inGameplayScene)
            return;

        if (!active)
        {
            _musicTargetVolume = 1f;
            if (!_chaseMusicActive)
                RefreshPlayerLayerMusic(force: true);
            else
                ApplyGameplayMusic();
            return;
        }

        EnsureClipReady(clip);
        ApplyGameplayMusic();
    }

    /// <summary>Warm a clip on a background load so the first Play does not hitch the main thread.</summary>
    public static void EnsureClipReady(AudioClip clip)
    {
        if (clip == null)
            return;
        if (clip.loadState == AudioDataLoadState.Loaded)
            return;
        clip.LoadAudioData();
    }

    public void PlayMusic(AudioClip clip, float fade = 0.5f)
    {
        SyncMixerVolumeFromSettings();
        if (clip == null) { StopMusic(fade); return; }
        EnsureClipReady(clip);
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
        PlayCricketAmbience(loop);
    }

    void PlayCricketAmbience(bool loop = true)
    {
        if (cricketAmbienceSource == null)
            cricketAmbienceSource = CreateChildSource("Cricket Ambience", ambienceGroup, loop: true);
        if (cricketAmbience == null)
        {
            cricketAmbienceSource.Stop();
            cricketAmbienceSource.clip = null;
            return;
        }

        EnsureClipReady(cricketAmbience);
        cricketAmbienceSource.loop = loop;
        cricketAmbienceSource.spatialBlend = 0f;
        cricketAmbienceSource.outputAudioMixerGroup = ambienceGroup;
        if (cricketAmbienceSource.clip != cricketAmbience)
        {
            cricketAmbienceSource.Stop();
            cricketAmbienceSource.clip = cricketAmbience;
        }
        if (!cricketAmbienceSource.isPlaying)
            cricketAmbienceSource.Play();
    }

    public void StopAmbience()
    {
        ambienceSource.Stop();
        if (cricketAmbienceSource != null)
            cricketAmbienceSource.Stop();
    }

    AudioClip GetSearchTensionClip()
    {
        if (tensionSFXBank == null || tensionSFXBank.Length <= SearchTensionClipIndex)
            return null;

        return tensionSFXBank[SearchTensionClipIndex];
    }

    void EnsureSearchTensionSource()
    {
        if (_searchTensionSource != null)
            return;

        _searchTensionSource = CreateChildSource("Search Tension", sfxGroup, loop: false);
    }

    /// <summary>0 = stop. 1 = full volume at end of search wait (scaled by capture chance in FSM).</summary>
    public void SetSearchTension(float intensity01)
    {
        var clip = GetSearchTensionClip();
        if (clip == null)
            return;

        intensity01 = Mathf.Clamp01(intensity01);
        if (intensity01 <= 0f)
        {
            StopSearchTension();
            return;
        }

        EnsureSearchTensionSource();

        if (_searchTensionSource.clip != clip)
        {
            _searchTensionSource.clip = clip;
            _searchTensionSource.time = 0f;
        }

        _searchTensionSource.volume = intensity01;
        if (!_searchTensionSource.isPlaying)
            _searchTensionSource.Play();
    }

    public void StopSearchTension()
    {
        if (_searchTensionSource == null)
            return;

        _searchTensionSource.Stop();
        _searchTensionSource.time = 0f;
    }

    /// <summary>Fades music and SFX mixer buses to silence. Ambience is left playing.</summary>
    public void FadeOutMusicAndSfx(float duration = 1.5f)
    {
        StopMusic(0.25f);
        StopSearchTension();
        _chaseMusicActive = false;
        _chaseMusicOverride = null;

        if (!mixer)
            return;

        StartCoroutine(FadeMixerParamsToSilence(duration, musicParam, sfxParam));
    }

    IEnumerator FadeMixerParamsToSilence(float duration, params string[] paramNames)
    {
        var startValues = new float[paramNames.Length];
        for (int i = 0; i < paramNames.Length; i++)
            mixer.GetFloat(paramNames[i], out startValues[i]);

        if (duration <= 0f)
        {
            for (int i = 0; i < paramNames.Length; i++)
                mixer.SetFloat(paramNames[i], -80f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < paramNames.Length; i++)
                mixer.SetFloat(paramNames[i], Mathf.Lerp(startValues[i], -80f, t));
            yield return null;
        }

        for (int i = 0; i < paramNames.Length; i++)
            mixer.SetFloat(paramNames[i], -80f);
    }

    public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (!clip) return;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume);
        sfxSource.pitch = 1f;
    }

    public void PlaySFXAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (!clip || threeDPool.Count == 0) return;

        var src = threeDPool.Dequeue();
        var go = src.gameObject;
        go.transform.position = position;
        go.SetActive(true);

        src.pitch = pitch;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.Play();
        // Extra tail so Steam Audio reflections aren't hard-stopped with the dry clip (audible cutoff).
        StartCoroutine(Return3DWhenDone(src, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch)) + 0.5f));
    }

    /// <summary>
    /// 3D one-shot that sticks to a moving transform (e.g. a bark that follows an NPC)
    /// instead of being left behind at the spawn position.
    /// </summary>
    public void PlaySFXFollowing(AudioClip clip, Transform follow, float volume = 1f, float pitch = 1f, Vector3 offset = default)
    {
        if (!clip || follow == null || threeDPool.Count == 0) return;

        var src = threeDPool.Dequeue();
        var go = src.gameObject;
        go.transform.position = follow.position + offset;
        go.SetActive(true);

        src.pitch = pitch;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.Play();
        StartCoroutine(FollowAndReturn3DWhenDone(src, follow, offset,
            clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch)) + 0.5f));
    }

    public void PlaySFXSafe(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        if (!IsClipPlayingAt(clip, position))
            PlaySFXAt(clip, position, volume, pitch);
    }

    void ResetPooledSource(AudioSource src)
    {
        src.pitch = 1f;
    }

    /// <summary>
    /// Legacy hook — forwards to <see cref="SyncMixerVolumeFromSettings"/>.
    /// </summary>
    public void ApplyMixerFromPrefs() => SyncMixerVolumeFromSettings();

    /// <summary>
    /// Push current settings onto the mixer immediately before any audio plays.
    /// Uses this manager's mixer reference so volume is applied even if
    /// GameSettingsManager ran too early in Awake.
    /// </summary>
    void SyncMixerVolumeFromSettings()
    {
        if (!mixer)
            return;

        GameSettingsManager.SettingsData settings =
            GameSettingsManager.Instance != null
                ? GameSettingsManager.Instance.CurrentSettings
                : GameSettingsManager.DefaultSettings;

        mixer.SetFloat(masterParam, Linear01ToDb(settings.masterVolume));
        mixer.SetFloat(musicParam, Linear01ToDb(settings.musicVolume));
        mixer.SetFloat(sfxParam, Linear01ToDb(settings.sfxVolume));
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

    private IEnumerator FollowAndReturn3DWhenDone(AudioSource src, Transform follow, Vector3 offset, float seconds)
    {
        float deadline = Time.time + Mathf.Max(0.05f, seconds);
        while (src != null && Time.time < deadline)
        {
            if (follow != null)
                src.transform.position = follow.position + offset;
            if (!src.isPlaying)
                break;
            yield return null;
        }
        yield return Return3DWhenDone(src, 0f);
    }

    private IEnumerator Return3DWhenDone(AudioSource src, float seconds)
    {
        float deadline = Time.time + Mathf.Max(0.05f, seconds);
        while (src != null && src.isPlaying && Time.time < deadline)
            yield return null;
        if (src == null)
            yield break;
        // Short fade instead of a hard Stop (avoids click / cut-off on door opens).
        float startVol = src.volume;
        for (float t = 0f; t < 0.08f && src != null; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(startVol, 0f, t / 0.08f);
            yield return null;
        }
        if (src == null)
            yield break;
        src.Stop();
        src.volume = startVol;
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
        float targetVol = _musicTargetVolume;
        if (newClip != null)
        {
            EnsureClipReady(newClip);
            // Never assign+Play a still-loading / not-yet-loaded clip — that freezes the game
            // (Streaming first buffer and DecompressOnLoad decode both hitch on the main thread).
            float wait = 0f;
            while (newClip.loadState != AudioDataLoadState.Loaded && wait < 2f)
            {
                if (newClip.loadState == AudioDataLoadState.Unloaded)
                    EnsureClipReady(newClip);
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
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
            if (newClip != null) to.volume = Mathf.Lerp(0f, targetVol, k);
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
            to.volume = targetVol;
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

        SyncMixerVolumeFromSettings();

        bool isMenu = IsMenuScene(scene.name);
        _inGameplayScene = !isMenu;
        _gameplayMusicReady = false;
        _player = null;
        _lastPlayerLayer = -1;
        _playerOnCaptiveLayer = false;
        _chaseMusicActive = false;
        _chaseMusicOverride = null;
        _zoneMusicActive = false;
        _zoneMusicOverride = null;
        _zoneMusicVolume = 1f;
        _musicTargetVolume = 1f;
        _playerInBasement = true;
        _returnScareAudioLock = false;
        _ambienceVolumeMul = 1f;
        _wasPlayerHiddenForMusic = false;

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
        // Kick loads before the first Play so large beds never decompress mid-frame.
        EnsureClipReady(defaultAmbience);
        EnsureClipReady(cricketAmbience);
        EnsureClipReady(gameMusic);
        EnsureClipReady(chaseMusic);
        EnsureClipReady(captiveLayerMusic);
        EnsureClipReady(menuMusic);

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

        RefreshBasementMusicState(force: true);
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
        bool hidden = layer == hiddenLayerIndex;

        // Hiding uses a stealth layer only — never cut or swap music for it.
        if (hidden)
        {
            _wasPlayerHiddenForMusic = true;
            return;
        }

        if (_wasPlayerHiddenForMusic)
        {
            _wasPlayerHiddenForMusic = false;
            force = true;
        }

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
        _musicTargetVolume = _zoneMusicActive ? _zoneMusicVolume : 1f;

        if (clip == null)
        {
            // No exploration track: let chase music fade out instead of looping on after the hunt.
            if (CurrentMusicClip != null && !(IsPlayerHiddenForMusic() && _chaseMusicActive))
            {
                float fade = _returnScareAudioLock ? returnScareMusicFadeOut : gameplayMusicFade;
                StopMusic(fade);
            }
            return;
        }

        if (CurrentMusicClip == clip)
        {
            // Same bed, maybe a volume change (e.g. zone volume).
            var active = usingA ? musicSourceA : musicSourceB;
            if (active != null && active.isPlaying
                && !Mathf.Approximately(active.volume, _musicTargetVolume)
                && musicFadeRoutine == null)
                active.volume = _musicTargetVolume;
            return;
        }

        // Hidden: only allow tension music to start — never fade to exploration/captive mid-hide.
        if (IsPlayerHiddenForMusic() && !_chaseMusicActive)
            return;

        PlayMusic(clip, gameplayMusicFade);
    }

    void RefreshBasementMusicState(bool force = false)
    {
        if (_player == null)
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                _player = playerGo.transform;
        }
        if (_player == null)
            return;

        // Room vote — not world Y. The yard / car sit near basement height and used to keep the bed on.
        bool inBasement = InteractionController.IsPlayerInBasementRoom(_player.position, basementRoomZoneId);
        if (!force && inBasement == _playerInBasement)
            return;
        _playerInBasement = inBasement;
        if (force)
            return;
        ApplyGameplayMusic();
    }

    bool IsPlayerHiddenForMusic()
    {
        return _player != null && _player.gameObject.layer == hiddenLayerIndex;
    }

    AudioClip ResolveGameplayMusicClip()
    {
        if (_chaseMusicActive && _chaseMusicOverride != null)
            return _chaseMusicOverride;
        if (_chaseMusicActive && chaseMusic != null)
            return chaseMusic;

        // Active zone with a null clip = intentional silence (post-scare hold).
        if (_zoneMusicActive)
            return _zoneMusicOverride;

        // Cage: only play an explicit captive track — never fall through to basement music.
        if (_playerOnCaptiveLayer)
            return captiveLayerMusic;

        // Return scare: no basement bed in corridor/cage until lock clears.
        if (_returnScareAudioLock)
            return null;

        // Outdoors / above-ground: no bed. Basement only.
        if (!_playerInBasement)
            return null;

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
