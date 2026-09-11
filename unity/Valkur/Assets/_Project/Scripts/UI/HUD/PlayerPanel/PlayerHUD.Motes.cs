using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The panel's event bursts. Every one of them answers something that just HAPPENED and dies
    /// inside a second; nothing here emits at rest. Budgets come from <c>PlayerHudStyle</c> and the
    /// layer's capacity bounds the worst case (a level-up landing on a blow) regardless.
    /// </summary>
    public sealed partial class PlayerHUD
    {
        private const float LevelUpRefillDelay = 0.28f;

        private float _levelUpLeft;
        private float _levelUpElapsed;
        private bool _levelUpRefilled;

        private Vector2 BarPoint(HudBar bar, float ratio, float yUnit)
        {
            var root = bar.Root.anchoredPosition;
            return new Vector2(root.x + bar.XAtRatio(ratio), root.y + 1f + bar.InnerHeight * yUnit);
        }

        /// <summary>Shards thrown off the span of health a blow took, falling.</summary>
        private void BurstDamage(float before, float after)
        {
            int budget = _style.motesOnHit;
            if (budget <= 0 || before <= after + 1e-3f) return;
            float lost = before - after;
            int n = Mathf.Clamp(Mathf.RoundToInt(budget * Mathf.Clamp01(lost * 4f)), 3, budget);
            float speed = _style.moteSpeedTexels;
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                var p = BarPoint(_healthBar, Mathf.Lerp(after, before, u), Random.value);
                var v = new Vector2(Mathf.Lerp(-0.25f, 1f, Random.value) * speed * 0.6f,
                                    Mathf.Lerp(0.35f, 1f, Random.value) * speed);
                var c = (i & 1) == 0 ? _style.healthChip : Color.Lerp(_style.healthLow, Color.white, 0.2f);
                c.a = 1f;
                _motes.Emit(p, v, c, _style.moteLifeSeconds * Mathf.Lerp(0.75f, 1.15f, Random.value),
                            (i % 3) == 0 ? HudMoteShape.Plus : HudMoteShape.Dot, _style.moteGravityTexels);
            }
        }

        /// <summary>Motes rising off the span a heal filled, weightless, twinkling.</summary>
        private void BurstHeal(float before, float after)
        {
            int budget = _style.motesOnHeal;
            if (budget <= 0 || after <= before + 1e-3f) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(budget * Mathf.Clamp01((after - before) * 5f)), 2, budget);
            float speed = _style.moteSpeedTexels * 0.45f;
            var c = Color.Lerp(_style.heal, Color.white, 0.2f);
            for (int i = 0; i < n; i++)
            {
                var p = BarPoint(_healthBar, Mathf.Lerp(before, after, Random.value), 1f);
                var v = new Vector2((Random.value - 0.5f) * speed * 0.35f, Mathf.Lerp(0.6f, 1f, Random.value) * speed);
                _motes.Emit(p, v, c, _style.moteLifeSeconds * 1.4f, HudMoteShape.Plus, 0f, 1.2f, twinkle: true);
            }
        }

        /// <summary>A few sparks off the mana fill's edge when a spell is paid for.</summary>
        private void BurstSpend()
        {
            int n = _style.motesOnSpend;
            if (n <= 0) return;
            float speed = _style.moteSpeedTexels * 0.5f;
            var c = Color.Lerp(_style.mana, Color.white, 0.4f);
            for (int i = 0; i < n; i++)
            {
                var p = BarPoint(_manaBar, _manaBar.Target, Random.value);
                var v = new Vector2(Mathf.Lerp(-0.3f, 0.6f, Random.value) * speed, Mathf.Lerp(0.5f, 1f, Random.value) * speed);
                _motes.Emit(p, v, c, _style.moteLifeSeconds * 0.8f, HudMoteShape.Dot, _style.moteGravityTexels * 0.3f);
            }
        }

        /// <summary>A ring of sparks off a slot whose cooldown just ran out, in the spell's colour.</summary>
        private void OnSlotReady(HudAbilitySlot slot)
        {
            int n = _style.motesOnReady;
            if (n <= 0 || slot == null) return;
            var centre = slot.Centre;
            float r = _style.slotTexels * 0.5f;
            float speed = _style.moteSpeedTexels * 0.55f;
            var colour = slot.SpellColour(_style);
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.25f) / n * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var c = (i & 1) == 0 ? colour : Color.Lerp(colour, Color.white, 0.6f);
                _motes.Emit(centre + dir * r, dir * speed, c, _style.moteLifeSeconds * 0.7f,
                            HudMoteShape.Plus, 0f, 2.5f);
            }
        }

        /// <summary>Sparks off the dash pip when the charge comes back.</summary>
        private void OnDashReady()
        {
            int n = _style.motesOnDashReady;
            if (n <= 0) return;
            var centre = _pip.Centre;
            float speed = _style.moteSpeedTexels * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(centre + dir * 4f, dir * speed, _style.dashReady, _style.moteLifeSeconds * 0.6f,
                            HudMoteShape.Dot, 0f, 2f);
            }
        }

        // -- Level up ---------------------------------------------------------------------

        /// <summary>
        /// The one moment of progression the panel celebrates at full size: the XP line floods to
        /// full and flashes, the medallion glows, gold rises off both, the portrait lights — then
        /// the line empties to the new level's progress. Under a second end to end.
        /// </summary>
        private void BeginLevelUp()
        {
            _levelUpLeft = Mathf.Max(0.3f, _style.levelUpSeconds);
            _levelUpElapsed = 0f;
            _levelUpRefilled = false;
            _xpBar.SetRatio(1f, HudBarChange.Silent, instant: true);
            _xpBar.Flash(_style.levelBarFlash, 0.45f);
            _medallion.Pulse(_levelUpLeft);
            _portrait.Flash(_style.levelPortraitFlash, 0.45f);
            BurstLevelUp();
            // The word, once, in the panel's own face: the medallion's number changing is easy to
            // miss mid-fight, and this is the one event the panel is allowed to shout.
            // It rises off the panel's top edge over the portrait: inside the panel it would cross
            // the slots, and the slots are what the player is reading mid-fight.
            int level = _xp != null ? _xp.Level : _medallion.Level;
            float x = _style.paddingTexels + _style.portraitTexels * 0.5f;
            _floats.Spawn("NIVEL " + level, new Vector2(x, _heightTexels - 2f), Color.Lerp(_style.gold, Color.white, 0.25f));
        }

        private void BurstLevelUp()
        {
            int n = _style.motesOnLevelUp;
            if (n <= 0) return;
            float speed = _style.moteSpeedTexels;
            var gold = _style.gold;
            var pale = Color.Lerp(_style.gold, Color.white, 0.6f);
            var centre = _medallion.Centre;

            // A ring off the medallion first — the burst the eye catches before it reads anything.
            int ring = Mathf.Min(12, n / 3);
            for (int i = 0; i < ring; i++)
            {
                float a = (i + 0.5f) / ring * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // Born OUTSIDE the coin: a ring started at its centre covers the new number for
                // the first frames, which are the frames the player looks at it.
                float r = _medallion.Root.sizeDelta.x * 0.5f + 2f;
                _motes.Emit(centre + dir * r, dir * speed * 1.2f, pale, _style.moteLifeSeconds * 0.9f,
                            HudMoteShape.Star, 0f, 3f);
            }
            n -= ring;

            // Half of the rest from the medallion, a fountain.
            int fountain = n / 2;
            for (int i = 0; i < fountain; i++)
            {
                float a = Mathf.Lerp(0.15f, 0.85f, Random.value) * Mathf.PI;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(centre + dir * (_medallion.Root.sizeDelta.x * 0.5f), dir * speed * Mathf.Lerp(0.6f, 1.1f, Random.value),
                            (i & 1) == 0 ? gold : pale, _style.moteLifeSeconds * 1.6f,
                            (i % 3) == 0 ? HudMoteShape.Star : HudMoteShape.Plus,
                            _style.moteGravityTexels * 0.5f, 0.6f, twinkle: true);
            }
            // The rest rising off the whole length of the XP line.
            for (int i = fountain; i < n; i++)
            {
                var p = BarPoint(_xpBar, Random.value, 1f);
                var v = new Vector2((Random.value - 0.5f) * 6f, Mathf.Lerp(0.4f, 0.9f, Random.value) * speed);
                _motes.Emit(p, v, (i & 1) == 0 ? pale : gold, _style.moteLifeSeconds * 1.5f,
                            HudMoteShape.Dot, 0f, 1.4f, twinkle: true);
            }
        }

        private void TickLevelUp(float dt)
        {
            if (_healBurstCooldown > 0f) _healBurstCooldown -= dt;
            if (_refuseCooldown > 0f) _refuseCooldown -= dt;

            if (_levelUpLeft <= 0f) return;
            _levelUpLeft -= dt;
            _levelUpElapsed += dt;
            if (!_levelUpRefilled && _levelUpElapsed >= LevelUpRefillDelay)
            {
                _levelUpRefilled = true;
                float ratio = _xp != null ? _xp.NormalizedProgress : 0f;
                _xpBar.SetRatio(0f, HudBarChange.Silent, instant: true);
                _xpBar.SetRatio(ratio, HudBarChange.Heal);
                _xpBar.Glint();
            }
        }
    }
}
