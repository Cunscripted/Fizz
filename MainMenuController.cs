using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main menu scene controller: Start Game loads the gameplay scene via
/// SceneNavigator; Credits shows a LerpPanel with its own close button; Reset
/// Tutorial clears GameplayIntroController's "already seen" flag so the one-time
/// intro (video + rules prompt) plays again the next time the gameplay scene loads,
/// letting the player rewatch it on demand instead of only ever seeing it once.
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

    [Header("Reset Tutorial")]
    [Tooltip("Resets the one-time gameplay intro (video + rules prompt) so it plays again the next time the gameplay scene loads.")]
    public Button resetTutorialButton;
    [Tooltip("Optional - updated with resetTutorialConfirmationMessage when the button is pressed, so the player gets some feedback that it actually did something.")]
    public TMP_Text resetTutorialConfirmationText;
    [TextArea]
    public string resetTutorialConfirmationMessage = "Tutorial reset - it'll play again next time you start a game.";

    private void Awake()
    {
        if (startGameButton != null) startGameButton.onClick.AddListener(StartGame);
        if (creditsButton != null) creditsButton.onClick.AddListener(OpenCredits);
        if (closeCreditsButton != null) closeCreditsButton.onClick.AddListener(CloseCredits);
        if (resetTutorialButton != null) resetTutorialButton.onClick.AddListener(ResetTutorial);
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

    /// <summary>Wired to resetTutorialButton - clears GameplayIntroController's PlayerPrefs flag directly (no gameplay scene/component reference needed, since this runs from the main menu).</summary>
    public void ResetTutorial()
    {
        GameplayIntroController.ResetIntroStatus();

        if (resetTutorialConfirmationText != null)
            resetTutorialConfirmationText.text = resetTutorialConfirmationMessage;

        Debug.Log("[MainMenuController] Tutorial/intro status reset - it will play again on the next gameplay scene load.");
    }
}