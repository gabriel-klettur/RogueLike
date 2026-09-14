using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Player;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    public sealed partial class PlayerHUD
    {
        private GameObject _player;
        private Health _health;
        private Mana _mana;
        private Energy _energy;
        private Experience _xp;
        private SpellCaster _caster;
        private DashAbility _dash;
        private StatusEffectManager _statuses;
        private PlayerController _controller;
        private bool _globalLevelHooked;

        private int _lastHp = -1;
        private int _lastMana = -1;
        private int _lastEnergy = -1;
        private int _pendingDamage;
        private float _healBurstCooldown;
        private float _refuseCooldown;

        /// <summary>The player this panel is reading. For the tests.</summary>
        public GameObject BoundPlayer => _player;

        /// <summary>
        /// Binds every readout to <paramref name="health"/>'s entity. Safe to call again — it
        /// unbinds the previous one first, which is what a respawn replacing the player needs.
        /// </summary>
        public void Bind(Health health, Mana mana = null)
        {
            Unbind();
            _health = health;
            _player = health != null ? health.gameObject : null;
            if (_player == null) return;

            _mana = mana != null ? mana : _player.GetComponent<Mana>();
            _energy = _player.GetComponent<Energy>();
            _xp = _player.GetComponent<Experience>();
            _caster = _player.GetComponent<SpellCaster>();
            _dash = _player.GetComponent<DashAbility>();
            _statuses = _player.GetComponent<StatusEffectManager>();
            _controller = _player.GetComponent<PlayerController>();

            _health.OnDamaged += OnDamaged;
            _health.OnHpChanged += OnHpChanged;
            if (_mana != null)
            {
                _mana.OnManaChanged += OnManaChanged;
                _mana.OnManaConsumed += OnManaConsumed;
            }
            if (_energy != null) _energy.Changed += OnEnergyChanged;
            if (_xp != null)
            {
                _xp.OnXpGained += OnXpGained;
                _xp.OnXpLost += OnXpLost;
                _xp.OnLevelUp += OnLevelUp;
                // Experience.Initialize — the boot and every save restore — raises THIS and not
                // OnLevelUp. Listening only for level-ups is what left the old badge on "Lvl 0".
                _xp.OnStateChanged += OnXpStateChanged;
            }
            if (_caster != null) _caster.OnCastRefusedForMana += OnCastRefused;
            GameEvents.OnLevelUp += OnGlobalLevelUp;
            _globalLevelHooked = true;

            // Seed without animating: the first values are where the player already is.
            _lastHp = -1;
            _lastMana = -1;
            _lastEnergy = -1;
            OnHpChanged(_health.CurrentHp, _health.MaxHp);
            if (_mana != null) OnManaChanged(_mana.CurrentMana, _mana.MaxMana);
            else _manaBar.SetValue(0, 0, HudBarChange.Silent);
            if (_energy != null) OnEnergyChanged(_energy.Normalized);
            else _energyBar.SetValue(0, 0, HudBarChange.Silent);
            OnXpStateChanged();
            _portrait.Bind(_player);
        }

        private void Unbind()
        {
            if (_health != null)
            {
                _health.OnDamaged -= OnDamaged;
                _health.OnHpChanged -= OnHpChanged;
            }
            if (_mana != null)
            {
                _mana.OnManaChanged -= OnManaChanged;
                _mana.OnManaConsumed -= OnManaConsumed;
            }
            if (_energy != null) _energy.Changed -= OnEnergyChanged;
            if (_xp != null)
            {
                _xp.OnXpGained -= OnXpGained;
                _xp.OnXpLost -= OnXpLost;
                _xp.OnLevelUp -= OnLevelUp;
                _xp.OnStateChanged -= OnXpStateChanged;
            }
            if (_caster != null) _caster.OnCastRefusedForMana -= OnCastRefused;
            if (_globalLevelHooked)
            {
                GameEvents.OnLevelUp -= OnGlobalLevelUp;
                _globalLevelHooked = false;
            }
            _health = null;
            _mana = null;
            _energy = null;
            _xp = null;
            _caster = null;
            _dash = null;
            _statuses = null;
            _controller = null;
            _player = null;
        }

        // -- Health ---------------------------------------------------------------------

        private void OnDamaged(int amount) => _pendingDamage += Mathf.Max(0, amount);

        private void OnHpChanged(int current, int max)
        {
            var change = HudBarChange.Silent;
            if (_lastHp >= 0)
            {
                if (current < _lastHp) change = HudBarChange.Damage;
                else if (current > _lastHp) change = HudBarChange.Heal;
            }
            float before = _healthBar.Shown;
            _healthBar.SetValue(current, max, change);

            if (change == HudBarChange.Damage)
            {
                int lost = _pendingDamage > 0 ? _pendingDamage : _lastHp - current;
                float frac = max > 0 ? Mathf.Clamp01((float)lost / max) : 0f;
                Knock();
                _healthBar.Flash(_style.hitBarFlash, _style.hitFlashSeconds);
                _portrait.Flash(_style.hitPortraitFlash, 0.2f);
                _edge?.Hit(frac, _style);
                BurstDamage(before, _healthBar.Target);
            }
            else if (change == HudBarChange.Heal && current > 0)
            {
                // Regeneration heals a point at a time several times a second; a burst on each
                // would be a permanent fountain. One burst per window.
                if (_healBurstCooldown <= 0f)
                {
                    _healBurstCooldown = 0.35f;
                    // A level-up heals the player in the same frame; its gold flash wins.
                    if (_levelUpLeft <= 0f) _portrait.Flash(_style.healPortraitFlash, 0.25f);
                    BurstHeal(before, _healthBar.Target);
                }
            }
            _lastHp = current;
            _pendingDamage = 0;
        }

        // -- Mana -------------------------------------------------------------------------

        private void OnManaChanged(int current, int max)
        {
            // Spending chips; regeneration glides silently. A heal-style flash on every regen tick
            // would light the bar up permanently.
            var change = _lastMana >= 0 && current < _lastMana ? HudBarChange.Damage : HudBarChange.Silent;
            _manaBar.SetValue(current, max, change);
            _lastMana = current;
        }

        private void OnManaConsumed(int amount) => BurstSpend();

        // -- Energy -----------------------------------------------------------------------

        /// <summary>
        /// A drain from running chips, exactly like a mana spend; regeneration glides silently.
        /// <c>Energy.Changed</c> carries only the normalized fill, so the current/max pair is
        /// read straight off the component rather than from the event's own argument.
        /// </summary>
        private void OnEnergyChanged(float normalized)
        {
            if (_energy == null) return;
            var change = _lastEnergy >= 0 && _energy.Current < _lastEnergy
                ? HudBarChange.Damage : HudBarChange.Silent;
            _energyBar.SetValue(_energy.Current, _energy.Max, change);
            _lastEnergy = _energy.Current;
        }

        private void OnCastRefused(string spellKey)
        {
            // A held primary re-attempts every frame; once every few tenths is the message.
            if (_refuseCooldown > 0f) return;
            _refuseCooldown = 0.45f;
            var c = _manaBar != null ? _style.mana : Color.white;
            c = Color.Lerp(c, Color.white, 0.35f);
            c.a = 0.7f;
            _manaBar.Flash(c, _style.refusedFlashSeconds);
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].SpellKey == spellKey) _slots[i].Refused(_style);
        }

        // -- Experience ------------------------------------------------------------------

        private void OnXpStateChanged()
        {
            if (_xp == null)
            {
                _xpBar.SetRatio(0f, HudBarChange.Silent, instant: true);
                _medallion.SetLevel(0);
                return;
            }
            _medallion.SetLevel(_xp.Level);
            _xpBar.SetRatio(_xp.NormalizedProgress, HudBarChange.Silent, instant: true);
        }

        private void OnXpGained(int amount)
        {
            if (_xp == null) return;
            // A level-up in the same award is handled by OnLevelUp's own sequence.
            if (_levelUpLeft <= 0f) _xpBar.SetRatio(_xp.NormalizedProgress, HudBarChange.Heal);
            _xpBar.Glint();
            if (amount > 0)
            {
                var root = _xpBar.Root.anchoredPosition;
                float x = root.x + _xpBar.XAtRatio(_xp.NormalizedProgress);
                x = Mathf.Clamp(x, 18f, _widthTexels - 18f);
                _floats.Spawn("+" + amount, new Vector2(x, root.y + _xpBar.Root.sizeDelta.y + 1f), _style.xp);
            }
        }

        /// <summary>The death penalty takes experience back: it chips, like any other loss.</summary>
        private void OnXpLost(int amount)
        {
            if (_xp == null) return;
            _medallion.SetLevel(_xp.Level);
            _xpBar.SetRatio(_xp.NormalizedProgress, HudBarChange.Damage);
        }

        private void OnLevelUp(int level)
        {
            _medallion.SetLevel(level);
            BeginLevelUp();
        }

        private void OnGlobalLevelUp(GameObject entity, int level)
        {
            if (entity == null || entity != _player) return;
            // Safety net for level changes that bypass Experience's own event.
            _medallion.SetLevel(level);
        }
    }
}
