using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Turret : MonoBehaviour
{
    private Transform target;
    private Enemy targetEnemy;

    public string nameString;
    public List<TurretModule> upgrades;

    [Header("General")]
    public GameObject upgradeStorage;
    public int slotFromShop;
    public int upgradeCost = 100;
    public int sellAmount = 100;
    public int nextUpgradeCost;
    public float damage = 50f;
    public int pierce = 1;
    public float pierceMultiplier = 1f;
    public float range = 15f;
    public float damageMultiplier = 1f;
    public float fireRateMultiplier = 1f;

    [Header("Upgrade Locations")]
    public GameObject barrel; //1
    public GameObject barrelTip; //2
    public GameObject barrelUnder; //3
    public GameObject barrelBase; //4
    public GameObject turretBase; //5
    public GameObject leftFront; //6
    public GameObject leftCenter; //7
    public GameObject leftBack; //8
    public GameObject rightFront; //9
    public GameObject rightCenter; //10
    public GameObject rightBack; //11
    public GameObject rear; //12
    public GameObject frontTopRightCorner; //13
    public GameObject frontBottomRightCorner; //14
    public GameObject frontTopLeftCorner; //15
    public GameObject frontBottomLeftCorner; //16
    public GameObject topFrontLeft; //17
    public GameObject topFrontMiddle; //18
    public GameObject topFrontRight; //19
    public GameObject topCenterLeft; //20
    public GameObject topCenterMiddle; //21
    public GameObject topCenterRight; //22
    public GameObject topBackLeft; //23
    public GameObject topBackMiddle; //24
    public GameObject topBackRight; //25
    


    [Header("Targeting Modes")]
    public bool useFirstTargeting = true;
    public bool useFarthestTargeting = false;
    public bool useClosestTargeting = false;
    public bool useLastTargeting = false;
    public bool useStrongestTargeting = false;
    public bool useWeakestTargeting = false;

    [Header("Bullet/Effects")]
    public bool shouldSeek = false;
    public float explosionRadius = 0f;
    public float seekStrength = .5f;
    public float minSpeed = 70f;
    public float maxSpeed = 80f;
    public float bleedDot = 0f;
    public float fireDot = 0f;
    public float iceEffect = 0f;
    public float slowEffect = 0f;
    public float poisonDot = 0f;
    public float bleedBonusDamage = 0f;
    public float poisonBonusDamage = 0f;
    public float fireBonusDamage = 0f;
    public bool hasParticles = false;
    public bool isEssential = false;
    public bool explosionsPierce = false;

    [Header("Use Bullets (default)")]
    public int numberOfBullets = 1;
    public float numberOfBulletsMultiplier = 1f;
    public float fireRate = 1f;
    private float fireCountdown = 0f;
    public GameObject bulletPrefab;
    public GameObject particledBulletPrefab;
    public float inaccuracyAngleRange = 10f;
    public float bulletLifetime = 5f;

    [Header("Use Laser")]
    public bool useLaser = false;
    public LineRenderer lineRenderer;
    public ParticleSystem impactEffect;
    public Light impactLight;
    public int damageOverTime = 30;

    [Header("Unity Setup Fields")]
    public string enemyTag = "Enemy";
    public Transform partToRotate;
    public float turnSpeed = 10f;
    public Transform firePoint;

    // --- Responsiveness controls ---
    [Header("Targeting Performance")]
    [Tooltip("Minimum delay between target scans (reacquires even if a target is already locked).")]
    public float scanInterval = 0.1f;
    private float nextScanTime = 0f;

    [Tooltip("Avoid rapid ping-ponging: require this relative improvement to swap targets (e.g., 0.02 = 2%).")]
    [Range(0f, 0.2f)] public float swapHysteresis = 0.02f;

    [Tooltip("Optional: restrict scanning to this enemy LayerMask for performance.")]
    public LayerMask enemyLayer = ~0;

    // Optional preallocated buffer (tweak size to your max expected enemies in range)
    private static readonly Collider[] scanBuffer = new Collider[256];

    void Awake()
    {
        if (isEssential)
            DontDestroyOnLoad(transform.gameObject);
    }

    void Start()
    {
        // Initial scan
        TryAcquireTarget(force: true);
    }

    void Update()
    {
        // Tick fire cooldown
        fireCountdown = Mathf.Max(0f, fireCountdown - Time.deltaTime);

        // Re-evaluate best target on interval — EVEN if current target is valid.
        if (Time.time >= nextScanTime)
        {
            EvaluateAndMaybeSwapTarget();
            nextScanTime = Time.time + scanInterval;
        }

        // If target missing/invalid/out of range, try immediately to grab a new one (without waiting for nextScanTime)
        if (!IsTargetValid(targetEnemy))
        {
            ClearTarget();
            TryAcquireTarget(force: true);

            if (target == null)
            {
                if (useLaser) DisableLaser();
                return;
            }
        }

        LockOnTarget();

        if (useLaser)
            Laser();
        else
            HandleShooting();
    }

    // ---------- Compatibility wrapper for older code ----------
    public void UpdateTarget()
    {
        TryAcquireTarget(force: true);
    }

    // ---------- Targeting ----------
    bool IsTargetValid(Enemy e)
    {
        if (e == null || e.transform == null) return false;
        if (!e.gameObject.activeInHierarchy) return false;
        if (e.health <= 0f) return false;
        if ((e.transform.position - transform.position).sqrMagnitude > range * range) return false;
        return true;
    }

    void ClearTarget()
    {
        target = null;
        targetEnemy = null;
    }

    /// <summary>
    /// Scores an enemy based on the selected targeting mode. Higher score = better.
    /// </summary>
    float ScoreEnemy(Enemy enemy)
    {
        // Common terms
        float distSqr = (enemy.transform.position - transform.position).sqrMagnitude;

        // Modes:
        if (useFirstTargeting)     return enemy.distanceTraveled;          // more progress = better
        if (useLastTargeting)      return -enemy.distanceTraveled;         // less progress = better
        if (useFarthestTargeting)  return distSqr;                          // larger distance = better
        if (useClosestTargeting)   return -distSqr;                         // smaller distance = better
        if (useStrongestTargeting) return enemy.health;                     // higher HP = better
        if (useWeakestTargeting)   return -enemy.health;                    // lower HP = better

        // Default = closest
        return -distSqr;
    }

    /// <summary>
    /// Re-scan and swap if there is a clearly better target than the current one.
    /// </summary>
    void EvaluateAndMaybeSwapTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, scanBuffer, enemyLayer, QueryTriggerInteraction.Ignore);

        GameObject bestGO = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < count; i++)
        {
            var col = scanBuffer[i];
            if (col == null) continue;

            var go = col.transform.gameObject;
            if (!go.CompareTag(enemyTag)) continue;

            var enemy = go.GetComponent<Enemy>();
            if (enemy == null || enemy.health <= 0f) continue;

            // Score it
            float score = ScoreEnemy(enemy);

            if (score > bestScore)
            {
                bestScore = score;
                bestGO = go;
            }
        }

        // If nothing found, clear & bail
        if (bestGO == null)
        {
            ClearTarget();
            return;
        }

        // If we have a current target, only swap if "meaningfully" better
        if (targetEnemy != null && IsTargetValid(targetEnemy))
        {
            float currentScore = ScoreEnemy(targetEnemy);

            // Require a modest improvement to avoid jitter (e.g., 2%)
            // If currentScore could be negative, compare difference instead of relative improvement.
            bool shouldSwap;
            if (Mathf.Abs(currentScore) < 0.0001f)
            {
                shouldSwap = (bestScore - currentScore) > 0.0001f;
            }
            else
            {
                shouldSwap = (bestScore > currentScore * (1f + swapHysteresis));
            }

            if (!shouldSwap) return;
        }

        // Assign new target
        target = bestGO.transform;
        targetEnemy = bestGO.GetComponent<Enemy>();
    }

    public void TryAcquireTarget(bool force)
    {
        if (!force && Time.time < nextScanTime) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, range, scanBuffer, enemyLayer, QueryTriggerInteraction.Ignore);

        GameObject bestGO = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < count; i++)
        {
            var col = scanBuffer[i];
            if (col == null) continue;

            var go = col.transform.gameObject;
            if (!go.CompareTag(enemyTag)) continue;

            var enemy = go.GetComponent<Enemy>();
            if (enemy == null || enemy.health <= 0f) continue;

            float score = ScoreEnemy(enemy);
            if (score > bestScore)
            {
                bestScore = score;
                bestGO = go;
            }
        }

        if (bestGO != null)
        {
            target = bestGO.transform;
            targetEnemy = bestGO.GetComponent<Enemy>();
        }
        else
        {
            ClearTarget();
        }
    }

    // ---------- Laser / Shooting ----------
    void DisableLaser()
    {
        if (lineRenderer != null && lineRenderer.enabled)
        {
            lineRenderer.enabled = false;
            if (impactEffect != null) impactEffect.Stop();
            if (impactLight != null) impactLight.enabled = false;
        }
    }

    void Laser()
    {
        if (targetEnemy == null) return;

        targetEnemy.TakeDamage(damageOverTime * Time.deltaTime);

        if (lineRenderer != null && !lineRenderer.enabled)
        {
            lineRenderer.enabled = true;
            if (impactEffect != null) impactEffect.Play();
            if (impactLight != null) impactLight.enabled = true;
        }

        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, firePoint.position);
            lineRenderer.SetPosition(1, target.position);
        }

        if (impactEffect != null)
        {
            Vector3 dir = firePoint.position - target.position;
            impactEffect.transform.position = target.position + dir.normalized;
            impactEffect.transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    void LockOnTarget()
    {
        if (partToRotate == null || target == null) return;

        Vector3 dir = target.position - transform.position;
        Quaternion lookRotation = Quaternion.LookRotation(dir);
        Vector3 rotation = Quaternion.Lerp(partToRotate.rotation, lookRotation, Time.deltaTime * turnSpeed).eulerAngles;
        partToRotate.rotation = Quaternion.Euler(0f, rotation.y, 0f);
    }

    void HandleShooting()
    {
        if (fireCountdown <= 0f && target != null)
        {
            Shoot((int)(numberOfBullets * numberOfBulletsMultiplier));
            fireCountdown = 1f / Mathf.Max(0.0001f, (fireRate * fireRateMultiplier));
        }
        // cooldown tick is in Update()
    }

    void Shoot(int numberOfBullets)
    {
        if (target == null) return;

        if (inaccuracyAngleRange < 0) inaccuracyAngleRange = 0;
        if (hasParticles) bulletPrefab = particledBulletPrefab;

        for (int i = 0; i < numberOfBullets; i++)
        {
            float randomRotation = Random.Range(-inaccuracyAngleRange, inaccuracyAngleRange);
            Quaternion randomRotationQuaternion = Quaternion.Euler(0f, randomRotation, 0f);

            Vector3 initialDirection = firePoint.forward;
            Vector3 rotatedDirection = randomRotationQuaternion * initialDirection;

            float randomSpeed = Random.Range(minSpeed, maxSpeed);

            GameObject bulletGO = Instantiate(bulletPrefab, firePoint.position, Quaternion.LookRotation(rotatedDirection));
            Bullet bullet = bulletGO.GetComponent<Bullet>();
            bullet.SetTurretStats(this);
            if (bullet != null)
            {
                bullet.Seek(target);
                bullet.SetSpeed(randomSpeed);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }

    public Vector3 GetUpgradePosition(int location){
        if(location == 1) return barrel.transform.position;
        else if(location == 2) return barrelTip.transform.position;
        else if(location == 3) return barrelUnder.transform.position;
        else if(location == 4) return barrelBase.transform.position;
        else if(location == 5) return turretBase.transform.position;
        else if(location == 6) return leftFront.transform.position;
        else if(location == 7) return leftCenter.transform.position;
        else if(location == 8) return leftBack.transform.position;
        else if(location == 9) return rightFront.transform.position;
        else if(location == 10) return rightCenter.transform.position;
        else if(location == 11) return rightBack.transform.position;
        else if(location == 12) return rear.transform.position;
        else if(location == 13) return frontTopRightCorner.transform.position;
        else if(location == 14) return frontBottomRightCorner.transform.position;
        else if(location == 15) return frontTopLeftCorner.transform.position;
        else if(location == 16) return frontBottomLeftCorner.transform.position;
        else if(location == 17) return topFrontLeft.transform.position;
        else if(location == 18) return topFrontMiddle.transform.position;
        else if(location == 19) return topFrontRight.transform.position;
        else if(location == 20) return topCenterLeft.transform.position;
        else if(location == 21) return topCenterMiddle.transform.position;
        else if(location == 22) return topCenterRight.transform.position;
        else if(location == 23) return topBackLeft.transform.position;
        else if(location == 24) return topBackMiddle.transform.position;
        else if(location == 25) return topBackRight.transform.position;
        return barrel.transform.position;
    }
}