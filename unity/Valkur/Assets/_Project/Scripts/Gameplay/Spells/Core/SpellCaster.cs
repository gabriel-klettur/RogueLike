using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spell casting coordinator: phase FSM (prepare/channel/cooldown),
    /// cooldown management, and mana consumption.
    /// Delegates actual spell execution to ISpellExecutor strategies.
    /// Supports both slot-based and key-based spell lookup (SpellBook).
    /// </summary>
    public partial class SpellCaster : MonoBehaviour
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
        private string _activeKey;
        private Vector2 _castDirection;
        private float[] _cooldownTimers;
        private Mana _mana;
        private bool _missingManaWarningLogged;

        // SpellBook: key-based spell lookup for expanded bindings beyond 4 slots
        private readonly Dictionary<string, SpellDefinition> _spellBook = new Dictionary<string, SpellDefinition>();
        private readonly Dictionary<string, float> _spellBookCooldowns = new Dictionary<string, float>();

        private static readonly Dictionary<SpellType, ISpellExecutor> Executors = new Dictionary<SpellType, ISpellExecutor>
        {
            { SpellType.Projectile,       new ProjectileExecutor() },
            { SpellType.Slash,            new SlashExecutor() },
            { SpellType.Area,             new AreaExecutor() },
            { SpellType.Dash,             new DashExecutor() },
            { SpellType.Teleport,         new TeleportExecutor() },
            { SpellType.Boomerang,        new BoomerangExecutor() },
            { SpellType.Lightning,        new LightningExecutor() },
            { SpellType.ChainLightning,   new LightningExecutor() },
            { SpellType.Beam,             new LaserBeamExecutor() },
            { SpellType.Smoke,            new SmokeExecutor() },
            { SpellType.SmokeEmitter,     new SmokeEmitterExecutor() },
            { SpellType.Wall,             new WallExecutor() },
            { SpellType.Mine,             new MineExecutor() },
            { SpellType.SphereMagicShield,new ShieldExecutor() },
            { SpellType.Meteor,           new MeteorExecutor() },
            { SpellType.Aura,             new AuraExecutor() },
            { SpellType.ArcaneFlame,      new ArcaneFlameExecutor() },
            { SpellType.FireworkLaunch,   new FireworkLaunchExecutor() },
            { SpellType.Puddle,           new PuddleExecutor() },
            { SpellType.VortexField,      new VortexFieldExecutor() },
            { SpellType.ConeBreath,       new ConeBreathExecutor() },
            { SpellType.Summon,           new SummonExecutor() },
            { SpellType.Totem,            new TotemExecutor() },
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

        /// <summary>Get the spell definition at the given slot, or null.</summary>
        public SpellDefinition GetSpellAtSlot(int slot)
        {
            if (spellSlots == null || slot < 0 || slot >= spellSlots.Length) return null;
            return spellSlots[slot];
        }

        /// <summary>Get cooldown progress for a slot (0 = ready, 1 = full cooldown).</summary>
        public float GetCooldownNormalized(int slot)
        {
            EnsureCooldownTimers();
            if (_cooldownTimers == null || slot < 0 || slot >= _cooldownTimers.Length) return 0f;
            if (_cooldownTimers[slot] <= 0f) return 0f;
            var spell = GetSpellAtSlot(slot);
            if (spell == null || spell.cooldownDuration <= 0f) return 0f;
            return Mathf.Clamp01(_cooldownTimers[slot] / spell.cooldownDuration);
        }

        private void Awake()
        {
            EnsureCooldownTimers();
            _mana = GetComponent<Mana>();
        }

        private void Update()
        {
            for (int i = 0; i < _cooldownTimers.Length; i++)
            {
                if (_cooldownTimers[i] > 0f)
                    _cooldownTimers[i] -= Time.deltaTime;
            }

            // Tick spell book cooldowns
            var keysToUpdate = new List<string>();
            foreach (var kv in _spellBookCooldowns)
            {
                if (kv.Value > 0f) keysToUpdate.Add(kv.Key);
            }
            foreach (var key in keysToUpdate)
            {
                _spellBookCooldowns[key] -= Time.deltaTime;
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
            EnsureCooldownTimers();
            if (slotIndex < 0 || slotIndex >= _cooldownTimers.Length) return 0f;
            return Mathf.Max(0f, _cooldownTimers[slotIndex]);
        }

        public void SetSpell(int slotIndex, SpellDefinition spell)
        {
            EnsureCooldownTimers();
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

        private void EnsureCooldownTimers()
        {
            if (spellSlots == null)
                return;

            if (_cooldownTimers == null || _cooldownTimers.Length != spellSlots.Length)
                _cooldownTimers = new float[spellSlots.Length];
        }

    }
}
