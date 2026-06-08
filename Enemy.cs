using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class Enemy : MonoBehaviour
{
    public Bullet bulletStats;

    [Header("Speed")]
    public float startSpeed = 10f;
    public float lowerSpeedLimit = 1f;
    public float upperSpeedLimit = 75f;

    private float baseSpeed;        // permanent buffs/debuffs (e.g., Berserker)
    private float currentSpeed;     // baseSpeed * slowMultiplier, clamped
    public float speed
    {
        get => currentSpeed;
        private set => currentSpeed = value;
    }

    [Header("Health")]
    public float startHealth = 100;
    public float health;
    public int damageThreshold = 0;
    public int pierceTaken = 1;
    public int worth = 50;
    public int lifeCount = 1;

    [Header("Poison")]
    public float maxPoisonResistance = 100;
    public float poisonResistance = 0;

    [Header("Types/Behaviors")]
    public bool isGhost;
    public bool isHealer;
    public float healPool = 0;
    public float healRate = 1;
    public bool isSummoner;
    public float summonRate;
    public GameObject enemyToSummon;

    public bool isRusher;
    public float rusherWaitTime;
    public bool isBerserker;
    public float speedBoost = 1.2f;

    public bool isBoss = false;
    public bool isTank = false;
    public int dropChance = 0;

    [Header("Immunities")]
    public bool immuneToBleed = false;
    public bool immuneToFire = false;
    public bool immuneToIce = false;
    public bool immuneToPoison = false;
    public bool immuneToSlow = false;

    [Header("Status Flags")]
    public bool isOnFire = false;
    public bool isBleeding = false;
    public bool isPoisoned = false;
    public bool isFrozen = false;
    public bool isSlowed = false;

    public float damageResistance = 1f;

    private int iceCheck = 0;
    private int slowCheck = 0;
    private int fireCheck = 0;

    private readonly List<float> slowFactors = new List<float>(); // each in (0,1], product applied to baseSpeed

    private WaveSpawner waveSpawner;

    public Material ghostPostDeath;
    public GameObject deathEffect;

    [Header("Unity Stuff")]
    public Image healthBar;
    public Image poisonBar;

    public bool isDead = false;

    public float distanceTraveled = 0f;

    public void SetBulletStats(Bullet stats) => bulletStats = stats;

    void Start()
    {
        SetBulletStats(GameObject.FindObjectOfType<Bullet>());
        baseSpeed = startSpeed;
        RecomputeSpeed();
        health = startHealth;
        waveSpawner = FindObjectOfType<WaveSpawner>();
        if (isSummoner)
            StartCoroutine(Summon(enemyToSummon, summonRate));
    }

    private float SlowMultiplierProduct()
    {
        float prod = 1f;
        for (int i = 0; i < slowFactors.Count; i++)
            prod *= slowFactors[i]; // each factor ∈ (0,1]
        return prod;
    }

    private void RecomputeSpeed()
    {
        float slowMult = SlowMultiplierProduct();
        currentSpeed = Mathf.Clamp(baseSpeed * slowMult, lowerSpeedLimit, upperSpeedLimit);
    }

    private void ApplySlowFactor(float factor) // factor in (0,1], e.g. 0.5f = 50% speed
    {
        slowFactors.Add(factor);
        isSlowed = true;
        RecomputeSpeed();
    }

    private void RemoveSlowFactor(float factor)
    {
        // Remove first occurrence; use Mathf.Approximately for float comparison
        for (int i = 0; i < slowFactors.Count; i++)
        {
            if (Mathf.Approximately(slowFactors[i], factor))
            {
                slowFactors.RemoveAt(i);
                break;
            }
        }
        if (slowFactors.Count == 0) isSlowed = false;
        RecomputeSpeed();
    }

    public void UpdateDistanceTraveled(float distance)
    {
        distanceTraveled += distance;
    }

    public void TakeDamage(float amount)
    {
        if (isTank)
        {
            if (damageThreshold > 0 && amount > damageThreshold)
            {
                health -= 1;
                healthBar.fillAmount = health / startHealth;
                if (health <= 0 && !isDead) Die();
                return;
            }
            health -= 1;
            healthBar.fillAmount = health / startHealth;
            if (health <= 0 && !isDead) Die();
            return;
        }

        if (amount * damageResistance > damageThreshold)
            health -= amount * damageResistance;

        healthBar.fillAmount = health / startHealth;

        if (isHealer && healPool > 0)
            StartCoroutine(Heal(amount));

        // Berserker: permanently gains speed on each hit, clamped to upperSpeedLimit
        if (isBerserker)
        {
            baseSpeed *= Mathf.Max(1f, speedBoost);
            RecomputeSpeed();
            if (currentSpeed >= upperSpeedLimit)
            {
                baseSpeed = Mathf.Min(baseSpeed, upperSpeedLimit);
                RecomputeSpeed();
            }
        }

        if (health <= 0 && !isDead)
            Die();
    }

    public void TakeFireDamage(float dot)
    {
        if (!immuneToFire)
        {
            isOnFire = true;
            StartCoroutine(Fire(dot));
        }
    }

    public void TakePoisonDamage(float dot)
    {
        if (!immuneToPoison)
        {
            isPoisoned = true;
            poisonResistance += dot;
            poisonBar.fillAmount = poisonResistance / maxPoisonResistance;
            if (poisonResistance >= maxPoisonResistance)
                Die();
        }
    }

    public void TakeBleedDamage(float dot)
    {
        if (!immuneToBleed)
        {
            isBleeding = true;
            StartCoroutine(Bleed(dot));
        }
    }

    public void TakeIceDamage(float brittleEffect)
    {
        if (!immuneToIce)
        {
            isFrozen = true;
            StartCoroutine(Ice(brittleEffect));
        }
    }

    public void TakeSlowDamage(float slow) // slow >= 1, e.g. 1.5 = 33% speed reduction
    {
        if (!immuneToSlow)
        {
            isSlowed = true;
            StartCoroutine(Slow(slow));
        }
    }

    void Die()
    {
        if (!isDead)
            WaveSpawner.EnemiesAlive--;

        removeEffects();

        if (!isGhost)
        {
            isDead = true;
            PlayerStats.Money += worth;

            if (deathEffect != null)
            {
                GameObject effect = Instantiate(deathEffect, transform.position, Quaternion.identity);
                Destroy(effect, 5f);
            }

            Destroy(gameObject);
        }
        else
        {
            isDead = true;
            StartCoroutine(ghostDeath());
        }
    }

    public void removeEffects()
    {
        StopAllCoroutines(); // stops all running DOTs and slows

        isBleeding = false;
        isFrozen = false;
        isOnFire = false;
        isSlowed = false;
        isPoisoned = false;

        fireCheck = 0;
        iceCheck = 0;
        slowCheck = 0;

        slowFactors.Clear();
        baseSpeed = Mathf.Clamp(baseSpeed, lowerSpeedLimit, upperSpeedLimit);
        RecomputeSpeed();
    }

    private IEnumerator Bleed(float damageToTake)
    {
        while (!isDead)
        {
            yield return new WaitForSeconds(.5f);
            health -= damageToTake;
            healthBar.fillAmount = health / startHealth;
            if (health <= 0 && !isDead) Die();
        }
    }

    private IEnumerator Fire(float damageToTake)
    {
        fireCheck++;
        int counter = 0;
        while (counter <= 5)
        {
            yield return new WaitForSeconds(1f);
            health -= damageToTake;
            healthBar.fillAmount = health / startHealth;
            counter++;
            if (health <= 0 && !isDead) Die();
        }
        fireCheck--;
        if (fireCheck <= 0)
        {
            fireCheck = 0;
            isOnFire = false;
        }
    }

    private IEnumerator Ice(float brittleEffect)
    {
        iceCheck++;
        damageResistance += brittleEffect;
        yield return new WaitForSeconds(5f);
        damageResistance -= brittleEffect;
        iceCheck--;
        if (iceCheck <= 0)
        {
            iceCheck = 0;
            isFrozen = false;
        }
    }

    private IEnumerator Slow(float slow)
    {
        // Convert slow strength (>=1) to a multiplicative speed factor in (0,1]
        float factor = 1f / Mathf.Max(1f, slow);

        slowCheck++;
        ApplySlowFactor(factor);

        yield return new WaitForSeconds(5f);

        RemoveSlowFactor(factor);
        slowCheck--;
        if (slowCheck <= 0)
        {
            slowCheck = 0;
            isSlowed = false;
        }
    }

    private IEnumerator ghostDeath()
    {
        if (deathEffect != null)
        {
            GameObject effect = Instantiate(deathEffect, transform.position, Quaternion.identity);
            Destroy(effect, 5f);
        }

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && ghostPostDeath != null)
            renderer.material = ghostPostDeath;

        this.tag = "Ghost";

        yield return new WaitForSeconds(5f);

        PlayerStats.Money += worth;
        if (deathEffect != null)
        {
            GameObject effect = Instantiate(deathEffect, transform.position, Quaternion.identity);
            Destroy(effect, 5f);
        }

        Destroy(gameObject);
    }

    private IEnumerator Heal(float amount)
    {
        int counter = 0;
        while (counter <= 4 && healPool > 0)
        {
            yield return new WaitForSeconds(.5f);
            float delta = amount * healRate;
            health += delta;
            healPool -= delta;
            healthBar.fillAmount = health / startHealth;
            counter++;
            if (health <= 0 && !isDead) Die();
        }
    }

    private IEnumerator Summon(GameObject enemy, float rate)
    {
        while (!isDead)
        {
            yield return new WaitForSeconds(1f * Mathf.Max(0.01f, summonRate));
            if (waveSpawner != null && waveSpawner.spawnPoint != null && enemy != null)
            {
                Instantiate(enemy, waveSpawner.spawnPoint.position, waveSpawner.spawnPoint.rotation);
                WaveSpawner.EnemiesAlive++;
            }
        }
    }
}
