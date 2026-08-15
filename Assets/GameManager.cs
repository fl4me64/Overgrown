using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("UI Text References")]
    public TextMeshProUGUI percentageText;
    public TextMeshProUGUI levelCompleteText;
    public TextMeshProUGUI gameOverStatsText;

    [Header("UI Screen Panels")]
    public GameObject levelCompletePanel;

    [Header("References")]
    public LevelTimer levelTimer;

    private int totalGrassCount;
    private int currentGrassCount;    
    private bool isGameActive = true;
    public bool IsGameActive => isGameActive;

    public AudioClip levelCompleteSoundClip;
    public AudioSource sfxSource;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        AudioListener.pause = false;

        // Finds all grass tiles tagged "Grass" at level start
        // totalGrassCount = GameObject.FindGameObjectsWithTag("Grass").Length;
        totalGrassCount = FindObjectsByType<Grass>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        Debug.Log(totalGrassCount);
        currentGrassCount = totalGrassCount;

        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);
        UpdatePercentageUI();
    }    

    public void GrassMowed()
    {
        if (!isGameActive) return;

        currentGrassCount--;
        UpdatePercentageUI();

        Debug.Log($"Grass Remaining: {currentGrassCount}");

        if (currentGrassCount <= 0)
        {
            StartCoroutine(LevelCompleteSequence());
        }
    }

    // Inverse of GrassMowed()
    public void GrassRegrown()
    {
        if (!isGameActive) return;

        if (currentGrassCount >= totalGrassCount) return;

        currentGrassCount++;
        UpdatePercentageUI();
    }

    void UpdatePercentageUI()
    {
        if (percentageText != null)
        {
            percentageText.text = $"{GetPercentageMowed():F0}%";
        }
    }

    public float GetPercentageMowed()
    {
        if (totalGrassCount == 0) return 0f;

        if (currentGrassCount > 0)
        {
            float rawPercent = ((float)(totalGrassCount - currentGrassCount) / totalGrassCount) * 100f;
            return Mathf.Min(rawPercent, 99f);
        }

        return 100f;
    }

    public string GetFormattedTime()
    {
        if (levelTimer == null) return "00:00";

        float elapsed = levelTimer.GetElapsedTime();
        int minutes = Mathf.FloorToInt(elapsed / 60f);
        int seconds = Mathf.FloorToInt(elapsed % 60f);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    private IEnumerator LevelCompleteSequence()
    {
        isGameActive = false;

        // Stop the level timer
        if (levelTimer != null) levelTimer.SetTimerRunning(false);

        // Freeze world physics execution 
        Time.timeScale = 0f;

        // One-second visual freeze
        yield return new WaitForSecondsRealtime(1f);

        if (levelCompleteSoundClip != null && sfxSource != null)
        {
            sfxSource.ignoreListenerPause = true;
            sfxSource.PlayOneShot(levelCompleteSoundClip);
            AudioListener.pause = true;
            yield return new WaitForSecondsRealtime(levelCompleteSoundClip.length);
            sfxSource.ignoreListenerPause = false;
        }
        else
        {
            AudioListener.pause = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Display Level Complete Panel
        if (levelCompletePanel != null)
        {
            levelCompletePanel.SetActive(true);
            if (levelCompleteText != null)
            {
                levelCompleteText.text = $"LEVEL COMPLETE!\nTime: {GetFormattedTime()}";
            }
        }
    }

    public void NextLevel()
    {
        Time.timeScale = 1f;        
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    public void ReplayLevel()
    {
        Time.timeScale = 1f;        
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturntoMainMenu()
    {
        Time.timeScale = 1f;        
        SceneManager.LoadScene(0);
    }

    public void TrackGameOverStats()
    {
        isGameActive = false;
        if (gameOverStatsText != null)
        {
            gameOverStatsText.text = $"Time: {GetFormattedTime()}\n{GetPercentageMowed():F0}% Mowed";
        }
    }
}
