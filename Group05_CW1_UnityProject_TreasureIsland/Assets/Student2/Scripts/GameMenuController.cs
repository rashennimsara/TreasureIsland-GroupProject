using UnityEngine;

public class GameMenuController : MonoBehaviour
{
    public GameObject mainMenuPanel;

    void Start()
    {
        Time.timeScale = 0f;
        mainMenuPanel.SetActive(true);
        ShowCursor();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (mainMenuPanel.activeSelf)
            {
                ResumeGame();
            }
            else
            {
                OpenMenu();
            }
        }
    }

    public void StartGame()
    {
        mainMenuPanel.SetActive(false);
        Time.timeScale = 1f;
        HideCursor();
    }

    public void ResumeGame()
    {
        mainMenuPanel.SetActive(false);
        Time.timeScale = 1f;
        HideCursor();
    }

    public void OpenMenu()
    {
        mainMenuPanel.SetActive(true);
        Time.timeScale = 0f;
        ShowCursor();
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    private void HideCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}