using UnityEngine;

public class GameMenuController : MonoBehaviour
{
    public GameObject mainMenuPanel;

    public void StartGame()
    {
        Time.timeScale = 1f;
        mainMenuPanel.SetActive(false);
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;
        mainMenuPanel.SetActive(false);
    }

    public void OpenMenu()
    {
        mainMenuPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}