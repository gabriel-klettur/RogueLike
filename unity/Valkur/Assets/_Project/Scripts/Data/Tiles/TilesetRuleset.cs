using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-folder auto-tiling configuration for a tileset under <c>Resources/Tiles/&lt;folder&gt;/</c>.
    ///
    /// One <c>TilesetRuleset.asset</c> lives next to the sprites. Declares which
    /// terrain(s) the folder represents, the auto-tile model, and the mapping from
    /// each Blob16 slot to its sprite variants. Used by the runtime solver and by
    /// the in-game configurator panel (F8 Tile Editor wizard).
    ///
    /// A "base" ruleset has only <see cref="TerrainPrimary"/> set (e.g. solid grass).
    /// A "transition" ruleset has both <see cref="TerrainPrimary"/> and
    /// <see cref="TerrainSecondary"/> set (e.g. grass↔dirt borders).
    /// </summary>
    [CreateAssetMenu(fileName = "TilesetRuleset", menuName = "Valkur/Tiles/Tileset Ruleset")]
    public class TilesetRuleset : ScriptableObject
    {
        [Tooltip("Folder name under Resources/Tiles/, e.g. 'grass_dirt'.")]
        [SerializeField] private string folderName;

        [Tooltip("Auto-tile model used by this tileset.")]
        [SerializeField] private AutoTileModel model = AutoTileModel.Blob16;

        [Tooltip("Primary terrain ID, e.g. 'grass'.")]
        [SerializeField] private string terrainPrimary;

        [Tooltip("Secondary terrain ID for transition tilesets, e.g. 'dirt'. Empty for base terrains.")]
        [SerializeField] private string terrainSecondary;

        [Tooltip("Higher priority wins when terrains overlap or compete.")]
        [SerializeField] private int priority;

        [Tooltip("Mapping from each Blob16 slot to its sprite variants. Solver picks one variant deterministically by cell hash.")]
        [SerializeField] private List<SlotMapping> slots = new List<SlotMapping>();

        [Tooltip("Mapping from each Corner16 slot to its sprite variants. Only used when Model == Corner16. Solver picks one variant deterministically by cell hash.")]
        [SerializeField] private List<CornerSlotMapping> cornerSlots = new List<CornerSlotMapping>();

        [Tooltip("Sprites in this folder that are NOT used by the auto-tile system (legacy / duplicate variants).")]
        [SerializeField] private List<Sprite> hiddenLegacy = new List<Sprite>();

        [Tooltip("Icon shown in the Auto-tile Region picker.")]
        [SerializeField] private Sprite previewIcon;

        public string FolderName => folderName;
        public AutoTileModel Model => model;
        public string TerrainPrimary => terrainPrimary;
        public string TerrainSecondary => terrainSecondary;
        public int Priority => priority;
        public IReadOnlyList<SlotMapping> Slots => slots;
        public IReadOnlyList<CornerSlotMapping> CornerSlots => cornerSlots;
        public IReadOnlyList<Sprite> HiddenLegacy => hiddenLegacy;
        public Sprite PreviewIcon => previewIcon;

        public bool IsTransition => !string.IsNullOrEmpty(terrainSecondary);

        /// <summary>
        /// Returns the sprite array assigned to a slot, or null if the slot is unassigned.
        /// </summary>
        public Sprite[] GetVariants(Blob16Slot slot)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].slot == slot)
                    return slots[i].variants;
            }
            return null;
        }

        /// <summary>
        /// Returns the sprite array assigned to a Corner16 slot, or null if unassigned.
        /// Mirrors <see cref="GetVariants(Blob16Slot)"/> for the corner model.
        /// </summary>
        public Sprite[] GetVariants(Corner16Slot slot)
        {
            for (int i = 0; i < cornerSlots.Count; i++)
            {
                if (cornerSlots[i].slot == slot)
                    return cornerSlots[i].variants;
            }
            return null;
        }

        /// <summary>
        /// True if every required slot for the declared model has at least one variant.
        /// Blob47 is reserved for v2 and always returns false. Corner16 additionally
        /// requires <see cref="TerrainSecondary"/> to be set — a corner ruleset is by
        /// definition a two-material transition, and without a secondary terrain the
        /// corner-mask calculator (Gameplay assembly) has nothing to test corners against.
        /// </summary>
        public bool IsComplete()
        {
            if (model == AutoTileModel.Blob16)
                return AllSixteenSlotsAssigned(i => GetVariants((Blob16Slot)i));

            if (model == AutoTileModel.Corner16)
                return !string.IsNullOrEmpty(terrainSecondary) && AllSixteenSlotsAssigned(i => GetVariants((Corner16Slot)i));

            return false; // Blob47 reserved for v2
        }

        private static bool AllSixteenSlotsAssigned(Func<int, Sprite[]> getVariants)
        {
            for (int i = 0; i < 16; i++)
            {
                var variants = getVariants(i);
                if (variants == null || variants.Length == 0) return false;
                for (int j = 0; j < variants.Length; j++)
                    if (variants[j] == null) return false;
            }
            return true;
        }

#if UNITY_EDITOR
        public void EditorSetSlot(Blob16Slot slot, Sprite[] variants)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].slot == slot)
                {
                    var entry = slots[i];
                    entry.variants = variants;
                    slots[i] = entry;
                    UnityEditor.EditorUtility.SetDirty(this);
                    return;
                }
            }
            slots.Add(new SlotMapping { slot = slot, variants = variants });
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Corner16 counterpart of the <c>EditorSetSlot(Blob16Slot, Sprite[])</c> overload above.</summary>
        public void EditorSetSlot(Corner16Slot slot, Sprite[] variants)
        {
            for (int i = 0; i < cornerSlots.Count; i++)
            {
                if (cornerSlots[i].slot == slot)
                {
                    var entry = cornerSlots[i];
                    entry.variants = variants;
                    cornerSlots[i] = entry;
                    UnityEditor.EditorUtility.SetDirty(this);
                    return;
                }
            }
            cornerSlots.Add(new CornerSlotMapping { slot = slot, variants = variants });
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void EditorSetMetadata(string folder, string primary, string secondary, int prio, AutoTileModel mdl)
        {
            folderName = folder;
            terrainPrimary = primary;
            terrainSecondary = secondary;
            priority = prio;
            model = mdl;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void EditorSetHiddenLegacy(List<Sprite> hidden)
        {
            hiddenLegacy = hidden ?? new List<Sprite>();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public void EditorSetPreviewIcon(Sprite icon)
        {
            previewIcon = icon;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    [Serializable]
    public struct SlotMapping
    {
        public Blob16Slot slot;
        public Sprite[] variants;
    }

    [Serializable]
    public struct CornerSlotMapping
    {
        public Corner16Slot slot;
        public Sprite[] variants;
    }
}
