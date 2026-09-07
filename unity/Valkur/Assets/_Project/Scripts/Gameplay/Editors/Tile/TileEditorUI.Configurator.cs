using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Wires the "CONFIGURE TILESET" button (in the Tiles panel) and the
    /// <see cref="TilesetConfiguratorPanel"/> wizard. The wizard is built
    /// lazily as a sibling of the menu UI on the same canvas, so it overlays
    /// the editor when opened.
    /// </summary>
    public partial class TileEditorUI
    {
        private TilesetConfiguratorPanel _configuratorPanel;

        /// <summary>
        /// Hooked from <see cref="BuildUI"/> after the UI builder returns. Wires
        /// the Configure button click and primes the enabled-state tracking.
        /// </summary>
        private void WireConfiguratorButton()
        {
            if (_refs.ConfigureTilesetBtn == null) return;
            _refs.ConfigureTilesetBtn.onClick.RemoveAllListeners();
            _refs.ConfigureTilesetBtn.onClick.AddListener(OpenConfiguratorForCurrentCategory);
            RefreshConfiguratorButtonState();
        }

        /// <summary>
        /// Updates whether the Configure button is interactable, based on whether
        /// the currently-selected category has a <c>ruleset.asset</c> on disk.
        /// Called after every category selection.
        /// </summary>
        private void RefreshConfiguratorButtonState()
        {
            if (_refs.ConfigureTilesetBtn == null) return;
            bool hasCategory = !string.IsNullOrEmpty(_currentCategory);
            bool hasRuleset = hasCategory && LoadRulesetForCategory(_currentCategory) != null;
            _refs.ConfigureTilesetBtn.interactable = hasRuleset;

            if (_refs.ConfigureTilesetBtnLabel != null)
            {
                _refs.ConfigureTilesetBtnLabel.color = hasRuleset ? ACCENT : TEXT_MUTED;
                _refs.ConfigureTilesetBtnLabel.text = hasCategory
                    ? (hasRuleset ? $"CONFIGURE: {_currentCategory.ToUpperInvariant()}" : "NO RULESET FOR CATEGORY")
                    : "PICK A CATEGORY FIRST";
            }
        }

        private void OpenConfiguratorForCurrentCategory()
        {
            if (string.IsNullOrEmpty(_currentCategory)) return;
            var ruleset = LoadRulesetForCategory(_currentCategory);
            if (ruleset == null) return;
            EnsureConfiguratorPanel();
            _configuratorPanel.Open(ruleset, _currentCategory);
        }

        private void EnsureConfiguratorPanel()
        {
            if (_configuratorPanel != null) return;
            // Place the panel as a sibling of the menu UI on the same canvas so
            // it inherits the same scaler / sortingOrder.
            var canvas = GetComponentInChildren<Canvas>(includeInactive: true);
            var parent = canvas != null ? canvas.transform : transform;
            var go = new GameObject("TilesetConfiguratorPanel", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _configuratorPanel = go.AddComponent<TilesetConfiguratorPanel>();
        }

        /// <summary>
        /// The ruleset that governs a picker category, or null when the category is not
        /// part of an auto-tile pack.
        /// </summary>
        private TilesetRuleset LoadRulesetForCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return null;

            var own = Resources.Load<TilesetRuleset>($"Tiles/{category}/ruleset");
            if (own != null) return own;

            return FindOwningRuleset(category);
        }

        /// <summary>
        /// A pack cut from several sheets shows ONE picker category per sheet but keeps a
        /// single ruleset, in the pack folder. Four rulesets all claiming &apos;grass&apos; would
        /// leave three of them permanently unreachable from the auto-brush, silently, because
        /// <c>TerrainCatalog.FindPaintRuleset</c> resolves a terrain NAME to exactly one
        /// ruleset (highest priority, ties by list order). So a sheet category has no
        /// <c>ruleset.asset</c> of its own and the owner is answered from the DATA — the
        /// ruleset whose slots hold this category&apos;s sprites — rather than from a folder-name
        /// convention, which is the half that drifts the moment a sheet is renamed.
        /// </summary>
        private TilesetRuleset FindOwningRuleset(string category)
        {
            var catalog = TerrainCatalogLoader.Load();
            if (catalog == null || _catalog == null) return null;

            var tiles = _catalog.GetTilesForCategory(category);
            if (tiles == null || tiles.Count == 0) return null;

            var names = new HashSet<string>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
                if (!string.IsNullOrEmpty(tiles[i].tileName)) names.Add(tiles[i].tileName);

            for (int i = 0; i < catalog.Rulesets.Count; i++)
            {
                var ruleset = catalog.Rulesets[i];
                if (ruleset == null) continue;

                for (int s = 0; s < ruleset.Slots.Count; s++)
                    if (HoldsAny(ruleset.Slots[s].variants, names)) return ruleset;
                for (int s = 0; s < ruleset.CornerSlots.Count; s++)
                    if (HoldsAny(ruleset.CornerSlots[s].variants, names)) return ruleset;
            }
            return null;
        }

        private static bool HoldsAny(Sprite[] variants, HashSet<string> names)
        {
            if (variants == null) return false;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null && names.Contains(variants[i].name)) return true;
            return false;
        }
    }
}
