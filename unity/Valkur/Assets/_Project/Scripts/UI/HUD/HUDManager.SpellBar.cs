using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private SpellBarHUD _spellBar;

        /// <summary>The bottom-centre action bar, once the player exists.</summary>
        public SpellBarHUD SpellBar => _spellBar;

        /// <summary>
        /// Builds the action bar beside the player panel. It is created HERE, inside the HUD
        /// canvas, rather than on a canvas of its own: that puts it under <c>[UI]</c> with the
        /// rest of the HUD (hidden together when an editor opens), on the HUD's sorting order
        /// instead of a number of its own that collided with the music widget's 150, and gives
        /// it the panel to stand clear of.
        ///
        /// <para>It replaces two things at once: the old <c>SpellBarHUD</c> in Valkur.Gameplay,
        /// and the top-left <c>SpellCooldownHUD</c> text stack, which drew every cooldown a
        /// third time in a third style (<c>.github/HUD_VISUAL_LANGUAGE.md</c>, R11).</para>
        /// </summary>
        private void CreateSpellBar(GameObject player)
        {
            if (_canvas == null || player == null) return;
            if (_spellBar != null) { _spellBar.Bind(player); return; }
            _spellBar = SpellBarHUD.Create(_canvas, player, _playerHUD != null ? _playerHUD.Panel : null);
        }
    }
}
