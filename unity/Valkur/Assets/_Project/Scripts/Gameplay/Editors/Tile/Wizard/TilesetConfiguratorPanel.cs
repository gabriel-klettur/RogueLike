using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// In-game wizard (Tile Editor F8) that lets the user assign sprites from a
    /// tile folder to the 16 Blob16 slots of a <see cref="TilesetRuleset"/>.
    ///
    /// Lifecycle:
    ///   1. F8 picker shows a "Configure" button when the selected category has a
    ///      ruleset.asset.
    ///   2. <see cref="Open"/> populates the panel with the ruleset's current state
    ///      and the sprite list loaded from Resources/Tiles/&lt;folder&gt;/.
    ///   3. The user drags sprites onto slots, optionally hides legacy sprites,
    ///      and clicks Save.
    ///   4. <see cref="Save"/> writes the changes back to the asset (Editor only).
    ///
    /// Multi-variant per slot is NOT supported in v1 — one sprite per slot. The
    /// underlying data model is array-based so variants can be added later
    /// without breaking saved rulesets.
    /// </summary>
    public partial class TilesetConfiguratorPanel : MonoBehaviour
    {
        // ── State ──
        private TilesetRuleset _ruleset;
        private string _folderName;
        private readonly List<Sprite> _allSprites = new List<Sprite>();
        private readonly Dictionary<Blob16Slot, Sprite> _assignments =
            new Dictionary<Blob16Slot, Sprite>();
        private readonly HashSet<Sprite> _hidden = new HashSet<Sprite>();
        private bool _dirty;

        // ── UI refs (built lazily on first Open) ──
        private GameObject _root;
        private RectTransform _slotsContent;
        private RectTransform _spritesContent;
        private TMPro.TextMeshProUGUI _titleText;
        private TMPro.TextMeshProUGUI _statusText;
        private readonly Dictionary<Blob16Slot, UnityEngine.UI.Image> _slotPreviews =
            new Dictionary<Blob16Slot, UnityEngine.UI.Image>();

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Opens the wizard for the given ruleset. <paramref name="folderName"/>
        /// must match the on-disk folder under <c>Resources/Tiles/</c>.
        /// </summary>
        public void Open(TilesetRuleset ruleset, string folderName)
        {
            if (ruleset == null) return;
            _ruleset = ruleset;
            _folderName = folderName;
            _dirty = false;

            LoadStateFromRuleset();
            LoadSpritesFromFolder();
            EnsureBuilt();
            RefreshSlots();
            RefreshSpriteList();
            UpdateTitle();
            SetStatus("Drag sprites from the right onto a slot. Click 'Hide' to mark a sprite as legacy.");
            _root.SetActive(true);
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            _ruleset = null;
            _allSprites.Clear();
            _assignments.Clear();
            _hidden.Clear();
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>
        /// Called by <see cref="TilesetSlotDropTarget"/> when a sprite is dropped
        /// onto a slot cell. Replaces any previous assignment for that slot.
        /// </summary>
        public void AssignSpriteToSlot(Sprite sprite, Blob16Slot slot)
        {
            if (sprite == null) return;
            _assignments[slot] = sprite;
            // If the sprite was hidden, un-hide it (assigning implies "in use").
            if (_hidden.Remove(sprite)) RefreshSpriteList();
            _dirty = true;
            RefreshSlots();
            SetStatus($"Assigned '{sprite.name}' to slot {slot}.");
        }

        /// <summary>
        /// Removes the sprite currently in <paramref name="slot"/>, if any.
        /// Triggered by clicking the slot's "Clear" button.
        /// </summary>
        public void ClearSlot(Blob16Slot slot)
        {
            if (!_assignments.Remove(slot)) return;
            _dirty = true;
            RefreshSlots();
            SetStatus($"Cleared slot {slot}.");
        }

        /// <summary>
        /// Toggles whether a sprite is in the legacy/hidden bucket. Hidden sprites
        /// stay visible in the wizard's right pane (greyed) but are excluded from
        /// the auto-tile picker chips when the ruleset is saved.
        /// </summary>
        public void ToggleSpriteHidden(Sprite sprite)
        {
            if (sprite == null) return;
            if (!_hidden.Add(sprite)) _hidden.Remove(sprite);
            _dirty = true;
            RefreshSpriteList();
        }

        /// <summary>
        /// Persists the current state to the ruleset asset. No-op outside the
        /// Unity Editor (the wizard is read-only in built games).
        /// </summary>
        public void Save()
        {
            if (_ruleset == null) return;
#if UNITY_EDITOR
            for (int i = 0; i < 16; i++)
            {
                var slot = (Blob16Slot)i;
                Sprite[] variants = _assignments.TryGetValue(slot, out var s) && s != null
                    ? new[] { s }
                    : System.Array.Empty<Sprite>();
                _ruleset.EditorSetSlot(slot, variants);
            }
            _ruleset.EditorSetHiddenLegacy(new List<Sprite>(_hidden));
            UnityEditor.AssetDatabase.SaveAssetIfDirty(_ruleset);
            _dirty = false;
            SetStatus($"Saved {_assignments.Count}/16 slots, {_hidden.Count} hidden.");
#else
            SetStatus("Save is Editor-only. Open this scene in the Unity Editor to persist changes.");
#endif
        }

        // =====================================================================
        // INTERNAL HELPERS
        // =====================================================================

        private void LoadStateFromRuleset()
        {
            _assignments.Clear();
            for (int i = 0; i < _ruleset.Slots.Count; i++)
            {
                var entry = _ruleset.Slots[i];
                if (entry.variants == null || entry.variants.Length == 0) continue;
                if (entry.variants[0] == null) continue;
                _assignments[entry.slot] = entry.variants[0];
            }
            _hidden.Clear();
            for (int i = 0; i < _ruleset.HiddenLegacy.Count; i++)
            {
                var s = _ruleset.HiddenLegacy[i];
                if (s != null) _hidden.Add(s);
            }
        }

        private void LoadSpritesFromFolder()
        {
            _allSprites.Clear();
            if (string.IsNullOrEmpty(_folderName)) return;
            var loaded = Resources.LoadAll<Sprite>($"Tiles/{_folderName}");
            if (loaded == null) return;
            // Stable ordering: by name so the user sees a predictable list.
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
            _allSprites.AddRange(loaded);
        }

        private void UpdateTitle()
        {
            if (_titleText == null) return;
            string p = _ruleset.TerrainPrimary ?? "?";
            string s = _ruleset.IsTransition ? $"↔{_ruleset.TerrainSecondary}" : "";
            _titleText.text = $"Configure: {_folderName}  ({p}{s})";
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = (_dirty ? "[unsaved] " : "") + message;
        }
    }
}
