using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The bar's event bursts. Every one answers something that just HAPPENED and dies inside a
    /// second; nothing here emits at rest (<c>.github/HUD_VISUAL_LANGUAGE.md</c>, R8). A refusal
    /// throws nothing: a "no" is a mana-blue flash on the slot, not a celebration.
    /// </summary>
    public sealed partial class SpellBarHUD
    {
        private float _gemPulseLeft;
        private Color _gemPulseColour = Color.white;
        private float _refuseCooldown;

        private Cell CellForSpell(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return null;
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i].Entry.Kind == SpellBarEntryKind.Spell && _cells[i].Entry.Key == spellKey)
                    return _cells[i];
            return null;
        }

        // -- Events in -------------------------------------------------------------------

        private void OnSpellCast(GameObject caster, string spellKey, string displayName, float cooldown)
        {
            if (caster == null || caster != _player) return;
            // Only spells ON the bar light it. The left click's 0.4 s fireball would otherwise
            // keep the gem lit for as long as the player fights — a lamp, not an event.
            var cell = CellForSpell(spellKey);
            if (cell == null) return;
            var colour = cell.Slot.SpellColour(_hud);
            PulseGem(colour);
            BurstCast(cell, colour);
        }

        private void OnCastRefused(string spellKey)
        {
            // A held key re-attempts every frame; once every few tenths is the message.
            if (_refuseCooldown > 0f) return;
            var cell = CellForSpell(spellKey);
            if (cell == null) return;
            _refuseCooldown = 0.45f;
            cell.Slot.Refused(_hud);
        }

        private void OnSlotReady(HudAbilitySlot slot)
        {
            int n = _style.motesOnReady;
            if (n <= 0 || slot == null) return;
            Cell cell = null;
            for (int i = 0; i < _cells.Count; i++) if (_cells[i].Slot == slot) { cell = _cells[i]; break; }
            if (cell == null) return;
            float r = _style.SlotTexels * 0.5f;
            float speed = _hud.moteSpeedTexels * 0.55f;
            var colour = slot.SpellColour(_hud);
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.25f) / n * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var c = (i & 1) == 0 ? colour : Color.Lerp(colour, Color.white, 0.6f);
                _motes.Emit(cell.Centre + dir * r, dir * speed, c, _hud.moteLifeSeconds * 0.7f,
                            HudMoteShape.Plus, 0f, 2.5f);
            }
        }

        // -- Bursts out ------------------------------------------------------------------------

        /// <summary>A few motes of the spell's colour lifting off its slot as it leaves the hands.</summary>
        private void BurstCast(Cell cell, Color colour)
        {
            int n = _style.motesOnCast;
            float speed = _hud.moteSpeedTexels * 0.6f;
            float half = _style.SlotTexels * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var p = cell.Centre + new Vector2(Mathf.Lerp(-half + 3f, half - 3f, (i + 0.5f) / n), half - 1f);
                var v = new Vector2((Random.value - 0.5f) * speed * 0.4f, Mathf.Lerp(0.7f, 1f, Random.value) * speed);
                var c = (i & 1) == 0 ? colour : Color.Lerp(colour, Color.white, 0.5f);
                _motes.Emit(p, v, c, _hud.moteLifeSeconds * 0.8f, HudMoteShape.Dot, 0f, 2f);
            }
        }

        /// <summary>A short gold sparkle off a verb the player clicked.</summary>
        private void BurstVerb(Cell cell)
        {
            int n = _style.motesOnVerb;
            float speed = _hud.moteSpeedTexels * 0.45f;
            var tint = cell.Slot.AccentColour(_hud);
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(0.2f, 0.8f, (i + 0.5f) / n) * Mathf.PI;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(cell.Centre + dir * (_style.SlotTexels * 0.4f), dir * speed,
                            Color.Lerp(tint, Color.white, 0.35f), _hud.moteLifeSeconds * 0.6f,
                            HudMoteShape.Dot, 0f, 2.5f);
            }
        }

        /// <summary>
        /// A spell just learned: gold stars out of its slot in every direction. The largest burst
        /// the War face makes, because it is the moment the bar grows.
        /// </summary>
        private void BurstLearn(Cell cell)
        {
            int n = _style.motesOnLearn;
            float speed = _hud.moteSpeedTexels * 0.9f;
            var gold = _hud.gold;
            var pale = Color.Lerp(_hud.gold, Color.white, 0.6f);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.5f) / n * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(cell.Centre + dir * 4f, dir * speed * Mathf.Lerp(0.5f, 1f, Random.value),
                            (i & 1) == 0 ? gold : pale, _hud.moteLifeSeconds * 1.3f,
                            (i % 3) == 0 ? HudMoteShape.Star : HudMoteShape.Plus,
                            _hud.moteGravityTexels * 0.4f, 0.8f, twinkle: true);
            }
        }

        /// <summary>
        /// The posture changed: the gem is re-cut, the new face's word rises off the top edge,
        /// and a curtain of motes in the posture's colour lifts off the whole length of the bar.
        /// </summary>
        private void OnFaceArrived()
        {
            var accent = _style.AccentFor(_face);
            PulseGem(accent);
            _floats.Spawn(_face == Stance.Peace ? "PAZ" : "GUERRA",
                          new Vector2(_widthTexels * 0.5f, _heightTexels - 1f), Color.Lerp(accent, Color.white, 0.2f));

            int n = _style.motesOnFlip;
            float speed = _hud.moteSpeedTexels * 0.7f;
            var pale = Color.Lerp(accent, Color.white, 0.55f);
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(3f, _widthTexels - 3f, (i + Random.value) / n);
                var p = new Vector2(x, _heightTexels - 2f);
                var v = new Vector2((Random.value - 0.5f) * 10f, Mathf.Lerp(0.45f, 1f, Random.value) * speed);
                _motes.Emit(p, v, (i & 1) == 0 ? accent : pale, _hud.moteLifeSeconds * 1.2f,
                            (i % 4) == 0 ? HudMoteShape.Star : HudMoteShape.Dot, 0f, 1.6f, twinkle: (i % 3) == 0);
            }
        }

        // -- The gem ---------------------------------------------------------------------------

        private void PulseGem(Color colour)
        {
            _gemPulseLeft = _style.gemPulseSeconds;
            _gemPulseColour = colour;
        }

        private void TickGem(float dt)
        {
            if (_refuseCooldown > 0f) _refuseCooldown -= dt;
            if (_gemPulseLeft <= 0f)
            {
                if (_gemGlow.enabled) _gemGlow.enabled = false;
                return;
            }
            _gemPulseLeft = Mathf.Max(0f, _gemPulseLeft - dt);
            float t = _gemPulseLeft / Mathf.Max(0.01f, _style.gemPulseSeconds);
            var c = _gemPulseColour;
            c.a = t * t;
            if (!_gemGlow.enabled) _gemGlow.enabled = true;
            _gemGlow.color = c;
        }
    }
}
