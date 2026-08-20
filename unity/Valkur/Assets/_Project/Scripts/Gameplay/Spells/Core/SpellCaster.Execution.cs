using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    public partial class SpellCaster : MonoBehaviour
    {

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

            // Play spell SFX by spellKey (e.g. "fireball" → fireball SFX in catalog).
            // This is a SPECULATIVE probe: most spells have no authored clip yet, and a
            // spell without a sound is missing content, not a data bug. Gate on HasSfx so
            // the miss stays silent — calling PlaySfxById blind warns once per spellKey,
            // which used to dirty the console the moment anyone cast anything but fireball.
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && !string.IsNullOrEmpty(spell.spellKey) && audio.HasSfx(spell.spellKey))
                audio.PlaySfxById(spell.spellKey);

            // Broadcast cast for HUD overlays (cooldown countdown stack, etc.).
            // Primitive payload keeps Valkur.Core free of Valkur.Data references.
            GameEvents.FireSpellCast(gameObject, spell.spellKey, spell.displayName, spell.cooldownDuration);
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
        /// <paramref name="ignoreManaCost"/> is a per-call authoring override used by
        /// the active Spells Editor; it is never stored on the caster or spell.
        /// </summary>
        public bool TryCastByKey(string spellKey, Vector2 direction, bool ignoreManaCost = false)
        {
            if (_phase != CastPhase.Ready) return false;
            if (!_spellBook.TryGetValue(spellKey, out var spell)) return false;
            if (_spellBookCooldowns.TryGetValue(spellKey, out float cd) && cd > 0f) return false;

            int manaCost = Mathf.Max(0, Mathf.RoundToInt(spell.manaCost));
            if (manaCost > 0 && !ignoreManaCost)
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

        /// <summary>
        /// Read-only enumeration of all spells registered in the spell book
        /// (used by HUD action bars). Order is dictionary-insertion order.
        /// </summary>
        public IEnumerable<KeyValuePair<string, SpellDefinition>> GetAllRegisteredSpells()
        {
            return _spellBook;
        }

        /// <summary>Number of spells in the spell book.</summary>
        public int RegisteredSpellCount => _spellBook.Count;

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
