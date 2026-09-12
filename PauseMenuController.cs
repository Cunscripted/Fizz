using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pause button + lerping pause menu with Resume / Main Menu / Deck Stats.
/// Pausing here just shows the menu and (optionally) freezes gameplay via
/// Time.timeScale - LerpPanel itself always animates on unscaled time, so the
/// pause menu keeps animating smoothly even while everything else is frozen.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("References")]
    public Button pauseButton;
    public LerpPanel pausePanel;
    public Button resumeButton;
    public Button mainMenuButton;
    public Button deckStatsButton;
    public DeckStatsPanel deckStatsPanel;

    [Header("Main Menu")]
    [Tooltip("Scene to load when Main Menu is pressed. Leave blank if you'd rather handle " +
             "OnMainMenuRequested yourself (e.g. a non-scene-based main menu).")]
    public string mainMenuSceneName;
    public UnityEvent OnMainMenuRequested;

    [Header("Options")]
    [Tooltip("Freezes Time.timeScale while paused. Gameplay coroutines using scaled Time.deltaTime " +
             "will pause with it; LerpPanel animations are unaffected since they use unscaled time.")]
    public bool freezeTimeScale = true;

    private void Awake()
    {
        if (pauseButton != null) pauseButton.onClick.AddListener(Open);
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
        if (deckStatsButton != null) deckStatsButton.onClick.AddListener(OpenDeckStats);
    }

    public void Open()
    {
        pausePanel.Show();
        if (freezeTimeScale) Time.timeScale = 0f;
    }

    public void Close()
    {
        pausePanel.Hide();
        if (freezeTimeScale) Time.timeScale = 1f;
    }

    private void OpenDeckStats()
    {
        if (deckStatsPanel != null) deckStatsPanel.Show();
    }

    private void GoToMainMenu()
    {
        if (freezeTimeScale) Time.timeScale = 1f; // don't leave the next scene permanently frozen
        if (!string.IsNullOrEmpty(mainMenuSceneName))
            SceneManager.LoadScene(mainMenuSceneName);
        OnMainMenuRequested?.Invoke();
    }
}
