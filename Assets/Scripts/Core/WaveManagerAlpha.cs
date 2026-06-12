using System.Collections.Generic;
using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Basic enemy spawning system. Spawns primitive enemies (cubes/spheres) at
    /// the arena perimeter that march linearly toward the player. Tracks wave
    /// progression and scales enemy count and health each wave.
    /// </summary>
    public class WaveManagerAlpha : MonoBehaviour
    {
        private GameCore _core;

        [Header("Spawn Settings")]
        public float spawnRadius = 13f;
        public float spawnInterval = 0.9f;
        public int baseEnemiesPerWave = 6;
        public int enemiesAddedPerWave = 3;

        [Header("Enemy Scaling")]
        public float baseEnemyHealth = 50f;
        public float healthPerWave = 20f;
        public float baseEnemySpeed = 2.2f;
        public float speedPerWave = 0.15f;
        public float enemyContactDamage = 10f;

        [Header("Wave Flow")]
        public float timeBetweenWaves = 3f;

        public int CurrentWave { get; private set; }
        public int AliveEnemyCount => _enemies.Count;

        private readonly List<EnemyUnit> _enemies = new List<EnemyUnit>();
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
        }

        private void Update()
        {
            if (!_running || _core == null || _core.CombatFrozen)
            {
                return;
            }

            HandleSpawning();
            UpdateEnemies();
            HandleWaveProgression();
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
                _spawnTimer = spawnInterval;
            }
        }

        private void SpawnEnemy()
        {
            // Alternate between cube and sphere primitive enemies.
            bool isCube = (_enemiesSpawnedThisWave % 2) == 0;
            PrimitiveType type = isCube ? PrimitiveType.Cube : PrimitiveType.Sphere;

            GameObject enemyObj = GameObject.CreatePrimitive(type);
            enemyObj.name = "Enemy_W" + CurrentWave;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 pos = new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);
            enemyObj.transform.position = pos;
            enemyObj.transform.localScale = Vector3.one * (isCube ? 1.1f : 1.2f);

            enemyObj.GetComponent<Renderer>().material =
                GameCore.MakeMaterial(new Color(0.92f, 0.92f, 0.95f, 1f));
            Destroy(enemyObj.GetComponent<Collider>());

            var unit = enemyObj.AddComponent<EnemyUnit>();
            float hp = baseEnemyHealth + (CurrentWave - 1) * healthPerWave;
            float speed = baseEnemySpeed + (CurrentWave - 1) * speedPerWave;
            unit.Setup(this, _core, hp, speed, enemyContactDamage);

            _enemies.Add(unit);
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

        public void NotifyEnemyKilled(EnemyUnit unit, bool byPlayer)
        {
            _enemies.Remove(unit);

            if (byPlayer && _core.Health != null)
            {
                _core.Health.RegisterKill(CurrentWave);
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
    }

    /// <summary>
    /// Individual enemy unit. Marches linearly toward the player, holds a health
    /// pool, takes damage from projectiles, and damages the player on contact.
    /// </summary>
    public class EnemyUnit : MonoBehaviour
    {
        private WaveManagerAlpha _manager;
        private GameCore _core;
        private float _health;
        private float _maxHealth;
        private float _speed;
        private float _contactDamage;
        private float _contactRadius = 1.2f;
        private Renderer _renderer;
        private Color _baseColor;
        private float _hitFlash;

        public void Setup(WaveManagerAlpha manager, GameCore core, float health, float speed, float contactDamage)
        {
            _manager = manager;
            _core = core;
            _health = health;
            _maxHealth = health;
            _speed = speed;
            _contactDamage = contactDamage;
            _renderer = GetComponent<Renderer>();
            _baseColor = _renderer != null ? _renderer.material.color : Color.white;
        }

        public void Tick(Vector3 playerPos)
        {
            // Linear march toward the player (transform-based, no physics).
            Vector3 dir = playerPos - transform.position;
            dir.y = 0f;
            float dist = dir.magnitude;

            if (dist > 0.001f)
            {
                transform.position += (dir / dist) * _speed * Time.deltaTime;
                transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
            }

            // Hit flash decay.
            if (_hitFlash > 0f && _renderer != null)
            {
                _hitFlash -= Time.deltaTime * 4f;
                _renderer.material.color = Color.Lerp(_baseColor, Color.red, Mathf.Clamp01(_hitFlash));
            }

            // Contact with player.
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
}
