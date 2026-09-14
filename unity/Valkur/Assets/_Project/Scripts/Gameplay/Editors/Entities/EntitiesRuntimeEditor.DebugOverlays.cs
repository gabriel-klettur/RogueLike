using Valkur.Gameplay.Spells.Debugging;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The two debug overlays reachable from the Entities editor's menu bar: every entity's
    /// colliders (with the ones a spell reached frozen until the next collision) and the spell
    /// impact areas.
    ///
    /// <para><b>Both buttons write the SHARED switch</b>, the same static the <c>colisiones</c> and
    /// <c>areas</c> commands and the Spells editor write. A private copy is how a button comes to
    /// report a state the game is not in. Because those other writers exist, the labels are
    /// re-read every frame the editor is open rather than only when clicked - a comparison of two
    /// bools, with a write only when one changed.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private bool _shownCollisionsOn;
        private bool _shownAreasOn;
        private bool _debugTogglesPainted;

        private void OnToggleEntityCollisions()
        {
            EntityCollisionDebug.Enabled = !EntityCollisionDebug.Enabled;
            RefreshDebugOverlayToggles();
            SetStatus(EntityCollisionDebug.Enabled
                ? "Colisiones ON: lo que toque un hechizo queda congelado hasta la siguiente colision (verde = golpe, ámbar = tocado sin daño)"
                : "Colisiones OFF");
        }

        private void OnToggleSpellAreas()
        {
            SpellDebugAreas.Enabled = !SpellDebugAreas.Enabled;
            RefreshDebugOverlayToggles();
            SetStatus(SpellDebugAreas.Enabled
                ? "Areas ON: el dibujo de un hechizo se queda hasta el siguiente lanzamiento"
                : "Areas OFF");
        }

        private void TickDebugOverlayToggles()
        {
            if (_debugTogglesPainted &&
                _shownCollisionsOn == EntityCollisionDebug.Enabled &&
                _shownAreasOn == SpellDebugAreas.Enabled) return;
            RefreshDebugOverlayToggles();
        }

        private void RefreshDebugOverlayToggles()
        {
            _shownCollisionsOn = EntityCollisionDebug.Enabled;
            _shownAreasOn = SpellDebugAreas.Enabled;
            _debugTogglesPainted = true;

            PaintToggle(_ui.CollisionsToggleImg, _ui.CollisionsToggleTmp, "Colisiones", _shownCollisionsOn);
            PaintToggle(_ui.AreasToggleImg, _ui.AreasToggleTmp, "Areas", _shownAreasOn);
        }

        private static void PaintToggle(UnityEngine.UI.Image img, TMPro.TextMeshProUGUI tmp, string name, bool on)
        {
            // The menu bar's own open/closed style: ON reads exactly like an open dropdown.
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, on);
            if (tmp != null) tmp.text = name + (on ? ": ON" : ": OFF");
        }
    }
}
