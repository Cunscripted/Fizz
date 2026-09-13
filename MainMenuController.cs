using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main menu scene controller: Start Game loads the gameplay scene via
/// SceneNavigator; Credits shows a LerpPanel with its own close button.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Buttons")]
    public Button startGameButton;
    public Button creditsButton;
    public Button closeCreditsButton;

    [Header("Scene")]
    public SceneNavigator sceneNavigator;
    public string gameplaySceneName;

    [Header("Credits")]
    public LerpPanel creditsPanel;

    private void Awake()
    {
        if (startGameButton != null) startGameButton.onClick.AddListener(StartGame);
        if (creditsButton != null) creditsButton.onClick.AddListener(OpenCredits);
        if (closeCreditsButton != null) closeCreditsButton.onClick.AddListener(CloseCredits);
    }

    public void StartGame()
    {
        if (sceneNavigator == null)
        {
            Debug.LogError($"[MainMenuController] '{name}' has no sceneNavigator assigned - can't start the game.", this);
            return;
        }
        sceneNavigator.LoadScene(gameplaySceneName);
    }

    public void OpenCredits()
    {
        if (creditsPanel != null) creditsPanel.Show();
    }

    public void CloseCredits()
    {
        if (creditsPanel != null) creditsPanel.Hide();
    }
}
