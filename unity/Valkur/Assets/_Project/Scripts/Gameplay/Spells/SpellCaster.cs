using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spell casting system with prepare/channel/cooldown phases.
    /// Maps to Python's SpellConfig FSM phases and spell casting logic.
    /// </summary>
    public class SpellCaster : MonoBehaviour
    {
        public enum CastPhase { Ready, Prepare, Channel, Cooldown }

        [Header("Slots")]
        [SerializeField] private SpellDefinition[] spellSlots = new SpellDefinition[4];

        [Header("Projectile")]
        [SerializeField] private GameObject projectilePrefab;

        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayers;

        private CastPhase _phase = CastPhase.Ready;
        private float _phaseTimer;
        private int _activeSlot = -1;
        private Vector2 _castDirection;
        private float[] _cooldownTimers;

        public CastPhase CurrentPhase => _phase;
        public int ActiveSlot => _activeSlot;

        private void Awake()
        {
            _cooldownTimers = new float[spellSlots.Length];
        }

        private void Update()
        {
            // Tick cooldowns
            for (int i = 0; i < _cooldownTimers.Length; i++)
            {
                if (_cooldownTimers[i] > 0f)
                    _cooldownTimers[i] -= Time.deltaTime;
            }

            // Tick active cast phase
            if (_phase != CastPhase.Ready)
            {
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    AdvancePhase();
            }
        }

        /// <summary>
        /// Attempt to cast a spell from the given slot in the given direction.
        /// Returns true if cast started successfully.
        /// </summary>
        public bool TryCast(int slotIndex, Vector2 direction)
        {
            if (slotIndex < 0 || slotIndex >= spellSlots.Length) return false;
            if (_phase != CastPhase.Ready) return false;

            var spell = spellSlots[slotIndex];
            if (spell == null) return false;
            if (_cooldownTimers[slotIndex] > 0f) return false;

            // Check mana (stub — will integrate with player stats)
            _activeSlot = slotIndex;
            _castDirection = direction.normalized;

            // Start prepare phase
            if (spell.prepareDuration > 0f)
            {
                _phase = CastPhase.Prepare;
                _phaseTimer = spell.prepareDuration;
            }
            else
            {
                ExecuteSpell(spell);
                StartCooldown(spell, slotIndex);
            }

            return true;
        }

        public bool CanCast(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= spellSlots.Length) return false;
            if (_phase != CastPhase.Ready) return false;
            if (spellSlots[slotIndex] == null) return false;
            return _cooldownTimers[slotIndex] <= 0f;
        }

        public float GetCooldownRemaining(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _cooldownTimers.Length) return 0f;
            return Mathf.Max(0f, _cooldownTimers[slotIndex]);
        }

        public void SetSpell(int slotIndex, SpellDefinition spell)
        {
            if (slotIndex >= 0 && slotIndex < spellSlots.Length)
                spellSlots[slotIndex] = spell;
        }

        public void SetTargetLayers(LayerMask layers)
        {
            targetLayers = layers;
        }

        public void SetProjectilePrefab(GameObject prefab)
        {
            projectilePrefab = prefab;
        }

        private void AdvancePhase()
        {
            var spell = spellSlots[_activeSlot];
            if (spell == null)
            {
                ResetPhase();
                return;
            }

            switch (_phase)
            {
                case CastPhase.Prepare:
                    // Execute the spell effect
                    ExecuteSpell(spell);
                    if (spell.channelDuration > 0f)
                    {
                        _phase = CastPhase.Channel;
                        _phaseTimer = spell.channelDuration;
                    }
                    else
                    {
                        StartCooldown(spell, _activeSlot);
                    }
                    break;

                case CastPhase.Channel:
                    StartCooldown(spell, _activeSlot);
                    break;

                case CastPhase.Cooldown:
                    ResetPhase();
                    break;
            }
        }

        private void ExecuteSpell(SpellDefinition spell)
        {
            switch (spell.type)
            {
                case SpellType.Projectile:
                    SpawnProjectile(spell);
                    break;
                case SpellType.Slash:
                    PerformSlash(spell);
                    break;
                case SpellType.Area:
                    PerformArea(spell);
                    break;
                case SpellType.Dash:
                    PerformDash(spell);
                    break;
                default:
                    SpawnProjectile(spell);
                    break;
            }
        }

        private void SpawnProjectile(SpellDefinition spell)
        {
            if (projectilePrefab == null) return;

            Vector3 spawnPos = transform.position + (Vector3)(_castDirection * 0.5f);
            var go = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

            var proj = go.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.Initialize(
                    _castDirection,
                    spell.speed,
                    spell.damage,
                    spell.lifetime > 0 ? spell.lifetime : 3f,
                    spell.range > 0 ? spell.range : 20f,
                    targetLayers
                );
            }

            // Set sprite if available
            if (spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.sprite = spell.sprite;
            }
        }

        private void PerformSlash(SpellDefinition spell)
        {
            float arc = spell.arcRangeDegrees > 0 ? spell.arcRangeDegrees : 90f;
            float hitRadius = spell.hitRadius > 0 ? spell.hitRadius : spell.range;
            if (hitRadius <= 0) hitRadius = 1.5f;

            var hits = Physics2D.OverlapCircleAll(
                (Vector2)transform.position + _castDirection * (hitRadius * 0.5f),
                hitRadius,
                targetLayers);

            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 toTarget = (hit.transform.position - transform.position).normalized;
                float angle = Vector2.Angle(_castDirection, toTarget);
                if (angle <= arc * 0.5f)
                {
                    health.TakeDamage(Mathf.RoundToInt(spell.damage));
                }
            }
        }

        private void PerformArea(SpellDefinition spell)
        {
            float radius = spell.radius > 0 ? spell.radius : 2f;
            Vector2 center = (Vector2)transform.position + _castDirection * radius;

            var hits = Physics2D.OverlapCircleAll(center, radius, targetLayers);
            foreach (var hit in hits)
            {
                if (hit.gameObject == gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    health.TakeDamage(Mathf.RoundToInt(spell.damage));
                }
            }
        }

        private void PerformDash(SpellDefinition spell)
        {
            float dist = spell.distance > 0 ? spell.distance : 3f;
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.MovePosition(rb.position + _castDirection * dist);
            }

            // Collision damage during dash
            if (spell.collisionDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(transform.position, 1f, targetLayers);
                foreach (var hit in hits)
                {
                    if (hit.gameObject == gameObject) continue;
                    var health = hit.GetComponent<Health>();
                    if (health != null && !health.IsDead)
                        health.TakeDamage(Mathf.RoundToInt(spell.collisionDamage));
                }
            }
        }

        private void StartCooldown(SpellDefinition spell, int slotIndex)
        {
            _cooldownTimers[slotIndex] = spell.cooldownDuration;
            if (spell.cooldownDuration > 0f)
            {
                _phase = CastPhase.Cooldown;
                _phaseTimer = spell.cooldownDuration;
            }
            else
            {
                ResetPhase();
            }
        }

        private void ResetPhase()
        {
            _phase = CastPhase.Ready;
            _phaseTimer = 0f;
            _activeSlot = -1;
        }
    }
}
