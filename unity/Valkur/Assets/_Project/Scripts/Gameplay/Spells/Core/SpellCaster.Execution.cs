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

            // The flourish on the CASTER. Fired here rather than from PlayerController because
            // this is the one seam every cast passes through — a monster casting gets it too,
            // which is most of what makes an enemy wind-up readable. It refuses the two spell
            // types it would be wrong for; see SpellCastFlourishFX.Play.
            SpellCastFlourishFX.Play(spell, transform, _castDirection);

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
            // ResolveCooldown, not spell.cooldownDuration: the reduction stat has to reach
            // every path a cast can leave by, and there are three of them (slot, book, and
            // the deferred one AdvancePhase takes after a prepare or a channel). A stat
            // honoured by two of the three is a stat the player cannot verify.
            float cooldown = ResolveCooldown(spell);
            _cooldownTimers[slotIndex] = cooldown;
            if (cooldown > 0f)
            {
                _phase = CastPhase.Cooldown;
                _phaseTimer = cooldown;
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
            float cooldown = ResolveCooldown(spell);
            if (!string.IsNullOrEmpty(_activeKey))
                _spellBookCooldowns[_activeKey] = cooldown;

            if (cooldown > 0f)
            {
                _phase = CastPhase.Cooldown;
                _phaseTimer = cooldown;
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

            int manaCost = ResolveManaCost(spell);
            if (manaCost > 0 && !ignoreManaCost)
            {
                var mana = ResolveMana();
                if (mana != null)
                {
                    if (!mana.TryConsume(manaCost)) return false;
                }
                else if (!_freeCastWithoutMana)
                {
                    if (!_missingManaWarningLogged)
                    {
                        Debug.LogWarning($"[SpellCaster] Spell '{spell.spellKey}' requires mana ({manaCost}) but no Mana component on '{name}'.");
                        _missingManaWarningLogged = true;
                    }
                    return false;
                }
                // else: opted out via SetFreeCastWithoutMana(true) — casts for free
                // (BossCueDispatcher's chart-driven casts route through here).
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
                float cooldown = ResolveCooldown(spell);
                _spellBookCooldowns[spellKey] = cooldown;
                if (cooldown > 0f)
                {
                    _phase = CastPhase.Cooldown;
                    _phaseTimer = cooldown;
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

        // ── Known-spell sync + stat scaling ──────────────────────────────────────

        /// <summary>
        /// Makes the book contain exactly <paramref name="keys"/> and nothing else.
        ///
        /// Replacement rather than addition is the point: a respec has to take spells
        /// away, and an additive sync can never do that. Cooldowns already in flight are
        /// preserved for the keys that survive, so relearning a spell mid-cooldown does not
        /// hand the player a free reset.
        /// </summary>
        /// <summary>
        /// While true, <see cref="ReplaceSpellBook"/> is refused and the book keeps
        /// everything registered in it.
        ///
        /// It exists for the Spells Editor and the dev console, which cast spells the
        /// character has not learned — that is their entire job. The F4 editor casts
        /// through <c>TryCastByKey</c>, and the nineteen <c>AnimationProbe</c> spells exist
        /// so an artist can watch an animation that no gameplay spell will ever reach. A
        /// book synced to the known set would empty both out from under them.
        ///
        /// A flag rather than a permanent carve-out because "the book is exactly what the
        /// character knows" has to stay a statement a test can check in normal play.
        /// </summary>
        public bool AuthoringUnlockAll { get; private set; }

        public void SetAuthoringUnlockAll(bool value) => AuthoringUnlockAll = value;

        public void ReplaceSpellBook(IReadOnlyList<string> keys,
                                     System.Func<string, SpellDefinition> resolve)
        {
            if (keys == null || AuthoringUnlockAll) return;

            _spellBookReplaceBuffer.Clear();
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;

                // Prefer the definition already in the book: on a save load the resolver
                // has not been given a catalog yet, and dropping a spell the caster already
                // holds would silently un-learn it.
                if (!_spellBook.TryGetValue(key, out var def) || def == null)
                    def = resolve != null ? resolve(key) : null;

                if (def == null)
                {
                    Debug.LogWarning($"[SpellCaster] Known spell '{key}' could not be " +
                                     "resolved to a SpellDefinition — it will not be castable.");
                    continue;
                }
                _spellBookReplaceBuffer[key] = def;
            }

            _spellBook.Clear();
            foreach (var pair in _spellBookReplaceBuffer)
            {
                _spellBook[pair.Key] = pair.Value;
                if (!_spellBookCooldowns.ContainsKey(pair.Key))
                    _spellBookCooldowns[pair.Key] = 0f;
            }

            // Drop cooldown bookkeeping for spells that are no longer known, or a respec
            // would leave the dictionary growing across every rebuild of the run.
            _spellBookTickBuffer.Clear();
            foreach (var key in _spellBookCooldowns.Keys)
                if (!_spellBook.ContainsKey(key)) _spellBookTickBuffer.Add(key);
            foreach (var key in _spellBookTickBuffer)
                _spellBookCooldowns.Remove(key);
            _spellBookTickBuffer.Clear();
        }

        public bool KnowsSpell(string key)
            => !string.IsNullOrEmpty(key) && _spellBook.ContainsKey(key);

        // Resolved lazily and re-queried while null, because EntitySetup adds PlayerStats
        // after the SpellCaster on the player prefab — the same reason Health re-queries
        // PlayerSpiritState. A monster never has one, so the lookup stays null and every
        // multiplier below reads 1.
        private PlayerStats _casterStats;

        private PlayerStats ResolveStats()
        {
            if (_casterStats == null) _casterStats = GetComponent<PlayerStats>();
            return _casterStats;
        }

        /// <summary>Mana this caster actually pays for a spell, after ManaCostReduction.</summary>
        public int ResolveManaCost(SpellDefinition spell)
        {
            if (spell == null) return 0;
            float cost = spell.manaCost;
            var stats = ResolveStats();
            if (stats != null) cost *= stats.SpellManaCostMultiplier;
            return Mathf.Max(0, Mathf.RoundToInt(cost));
        }

        /// <summary>Cooldown this caster actually waits, after SpellCooldownReduction.</summary>
        public float ResolveCooldown(SpellDefinition spell)
        {
            if (spell == null) return 0f;
            float cd = spell.cooldownDuration;
            var stats = ResolveStats();
            if (stats != null) cd *= stats.SpellCooldownMultiplier;
            return Mathf.Max(0f, cd);
        }
    }
}
