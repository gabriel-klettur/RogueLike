using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Spells-using-this-preset (Python particles_spells_list_panel parity) ──

        private void ToggleSpellsExpanded()
        {
            _spellsExpanded = !_spellsExpanded;
            if (_ui.SpellsHeaderTmp != null)
            {
                const string baseLbl = "SPELLS USING THIS PRESET";
                _ui.SpellsHeaderTmp.text = _spellsExpanded ? "▼ " + baseLbl : "▶ " + baseLbl;
            }
            if (_ui.SpellsContent != null)
                _ui.SpellsContent.gameObject.SetActive(_spellsExpanded);
        }

        private void RefreshSpellsPanel()
        {
            if (_ui.SpellsContent == null) return;
            for (int i = _ui.SpellsContent.childCount - 1; i >= 0; i--)
            {
                var child = _ui.SpellsContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            if (string.IsNullOrEmpty(_selectedPresetId))
            {
                var lbl = EditorUIHelpers.AddLabel(_ui.SpellsContent, "(no preset selected)", 11f);
                lbl.color = UITheme.TEXT_MUTED;
                return;
            }

            // Phase 2: scan SpellDefinition catalog for preset references.
            // Phase 1 placeholder mirrors Python two-column hint.
            var hint = EditorUIHelpers.AddLabel(_ui.SpellsContent,
                $"Usages of '<b>{_selectedPresetId}</b>' will appear here.\n" +
                "Columns: spell_key  ·  json_path",
                11f);
            hint.color = UITheme.TEXT_MUTED;
            hint.richText = true;
        }
    }
}
