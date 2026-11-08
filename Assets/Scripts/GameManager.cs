using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the game state: playing, paused, won, lost.
/// Put it on an empty GameObject called "GameManager" (only once per scene).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Goal")]
    [SerializeField] private int eggsToCollect = 5;

    [Header("UI (optional)")]
    [SerializeField] private Text eggCounterText;
    [SerializeField] private Text healthText;
    [SerializeField] private GameObject pauseScreen;
    [SerializeField] private GameObject winScreen;
    [SerializeField] private GameObject loseScreen;

    [Header("Restart")]
    [SerializeField] private float reloadDelay = 2f;

    public bool IsPlaying { get; private set; } = true;
    public bool IsGameOver { get; private set; }
    public int CollectedEggs { get; private set; }
    public int EggsToCollect => eggsToCollect;
    public bool HasAllEggs => CollectedEggs >= eggsToCollect;

    private float reloadTimer = -1f;

    private void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        if (pauseScreen != null) pauseScreen.SetActive(false);
        if (winScreen != null) winScreen.SetActive(false);
        if (loseScreen != null) loseScreen.SetActive(false);
        UpdateEggText();
    }

    private void Update()
    {
        // Esc toggles the pause menu
        if (!IsGameOver && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsPlaying) PauseGame();
            else ResumeGame();
        }

        // Reload the level after a delay (unscaled, so it also works while time is stopped)
        if (reloadTimer >= 0f)
        {
            reloadTimer -= Time.unscaledDeltaTime;
            if (reloadTimer <= 0f)
            {
                reloadTimer = -1f;
                ReloadGame();
            }
        }
    }

    public void PauseGame()
    {
        IsPlaying = false;
        Time.timeScale = 0f;
        if (pauseScreen != null && !IsGameOver) pauseScreen.SetActive(true);
    }

    public void ResumeGame()
    {
        if (IsGameOver) return;
        IsPlaying = true;
        Time.timeScale = 1f;
        if (pauseScreen != null) pauseScreen.SetActive(false);
    }

    public void StopGame()
    {
        IsPlaying = false;
        IsGameOver = true;
        Time.timeScale = 0f;
    }

    public void ReloadGame()
    {
        Time.timeScale = 1f;
        // The scene must be in File > Build Profiles > Scene List, otherwise this fails
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void AddEgg()
    {
        CollectedEggs++;
        UpdateEggText();
    }

    public void UpdateHealth(int currentHealth, int maxHealth)
    {
        if (healthText != null) healthText.text = $"Health: {currentHealth}/{maxHealth}";
    }

    public void WinGame()
    {
        if (IsGameOver) return;
        StopGame();
        if (winScreen != null) winScreen.SetActive(true);
        reloadTimer = reloadDelay + 1f;
    }

    public void LoseGame()
    {
        if (IsGameOver) return;
        StopGame();
        if (loseScreen != null) loseScreen.SetActive(true);
        reloadTimer = reloadDelay;
    }

    private void UpdateEggText()
    {
        if (eggCounterText != null) eggCounterText.text = $"Eggs: {CollectedEggs}/{eggsToCollect}";
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
