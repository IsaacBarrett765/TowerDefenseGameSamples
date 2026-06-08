using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Bullet : MonoBehaviour
{
    private Transform target;

    // NEW: remember the first assigned target; we must hit it once before free-homing.
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

    // Movement
    public float speed = 70f;

    // Homing / steering
    [Header("Homing")]
    public float homingDetectionRadius = 10f;   // Distance where we boost turn rate
    public float closeHomingMultiplier = 3f;    // How much we boost near target
    public float maxTurnRateDeg = 360f;         // Base max turn rate (deg/sec)

    // Retarget behavior
    [Header("Retargeting")]
    public float retargetInterval = 0.1f;       // How often to try retargeting (s)
    private float retargetTimer = 0f;
    public bool preferFrontCone = true;         // Prefer enemies roughly in front
    public float frontConeDot = 0.25f;          // cos(theta): 0.25 ~= 75° cone

    // Post-hit homing suppression (> 0 -> homing temporarily disabled)
    private float homingDisableTimer = 0f;

    // Re-hit cooldown for the same enemy
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

        // If Turret called Seek before Start(), we keep it; otherwise remember our initial forward.
        if (target == null)
        {
            initialDirection = transform.forward;
        }
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

        // Count down post-hit homing suppression
        if (homingDisableTimer > 0f)
            homingDisableTimer -= Time.deltaTime;

        // Drop target if it became dead
        if (target != null)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te == null || te.isDead) target = null;
        }

        // Keep primaryTarget validity up-to-date
        if (primaryTarget != null)
        {
            Enemy pe = primaryTarget.GetComponent<Enemy>();
            if (pe == null || pe.isDead)
            {
                // Primary died before we ever hit it -> allow free retargeting now
                primaryTarget = null;
            }
        }

        // Targeting logic
        if (turretStats.shouldSeek)
        {
            retargetTimer -= Time.deltaTime;

            bool mustStickToPrimary = !primaryHitOnce && primaryTarget != null && homingDisableTimer <= 0f;
            if (mustStickToPrimary)
            {
                // While we haven't hit the primary yet and it's alive, keep steering to it.
                target = primaryTarget;
            }
            else
            {
                // Normal behavior (nearest/upgrade) once primary was hit, or if there is no valid primary
                if (homingDisableTimer <= 0f)
                {
                    if ((target == null) && retargetTimer <= 0f)
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

        // Move/steer
        MoveBullet();

        // Cheap forward/side ray checks to catch grazing hits (optional)
        DoShortRangeHitChecks();
    }

    void MoveBullet()
    {
        float step = speed * Time.deltaTime;

        // If we are homing and have a (live) target, steer toward it (unless suppression active)
        if (turretStats.shouldSeek && target != null && homingDisableTimer <= 0f)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te != null && !te.isDead)
            {
                Vector3 toTarget = (target.position - transform.position);
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    // Base turn rate, with a boost when close
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
                // Target is dead/null => clear so we can retarget
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

        // Prioritize the designated target if present
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

        // NEW: record the first assigned target as "primary"
        primaryTarget = _target;
        primaryHitOnce = false;

        if (target != null)
        {
            Enemy te = target.GetComponent<Enemy>();
            if (te != null && te.isDead)
            {
                target = null;
                // If the initial target is dead, don't keep it as primary.
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
            // Keep current forward to continue traveling if we lose/skip target
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

    // Re-hit cooldown gate
    if (!CanHit(collidedTransform)) return;

    // Record this hit time (so we don't immediately double-hit via ray/trigger)
    lastHitTime[collidedTransform] = Time.time;

    if (turretStats.explosionRadius > 0f)
    {
        // Ensure the directly-hit enemy ALWAYS receives on-hit bullet effects
        Explode(collidedTransform);
    }
    else
    {
        // Non-explosive: direct hit damage as usual
        Damage(collidedTransform);
        targetsPierced += enemy.pierceTaken;

        // Start post-hit homing suppression based on seekStrength
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

            // Respect re-hit cooldown too (keeps it from immediately snapping back)
            if (!CanHit(t)) continue;

            // If we haven't hit the primary yet, don't consider switching to others
            if (!primaryHitOnce && primaryTarget != null && t != primaryTarget) continue;

            Vector3 to = (t.position - transform.position);
            float sqrDist = to.sqrMagnitude;
            if (sqrDist >= bestDist) continue;

            Vector3 dir = to.normalized;
            float dot = Vector3.Dot(fwd, dir);
            if (dot >= frontConeDot) // within front cone
            {
                best = t;
                bestDist = sqrDist;
            }
        }

        if (best != target && best != null)
        {
            target = best;
        }
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

            // Also skip if within re-hit cooldown
            if (!CanHit(et)) continue;

            // If we haven't hit the primary yet, only consider the primary itself
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

    // VFX
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

    // Ensure the enemy we physically collided with is ALWAYS damaged first
    if (primaryHit != null)
    {
        Enemy pe = primaryHit.GetComponent<Enemy>();
        if (pe != null && !pe.isDead)
        {
            // We already passed CanHit in HandleCollision and stamped lastHitTime there.
            Damage(primaryHit);
            targetsPierced += pe.pierceTaken;
            enemiesDamaged++;

            // If we’ve exhausted pierce capacity with the primary, we can end here.
            if (enemiesDamaged >= pierceLimit && turretStats.explosionsPierce)
            {
                DestroyBullet();
                return;
            }
        }
    }

    // AoE pass
    Collider[] colliders = Physics.OverlapSphere(transform.position, turretStats.explosionRadius);
    for (int i = 0; i < colliders.Length; i++)
    {
        Collider c = colliders[i];
        if (c == null || !c.CompareTag("Enemy")) continue;

        Transform t = c.transform;
        if (primaryHit != null && t == primaryHit) continue; // already applied

        Enemy e = t.GetComponent<Enemy>();
        if (e == null || e.isDead) continue;

        // Respect re-hit cooldown for AoE targets (ok—primary already got hit above)
        if (!CanHit(t)) continue;

        Damage(t);
        lastHitTime[t] = Time.time;  // record AoE hit time
        targetsPierced += e.pierceTaken;
        enemiesDamaged++;

        // Stop early if we reached pierce capacity
        if (turretStats.explosionsPierce && enemiesDamaged >= pierceLimit)
            break;
    }

    // Post-hit homing suppression based on seekStrength
    homingDisableTimer = ComputePostHitDisableSeconds();

    // Destruction logic:
    // - If explosions don't pierce, the projectile ends after exploding.
    // - If they do pierce, end when we’ve consumed our pierce capacity.
    if (!turretStats.explosionsPierce || enemiesDamaged >= pierceLimit || targetsPierced >= pierceLimit)
    {
        DestroyBullet();
    }
    else
    {
        // Allow quick re-targeting for homing bullets after the explosion
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

    // === Post-hit homing suppression mapping ===
    // Clamp seekStrength to [1,10], map 1->1s, 10->0s linearly.
    float ComputePostHitDisableSeconds()
    {
        float s = Mathf.Clamp(turretStats.seekStrength, 1f, 10f);
        return (10f - s) / 9f;  // s=1 => 1.0s, s=10 => 0.0s
    }
}