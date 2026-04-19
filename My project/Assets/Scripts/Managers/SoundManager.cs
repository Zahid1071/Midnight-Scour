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
    [Tooltip("Scenes considered 'menus' will play menuMusic. Others play gameMusic + ambience.")]
    [SerializeField] private string[] menuSceneNames = new[] { "Main Menu", "Menu", "Title" };

    [Header("Clips")]
    public AudioClip menuMusic;
    public AudioClip gameMusic;
    public AudioClip defaultAmbience;
    [Header("UI Clips")]
    public AudioClip uiClickClip;
    public AudioClip uiHoverClip; // optional

    [Header("Optional banks")]
    public AudioClip[] musicBank;
    public AudioClip[] ambienceBank;
    public AudioClip[] sfxBank;
    public AudioClip[] tensionSFXBank;
    public AudioClip[] footstepClips;

    [Header("Runtime AudioSources (auto-created if null)")]
    [SerializeField] private AudioSource musicSourceA;
    [SerializeField] private AudioSource musicSourceB; // for crossfades
    [SerializeField] private AudioSource ambienceSource;
    [SerializeField] private AudioSource sfxSource;    // 2D one-shots

    [Header("3D SFX Pool")]
    [SerializeField] private int poolSize = 8;
    [SerializeField] private float threeDSpatialBlend = 1f; // full 3D
    private readonly Queue<AudioSource> threeDPool = new();

    private Coroutine musicFadeRoutine;
    private bool usingA = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureSources();
        Build3DPool();

        // listen scene changes
        SceneManager.activeSceneChanged += OnSceneChanged;

        // pull volumes from PlayerPrefs once at boot (your settings menu can call ApplyMixerFromPrefs() again on Apply)
        ApplyMixerFromPrefs();
    }



    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.activeSceneChanged -= OnSceneChanged;
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
            src.spatialBlend = threeDSpatialBlend; // full 3D
            src.rolloffMode = AudioRolloffMode.Linear;
            go.SetActive(false);
            threeDPool.Enqueue(src);
        }
    }

    // ---------- PUBLIC API ----------

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
        if (!clip || threeDPool.Count == 0) return;
        var src = threeDPool.Dequeue();
        var go = src.gameObject;
        go.transform.position = position;
        go.SetActive(true);

        src.pitch = pitch;
        src.clip = clip;
        src.volume = volume;
        src.Play();
        StartCoroutine(Return3DWhenDone(src, clip.length / Mathf.Abs(pitch)));
    }

    public void PlaySFXSafe(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        // Eğer bu ses, bu pozisyonda zaten çalıyorsa tekrar çalma (Anti-Spam)
        if (!IsClipPlayingAt(clip, position))
        {
            PlaySFXAt(clip, position, volume, pitch);
        }
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

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        // decide what to play
        bool isMenu = IsMenuScene(newScene.name);
        if (isMenu)
        {
            PlayAmbience(null);
            PlayMusic(menuMusic, 0.4f);
        }
        else
        {
            PlayMusic(gameMusic, 0.6f);
            PlayAmbience(defaultAmbience != null ? defaultAmbience : (ambienceBank != null && ambienceBank.Length > 0 ? ambienceBank[0] : null), true);
        }
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
