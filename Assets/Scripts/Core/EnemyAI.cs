using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Drives a single hostile unit. Three divergent behaviour profiles are selected by
    /// <see cref="EnemyArchetype"/> (the shared enum declared in WaveManagerAlpha.cs):
    ///
    ///   * <b>Cube</b>   — Tank / Breaker. Marches directly at the player and, once within
    ///                     <see cref="_ramTriggerRange"/>, performs a physics-driven ram by
    ///                     switching its rigidbody to non-kinematic and applying an impulse.
    ///   * <b>Sphere</b> — Swarmer / Scout. Advances toward the player while weaving along a
    ///                     sine offset perpendicular to its approach vector (zigzag).
    ///   * <b>Pyramid</b>— Stalker / Sniper. Maintains an ~8 unit stand-off distance, kites
    ///                     away if the player closes in, strafes at range, and fires an
    ///                     <see cref="EnemyProjectile"/> every <see cref="_fireInterval"/>.
    ///
    /// All variants share a health system, deal contact damage to the player on melee range,
    /// and emit a procedural particle burst on death. The component is fully self-contained:
    /// the spawner builds the GameObject (mesh / collider / material / rigidbody / tag) and
    /// then calls <see cref="Initialize"/>. Player weapons damage the enemy through the public
    /// <see cref="TakeDamage"/> entry point.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Identity")]
        public EnemyArchetype Archetype = EnemyArchetype.Cube;
        public bool IsBoss;

        [Header("Stats")]
        public float Health = 30.0f;
        public float MaxHealth = 30.0f;
        public float MoveSpeed = 3.5f;
        public float ContactDamage = 10.0f;

        [Header("Cube — Ram")]
        [SerializeField] private float _ramTriggerRange = 5.0f;
        [SerializeField] private float _ramImpulse = 14.0f;
        [SerializeField] private float _ramDuration = 0.5f;
        [SerializeField] private float _ramCooldown = 1.6f;

        [Header("Sphere — Zigzag")]
        [SerializeField] private float _zigFrequency = 6.5f;
        [SerializeField] private float _zigAmplitude = 0.85f;

        [Header("Pyramid — Stalker")]
        [SerializeField] private float _standoffDistance = 8.0f;
        [SerializeField] private float _kiteThreshold = 6.0f;
        [SerializeField] private float _fireInterval = 1.8f;
        [SerializeField] private float _projectileSpeed = 12.0f;
        [SerializeField] private float _projectileDamage = 8.0f;
        [SerializeField] private float _strafeSpeedFactor = 0.6f;

        [Header("Melee")]
        [SerializeField] private float _meleeRange = 1.7f;
        [SerializeField] private float _meleeCooldown = 0.6f;

        private GameCore _core;
        private Transform _player;
        private Rigidbody _rb;
        private int _wave = 1;

        private float _zigPhase;
        private float _nextFireTime;
        private float _meleeCooldownEnd;
        private int _strafeDir = 1;

        private bool _ramming;
        private float _ramEndTime;
        private float _nextRamTime;

        private bool _dead;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.isKinematic = true;
                _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            _zigPhase = Random.Range(0.0f, Mathf.PI * 2.0f);
            _strafeDir = Random.value < 0.5f ? -1 : 1;
        }

        /// <summary>
        /// Configure the unit. Called by the spawner immediately after the GameObject and its
        /// visual components have been built.
        /// </summary>
        public void Initialize(GameCore core, EnemyArchetype archetype, float health, float speed, float contactDamage, int wave, bool isBoss)
        {
            _core = core;
            Archetype = archetype;
            Health = Mathf.Max(1.0f, health);
            MaxHealth = Health;
            MoveSpeed = Mathf.Max(0.1f, speed);
            ContactDamage = Mathf.Max(0.0f, contactDamage);
            _wave = Mathf.Max(1, wave);
            IsBoss = isBoss;

            _nextFireTime = Time.time + Random.Range(0.4f, _fireInterval);
            _nextRamTime = Time.time;

            if (_core != null && _core.Player != null)
            {
                _player = _core.Player.transform;
            }
        }

        private void Update()
        {
            if (_dead)
            {
                return;
            }

            // Hold completely still while the global combat freeze is active
            // (board-wipe slow-mo, pause, between-state transitions).
            if (_core == null || _core.CombatFrozen)
            {
                return;
            }

            if (_player == null)
            {
                _player = ResolvePlayer();
                if (_player == null)
                {
                    return;
                }
            }

            float dt = Time.deltaTime;
            Vector3 self = transform.position;
            Vector3 toPlayer = _player.position - self;
            toPlayer.y = 0.0f;
            float distance = toPlayer.magnitude;
            Vector3 dir = distance > 0.0001f ? toPlayer / distance : transform.forward;

            switch (Archetype)
            {
                case EnemyArchetype.Cube:
                    TickCube(dir, distance, dt);
                    break;
                case EnemyArchetype.Sphere:
                    TickSphere(dir, dt);
                    break;
                case EnemyArchetype.Pyramid:
                    TickPyramid(dir, distance, dt);
                    break;
            }

            FacePlayer(dir);
            TryMelee(distance);
        }

        #region Behaviour profiles

        private void TickCube(Vector3 dir, float distance, float dt)
        {
            // Currently mid-ram: let physics carry the impulse, then settle back.
            if (_ramming)
            {
                if (Time.time >= _ramEndTime)
                {
                    EndRam();
                }
                return;
            }

            // Begin a ram when close enough and off cooldown.
            if (distance <= _ramTriggerRange && Time.time >= _nextRamTime)
            {
                BeginRam(dir);
                return;
            }

            // Otherwise march steadily toward the player.
            transform.position += dir * (MoveSpeed * dt);
        }

        private void BeginRam(Vector3 dir)
        {
            _ramming = true;
            _ramEndTime = Time.time + _ramDuration;
            _nextRamTime = Time.time + _ramDuration + _ramCooldown;

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.velocity = Vector3.zero;
                _rb.AddForce(dir * (_ramImpulse * Mathf.Max(1.0f, _rb.mass)), ForceMode.Impulse);
            }
        }

        private void EndRam()
        {
            _ramming = false;
            if (_rb != null)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
        }

        private void TickSphere(Vector3 dir, float dt)
        {
            _zigPhase += dt * _zigFrequency;

            // Perpendicular axis in the XZ plane for the weave.
            Vector3 perpendicular = new Vector3(-dir.z, 0.0f, dir.x);
            float weave = Mathf.Sin(_zigPhase) * _zigAmplitude;

            Vector3 move = (dir + perpendicular * weave).normalized;
            transform.position += move * (MoveSpeed * dt);
        }

        private void TickPyramid(Vector3 dir, float distance, float dt)
        {
            if (distance > _standoffDistance)
            {
                // Too far: close the gap.
                transform.position += dir * (MoveSpeed * dt);
            }
            else if (distance < _kiteThreshold)
            {
                // Too close: back away to re-establish stand-off.
                transform.position -= dir * (MoveSpeed * dt);
            }
            else
            {
                // In the sweet spot: strafe laterally to be a harder target.
                Vector3 perpendicular = new Vector3(-dir.z, 0.0f, dir.x) * _strafeDir;
                transform.position += perpendicular * (MoveSpeed * _strafeSpeedFactor * dt);

                // Occasionally flip strafe direction for unpredictability.
                if (Random.value < 0.01f)
                {
                    _strafeDir = -_strafeDir;
                }
            }

            // Fire on cadence whenever roughly within engagement range.
            if (Time.time >= _nextFireTime && distance <= _standoffDistance * 2.5f)
            {
                FireProjectile(dir);
                _nextFireTime = Time.time + _fireInterval;
            }
        }

        private void FireProjectile(Vector3 dir)
        {
            if (_player == null)
            {
                return;
            }

            Vector3 origin = transform.position + dir * 1.0f + Vector3.up * 0.2f;
            Vector3 aim = (_player.position - origin);
            aim.y = 0.0f;
            if (aim.sqrMagnitude < 0.0001f)
            {
                aim = dir;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EnemyProjectile";
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.45f;

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color projColor = new Color(1.0f, 0.55f, 0.2f, 1.0f);
                if (_core != null && _core.Biome != null)
                {
                    projColor = _core.Biome.EnemyColorFor(EnemyArchetype.Pyramid);
                }
                renderer.sharedMaterial = GameCore.MakeMaterial(projColor);
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var projectile = go.AddComponent<EnemyProjectile>();
            projectile.Initialize(_core, aim.normalized, _projectileSpeed, _projectileDamage);

            if (ProceduralAudioManager.Instance != null)
            {
                ProceduralAudioManager.Instance.PlayLaserTone();
            }
        }

        #endregion

        #region Shared combat

        private void FacePlayer(Vector3 dir)
        {
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 8.0f);
            }
        }

        private void TryMelee(float distance)
        {
            if (distance > _meleeRange || Time.time < _meleeCooldownEnd)
            {
                return;
            }

            _meleeCooldownEnd = Time.time + _meleeCooldown;

            if (_core != null && _core.Health != null)
            {
                _core.Health.ApplyDamageToPlayer(ContactDamage);
            }

            if (ProceduralAudioManager.Instance != null)
            {
                ProceduralAudioManager.Instance.PlayImpactSound();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_dead || collision == null || collision.collider == null)
            {
                return;
            }

            bool hitPlayer = collision.collider.CompareTag("Player") ||
                             (_core != null && _core.Player != null && collision.transform == _core.Player.transform);

            if (hitPlayer && Time.time >= _meleeCooldownEnd)
            {
                _meleeCooldownEnd = Time.time + _meleeCooldown;
                if (_core != null && _core.Health != null)
                {
                    _core.Health.ApplyDamageToPlayer(ContactDamage);
                }
                if (ProceduralAudioManager.Instance != null)
                {
                    ProceduralAudioManager.Instance.PlayImpactSound();
                }
                // A ramming cube spends its momentum on the hit.
                if (_ramming)
                {
                    EndRam();
                }
            }
        }

        /// <summary>
        /// Public damage entry point used by the player's weapons.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (_dead || amount <= 0.0f)
            {
                return;
            }

            Health -= amount;
            if (Health <= 0.0f)
            {
                Die(true);
            }
        }

        private Transform ResolvePlayer()
        {
            if (_core != null && _core.Player != null)
            {
                return _core.Player.transform;
            }
            return null;
        }

        private void Die(bool killedByPlayer)
        {
            if (_dead)
            {
                return;
            }
            _dead = true;

            Color burstColor = new Color(1.0f, 0.6f, 0.25f, 1.0f);
            if (_core != null && _core.Biome != null)
            {
                burstColor = _core.Biome.EnemyColorFor(Archetype);
            }
            SpawnDeathParticles(transform.position, burstColor);

            if (killedByPlayer && _core != null && _core.Health != null)
            {
                _core.Health.RegisterKill(_wave);
            }

            if (ProceduralAudioManager.Instance != null)
            {
                ProceduralAudioManager.Instance.PlayExplosionBurst();
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Procedural shatter burst — a short-lived particle system created entirely in code so
        /// the project needs no prefab assets. Mirrors the burst style used by BiomeManager.
        /// </summary>
        private void SpawnDeathParticles(Vector3 position, Color color)
        {
            var holder = new GameObject("EnemyDeathBurst");
            holder.transform.position = position;

            var ps = holder.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.7f;
            main.loop = false;
            main.startLifetime = 0.55f;
            main.startSpeed = IsBoss ? 9.0f : 6.0f;
            main.startSize = IsBoss ? 0.5f : 0.32f;
            main.startColor = color;
            main.gravityModifier = 0.15f;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;
            int burstCount = IsBoss ? 48 : 18;
            emission.SetBursts(new ParticleSystem.Burst[]
            {
                new ParticleSystem.Burst(0.0f, (short)burstCount)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = IsBoss ? 0.9f : 0.45f;

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = color;
                    renderer.sharedMaterial = mat;
                }
            }

            ps.Play();
            Destroy(holder, 1.2f);
        }

        #endregion
    }
}
