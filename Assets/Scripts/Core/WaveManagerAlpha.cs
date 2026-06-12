using System.Collections.Generic;
using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Enemy archetypes with fully divergent behavior profiles.
    /// </summary>
    public enum EnemyArchetype
    {
        Cube,    // Tank / Breaker  - slow, heavy, performs a high-velocity dash when close.
        Sphere,  // Swarmer / Scout - fast, fragile, weaves with a sine-wave orbital offset.
        Pyramid  // Stalker / Sniper - keeps its distance and fires slow telemetry projectiles.
    }

    /// <summary>
    /// Phase 2 wave manager. Spawns three behaviorally distinct enemy archetypes,
    /// scales difficulty per wave, integrates with the BiomeManager (spawn cadence
    /// and palette) and the ProceduralAudioManager (impact cues), manages enemy
    /// telemetry projectiles, and supports the DND board-wipe enemy shatter.
    /// </summary>
    public class WaveManagerAlpha : MonoBehaviour
    {
        private GameCore _core;

        [Header("Spawn Settings")]
        public float spawnRadius = 13f;
        public float baseSpawnInterval = 0.9f;
        public int baseEnemiesPerWave = 6;
        public int enemiesAddedPerWave = 3;

        [Header("Tactical Unit Scaling")]
        // The behavior thresholds below are authored in the same "tactical units"
        // used by the WebGL telemetry build (pixel-space). This factor maps those
        // numbers onto the compact 30-unit Unity diorama so behavior reads the same.
        public float tacticalUnitToWorld = 0.05f;

        [Header("Cube (Tank / Breaker)")]
        public float cubeHealth = 160f;
        public float cubeHealthPerWave = 40f;
        public float cubeMarchSpeed = 1.1f;
        public float cubeDashTriggerTacticalRadius = 120f; // dashes once within this range
        public float cubeDashSpeed = 16f;
        public float cubeDashDuration = 0.45f;
        public float cubeDashCooldown = 1.8f;
        public float cubeContactDamage = 18f;

        [Header("Sphere (Swarmer / Scout)")]
        public float sphereHealth = 40f;
        public float sphereHealthPerWave = 10f;
        public float sphereSpeed = 4.2f;
        public float sphereSpeedPerWave = 0.2f;
        public float sphereWeaveAmplitude = 3.0f;   // lateral sine offset (world units)
        public float sphereWeaveFrequency = 4.0f;    // oscillations per second
        public float sphereContactDamage = 8f;

        [Header("Pyramid (Stalker / Sniper)")]
        public float pyramidHealth = 70f;
        public float pyramidHealthPerWave = 18f;
        public float pyramidSpeed = 2.6f;
        public float pyramidStandoffTacticalDistance = 150f; // hard-held distance
        public float pyramidFireInterval = 2.2f;
        public float pyramidProjectileSpeed = 4.5f;          // intentionally slow telemetry
        public float pyramidProjectileDamage = 12f;
        public float pyramidContactDamage = 10f;

        [Header("Wave Flow")]
        public float timeBetweenWaves = 3f;

        public int CurrentWave { get; private set; }
        public int AliveEnemyCount => _enemies.Count;

        // World-space derived thresholds (computed from the tactical values).
        public float CubeDashTriggerWorld => cubeDashTriggerTacticalRadius * tacticalUnitToWorld;
        public float PyramidStandoffWorld => pyramidStandoffTacticalDistance * tacticalUnitToWorld;

        private readonly List<EnemyUnit> _enemies = new List<EnemyUnit>();
        private readonly List<TelemetryProjectile> _enemyProjectiles = new List<TelemetryProjectile>();
        private bool _running;
        private int _enemiesToSpawnThisWave;
        private int _enemiesSpawnedThisWave;
        private float _spawnTimer;
        private float _interWaveTimer;
        private bool _waitingForNextWave;

        public void Initialize(GameCore core)
        {
            _core = core;
        }

        public void BeginWaves()
        {
            _running = true;
            CurrentWave = 0;
            ClearEnemies();
            StartNextWave();
        }

        public void StopWaves()
        {
            _running = false;
            ClearEnemies();
            ClearEnemyProjectiles();
        }

        private void StartNextWave()
        {
            CurrentWave++;
            _enemiesToSpawnThisWave = baseEnemiesPerWave + (CurrentWave - 1) * enemiesAddedPerWave;
            _enemiesSpawnedThisWave = 0;
            _spawnTimer = 0f;
            _waitingForNextWave = false;

            if (_core.Health != null)
            {
                _core.Health.SetWave(CurrentWave);
            }

            // Inform the biome matrix; it decides whether to rotate biome / board-wipe.
            if (_core.Biome != null)
            {
                _core.Biome.OnWaveStarted(CurrentWave);
            }
        }

        private void Update()
        {
            if (!_running || _core == null || _core.CombatFrozen)
            {
                return;
            }

            HandleSpawning();
            UpdateEnemies();
            UpdateEnemyProjectiles();
            HandleWaveProgression();
        }

        private float CurrentSpawnInterval()
        {
            // Biome can speed up / slow down the spawn cadence.
            float cadence = _core.Biome != null ? _core.Biome.SpawnCadenceMultiplier : 1f;
            return Mathf.Max(0.15f, baseSpawnInterval * cadence);
        }

        private void HandleSpawning()
        {
            if (_enemiesSpawnedThisWave >= _enemiesToSpawnThisWave)
            {
                return;
            }

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f)
            {
                SpawnEnemy();
                _enemiesSpawnedThisWave++;
                _spawnTimer = CurrentSpawnInterval();
            }
        }

        /// <summary>
        /// Picks an archetype using a wave-weighted distribution: spheres are common
        /// early, cubes and pyramids become more frequent as waves climb.
        /// </summary>
        private EnemyArchetype PickArchetype()
        {
            float roll = Random.value;
            float cubeChance = Mathf.Clamp(0.20f + CurrentWave * 0.02f, 0.20f, 0.45f);
            float pyramidChance = Mathf.Clamp(0.10f + CurrentWave * 0.02f, 0.10f, 0.35f);

            if (roll < cubeChance)
            {
                return EnemyArchetype.Cube;
            }
            if (roll < cubeChance + pyramidChance)
            {
                return EnemyArchetype.Pyramid;
            }
            return EnemyArchetype.Sphere;
        }

        private void SpawnEnemy()
        {
            EnemyArchetype archetype = PickArchetype();

            GameObject enemyObj = CreateArchetypeObject(archetype);
            enemyObj.name = "Enemy_" + archetype + "_W" + CurrentWave;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 pos = new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);
            enemyObj.transform.position = pos;

            Color enemyColor = _core.Biome != null
                ? _core.Biome.EnemyColorFor(archetype)
                : DefaultColorFor(archetype);
            var rend = enemyObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = GameCore.MakeMaterial(enemyColor);
            }

            float speedScale = _core.Biome != null ? _core.Biome.EnemySpeedMultiplier : 1f;
            float fireScale = _core.Biome != null ? _core.Biome.FireRateMultiplier : 1f;

            var unit = enemyObj.AddComponent<EnemyUnit>();
            unit.Setup(this, _core, archetype, CurrentWave, speedScale, fireScale, enemyColor);

            _enemies.Add(unit);
        }

        /// <summary>
        /// Builds the visual GameObject for an archetype. Cubes/Spheres use Unity
        /// primitives; Pyramids use a procedurally generated 4-sided pyramid mesh.
        /// </summary>
        private GameObject CreateArchetypeObject(EnemyArchetype archetype)
        {
            GameObject obj;
            switch (archetype)
            {
                case EnemyArchetype.Cube:
                    obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obj.transform.localScale = Vector3.one * 1.4f;
                    break;

                case EnemyArchetype.Pyramid:
                    obj = new GameObject("Pyramid");
                    var mf = obj.AddComponent<MeshFilter>();
                    var mr = obj.AddComponent<MeshRenderer>();
                    mf.sharedMesh = BuildPyramidMesh();
                    mr.material = GameCore.MakeMaterial(Color.white);
                    obj.transform.localScale = Vector3.one * 1.5f;
                    break;

                default: // Sphere
                    obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    obj.transform.localScale = Vector3.one * 0.95f;
                    break;
            }

            // Transform-based movement only; no physics colliders.
            var col = obj.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
            return obj;
        }

        private static Color DefaultColorFor(EnemyArchetype a)
        {
            switch (a)
            {
                case EnemyArchetype.Cube: return new Color(0.95f, 0.30f, 0.35f, 1f);
                case EnemyArchetype.Pyramid: return new Color(0.65f, 0.45f, 1f, 1f);
                default: return new Color(1f, 0.70f, 0.25f, 1f);
            }
        }

        private void UpdateEnemies()
        {
            if (_core.Player == null)
            {
                return;
            }

            Vector3 playerPos = _core.Player.transform.position;

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                EnemyUnit e = _enemies[i];
                if (e == null)
                {
                    _enemies.RemoveAt(i);
                    continue;
                }
                e.Tick(playerPos);
            }
        }

        private void UpdateEnemyProjectiles()
        {
            for (int i = _enemyProjectiles.Count - 1; i >= 0; i--)
            {
                TelemetryProjectile p = _enemyProjectiles[i];
                if (p == null)
                {
                    _enemyProjectiles.RemoveAt(i);
                }
            }
        }

        private void HandleWaveProgression()
        {
            bool waveCleared = _enemiesSpawnedThisWave >= _enemiesToSpawnThisWave && _enemies.Count == 0;

            if (waveCleared && !_waitingForNextWave)
            {
                _waitingForNextWave = true;
                _interWaveTimer = timeBetweenWaves;
            }

            if (_waitingForNextWave)
            {
                _interWaveTimer -= Time.deltaTime;
                if (_interWaveTimer <= 0f)
                {
                    StartNextWave();
                }
            }
        }

        #region Enemy telemetry projectiles (Pyramid)

        /// <summary>Spawns a slow-moving enemy telemetry projectile aimed at a point.</summary>
        public void SpawnTelemetryProjectile(Vector3 origin, Vector3 direction, Color color)
        {
            GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projObj.name = "TelemetryProjectile";
            projObj.transform.position = origin;
            projObj.transform.localScale = Vector3.one * 0.5f;
            var rend = projObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = GameCore.MakeMaterial(color);
            }
            Destroy(projObj.GetComponent<Collider>());

            var proj = projObj.AddComponent<TelemetryProjectile>();
            proj.Launch(this, _core, direction.normalized, pyramidProjectileSpeed, pyramidProjectileDamage, 6f);
            _enemyProjectiles.Add(proj);
        }

        public void NotifyTelemetryHitPlayer(TelemetryProjectile proj, float damage)
        {
            _enemyProjectiles.Remove(proj);
            if (_core.Health != null)
            {
                _core.Health.ApplyDamageToPlayer(damage);
            }
            if (_core.Audio != null)
            {
                _core.Audio.PlayImpact();
            }
        }

        public void NotifyTelemetryExpired(TelemetryProjectile proj)
        {
            _enemyProjectiles.Remove(proj);
        }

        private void ClearEnemyProjectiles()
        {
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                if (_enemyProjectiles[i] != null)
                {
                    Destroy(_enemyProjectiles[i].gameObject);
                }
            }
            _enemyProjectiles.Clear();
        }

        #endregion

        #region Notifications & queries

        public void NotifyEnemyKilled(EnemyUnit unit, bool byPlayer)
        {
            _enemies.Remove(unit);

            if (byPlayer && _core.Health != null)
            {
                _core.Health.RegisterKill(CurrentWave);
            }
            if (_core.Audio != null)
            {
                _core.Audio.PlayImpact();
            }
        }

        public void NotifyEnemyContact(EnemyUnit unit, float contactDamage)
        {
            if (_core.Health != null)
            {
                _core.Health.ApplyDamageToPlayer(contactDamage);
            }
            _enemies.Remove(unit);
        }

        /// <summary>Returns the closest living enemy within range, or null.</summary>
        public Transform GetNearestEnemy(Vector3 origin, float maxRange)
        {
            Transform nearest = null;
            float best = maxRange * maxRange;

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyUnit e = _enemies[i];
                if (e == null)
                {
                    continue;
                }

                float d = (e.transform.position - origin).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = e.transform;
                }
            }
            return nearest;
        }

        /// <summary>Returns the first enemy within the radius (for projectile hits).</summary>
        public EnemyUnit GetEnemyInRadius(Vector3 point, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyUnit e = _enemies[i];
                if (e == null)
                {
                    continue;
                }

                if ((e.transform.position - point).sqrMagnitude <= r2)
                {
                    return e;
                }
            }
            return null;
        }

        #endregion

        #region Board wipe

        /// <summary>
        /// Kinetic board-wipe support: explode every active enemy into a custom
        /// particle cloud, then clear the field. Called by the BiomeManager during
        /// the DND board-wipe transition.
        /// </summary>
        public void ShatterAllEnemies(Color shardColor)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyUnit e = _enemies[i];
                if (e != null)
                {
                    SpawnShatterCloud(e.transform.position, shardColor, 10);
                    Destroy(e.gameObject);
                }
            }
            _enemies.Clear();

            // Telemetry projectiles get shattered too for a clean board.
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                if (_enemyProjectiles[i] != null)
                {
                    SpawnShatterCloud(_enemyProjectiles[i].transform.position, shardColor, 4);
                    Destroy(_enemyProjectiles[i].gameObject);
                }
            }
            _enemyProjectiles.Clear();

            // Reset wave spawn bookkeeping so a fresh wave can begin post-wipe.
            _enemiesSpawnedThisWave = _enemiesToSpawnThisWave;
            _waitingForNextWave = false;
        }

        private void SpawnShatterCloud(Vector3 center, Color color, int shardCount)
        {
            for (int i = 0; i < shardCount; i++)
            {
                GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "Shard";
                shard.transform.position = center + Random.insideUnitSphere * 0.4f;
                shard.transform.localScale = Vector3.one * Random.Range(0.12f, 0.3f);
                var rend = shard.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material = GameCore.MakeMaterial(color);
                }
                Destroy(shard.GetComponent<Collider>());

                Vector3 vel = new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 1f), Random.Range(-1f, 1f)).normalized
                              * Random.Range(4f, 9f);
                var sp = shard.AddComponent<ShatterParticle>();
                sp.Launch(vel, Random.Range(0.5f, 1.0f));
            }
        }

        #endregion

        /// <summary>Recolors all live enemies (used on an instant biome palette swap).</summary>
        public void RepaintEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].ApplyBiomeColor();
                }
            }
        }

        private void ClearEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    Destroy(_enemies[i].gameObject);
                }
            }
            _enemies.Clear();
        }

        /// <summary>
        /// Builds a simple 4-sided pyramid mesh (square base + apex) at runtime,
        /// since Unity has no pyramid primitive.
        /// </summary>
        public static Mesh BuildPyramidMesh()
        {
            var mesh = new Mesh { name = "ProceduralPyramid" };

            // Base square (y = 0) and apex.
            Vector3 b0 = new Vector3(-0.5f, 0f, -0.5f);
            Vector3 b1 = new Vector3(0.5f, 0f, -0.5f);
            Vector3 b2 = new Vector3(0.5f, 0f, 0.5f);
            Vector3 b3 = new Vector3(-0.5f, 0f, 0.5f);
            Vector3 apex = new Vector3(0f, 1f, 0f);

            // 4 side triangles (3 verts each) + 2 base triangles = 18 verts (flat shading).
            Vector3[] verts =
            {
                b0, b1, apex,   // side 1
                b1, b2, apex,   // side 2
                b2, b3, apex,   // side 3
                b3, b0, apex,   // side 4
                b0, b2, b1,     // base tri 1
                b0, b3, b2      // base tri 2
            };

            int[] tris = new int[verts.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                tris[i] = i;
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// A single enemy. Behavior is fully driven by its EnemyArchetype:
    /// Cube (march + dash), Sphere (sine-weave flank), Pyramid (standoff + fire).
    /// </summary>
    public class EnemyUnit : MonoBehaviour
    {
        private WaveManagerAlpha _manager;
        private GameCore _core;
        private EnemyArchetype _archetype;

        private float _health;
        private float _maxHealth;
        private float _baseSpeed;
        private float _contactDamage;
        private float _contactRadius = 1.2f;

        private Renderer _renderer;
        private Color _biomeColor;
        private float _hitFlash;

        // Cube dash state.
        private enum CubeState { March, Dashing, Cooldown }
        private CubeState _cubeState = CubeState.March;
        private Vector3 _dashDir;
        private float _dashTimer;
        private float _cooldownTimer;

        // Sphere weave phase.
        private float _weavePhase;

        // Pyramid fire timer.
        private float _fireTimer;

        public void Setup(WaveManagerAlpha manager, GameCore core, EnemyArchetype archetype,
                          int wave, float biomeSpeedMul, float biomeFireMul, Color biomeColor)
        {
            _manager = manager;
            _core = core;
            _archetype = archetype;
            _renderer = GetComponent<Renderer>();
            _biomeColor = biomeColor;
            _weavePhase = Random.Range(0f, Mathf.PI * 2f);

            switch (archetype)
            {
                case EnemyArchetype.Cube:
                    _maxHealth = manager.cubeHealth + (wave - 1) * manager.cubeHealthPerWave;
                    _baseSpeed = manager.cubeMarchSpeed * biomeSpeedMul;
                    _contactDamage = manager.cubeContactDamage;
                    _contactRadius = 1.5f;
                    break;

                case EnemyArchetype.Pyramid:
                    _maxHealth = manager.pyramidHealth + (wave - 1) * manager.pyramidHealthPerWave;
                    _baseSpeed = manager.pyramidSpeed * biomeSpeedMul;
                    _contactDamage = manager.pyramidContactDamage;
                    _contactRadius = 1.3f;
                    _fireTimer = manager.pyramidFireInterval / Mathf.Max(0.1f, biomeFireMul);
                    break;

                default: // Sphere
                    _maxHealth = manager.sphereHealth + (wave - 1) * manager.sphereHealthPerWave;
                    _baseSpeed = (manager.sphereSpeed + (wave - 1) * manager.sphereSpeedPerWave) * biomeSpeedMul;
                    _contactDamage = manager.sphereContactDamage;
                    _contactRadius = 1.0f;
                    break;
            }

            _health = _maxHealth;
            _biomeFireMul = biomeFireMul;
        }

        private float _biomeFireMul = 1f;

        public void ApplyBiomeColor()
        {
            if (_renderer != null && _core.Biome != null)
            {
                _biomeColor = _core.Biome.EnemyColorFor(_archetype);
                _renderer.material.color = _biomeColor;
            }
        }

        public void Tick(Vector3 playerPos)
        {
            Vector3 toPlayer = playerPos - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.0001f ? toPlayer / dist : Vector3.forward;

            switch (_archetype)
            {
                case EnemyArchetype.Cube:
                    TickCube(dir, dist);
                    break;
                case EnemyArchetype.Sphere:
                    TickSphere(dir, dist);
                    break;
                case EnemyArchetype.Pyramid:
                    TickPyramid(playerPos, dir, dist);
                    break;
            }

            UpdateHitFlash();
            HandlePlayerContact(dist);
        }

        /// <summary>
        /// Cube: low speed linear march until within the dash-trigger radius, then a
        /// high-velocity linear dash burst, followed by a cooldown before re-arming.
        /// </summary>
        private void TickCube(Vector3 dir, float dist)
        {
            float dashTrigger = _manager.CubeDashTriggerWorld;

            switch (_cubeState)
            {
                case CubeState.March:
                    transform.position += dir * _baseSpeed * Time.deltaTime;
                    if (dist <= dashTrigger)
                    {
                        // Lock in the dash vector and accelerate.
                        _dashDir = dir;
                        _dashTimer = _manager.cubeDashDuration;
                        _cubeState = CubeState.Dashing;
                        if (_core.Audio != null)
                        {
                            _core.Audio.PlayImpact();
                        }
                    }
                    break;

                case CubeState.Dashing:
                    transform.position += _dashDir * _manager.cubeDashSpeed * Time.deltaTime;
                    _dashTimer -= Time.deltaTime;
                    if (_dashTimer <= 0f)
                    {
                        _cubeState = CubeState.Cooldown;
                        _cooldownTimer = _manager.cubeDashCooldown;
                    }
                    break;

                case CubeState.Cooldown:
                    transform.position += dir * (_baseSpeed * 0.5f) * Time.deltaTime;
                    _cooldownTimer -= Time.deltaTime;
                    if (_cooldownTimer <= 0f)
                    {
                        _cubeState = CubeState.March;
                    }
                    break;
            }

            transform.Rotate(Vector3.up, 60f * Time.deltaTime, Space.World);
        }

        /// <summary>
        /// Sphere: advances toward the player but adds a perpendicular sine-wave
        /// offset so it weaves and flanks rather than charging head-on.
        /// </summary>
        private void TickSphere(Vector3 dir, float dist)
        {
            _weavePhase += _manager.sphereWeaveFrequency * Time.deltaTime;

            // Perpendicular (right-hand) vector on the ground plane.
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            float lateral = Mathf.Sin(_weavePhase) * _manager.sphereWeaveAmplitude;

            Vector3 forwardStep = dir * _baseSpeed * Time.deltaTime;
            Vector3 lateralStep = perp * lateral * Time.deltaTime;

            transform.position += forwardStep + lateralStep;
            transform.Rotate(Vector3.up, 200f * Time.deltaTime, Space.World);
        }

        /// <summary>
        /// Pyramid: holds a hard standoff distance. Backs away if the player gets
        /// closer than the threshold, and periodically fires slow telemetry shots.
        /// </summary>
        private void TickPyramid(Vector3 playerPos, Vector3 dir, float dist)
        {
            float standoff = _manager.PyramidStandoffWorld;
            float deadband = 0.6f;

            if (dist < standoff - deadband)
            {
                // Too close: retreat away from the player.
                transform.position -= dir * _baseSpeed * Time.deltaTime;
            }
            else if (dist > standoff + deadband)
            {
                // Too far: close in until at the standoff ring.
                transform.position += dir * _baseSpeed * Time.deltaTime;
            }
            // Otherwise hold position at the standoff ring.

            // Slowly rotate the pyramid to face the player (apex stays up).
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 5f * Time.deltaTime);
            }

            // Fire telemetry projectiles on an interval.
            _fireTimer -= Time.deltaTime;
            if (_fireTimer <= 0f)
            {
                Vector3 origin = transform.position + Vector3.up * 0.6f;
                _manager.SpawnTelemetryProjectile(origin, dir, _biomeColor);
                _fireTimer = _manager.pyramidFireInterval / Mathf.Max(0.1f, _biomeFireMul);
                if (_core.Audio != null)
                {
                    _core.Audio.PlayLaser();
                }
            }
        }

        private void UpdateHitFlash()
        {
            if (_hitFlash > 0f && _renderer != null)
            {
                _hitFlash -= Time.deltaTime * 4f;
                _renderer.material.color = Color.Lerp(_biomeColor, Color.white, Mathf.Clamp01(_hitFlash));
            }
        }

        private void HandlePlayerContact(float dist)
        {
            if (dist <= _contactRadius)
            {
                _manager.NotifyEnemyContact(this, _contactDamage);
                Destroy(gameObject);
            }
        }

        public void TakeDamage(float amount)
        {
            _health -= amount;
            _hitFlash = 1f;

            if (_health <= 0f)
            {
                _manager.NotifyEnemyKilled(this, true);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Slow-moving enemy projectile fired by Pyramids. Straight-line travel,
    /// distance-based hit detection against the player, self-destruct on timeout.
    /// </summary>
    public class TelemetryProjectile : MonoBehaviour
    {
        private WaveManagerAlpha _manager;
        private GameCore _core;
        private Vector3 _direction;
        private float _speed;
        private float _damage;
        private float _hitRadius = 1.1f;
        private float _life;

        public void Launch(WaveManagerAlpha manager, GameCore core, Vector3 direction,
                           float speed, float damage, float lifetime)
        {
            _manager = manager;
            _core = core;
            _direction = direction;
            _speed = speed;
            _damage = damage;
            _life = lifetime;
        }

        private void Update()
        {
            if (_core == null || _core.CombatFrozen)
            {
                return;
            }

            transform.position += _direction * _speed * Time.deltaTime;

            _life -= Time.deltaTime;
            if (_life <= 0f)
            {
                _manager.NotifyTelemetryExpired(this);
                Destroy(gameObject);
                return;
            }

            if (_core.Player != null)
            {
                float d = (transform.position - _core.Player.transform.position).sqrMagnitude;
                if (d <= _hitRadius * _hitRadius)
                {
                    _manager.NotifyTelemetryHitPlayer(this, _damage);
                    Destroy(gameObject);
                }
            }
        }
    }

    /// <summary>
    /// A board-wipe shard. Flies outward with a fixed velocity, shrinks/fades, and
    /// self-destructs. Used to shatter enemies into custom particle clouds.
    /// </summary>
    public class ShatterParticle : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _life;
        private float _maxLife;
        private Vector3 _spin;
        private Vector3 _baseScale;

        public void Launch(Vector3 velocity, float life)
        {
            _velocity = velocity;
            _life = life;
            _maxLife = life;
            _baseScale = transform.localScale;
            _spin = new Vector3(Random.Range(-360f, 360f), Random.Range(-360f, 360f), Random.Range(-360f, 360f));
        }

        private void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // Gravity-ish pull and drag for a satisfying burst arc.
            _velocity += Vector3.down * 6f * Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;
            transform.Rotate(_spin * Time.deltaTime, Space.Self);

            float t = Mathf.Clamp01(_life / _maxLife);
            transform.localScale = _baseScale * t;
        }
    }
}
