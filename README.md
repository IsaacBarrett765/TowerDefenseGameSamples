# Tower Defense Game — Code Samples

These are four C# scripts from a Unity tower defense game I am developing for commercial release. The full project is not public, but these files represent the core gameplay systems and are shared here as a portfolio sample.

The game features a modular turret upgrade system, enemy wave management, multiple targeting strategies, homing projectiles, and a full save/load system — all built in Unity with C#.

---

## Scripts

### `Turret.cs`
The main turret controller. Each turret scans for enemies using `Physics.OverlapSphereNonAlloc` into a pre-allocated static buffer to avoid per-frame allocations. Six targeting modes (First, Farthest, Closest, Last, Strongest, Weakest) are unified through a single `ScoreEnemy()` method — swapping modes does not change the scan loop, only the scoring function. A hysteresis threshold prevents the turret from jitter-swapping targets when scores are nearly equal. Supports both bullet-based and continuous laser attack modes.

### `Bullet.cs`
Projectile behavior with optional homing. When seeking is enabled, the bullet steers toward its target using `Quaternion.RotateTowards` with a configurable turn rate that scales up as the bullet closes in. A primary-target guarantee ensures the bullet tracks its initially assigned target until it lands the first hit before switching to free retargeting. Includes a pierce system (hitting multiple enemies in sequence), AoE explosion logic, per-enemy re-hit cooldowns, and short-range raycasts to catch grazing collisions the trigger collider might miss.

### `Enemy.cs`
Enemy state machine covering health, speed, status effects, and special behaviors. The slow system uses a list of multiplicative factors so multiple independent slow sources combine correctly — removing one source does not clear the others. Status effects (bleed, fire, ice, poison) run as coroutines and are tracked with reference counts so stacking works without duplicating or cancelling existing ticks. Special types include: `isBerserker` (permanently gains speed each time it takes damage), `isHealer` (partially heals in response to damage), `isSummoner` (spawns additional enemies), `isTank` (damage capped per hit), and `isGhost` (plays a different death sequence instead of being destroyed immediately). Distance traveled is tracked per-enemy to support the First/Last targeting modes in `Turret.cs`.

### `WaveSpawner.cs`
Wave management using coroutines. Each wave defines enemy types, counts, and spawn rate. Waves are sent sequentially with a configurable delay between them. Supports a `reverseMode` that iterates the wave array backwards (used for a game mode variant). The win condition checks both that all wave coroutines have finished and that no living enemies remain on the field, polling active enemy tags rather than relying on a counter alone to avoid false wins from edge-case timing.

---

*This project is in active development and not yet released. Full source is not available.*

*Note: I am the original author of these scripts. I used AI tools (ChatGPT/Claude) to help with code cleanup and polish.*
