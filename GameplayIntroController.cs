using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays a one-time intro video the FIRST time the gameplay scene is ever loaded
/// (tracked via PlayerPrefs, so it survives app restarts). Once the video ends (or is
/// skipped), optionally asks the player via rulesPromptPanel whether they'd like to
/// read how the game works - answering yes opens rulesPanel (a LerpPanel with the
/// rules explanation), answering no (or having no prompt configured at all) skips
/// straight to gameplay. The round doesn't begin until this whole sequence resolves.
/// Every load after the first goes straight to gameplay with none of this.
///
/// Setup: place this alongside RoundManager in the gameplay scene and point
/// RoundManager.introController back at it - that's what stops RoundManager from
/// auto-calling BeginRound() in its own Start() while an intro is about to play.
/// Wire videoOverlay to a full-screen Canvas/panel that sits above the rest of the
/// gameplay UI (a RawImage target-textured by videoPlayer, or a VideoPlayer in
/// CameraNearPlane/CameraFarPlane render mode covering the same canvas both work -
/// this script only handles show/hide, playback, and the temporary music ducking,
/// not how the video itself gets on screen).
///
/// Music ducking: while the video plays, MusicManager's volume is lowered to
/// duckedMusicVolume and restored the moment the video phase ends (before the rules
/// prompt/panel, so ducking only applies to the video itself, not the reading step).
/// </summary>
public class GameplayIntroController : MonoBehaviour
{
    public const string HasSeenIntroKey = "Fizz_HasSeenGameplayIntro";

    [Header("Scene References")]
    public RoundManager roundManager;
    public VideoPlayer videoPlayer;
    [Tooltip("Full-screen GameObject (Canvas/panel) hosting the video - enabled only while the intro plays, disabled the instant it ends.")]
    public GameObject videoOverlay;

    [Header("Music Ducking")]
    [Tooltip("Volume the background music is temporarily lowered to while the intro video plays (0-1). Restored once the video phase ends (video finished or skipped).")]
    [Range(0f, 1f)] public float duckedMusicVolume = 0.1f;

    [Header("Skip Video (optional)")]
    [Tooltip("Optional - lets the player skip straight past the video instead of waiting it out. Still leads into the rules prompt below, same as letting it play out.")]
    public Button skipButton;

    [Header("Rules Prompt (optional)")]
    [Tooltip("Optional - shown right after the video ends, asking whether the player wants to read the rules. Leave unset to skip straight to gameplay with no prompt at all.")]
    public LerpPanel rulesPromptPanel;
    [Tooltip("Button on rulesPromptPanel - opens rulesPanel.")]
    public Button rulesPromptYesButton;
    [Tooltip("Button on rulesPromptPanel - declines and goes straight to gameplay.")]
    public Button rulesPromptNoButton;

    [Header("Rules Panel (optional)")]
    [Tooltip("LerpPanel containing the rules explanation, opened if the player answers yes to the prompt above.")]
    public LerpPanel rulesPanel;
    [Tooltip("Optional - populated with rulesText on Start if assigned, so you don't have to author the copy separately in the Inspector.")]
    public TMP_Text rulesBodyText;
    [Tooltip("Button on rulesPanel - closes it and starts gameplay.")]
    public Button closeRulesButton;
    [TextArea(6, 14)]
    public string rulesText =
        "HOW TO PLAY\n\n" +
        "Drag additives off the belt and into the bottle, then hit Brew to score the mix.\n\n" +
        "You get several attempts each round - every attempt's score ADDS to your cumulative " +
        "total, which needs to reach the round's requirement before you run out of attempts.\n\n" +
        "Clear the requirement and you'll be offered new additives for your collection in the " +
        "shop. Every few rounds, you'll get a permanent syrup modifier to pick first instead.\n\n" +
        "Matching flavors and specific additives together can trigger combos for bonus points " +
        "or mult - keep an eye on the Active Combos panel to see what's currently working.";

    [Header("Testing")]
    [Tooltip("Editor-only: forces the intro to play every time regardless of whether PlayerPrefs says it's already been seen. Has no effect in a build.")]
    public bool forceReplayInEditor = false;

    /// <summary>
    /// True if the intro is about to play this load. Set in Awake() (which always
    /// runs before any Start()) so RoundManager.Start() can reliably check it
    /// regardless of script execution order between the two components.
    /// </summary>
    public bool WillPlayIntro { get; private set; }

    /// <summary>
    /// Clears the "already seen" flag so the intro (video + rules prompt) plays again
    /// the next time the gameplay scene loads - wire to a "Reset Tutorial" button on
    /// the main menu to let players rewatch it on demand. Safe to call at any time,
    /// including while this controller isn't active/loaded (e.g. from the main menu
    /// scene, where the gameplay scene - and this component - doesn't exist yet).
    /// </summary>
    public static void ResetIntroStatus()
    {
        PlayerPrefs.DeleteKey(HasSeenIntroKey);
        PlayerPrefs.Save();
    }

    private float _previousMusicVolume = -1f;
    private bool _videoPhaseEnded;
    private bool _introEnded;

    private void Awake()
    {
        bool alreadySeen = PlayerPrefs.GetInt(HasSeenIntroKey, 0) == 1;
#if UNITY_EDITOR
        if (forceReplayInEditor) alreadySeen = false;
#endif
        WillPlayIntro = !alreadySeen && videoPlayer != null;

        if (videoOverlay != null) videoOverlay.SetActive(WillPlayIntro);

        if (videoPlayer != null)
        {
            // VideoPlayer components default to "Play On Awake" checked in the Inspector,
            // which auto-starts playback (audio included) on every scene load regardless of
            // WillPlayIntro - the overlay being hidden only hides it visually, the audio
            // still plays. Take explicit control here, before that auto-play would normally
            // kick in, so playback only ever happens via our own Play() call below.
            videoPlayer.playOnAwake = false;
            if (!WillPlayIntro)
                videoPlayer.Stop(); // belt-and-braces in case it already started before this ran
        }
    }

    private void Start()
    {
        if (!WillPlayIntro) return;

        if (roundManager == null)
            Debug.LogError($"[GameplayIntroController] '{name}' has no roundManager assigned - can't hand off to gameplay once the intro ends.", this);
        if (videoOverlay == null)
            Debug.LogWarning($"[GameplayIntroController] '{name}' has no videoOverlay assigned - the video will play with nothing shown/hidden on screen for it.", this);

        if (rulesBodyText != null) rulesBodyText.text = rulesText;

        if (skipButton != null) skipButton.onClick.AddListener(SkipVideo);
        if (rulesPromptYesButton != null) rulesPromptYesButton.onClick.AddListener(OnRulesPromptYes);
        if (rulesPromptNoButton != null) rulesPromptNoButton.onClick.AddListener(OnRulesPromptNo);
        if (closeRulesButton != null) closeRulesButton.onClick.AddListener(OnRulesClosed);

        DuckMusic();

        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.Play();
    }

    private void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoFinished;
        if (skipButton != null) skipButton.onClick.RemoveListener(SkipVideo);
        if (rulesPromptYesButton != null) rulesPromptYesButton.onClick.RemoveListener(OnRulesPromptYes);
        if (rulesPromptNoButton != null) rulesPromptNoButton.onClick.RemoveListener(OnRulesPromptNo);
        if (closeRulesButton != null) closeRulesButton.onClick.RemoveListener(OnRulesClosed);
    }

    private void OnVideoFinished(VideoPlayer source) => EndVideoPhase();

    /// <summary>Wire to a "Skip" button if you want to let players bypass the video itself (the rules prompt still follows).</summary>
    public void SkipVideo() => EndVideoPhase();

    /// <summary>Ends the video (stop/hide/restore music) and moves on to the rules prompt, or straight to gameplay if none is configured.</summary>
    private void EndVideoPhase()
    {
        // Guards against both the skip button AND loopPointReached firing for the same playthrough.
        if (_videoPhaseEnded) return;
        _videoPhaseEnded = true;

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.Stop();
        }
        if (videoOverlay != null) videoOverlay.SetActive(false);

        RestoreMusic();

        PlayerPrefs.SetInt(HasSeenIntroKey, 1);
        PlayerPrefs.Save();

        if (rulesPromptPanel != null)
            rulesPromptPanel.Show();
        else
            EndIntro(); // no prompt configured - go straight to gameplay
    }

    private void OnRulesPromptYes()
    {
        rulesPromptPanel?.Hide();
        if (rulesPanel != null)
            rulesPanel.Show();
        else
            EndIntro(); // no rules panel configured despite saying yes - don't get stuck, just start the game
    }

    private void OnRulesPromptNo()
    {
        rulesPromptPanel?.Hide();
        EndIntro();
    }

    /// <summary>Wire to the rules panel's close button.</summary>
    private void OnRulesClosed()
    {
        rulesPanel?.Hide();
        EndIntro();
    }

    private void EndIntro()
    {
        if (_introEnded) return;
        _introEnded = true;

        if (roundManager != null) roundManager.BeginRound();
    }

    private void DuckMusic()
    {
        if (MusicManager.Instance == null || MusicManager.Instance.musicSource == null) return;
        _previousMusicVolume = MusicManager.Instance.musicSource.volume;
        MusicManager.Instance.SetVolume(duckedMusicVolume);
    }

    private void RestoreMusic()
    {
        if (MusicManager.Instance == null || _previousMusicVolume < 0f) return;
        MusicManager.Instance.SetVolume(_previousMusicVolume);
        _previousMusicVolume = -1f;
    }
}