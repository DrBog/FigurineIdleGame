using System.Collections.Generic;
using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Wave director for the divergent enemy roster. Spawns a procedurally-built mix of three
    /// archetypes, scales their count and stats per round, and injects a single high-health
    /// boss on every fifth wave.
    ///
    /// Composition (non-boss waves):
    ///   * 40% Cube   (Tank / Breaker)
    ///   * 40% Sphere (Swarmer / Scout)
    ///   * 20% Pyramid (Stalker / Sniper)
    ///
    /// Per-wave scaling:
    ///   * enemy count grows linearly with the wave index (clamped to a sane maximum),
    ///   * health and speed grow on gentle curves so later waves bite harder.
    ///
    /// Boss waves (every 5th round) spawn exactly one oversized Cube with a large health pool
    /// and contact damage, instead of the usual swarm.
    ///
    /// The spawner is self-contained: it builds each enemy GameObject (mesh / collider /
    /// material / rigidbody) in code, attaches an <see cref="EnemyAI"/>, tags it "Enemy", and
    /// tracks the live set so the next wave only begins once the arena is clear. It reads the
    /// active biome for enemy colours and asks the biome to evaluate a transition at the start
    /// of every wave.
    /// </summary>
    public class AdvancedWaveSpawner : MonoBehaviour
    {
        [Header("Composition")]
        [Range(0.0f, 1.0f)] public float cubeShare = 0.40f;
        [Range(0.0f, 1.0f)] public float sphereShare = 0.40f;
        // Pyramid share is the remainder (≈0.20).

        [Header("Scaling")]
        [SerializeField] private int _baseEnemyCount = 4;
        [SerializeField] private float _countGrowthPerWave = 1.15f;
        [SerializeField] private int _maxEnemiesPerWave = 26;

        [SerializeField] private float _baseHealth = 30.0f;
        [SerializeField] private float _healthGrowthPerWave = 6.0f;
        [SerializeField] private float _baseSpeed = 3.2f;
        [SerializeField] private float _speedGrowthPerWave = 0.16f;
        [SerializeField] private float _maxSpeed = 9.0f;
        [SerializeField] private float _baseContactDamage = 9.0f;
        [SerializeField] private float _contactDamageGrowthPerWave = 0.7f;

        [Header("Boss")]
        [SerializeField] private int _bossEveryNWaves = 5;
        [SerializeField] private float _bossHealthMultiplier = 14.0f;
        [SerializeField] private float _bossScale = 3.4f;
        [SerializeField] private float _bossSpeedFactor = 0.55f;
        [SerializeField] private float _bossContactDamage = 26.0f;

        [Header("Spawn geometry")]
        [SerializeField] private float _spawnRadius = 22.0f;
        [SerializeField] private float _spawnRadiusJitter = 4.0f;
        [SerializeField] private float _interSpawnDelay = 0.18f;
        [SerializeField] private float _postClearDelay = 1.4f;

        public int CurrentWave { get; private set; }
        public bool IsRunning { get; private set; }
        public bool WaveActive { get; private set; }
        public int LiveEnemyCount => _active.Count;

        private GameCore _core;
        private BiomeManager _biome;
        private ProceduralAudioManager _audio;
        private PlayerController _player;

        private readonly List<EnemyAI> _active = new List<EnemyAI>();
        private float _nextSpawnTime;
        private int _pendingSpawns;
        private readonly Queue<SpawnRequest> _spawnQueue = new Queue<SpawnRequest>();
        private float _clearedAtTime = -1.0f;

        private struct SpawnRequest
        {
            public EnemyArchetype Archetype;
            public bool IsBoss;
        }

        /// <summary>
        /// Wire the spawner to the active subsystems. Call once after GameCore has built
        /// the biome / audio / player references.
        /// </summary>
        public void Initialize(GameCore core)
        {
            _core = core;
            if (_core != null)
            {
                _biome = _core.Biome;
                _audio = _core.Audio;
                _player = _core.Player;
            }
            CurrentWave = 0;
            WaveActive = false;
            IsRunning = false;
            _active.Clear();
            _spawnQueue.Clear();
            _clearedAtTime = -1.0f;
        }

        public void BeginWaves()
        {
            IsRunning = true;
            WaveActive = false;
            _clearedAtTime = Time.time;
        }

        public void StopWaves()
        {
            IsRunning = false;
            WaveActive = false;
            _spawnQueue.Clear();
            ClearLiveEnemies();
        }

        private void Update()
        {
            if (!IsRunning || _core == null)
            {
                return;
            }

            // Pause all wave logic during board-wipe slow-mo / pause windows.
            if (_core.CombatFrozen)
            {
                return;
            }

            // Drain queued spawns on a small cadence so a wave materialises over time
            // rather than all at once.
            if (_spawnQueue.Count > 0 && Time.time >= _nextSpawnTime)
            {
                SpawnRequest req = _spawnQueue.Dequeue();
                SpawnEnemy(req.Archetype, req.IsBoss);
                _nextSpawnTime = Time.time + _interSpawnDelay;
            }

            PruneDeadReferences();

            // A wave is finished once the queue is empty and every spawned enemy is gone.
            if (WaveActive && _spawnQueue.Count == 0 && _active.Count == 0)
            {
                WaveActive = false;
                _clearedAtTime = Time.time;
            }

            // Start the next wave after a short breather.
            if (!WaveActive && _spawnQueue.Count == 0 && _active.Count == 0)
            {
                if (_clearedAtTime < 0.0f || Time.time - _clearedAtTime >= _postClearDelay)
                {
                    StartWave();
                }
            }
        }

        private void StartWave()
        {
            CurrentWave++;
            WaveActive = true;

            // Let the biome decide whether this wave triggers an environment transition.
            if (_biome != null)
            {
                _biome.CheckForBiomeTransition(CurrentWave);
            }

            if (_core.Health != null)
            {
                _core.Health.SetWave(CurrentWave);
            }

            if (_audio != null)
            {
                _audio.PlayUITick();
            }

            bool isBossWave = _bossEveryNWaves > 0 && (CurrentWave % _bossEveryNWaves == 0);
            if (isBossWave)
            {
                EnqueueSpawn(EnemyArchetype.Cube, true);
            }
            else
            {
                BuildSwarm(CurrentWave);
            }

            _nextSpawnTime = Time.time;
        }

        private void BuildSwarm(int wave)
        {
            int count = Mathf.Clamp(
                Mathf.RoundToInt(_baseEnemyCount + (wave - 1) * _countGrowthPerWave),
                _baseEnemyCount,
                _maxEnemiesPerWave);

            for (int i = 0; i < count; i++)
            {
                EnqueueSpawn(RollArchetype(), false);
            }
        }

        private EnemyArchetype RollArchetype()
        {
            float roll = Random.value;
            if (roll < cubeShare)
            {
                return EnemyArchetype.Cube;
            }
            if (roll < cubeShare + sphereShare)
            {
                return EnemyArchetype.Sphere;
            }
            return EnemyArchetype.Pyramid;
        }

        private void EnqueueSpawn(EnemyArchetype archetype, bool isBoss)
        {
            _spawnQueue.Enqueue(new SpawnRequest { Archetype = archetype, IsBoss = isBoss });
        }

        private void SpawnEnemy(EnemyArchetype archetype, bool isBoss)
        {
            GameObject go = BuildEnemyObject(archetype, isBoss, out float baseScale);

            // Position on a ring around the arena origin.
            Vector3 spawnPos = PickSpawnPosition();
            spawnPos.y = baseScale * 0.5f;
            go.transform.position = spawnPos;

            // Colour from the active biome palette.
            Color color = new Color(0.85f, 0.4f, 0.3f, 1.0f);
            if (_biome != null)
            {
                color = _biome.EnemyColorFor(archetype);
            }
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GameCore.MakeMaterial(color);
            }

            // Rigidbody (EnemyAI configures kinematics in Awake).
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = go.AddComponent<Rigidbody>();
            }

            // Tag for board-wipe sweeps & player targeting. Wrapped because the tag must exist
            // in the project's Tag Manager; fall back silently if it is not defined.
            TrySetEnemyTag(go);

            // Per-wave stat scaling.
            float health = _baseHealth + (CurrentWave - 1) * _healthGrowthPerWave;
            float speed = Mathf.Min(_maxSpeed, _baseSpeed + (CurrentWave - 1) * _speedGrowthPerWave);
            float contact = _baseContactDamage + (CurrentWave - 1) * _contactDamageGrowthPerWave;

            if (isBoss)
            {
                health *= _bossHealthMultiplier;
                speed *= _bossSpeedFactor;
                contact = _bossContactDamage;
            }

            // Biome speed modifier (if the active biome scales enemy speed).
            if (_biome != null)
            {
                speed *= Mathf.Max(0.1f, _biome.EnemySpeedMultiplier);
            }

            var ai = go.GetComponent<EnemyAI>();
            if (ai == null)
            {
                ai = go.AddComponent<EnemyAI>();
            }
            ai.Initialize(_core, archetype, health, speed, contact, CurrentWave, isBoss);

            _active.Add(ai);

            if (_audio != null)
            {
                _audio.PlayUITick();
            }
        }

        /// <summary>
        /// Build the visual GameObject for an enemy. Cubes and Spheres use Unity primitives;
        /// Pyramids use a custom mesh generated by <see cref="WaveManagerAlpha.BuildPyramidMesh"/>.
        /// </summary>
        private GameObject BuildEnemyObject(EnemyArchetype archetype, bool isBoss, out float baseScale)
        {
            baseScale = isBoss ? _bossScale : 1.0f;
            GameObject go;

            switch (archetype)
            {
                case EnemyArchetype.Cube:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = isBoss ? "Boss_Cube" : "Enemy_Cube";
                    go.transform.localScale = Vector3.one * baseScale;
                    break;

                case EnemyArchetype.Sphere:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Enemy_Sphere";
                    go.transform.localScale = Vector3.one * baseScale;
                    break;

                case EnemyArchetype.Pyramid:
                default:
                    go = new GameObject("Enemy_Pyramid");
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = WaveManagerAlpha.BuildPyramidMesh();
                    go.AddComponent<MeshRenderer>();
                    var box = go.AddComponent<BoxCollider>();
                    box.size = Vector3.one;
                    box.center = new Vector3(0.0f, 0.5f, 0.0f);
                    go.transform.localScale = Vector3.one * baseScale;
                    break;
            }

            return go;
        }

        private Vector3 PickSpawnPosition()
        {
            float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
            float radius = _spawnRadius + Random.Range(-_spawnRadiusJitter, _spawnRadiusJitter);
            return new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
        }

        private void TrySetEnemyTag(GameObject go)
        {
            try
            {
                go.tag = "Enemy";
            }
            catch (UnityEngine.UnityException)
            {
                // "Enemy" tag not defined in the Tag Manager — leave the default tag.
            }
        }

        private void PruneDeadReferences()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i] == null)
                {
                    _active.RemoveAt(i);
                }
            }
        }

        private void ClearLiveEnemies()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i] != null)
                {
                    Destroy(_active[i].gameObject);
                }
            }
            _active.Clear();
        }
    }
}
