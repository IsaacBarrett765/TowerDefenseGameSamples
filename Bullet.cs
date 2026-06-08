using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Bullet : MonoBehaviour
{
    private Transform target;
    private Transform primaryTarget;
    private bool primaryHitOnce = false;

    private Vector3 initialDirection;
    public Turret turretStats;

    public GameObject impactEffect;
    public GameObject explosionEffect;
    public GameObject explosionVisual;

    private float timer;
    private bool collided;
    private int targetsPierced = 0;

    public float speed = 70f;

    [Header("Homing")]
    public float homingDetectionRadius = 10f;   // Distance where we boost turn rate
    public float closeHomingMultiplier = 3f;    // How much we boost near target
    public float maxTurnRateDeg = 360f;         // Base max turn rate (deg/sec)

    [Header("Retargeting")]
    public float retargetInterval = 0.1f;       // How often to try retargeting (s)
    private float retargetTimer = 0f;
    public bool preferFrontCone = true;         // Prefer enemies roughly in front
    public float frontConeDot = 0.25f;          // cos(theta): 0.25 ~= 75° cone

    // Post-hit homing suppression (> 0 -> homing temporarily disabled)
    private float homingDisableTimer = 0f;

    [Header("Hit Cooldowns")]
    public float sameTargetCooldown = 0.2f;     // Can't hit same enemy within this window
    private readonly Dictionary<Transform, float> lastHitTime = new Dictionary<Transform, float>();

    public void SetTurretStats(Turret turret)
    {
        turretStats = turret;
    }

    void Start()
    {
        timer = turretStats.bulletLifetime;

        // If Turret called Seek before Start(), keep that target; otherwise store initial forward.
        if (target == null)
            initialDirection = transform.forward;
    }

    void Update()
    {
        if (!collided)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (homingDisableTimer > 0f)
            homingDisableTimer -= Time.deltaTime;

        if (target != null)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te == null || te.isDead) target = null;
        }

        if (primaryTarget != null)
        {
            Enemy pe = primaryTarget.GetComponent<Enemy>();
            if (pe == null || pe.isDead)
            {
                // Primary died before we hit it — allow free retargeting now.
                primaryTarget = null;
            }
        }

        if (turretStats.shouldSeek)
        {
            retargetTimer -= Time.deltaTime;

            bool mustStickToPrimary = !primaryHitOnce && primaryTarget != null && homingDisableTimer <= 0f;
            if (mustStickToPrimary)
            {
                // Keep steering to the primary until we've landed the first hit.
                target = primaryTarget;
            }
            else
            {
                if (homingDisableTimer <= 0f)
                {
                    if (target == null && retargetTimer <= 0f)
                    {
                        FindNearestEnemy();
                        retargetTimer = retargetInterval;
                    }
                    else if (retargetTimer <= 0f && target != null)
                    {
                        TryUpgradeTarget();
                        retargetTimer = retargetInterval;
                    }
                }
            }
        }

        MoveBullet();
        DoShortRangeHitChecks();
    }

    void MoveBullet()
    {
        float step = speed * Time.deltaTime;

        // If homing and suppression is inactive, steer toward target.
        if (turretStats.shouldSeek && target != null && homingDisableTimer <= 0f)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te != null && !te.isDead)
            {
                Vector3 toTarget = (target.position - transform.position);
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    // Boost turn rate as bullet closes in on target.
                    float dist = toTarget.magnitude;
                    float closeT = Mathf.Clamp01(1f - (dist / Mathf.Max(0.0001f, homingDetectionRadius)));
                    float effectiveTurn = maxTurnRateDeg * Mathf.Lerp(1f, closeHomingMultiplier, closeT);

                    Quaternion desired = Quaternion.LookRotation(toTarget);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        desired,
                        effectiveTurn * Time.deltaTime
                    );
                }
            }
            else
            {
                target = null;
            }
        }

        // Always advance forward — no early-return that can freeze movement.
        transform.position += transform.forward * step;
    }

    void DoShortRangeHitChecks()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, 0.5f))
        {
            if (hit.collider.CompareTag("Enemy") && CanHit(hit.collider.transform))
            {
                HandleCollision(hit.collider.transform);
                return;
            }
        }

        Vector3 sideDir = Vector3.Cross(transform.forward, Vector3.up);
        if (Physics.Raycast(transform.position, sideDir, out hit, 0.5f))
        {
            if (hit.collider.CompareTag("Enemy") && CanHit(hit.collider.transform))
            {
                HandleCollision(hit.collider.transform);
                return;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null || collided) return;

        // Prioritize the designated target if present.
        if (target != null && other.transform == target)
        {
            if (CanHit(target))
                HandleCollision(target);
        }
        else if (other.CompareTag("Enemy"))
        {
            if (CanHit(other.transform))
                HandleCollision(other.transform);
        }
    }

    public void Seek(Transform _target)
    {
        target = _target;
        primaryTarget = _target;
        primaryHitOnce = false;

        if (target != null)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te != null && te.isDead)
            {
                target = null;
                primaryTarget = null;
            }
        }

        if (target != null)
        {
            initialDirection = (target.position - transform.position).normalized;
            if (initialDirection.sqrMagnitude < 0.0001f)
                initialDirection = transform.forward;
        }
        else
        {
            initialDirection = transform.forward;
        }
    }

    bool CanHit(Transform t)
    {
        if (t == null) return false;
        Enemy e = t.GetComponent<Enemy>();
        if (e == null || e.isDead) return false;

        if (lastHitTime.TryGetValue(t, out float lastTime))
        {
            if (Time.time - lastTime < sameTargetCooldown) return false;
        }
        return true;
    }

    void HandleCollision(Transform collidedTransform)
    {
        if (collidedTransform == null) return;

        Enemy enemy = collidedTransform.GetComponent<Enemy>();
        if (enemy == null || enemy.isDead) return;

        if (!CanHit(collidedTransform)) return;

        // Record hit time to prevent immediate double-hits from ray/trigger overlap.
        lastHitTime[collidedTransform] = Time.time;

        if (turretStats.explosionRadius > 0f)
        {
            // Ensure the directly-hit enemy always receives on-hit effects first.
            Explode(collidedTransform);
        }
        else
        {
            Damage(collidedTransform);
            targetsPierced += enemy.pierceTaken;

            homingDisableTimer = ComputePostHitDisableSeconds();

            if (targetsPierced >= (int)(turretStats.pierce * turretStats.pierceMultiplier))
            {
                DestroyBullet();
            }
            else
            {
                if (turretStats.shouldSeek)
                    retargetTimer = 0f;
            }
        }
    }

    void TryUpgradeTarget()
    {
        if (!preferFrontCone) return;

        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        Transform best = target;
        float bestDist = (best != null) ? Vector3.SqrMagnitude(best.position - transform.position) : Mathf.Infinity;

        Vector3 fwd = transform.forward;

        foreach (GameObject go in enemies)
        {
            if (go == null) continue;
            Transform t = go.transform;
            if (t == null) continue;

            Enemy e = t.GetComponent<Enemy>();
            if (e == null || e.isDead) continue;

            if (!CanHit(t)) continue;

            // Don't switch away from primary until it's been hit.
            if (!primaryHitOnce && primaryTarget != null && t != primaryTarget) continue;

            Vector3 to = (t.position - transform.position);
            float sqrDist = to.sqrMagnitude;
            if (sqrDist >= bestDist) continue;

            Vector3 dir = to.normalized;
            if (Vector3.Dot(fwd, dir) >= frontConeDot) // within front cone
            {
                best = t;
                bestDist = sqrDist;
            }
        }

        if (best != target && best != null)
            target = best;
    }

    void FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        float shortestSqr = Mathf.Infinity;
        GameObject nearest = null;

        foreach (GameObject enemy in enemies)
        {
            if (enemy == null) continue;
            Transform et = enemy.transform;
            if (et == null) continue;

            Enemy e = et.GetComponent<Enemy>();
            if (e == null || e.isDead) continue;

            if (!CanHit(et)) continue;

            // Don't switch away from primary until it's been hit.
            if (!primaryHitOnce && primaryTarget != null && et != primaryTarget) continue;

            float sqr = (et.position - transform.position).sqrMagnitude;
            if (sqr < shortestSqr)
            {
                shortestSqr = sqr;
                nearest = enemy;
            }
        }

        target = nearest != null ? nearest.transform : null;
    }

    public void SetSpeed(float newSpeed) => speed = newSpeed;

    void Explode(Transform primaryHit = null)
    {
        int pierceLimit = (int)(turretStats.pierce * turretStats.pierceMultiplier);
        int enemiesDamaged = 0;

        if (explosionEffect != null)
        {
            GameObject fx = Instantiate(explosionEffect, transform.position, transform.rotation);
            Destroy(fx, 5f);
        }
        if (explosionVisual != null)
        {
            GameObject vis = Instantiate(explosionVisual, transform.position, transform.rotation);
            vis.transform.localScale = Vector3.one * (turretStats.explosionRadius * 2f);
            Destroy(vis, 0.05f);
        }

        // Ensure the enemy we physically collided with is always damaged first.
        if (primaryHit != null)
        {
            Enemy pe = primaryHit.GetComponent<Enemy>();
            if (pe != null && !pe.isDead)
            {
                Damage(primaryHit);
                targetsPierced += pe.pierceTaken;
                enemiesDamaged++;

                if (enemiesDamaged >= pierceLimit && turretStats.explosionsPierce)
                {
                    DestroyBullet();
                    return;
                }
            }
        }

        Collider[] colliders = Physics.OverlapSphere(transform.position, turretStats.explosionRadius);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || !c.CompareTag("Enemy")) continue;

            Transform t = c.transform;
            if (primaryHit != null && t == primaryHit) continue; // already applied above

            Enemy e = t.GetComponent<Enemy>();
            if (e == null || e.isDead) continue;

            if (!CanHit(t)) continue;

            Damage(t);
            lastHitTime[t] = Time.time;
            targetsPierced += e.pierceTaken;
            enemiesDamaged++;

            if (turretStats.explosionsPierce && enemiesDamaged >= pierceLimit)
                break;
        }

        homingDisableTimer = ComputePostHitDisableSeconds();

        // Projectile ends after a non-piercing explosion, or once pierce capacity is consumed.
        if (!turretStats.explosionsPierce || enemiesDamaged >= pierceLimit || targetsPierced >= pierceLimit)
        {
            DestroyBullet();
        }
        else
        {
            if (turretStats.shouldSeek)
                retargetTimer = 0f;
        }
    }

    void Damage(Transform enemy)
    {
        if (enemy == null) return;

        Enemy e = enemy.GetComponent<Enemy>();
        if (e == null || e.isDead) return;

        e.TakeDamage(turretStats.damage * turretStats.damageMultiplier);
        if (turretStats.fireDot > 0) e.TakeFireDamage(turretStats.fireDot);
        if (turretStats.poisonDot > 0) e.TakePoisonDamage(turretStats.poisonDot);
        if (turretStats.bleedDot > 0) e.TakeBleedDamage(turretStats.bleedDot);
        if (turretStats.bleedBonusDamage > 0 && e.isBleeding) e.TakeDamage(turretStats.bleedBonusDamage * turretStats.damageMultiplier);
        if (turretStats.fireBonusDamage > 0 && e.isOnFire) e.TakeDamage(turretStats.fireBonusDamage * turretStats.damageMultiplier);
        if (turretStats.poisonBonusDamage > 0 && e.isPoisoned) e.TakeDamage(turretStats.poisonBonusDamage * turretStats.damageMultiplier);
        if (turretStats.iceEffect > 0) e.TakeIceDamage(turretStats.iceEffect);
        if (turretStats.slowEffect > 0) e.TakeSlowDamage(turretStats.slowEffect);
    }

    void OnDrawGizmosSelected()
    {
        if (turretStats == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, turretStats.explosionRadius);
    }

    void DestroyBullet()
    {
        if (impactEffect != null)
        {
            GameObject fx = Instantiate(impactEffect, transform.position, transform.rotation);
            Destroy(fx, 5f);
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        speed = 0f;
        enabled = false;

        if (!turretStats.hasParticles)
        {
            Destroy(gameObject);
        }
        else
        {
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
            StartCoroutine(DestroyBulletAfterDelay(5f));
        }
    }

    IEnumerator DestroyBulletAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    // Maps seekStrength [1,10] to post-hit suppression duration: strength=1 -> 1s, strength=10 -> 0s
    float ComputePostHitDisableSeconds()
    {
        float s = Mathf.Clamp(turretStats.seekStrength, 1f, 10f);
        return (10f - s) / 9f;
    }
}
