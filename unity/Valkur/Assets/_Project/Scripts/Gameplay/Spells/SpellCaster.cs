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
            if (_cooldownTimers == null || slot < 0 || slot >= _cooldownTimers.Length) return 0f;
            if (_cooldownTimers[slot] <= 0f) return 0f;
            var spell = GetSpellAtSlot(slot);
            if (spell == null || spell.cooldownDuration <= 0f) return 0f;
            return Mathf.Clamp01(_cooldownTimers[slot] / spell.cooldownDuration);
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
            SpellDefinition spell = null;
            if (_activeSlot >= 0 && _activeSlot < spellSlots.Length)
                spell = spellSlots[_activeSlot];
            else if (!string.IsNullOrEmpty(_activeKey) && _spellBook.TryGetValue(_activeKey, out var bookSpell))
                spell = bookSpell;

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
                        StartCooldownForSpell(spell);
                    }
                    break;

                case CastPhase.Channel:
                    StartCooldownForSpell(spell);
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
            {
                Debug.Log($"[SpellCaster] Executing '{spell.spellKey}' (type={spell.type}) on {name} → {executor.GetType().Name}, dir={_castDirection}, dmg={spell.damage}, cd={spell.cooldownDuration:F2}s");
                executor.Execute(ctx);
            }
            else
            {
                Debug.LogWarning($"[SpellCaster] No executor for type {spell.type}, falling back to Projectile for '{spell.spellKey}'");
                Executors[SpellType.Projectile].Execute(ctx);
            }

            // Play spell SFX by spellKey (e.g. "fireball" → fireball SFX in catalog)
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && !string.IsNullOrEmpty(spell.spellKey))
                audio.PlaySfxById(spell.spellKey);
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

        /// <summary>
        /// Starts cooldown for spell resolved from either slot or book.
        /// Called from AdvancePhase when the cast was initiated via either path.
        /// </summary>
        private void StartCooldownForSpell(SpellDefinition spell)
        {
            // If we know the slot, use the slot-based cooldown array
            if (_activeSlot >= 0 && _activeSlot < _cooldownTimers.Length)
            {
                StartCooldown(spell, _activeSlot);
                return;
            }

            // Otherwise use spell book cooldown dictionary
            if (!string.IsNullOrEmpty(_activeKey))
                _spellBookCooldowns[_activeKey] = spell.cooldownDuration;

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
            _activeKey = null;
        }

        // ── Spell Book API ──

        /// <summary>
        /// Registers a spell in the key-based spell book for bindings beyond 4 slots.
        /// </summary>
        public void RegisterSpell(string key, SpellDefinition spell)
        {
            if (string.IsNullOrEmpty(key) || spell == null) return;
            _spellBook[key] = spell;
            if (!_spellBookCooldowns.ContainsKey(key))
                _spellBookCooldowns[key] = 0f;
        }

        /// <summary>
        /// Try to cast a spell from the spell book by its key.
        /// </summary>
        public bool TryCastByKey(string spellKey, Vector2 direction)
        {
            if (_phase != CastPhase.Ready) return false;
            if (!_spellBook.TryGetValue(spellKey, out var spell)) return false;
            if (_spellBookCooldowns.TryGetValue(spellKey, out float cd) && cd > 0f) return false;

            int manaCost = Mathf.Max(0, Mathf.RoundToInt(spell.manaCost));
            if (manaCost > 0)
            {
                var mana = ResolveMana();
                if (mana == null)
                {
                    if (!_missingManaWarningLogged)
                    {
                        Debug.LogWarning($"[SpellCaster] Spell '{spell.spellKey}' requires mana ({manaCost}) but no Mana component on '{name}'.");
                        _missingManaWarningLogged = true;
                    }
                    return false;
                }
                if (!mana.TryConsume(manaCost)) return false;
            }

            _activeSlot = -1;
            _activeKey = spellKey;
            _castDirection = direction.normalized;

            if (spell.prepareDuration > 0f)
            {
                _phase = CastPhase.Prepare;
                _phaseTimer = spell.prepareDuration;
            }
            else
            {
                ExecuteSpell(spell);
                _spellBookCooldowns[spellKey] = spell.cooldownDuration;
                if (spell.cooldownDuration > 0f)
                {
                    _phase = CastPhase.Cooldown;
                    _phaseTimer = spell.cooldownDuration;
                }
            }

            Debug.Log($"[SpellCaster] TryCastByKey '{spellKey}' → {spell.displayName} (type={spell.type})");
            return true;
        }

        public SpellDefinition GetSpellByKey(string key)
        {
            _spellBook.TryGetValue(key, out var spell);
            return spell;
        }

        public float GetBookCooldownRemaining(string key)
        {
            if (_spellBookCooldowns.TryGetValue(key, out float cd))
                return Mathf.Max(0f, cd);
            return 0f;
        }

        private Mana ResolveMana()
        {
            if (_mana == null)
                _mana = GetComponent<Mana>();
            return _mana;
        }
    }
}
