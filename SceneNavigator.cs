using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tiny reusable scene-loading utility. Wire a Button's OnClick directly to
/// LoadScene(string) with the target scene name typed into the Inspector's
/// argument field, or call it from code. Shared by the main menu (Start Game)
/// and the gameplay scene (a "Main Menu" button, wherever you put it - doesn't
/// have to live inside the pause menu specifically).
/// </summary>
public class SceneNavigator : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneNavigator] LoadScene called with an empty scene name.", this);
            return;
        }
        SceneManager.LoadScene(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
