using UnityEngine;

/// <summary>
/// Stores the state of an enemy (e.g. stunned). The Behavior Tree reads this later.
/// Put it on every enemy.
/// </summary>
public class EnemyStatus : MonoBehaviour
{
    private float stunnedUntil;

    public bool IsStunned => Time.time < stunnedUntil;

    public void Stun(float duration)
    {
        stunnedUntil = Time.time + duration;
    }
}
