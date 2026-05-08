using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Compact ability-bar driver for the inline 3-slot row inside the unified
    /// bottom-left HUD. Polls the bound player's <see cref="SpellCaster"/> each
    /// frame and updates icons + radial cooldown rings. Distinct from the
    /// full-size <c>Valkur.Gameplay.UI.SpellBarHUD</c> (24-slot WoW bar) which
    /// is hidden by default.
    /// </summary>
    public sealed class PlayerAbilityRowHUD : MonoBehaviour
    {
        private GameObject _player;
        private SpellCaster _caster;
        private Image[] _icons;
        private CooldownRing[] _rings;

        public void Bind(GameObject player, Image[] icons, CooldownRing[] rings)
        {
            _player = player;
            _icons  = icons;
            _rings  = rings;
        }

        private void Update() => Refresh();

        // Internal seam for EditMode tests: Update() is not driven by Unity's
        // test runner outside PlayMode, so tests call Refresh() directly.
        internal void Refresh()
        {
            if (_icons == null || _rings == null) return;

            if (_caster == null)
            {
                if (_player == null) _player = EntityRegistry.Player;
                if (_player == null) return;
                _caster = _player.GetComponent<SpellCaster>();
                if (_caster == null) return;
            }

            for (int i = 0; i < _icons.Length; i++)
            {
                var spell = _caster.GetSpellAtSlot(i);
                if (spell != null)
                {
                    _icons[i].color  = Color.white;
                    if (spell.sprite != null) _icons[i].sprite = spell.sprite;
                    _rings[i]?.SetProgress(_caster.GetCooldownNormalized(i));
                }
                else
                {
                    _icons[i].color = new Color(0.30f, 0.30f, 0.35f, 0.45f);
                    _icons[i].sprite = null;
                    _rings[i]?.SetProgress(0f);
                }
            }
        }
    }
}
