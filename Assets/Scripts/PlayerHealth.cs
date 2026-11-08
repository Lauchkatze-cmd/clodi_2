using UnityEngine;

/// <summary>
/// Player health. Enemies call TakeDamage(). At 0 health the game is lost.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float invincibleTime = 1.5f;

    private int currentHealth;
    private float invincibleUntil;

    private void Start()
    {
        currentHealth = maxHealth;
        if (GameManager.Instance != null) GameManager.Instance.UpdateHealth(currentHealth, maxHealth);
    }

    public void TakeDamage(int amount)
    {
        if (Time.time < invincibleUntil || currentHealth <= 0) return;

        invincibleUntil = Time.time + invincibleTime;
        currentHealth = Mathf.Max(currentHealth - amount, 0);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateHealth(currentHealth, maxHealth);
            if (currentHealth == 0) GameManager.Instance.LoseGame();
        }
    }
}
