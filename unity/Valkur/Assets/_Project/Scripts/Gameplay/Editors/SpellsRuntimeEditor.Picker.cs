using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — picker grid (search + 4-col thumbnail catalog).
    /// Phase 1 functionality: select-only. Mutate operations are stubs in
    /// <see cref="SpellsRuntimeEditor"/>.Modes.cs.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private void RefreshPicker()
        {
            var content = _uiRefs.PickerContent;
            if (content == null) return;
            if (_catalog == null)
            {
                SetStatus("(no SpellCatalog assigned)");
                return;
            }

            // Clear existing slots.
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            var keys   = _catalog.GetAllKeys();
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown  = 0;
            foreach (var key in keys)
            {
                if (!_catalog.TryGet(key, out var spell)) continue;
                if (filter.Length > 0)
                {
                    string name = (spell.displayName ?? key).ToLowerInvariant();
                    if (!name.Contains(filter) && !key.ToLowerInvariant().Contains(filter))
                        continue;
                }

                var capturedKey = key;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    content, spell.displayName ?? key, 64f,
                    () => SelectSpell(capturedKey));

                if (spell.sprite != null)
                {
                    icon.sprite  = spell.sprite;
                    icon.enabled = true;
                }
                label.text = TruncateName(spell.displayName ?? key, 9);

                if (key == _selectedKey)
                {
                    var bg = btn.GetComponent<Image>();
                    if (bg != null) bg.color = EditorUIHelpers.SLOT_SELECTED;
                }
                shown++;
            }

            SetStatus(filter.Length == 0
                ? $"{shown} spells"
                : $"{shown} match '{_searchFilter}'");
        }

        private void SelectSpell(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            RefreshPropertiesForm();
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}