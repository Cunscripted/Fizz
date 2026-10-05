using System.Collections;
using UnityEngine;

/// <summary>
/// Persistent background music player - survives scene loads via DontDestroyOnLoad,
/// so music keeps playing seamlessly between the main menu and gameplay scenes
/// instead of restarting or cutting out on every SceneManager.LoadScene call.
///
/// Singleton: if a second MusicManager loads in with a new scene (e.g. one placed
/// in both the main menu and gameplay scenes for convenience), the newer one
/// destroys itself and the original keeps playing - so you don't get overlapping
/// duplicate music tracks.
///
/// Put this on a GameObject in whichever scene loads FIRST (usually the main menu).
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    public AudioSource musicSource;
    [Tooltip("Played automatically on startup if assigned and musicSource isn't already playing something.")]
    public AudioClip defaultTrack;

    [Header("Game Over Slowdown")]
    [Tooltip("Pitch the music eases down to on game over - 1 = normal speed, lower = slower/deeper, dragging things to a halt.")]
    [Range(0.1f, 1f)]
    public float gameOverPitch = 0.5f;
    public float gameOverSlowdownDuration = 2f;

    [Header("Custom Loop Point With Fade")]
    [Tooltip("If on, instead of AudioSource's built-in seamless loop, the track fades OUT starting at " +
             "fadeOutStartTime, restarts from the beginning once the fade-out completes, and fades back IN - " +
             "a soft crossfade-style loop instead of an abrupt repeat. Useful when the clip doesn't loop " +
             "cleanly on its own. Turns off musicSource.loop automatically while this is on.")]
    public bool useCustomLoopFade = false;
    [Tooltip("Time (seconds into the clip) where the fade-out begins.")]
    public float fadeOutStartTime = 0f;
    [Tooltip("How long the fade-out (and the matching fade-in after restarting) takes.")]
    public float fadeDuration = 2f;

    private Coroutine _pitchRoutine;
    private Coroutine _loopFadeRoutine;
    private float _baseVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        if (musicSource != null) _baseVolume = musicSource.volume;

        if (defaultTrack != null && musicSource != null && !musicSource.isPlaying)
            PlayTrack(defaultTrack);
    }

    /// <summary>Starts (or switches to) a track. Resets pitch back to normal first - call ResetPitch() separately if you don't want that.</summary>
    public void PlayTrack(AudioClip clip, bool loop = true)
    {
        if (musicSource == null || clip == null) return;

        _baseVolume = musicSource.volume > 0f ? musicSource.volume : _baseVolume;
        musicSource.clip = clip;
        musicSource.loop = useCustomLoopFade ? false : loop;
        musicSource.pitch = 1f;
        musicSource.volume = _baseVolume;
        musicSource.Play();

        if (_loopFadeRoutine != null)
        {
            StopCoroutine(_loopFadeRoutine);
            _loopFadeRoutine = null;
        }
        if (useCustomLoopFade)
            _loopFadeRoutine = StartCoroutine(CustomLoopFadeRoutine());
    }

    public void SetVolume(float volume)
    {
        if (musicSource != null) musicSource.volume = volume;
    }

    /// <summary>Call on game over - eases the music's pitch down for a dramatic slowdown, rather than snapping instantly.</summary>
    public void PlayGameOverSlowdown()
    {
        if (musicSource == null) return;
        if (_pitchRoutine != null) StopCoroutine(_pitchRoutine);
        _pitchRoutine = StartCoroutine(SlowdownRoutine());
    }

    /// <summary>Resets pitch back to normal - call when starting a fresh run (Retry / Main Menu).</summary>
    public void ResetPitch()
    {
        if (_pitchRoutine != null)
        {
            StopCoroutine(_pitchRoutine);
            _pitchRoutine = null;
        }
        if (musicSource != null) musicSource.pitch = 1f;
    }

    private IEnumerator SlowdownRoutine()
    {
        float start = musicSource.pitch;
        float t = 0f;

        // Unscaled time so the slowdown still plays out smoothly even if something
        // else (a pause menu, a game-over freeze) sets Time.timeScale to 0.
        while (t < gameOverSlowdownDuration)
        {
            t += Time.unscaledDeltaTime;
            musicSource.pitch = Mathf.Lerp(start, gameOverPitch, t / gameOverSlowdownDuration);
            yield return null;
        }

        musicSource.pitch = gameOverPitch;
        _pitchRoutine = null;
    }

    /// <summary>
    /// Runs for the lifetime of the current track while useCustomLoopFade is on: waits
    /// until playback reaches fadeOutStartTime, fades volume to 0, jumps back to the
    /// start, then fades back up to _baseVolume - repeating for as long as this track plays.
    /// </summary>
    private IEnumerator CustomLoopFadeRoutine()
    {
        while (true)
        {
            // Wait until we reach the fade-out point (or the clip naturally ends first,
            // for a track shorter than fadeOutStartTime - bail out rather than loop forever).
            while (musicSource.isPlaying && musicSource.time < fadeOutStartTime)
                yield return null;

            if (!musicSource.isPlaying) yield break;

            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(_baseVolume, 0f, t / fadeDuration);
                yield return null;
            }
            musicSource.volume = 0f;

            musicSource.time = 0f;
            if (!musicSource.isPlaying) musicSource.Play();

            t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(0f, _baseVolume, t / fadeDuration);
                yield return null;
            }
            musicSource.volume = _baseVolume;
        }
    }
}