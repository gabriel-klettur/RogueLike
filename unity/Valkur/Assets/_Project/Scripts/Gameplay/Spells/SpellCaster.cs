using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spell casting coordinator: phase FSM (prepare/channel/cooldown),
    /// cooldown management, and mana consumption.
    /// Delegates actual spell execution to ISpellExecutor strategies.
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
        private Mana _mana;
        private bool _missingManaWarningLogged;

        private static readonly Dictionary<SpellType, ISpellExecutor> Executors = new Dictionary<SpellType, ISpellExecutor>
        {
            { SpellType.Projectile,    new ProjectileExecutor() },
            { SpellType.Slash,         new SlashExecutor() },
            { SpellType.Area,          new AreaExecutor() },
            { SpellType.Dash,          new DashExecutor() },
            { SpellType.Teleport,      new TeleportExecutor() },
            { SpellType.Boomerang,     new BoomerangExecutor() },
            { SpellType.Lightning,     new LightningExecutor() },
            { SpellType.ChainLightning,new LightningExecutor() },
            { SpellType.Beam,          new LaserBeamExecutor() },
        };

        public CastPhase CurrentPhase => _phase;
        public int ActiveSlot => _activeSlot;
        public float PhaseTimer => _phaseTimer;
        public int SlotCount => spellSlots != null ? spellSlots.Length : 0;

        public string GetSlotName(int slot)
        {
            if (spellSlots == null || slot < 0 || slot >= spellSlots.Length || spellSlots[slot] == null) return "-";
            return spellSlots[slot].displayName;
        }

        private void Awake()
        {
            _cooldownTimers = new float[spellSlots.Length];
            _mana = GetComponent<Mana>();
        }

        private void Update()
        {
            for (int i = 0; i < _cooldownTimers.Length; i++)
            {
                if (_cooldownTimers[i] > 0f)
                    _cooldownTimers[i] -= Time.deltaTime;
            }

            if (_phase != CastPhase.Ready)
            {
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    AdvancePhase();
            }
        }

        public bool TryCast(int slotIndex, Vector2 direction)
        {
            if (slotIndex < 0 || slotIndex >= spellSlots.Length) return false;
            if (_phase != CastPhase.Ready) return false;

            var spell = spellSlots[slotIndex];
            if (spell == null) return false;
            if (_cooldownTimers[slotIndex] > 0f) return false;

            int manaCost = Mathf.Max(0, Mathf.RoundToInt(spell.manaCost));
            if (manaCost > 0)
            {
                var mana = ResolveMana();
                if (mana == null)
                {
                    if (!_missingManaWarningLogged)
                    {
                        Debug.LogWarning($"[SpellCaster] Spell '{spell.spellKey}' requires mana ({manaCost}) but no Mana component is present on '{name}'. Cast cancelled.");
                        _missingManaWarningLogged = true;
                    }
                    return false;
                }

                if (!mana.TryConsume(manaCost))
                    return false;
            }

            _activeSlot = slotIndex;
            _castDirection = direction.normalized;

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
            var spell = spellSlots[slotIndex];
            if (spell == null) return false;
            int manaCost = Mathf.Max(0, Mathf.RoundToInt(spell.manaCost));
            if (manaCost > 0)
            {
                var mana = ResolveMana();
                if (mana == null || !mana.HasMana(manaCost))
                    return false;
            }
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
            if (spell == null) { ResetPhase(); return; }

            switch (_phase)
            {
                case CastPhase.Prepare:
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
            var ctx = new SpellContext
            {
                Spell = spell,
                Caster = transform,
                Direction = _castDirection,
                TargetLayers = targetLayers,
                ProjectilePrefab = projectilePrefab
            };

            if (Executors.TryGetValue(spell.type, out var executor))
                executor.Execute(ctx);
            else
                Executors[SpellType.Projectile].Execute(ctx);
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

        private Mana ResolveMana()
        {
            if (_mana == null)
                _mana = GetComponent<Mana>();
            return _mana;
        }
    }
}
