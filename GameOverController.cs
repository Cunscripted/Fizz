using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows a lerping game-over panel when RoundManager fires OnGameOver, displaying
/// the round reached and offering Retry / Main Menu. Also tells MusicManager (if
/// present in the scene) to ease the music's pitch down for a dramatic slowdown -
/// see MusicManager.PlayGameOverSlowdown().
/// </summary>
public class GameOverController : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public TMP_Text roundReachedText;
    public Button retryButton;
    public Button mainMenuButton;

    [Header("Scene Navigation")]
    public SceneNavigator sceneNavigator;
    [Tooltip("Scene to reload for Retry - usually the gameplay scene itself.")]
    public string gameplaySceneName;
    public string mainMenuSceneName;

    [Header("Options")]
    [Tooltip("Freezes Time.timeScale while the game-over panel is up. Not required - RoundManager already " +
             "stops processing brews once it's in the GameOver state, so gameplay halts on its own regardless.")]
    public bool freezeTimeScale = false;

    private void Awake()
    {
        if (retryButton != null) retryButton.onClick.AddListener(Retry);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
    }

    private void OnEnable()
    {
        if (roundManager != null) roundManager.OnGameOver.AddListener(ShowGameOver);
    }

    private void OnDisable()
    {
        if (roundManager != null) roundManager.OnGameOver.RemoveListener(ShowGameOver);
    }

    private void ShowGameOver()
    {
        if (roundReachedText != null && roundManager != null)
            roundReachedText.text = $"You reached Round {roundManager.RoundNumber}";

        if (MusicManager.Instance != null)
            MusicManager.Instance.PlayGameOverSlowdown();

        if (freezeTimeScale) Time.timeScale = 0f;

        panel?.Show();
    }

    private void Retry()
    {
        Cleanup();
        if (sceneNavigator != null) sceneNavigator.LoadScene(gameplaySceneName);
    }

    private void GoToMainMenu()
    {
        Cleanup();
        if (sceneNavigator != null) sceneNavigator.LoadScene(mainMenuSceneName);
    }

    private void Cleanup()
    {
        if (freezeTimeScale) Time.timeScale = 1f; // don't leave the next scene permanently frozen
        if (MusicManager.Instance != null) MusicManager.Instance.ResetPitch();
    }
}
