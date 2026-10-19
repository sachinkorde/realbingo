using UnityEngine;

public class GameSceneHandler : MonoBehaviour
{
    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Game is exiting...");
    }
}
