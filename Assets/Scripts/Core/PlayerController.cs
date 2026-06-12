using UnityEngine;

namespace FigurineIdleGame.Core
{
    /// <summary>
    /// Transform-based player controller (no Rigidbody). Reads normalized input
    /// from the VirtualJoystick, moves the figurine, tracks distance traveled,
    /// and auto-fires basic sphere projectiles at the nearest enemy.
    /// Visual representation: Capsule chassis with a hovering Diamond primitive.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        private GameCore _core;
        private VirtualJoystick _joystick;

        [Header("Movement")]
        public float moveSpeed = 7.5f;

        [Header("Combat")]
        public float damage = 25.0f;
        public float fireInterval = 0.45f;
        public float projectileSpeed = 18f;
        public float fireRange = 16f;
        public float projectileLifetime = 3f;

        [Header("Bounds")]
        public float arenaHalfExtent = 14f;

        public float TotalDistanceTraveled { get; private set; }

        private Transform _chassis;
        private Transform _diamond;
        private float _fireTimer;
        private Vector3 _lastPosition;
        private float _diamondBaseHeight;

        public void Initialize(GameCore core)
        {
            _core = core;
            moveSpeed = core.playerMoveSpeed;
            damage = core.playerDamage;
            BuildVisual();
            _lastPosition = transform.position;
        }

        public void AttachJoystick(VirtualJoystick joystick)
        {
            _joystick = joystick;
        }

        private void BuildVisual()
        {
            // Capsule chassis.
            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            chassis.name = "Chassis";
            chassis.transform.SetParent(transform, false);
            chassis.transform.localPosition = Vector3.zero;
            chassis.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
            chassis.GetComponent<Renderer>().material = GameCore.MakeMaterial(new Color(0.25f, 0.65f, 0.95f, 1f));
            // Remove primitive collider; player uses proximity checks, not physics.
            Destroy(chassis.GetComponent<Collider>());
            _chassis = chassis.transform;

            // Hovering Diamond marker (a Cube rotated 45deg, squashed into a diamond).
            GameObject diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diamond.name = "DiamondMarker";
            diamond.transform.SetParent(transform, false);
            _diamondBaseHeight = 1.9f;
            diamond.transform.localPosition = new Vector3(0f, _diamondBaseHeight, 0f);
            diamond.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            diamond.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            diamond.GetComponent<Renderer>().material = GameCore.MakeMaterial(new Color(1f, 0.85f, 0.25f, 1f));
            Destroy(diamond.GetComponent<Collider>());
            _diamond = diamond.transform;
        }

        public void ResetForRun()
        {
            transform.position = new Vector3(0f, 1.25f, 0f);
            _lastPosition = transform.position;
            TotalDistanceTraveled = 0f;
            _fireTimer = 0f;
        }

        private void Update()
        {
            AnimateDiamond();

            if (_core == null || _core.CombatFrozen)
            {
                return;
            }

            HandleMovement();
            HandleAutoFire();
        }

        private void AnimateDiamond()
        {
            if (_diamond == null)
            {
                return;
            }

            // Gentle hover bob + spin for visual life (cheap, frame-rate independent).
            float bob = Mathf.Sin(Time.time * 2.5f) * 0.12f;
            _diamond.localPosition = new Vector3(0f, _diamondBaseHeight + bob, 0f);
            _diamond.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
        }

        private void HandleMovement()
        {
            Vector2 input = _joystick != null ? _joystick.Direction : Vector2.zero;

            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 move = new Vector3(input.x, 0f, input.y) * moveSpeed * Time.deltaTime;
                transform.Translate(move, Space.World);

                // Clamp inside the diorama platform bounds.
                Vector3 p = transform.position;
                p.x = Mathf.Clamp(p.x, -arenaHalfExtent, arenaHalfExtent);
                p.z = Mathf.Clamp(p.z, -arenaHalfExtent, arenaHalfExtent);
                p.y = 1.25f;
                transform.position = p;

                // Face the movement direction.
                if (_chassis != null)
                {
                    Vector3 lookDir = new Vector3(input.x, 0f, input.y);
                    _chassis.rotation = Quaternion.Slerp(
                        _chassis.rotation,
                        Quaternion.LookRotation(lookDir, Vector3.up),
                        10f * Time.deltaTime);
                }
            }

            TotalDistanceTraveled += Vector3.Distance(transform.position, _lastPosition);
            _lastPosition = transform.position;
        }

        private void HandleAutoFire()
        {
            _fireTimer -= Time.deltaTime;
            if (_fireTimer > 0f)
            {
                return;
            }

            Transform target = FindNearestEnemy();
            if (target == null)
            {
                return;
            }

            FireProjectile(target);
            _fireTimer = fireInterval;
        }

        private Transform FindNearestEnemy()
        {
            if (_core.WaveManager == null)
            {
                return null;
            }

            return _core.WaveManager.GetNearestEnemy(transform.position, fireRange);
        }

        private void FireProjectile(Transform target)
        {
            GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projObj.name = "Projectile";
            projObj.transform.position = transform.position + Vector3.up * 1.0f;
            projObj.transform.localScale = Vector3.one * 0.35f;
            projObj.GetComponent<Renderer>().material = GameCore.MakeMaterial(new Color(1f, 0.95f, 0.5f, 1f));
            Destroy(projObj.GetComponent<Collider>());

            var proj = projObj.AddComponent<Projectile>();
            Vector3 dir = (target.position - projObj.transform.position).normalized;
            proj.Launch(_core, dir, projectileSpeed, damage, projectileLifetime);
        }
    }

    /// <summary>
    /// Lightweight projectile. Travels in a straight line and applies damage to
    /// the first enemy it gets close to. No Rigidbody — uses simple distance checks.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private GameCore _core;
        private Vector3 _direction;
        private float _speed;
        private float _damage;
        private float _hitRadius = 0.7f;

        public void Launch(GameCore core, Vector3 direction, float speed, float damage, float lifetime)
        {
            _core = core;
            _direction = direction;
            _speed = speed;
            _damage = damage;
            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            if (_core != null && _core.CombatFrozen)
            {
                return;
            }

            transform.position += _direction * _speed * Time.deltaTime;

            if (_core == null || _core.WaveManager == null)
            {
                return;
            }

            EnemyUnit hit = _core.WaveManager.GetEnemyInRadius(transform.position, _hitRadius);
            if (hit != null)
            {
                hit.TakeDamage(_damage);
                Destroy(gameObject);
            }
        }
    }
}
