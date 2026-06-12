using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// A self-contained hostile projectile fired by Pyramid stalker enemies.
    ///
    /// Behaviour:
    ///   * Travels in a fixed direction (locked in at spawn time) at a constant speed.
    ///   * Damages the player on proximity / collision, then self-destructs.
    ///   * Auto-destroys after a fixed lifetime, or when it leaves the arena boundary.
    ///   * Respects the global combat-freeze flag (board-wipe slow-mo / pause) by
    ///     halting movement while frozen so projectiles do not drift during the freeze.
    ///
    /// The projectile is intentionally decoupled from the rest of the combat stack: it
    /// only needs a reference to <see cref="GameCore"/> to read the player transform,
    /// apply damage through <see cref="HealthTracker"/>, and trigger an impact sound on
    /// <see cref="ProceduralAudioManager"/>. Everything else (mesh, collider, material)
    /// is created by the spawner before <see cref="Initialize"/> is called.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyProjectile : MonoBehaviour
    {
        [Header("Flight")]
        [SerializeField] private float _speed = 11.0f;
        [SerializeField] private float _damage = 8.0f;
        [SerializeField] private float _lifetime = 5.0f;
        [SerializeField] private float _hitRadius = 0.85f;
        [SerializeField] private float _arenaBoundary = 55.0f;

        private GameCore _core;
        private Vector3 _direction = Vector3.forward;
        private float _spawnTime;
        private bool _consumed;
        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.useGravity = false;
                _rb.isKinematic = true;
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        /// <summary>
        /// Configure the projectile after the spawner has built its visuals.
        /// </summary>
        /// <param name="core">Active game core (provides player + audio + health).</param>
        /// <param name="direction">World-space travel direction (will be normalised).</param>
        /// <param name="speed">Units per second.</param>
        /// <param name="damage">Damage dealt to the player on impact.</param>
        public void Initialize(GameCore core, Vector3 direction, float speed, float damage)
        {
            _core = core;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            _speed = Mathf.Max(0.1f, speed);
            _damage = Mathf.Max(0.0f, damage);
            _spawnTime = Time.time;
            _consumed = false;

            // Orient the visual mesh so any elongated bolt points along its travel path.
            if (_direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
            }
        }

        private void Update()
        {
            if (_consumed)
            {
                return;
            }

            // Freeze in place during board-wipe slow-mo / pause windows.
            if (_core != null && _core.CombatFrozen)
            {
                return;
            }

            float dt = Time.deltaTime;
            transform.position += _direction * (_speed * dt);

            // Lifetime expiry.
            if (Time.time - _spawnTime >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Arena boundary culling.
            Vector3 p = transform.position;
            if (Mathf.Abs(p.x) > _arenaBoundary || Mathf.Abs(p.z) > _arenaBoundary || p.y < -25.0f || p.y > 60.0f)
            {
                Destroy(gameObject);
                return;
            }

            // Proximity hit test against the player (robust even without physics collisions).
            Transform player = ResolvePlayer();
            if (player != null)
            {
                float sqr = (player.position - transform.position).sqrMagnitude;
                if (sqr <= _hitRadius * _hitRadius)
                {
                    DamagePlayer();
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_consumed || other == null)
            {
                return;
            }

            if (other.CompareTag("Player") || (_core != null && _core.Player != null && other.transform == _core.Player.transform))
            {
                DamagePlayer();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_consumed || collision == null || collision.collider == null)
            {
                return;
            }

            if (collision.collider.CompareTag("Player") || (_core != null && _core.Player != null && collision.transform == _core.Player.transform))
            {
                DamagePlayer();
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

        private void DamagePlayer()
        {
            if (_consumed)
            {
                return;
            }
            _consumed = true;

            if (_core != null && _core.Health != null)
            {
                _core.Health.ApplyDamageToPlayer(_damage);
            }

            if (ProceduralAudioManager.Instance != null)
            {
                ProceduralAudioManager.Instance.PlayImpactSound();
            }

            Destroy(gameObject);
        }
    }
}
