using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Lighting Editor — preset catalog browsing, search filter, selection, and
    /// the per-preset properties panel. Catalog is resolved from the live
    /// <see cref="WorldLightLoader"/> so the editor and the runtime always agree
    /// on the same source of truth.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        private void EnsureCatalog()
        {
            if (_catalog != null) return;

            // 1) Live loader first (single source of truth).
            if (WorldLightLoader.Instance != null && WorldLightLoader.Instance.Catalog != null)
            {
                _catalog = WorldLightLoader.Instance.Catalog;
                return;
            }

            // 2) Resources fallback (works in builds + editor).
            var fromResources = Resources.Load<LightPresetCatalog>("Catalogs/LightPresetCatalog");
            if (fromResources != null) { _catalog = fromResources; return; }

#if UNITY_EDITOR
            // 3) Editor-only AssetDatabase fallback — covers freshly migrated
            //    projects where the catalog hasn't been moved into Resources yet.
            var fromAssets = UnityEditor.AssetDatabase.LoadAssetAtPath<LightPresetCatalog>(
                "Assets/_Project/Data/Catalogs/Lighting/LightPresetCatalog.asset");
            if (fromAssets != null) _catalog = fromAssets;
#endif
        }

        private void OnSearchChanged(string text)
        {
            _searchFilter = text ?? "";
            RefreshPresetList();
        }

        private void RefreshPresetList()
        {
            if (_ui.PresetGrid == null) return;
            for (int i = _ui.PresetGrid.childCount - 1; i >= 0; i--)
                Destroy(_ui.PresetGrid.GetChild(i).gameObject);

            if (_catalog == null || _catalog.presets == null || _catalog.presets.Count == 0)
            {
                AddPresetPlaceholder("(catalog empty — run Valkur > Lighting > Import Presets)");
                return;
            }

            string filter = _searchFilter.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var preset in _catalog.presets)
            {
                if (preset == null || string.IsNullOrEmpty(preset.presetKey)) continue;
                if (filter.Length > 0 && !preset.presetKey.ToLowerInvariant().Contains(filter)) continue;
                AddPresetButton(preset);
                shown++;
            }
            if (shown == 0)
                AddPresetPlaceholder($"(no presets match '{_searchFilter}')");
        }

        private void AddPresetPlaceholder(string text)
        {
            var go = EditorUIHelpers.CreateUI("PlaceholderRow", _ui.PresetGrid);
            go.AddComponent<LayoutElement>().preferredHeight = 32f;
            var tmp                    = go.AddComponent<TextMeshProUGUI>();
            tmp.text                   = text;
            tmp.fontSize               = 10f;
            tmp.fontStyle              = FontStyles.Italic;
            tmp.alignment              = TextAlignmentOptions.Center;
            tmp.color                  = EditorUIHelpers.TEXT_MUTED;
            tmp.enableWordWrapping     = true;
            tmp.margin                 = new Vector4(4f, 4f, 4f, 4f);
        }

        private void AddPresetButton(LightPresetDefinition preset)
        {
            var key  = preset.presetKey;
            var go   = EditorUIHelpers.CreateUI($"Preset_{key}", _ui.PresetGrid);
            go.AddComponent<LayoutElement>().preferredHeight = 28f;

            var img       = go.AddComponent<Image>();
            img.color     = key == _selectedPresetKey
                ? EditorUIHelpers.BTN_ACTIVE
                : EditorUIHelpers.BTN_NORMAL;

            var btn       = go.AddComponent<Button>();
            var c         = btn.colors;
            c.normalColor      = img.color;
            c.highlightedColor = EditorUIHelpers.BTN_HOVER;
            c.pressedColor     = EditorUIHelpers.BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(() => SelectPreset(key));

            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.spacing                = 6f;
            hl.padding                = new RectOffset(6, 6, 2, 2);
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;
            hl.childAlignment         = TextAnchor.MiddleLeft;

            var swatchGo = EditorUIHelpers.CreateUI("Swatch", go.transform);
            swatchGo.AddComponent<LayoutElement>().preferredWidth = 14f;
            var swatchImg   = swatchGo.AddComponent<Image>();
            swatchImg.color = preset.color;

            var lblGo                       = EditorUIHelpers.CreateUI("Lbl", go.transform);
            var lblLE                       = lblGo.AddComponent<LayoutElement>();
            lblLE.flexibleWidth             = 1f;
            var lblTmp                      = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text                     = key;
            lblTmp.fontSize                 = 11f;
            lblTmp.fontStyle                = FontStyles.Bold;
            lblTmp.alignment                = TextAlignmentOptions.Left;
            lblTmp.color                    = EditorUIHelpers.TEXT_PRIMARY;
            lblTmp.enableWordWrapping       = false;

            var radGo                        = EditorUIHelpers.CreateUI("Rad", go.transform);
            radGo.AddComponent<LayoutElement>().preferredWidth = 56f;
            var radTmp                       = radGo.AddComponent<TextMeshProUGUI>();
            radTmp.text                      = $"r {preset.radius:F0}";
            radTmp.fontSize                  = 9f;
            radTmp.alignment                 = TextAlignmentOptions.Right;
            radTmp.color                     = EditorUIHelpers.TEXT_MUTED;
        }

        private void SelectPreset(string key)
        {
            _selectedPresetKey = key;
            RefreshPresetList();
            RefreshPresetProperties();
            // Auto-switch to Spawn mode so a single click can drop a fresh light.
            if (_mode != EditorMode.Delete) SetMode(EditorMode.Spawn);
            SetStatus($"Preset '{key}' selected. LMB on map to drop.");
        }

        private void RefreshPresetProperties()
        {
            if (_ui.PresetTitle == null || _ui.PresetBody == null) return;

            // Prefer the active map selection's preset if there is one.
            string key = _selectedPresetKey;
            if (string.IsNullOrEmpty(key) && _selectedLight != null)
                key = ExtractPresetFromName(_selectedLight.name);

            if (string.IsNullOrEmpty(key) || _catalog == null)
            {
                _ui.PresetTitle.text = "(no preset selected)";
                _ui.PresetBody.text  = "Pick a preset from the list above to inspect its properties, or click an existing light on the map.";
                return;
            }

            var preset = _catalog.GetByKey(key);
            if (preset == null)
            {
                _ui.PresetTitle.text = key;
                _ui.PresetBody.text  = $"Preset '{key}' is not in the catalog.";
                return;
            }

            _ui.PresetTitle.text = key;

            var sb = new StringBuilder(256);
            sb.AppendLine($"<b>Radius:</b> {preset.radius:F1} px ({preset.radius / 32f:F2} world units)");
            sb.AppendLine($"<b>Intensity:</b> {preset.intensity:F2}");
            sb.AppendLine($"<b>Falloff:</b> {preset.falloff:F2}");
            sb.AppendLine($"<b>Center scale:</b> {preset.centerScale:F2}");
            sb.AppendLine($"<b>Color:</b> #{ColorUtility.ToHtmlStringRGBA(preset.color)}");
            sb.AppendLine($"<b>Flicker amplitude:</b> {preset.flickerAmplitude:F2}");
            sb.AppendLine($"<b>Flicker speed:</b> {preset.flickerSpeed:F2} Hz");
            _ui.PresetBody.text = sb.ToString();
        }
    }
}
