using System;
using UnityEngine;
using Valkur.Core.UI;

namespace Valkur.Data
{
    /// <summary>
    /// Hand-painted art for the world bars, one slot per piece of
    /// <see cref="WorldBarSheetLayout"/>.
    ///
    /// <para><b>Every slot is optional and they are resolved one at a time.</b> A null slot falls
    /// back to the piece <c>WorldBarArt</c> generates, so an artist can paint the health frame,
    /// press play, look at it, and leave the other twenty as they are. An all-or-nothing skin
    /// would mean the first useful look at hand-painted art is only available after all of it
    /// exists.</para>
    ///
    /// <para><b>The cost of a partial skin is one extra draw call</b>, and it is worth naming:
    /// the generated pieces live on the runtime atlas and the painted ones on the imported sheet,
    /// so while both are in use the readout is two textures. Once every slot is filled it is one
    /// again — provided the whole skin came from a SINGLE PNG, which is what the importer
    /// produces and what <c>WorldBarSkinTests</c> checks.</para>
    ///
    /// <para>Sprites are referenced, not loaded by name: this asset already lives under
    /// <c>Resources/</c>, so the sheet itself can sit in <c>Art/</c> and be pulled into the build
    /// by reference — the same split <c>ProgressionCatalog</c> and <c>QuestCatalog</c> use to keep
    /// the build-everything folder carrying an index rather than content.</para>
    /// </summary>
    [Serializable]
    public class WorldBarSkin
    {
        [Tooltip("The chamfered ring around the health row. 8x" +
                 "healthRowTexels, 9-slice border 2,1,2,1.")]
        public Sprite frameHealth;

        [Tooltip("The ring around the resource row. 8xresourceRowTexels, border 2,1,2,1.")]
        public Sprite frameResource;

        [Tooltip("The recess inside the health frame. 8x(healthRowTexels-2), border 1,0,1,0. " +
                 "Not sliced vertically, so its height is fixed.")]
        public Sprite plateHealth;

        [Tooltip("The recess inside the resource frame. 8x(resourceRowTexels-2), border 1,0,1,0.")]
        public Sprite plateResource;

        [Tooltip("The health fill: a vertical ramp whose RIGHT-most column is the leading edge. " +
                 "8x(healthRowTexels-2), border 1,0,1,0.")]
        public Sprite fillHealth;

        [Tooltip("The resource fill. 8x(resourceRowTexels-2), border 1,0,1,0.")]
        public Sprite fillResource;

        [Tooltip("A plain white square. Quarter marks and the status overflow pip are drawn from " +
                 "it, so anything but flat white will tint wrong.")]
        public Sprite solid;

        [Tooltip("The dash pip's ring. pipTexels square, no border.")]
        public Sprite pipFrame;

        [Tooltip("The dash pip's interior, filled from the floor. (pipTexels-2) square.")]
        public Sprite pipCore;

        [Tooltip("Metal end caps of the health row: a one-texel column at each end, greyscale, " +
                 "tinted by rank.")]
        public Sprite capsHealth;

        [Tooltip("Metal end caps of the resource row.")]
        public Sprite capsResource;

        [Tooltip("Status glyphs indexed by StatusEffectKind's integer value: Burn, Poison, Stun, " +
                 "Freeze, Slow, Root, Vulnerable, Marked. iconTexels square, white on transparent.")]
        public Sprite[] statusIcons;

        /// <summary>True when not one slot is filled — the shipped state, and the fast path.</summary>
        public bool IsEmpty
        {
            get
            {
                if (frameHealth != null || frameResource != null) return false;
                if (plateHealth != null || plateResource != null) return false;
                if (fillHealth != null || fillResource != null) return false;
                if (solid != null || pipFrame != null || pipCore != null) return false;
                if (capsHealth != null || capsResource != null) return false;
                if (statusIcons != null)
                    for (int i = 0; i < statusIcons.Length; i++)
                        if (statusIcons[i] != null) return false;
                return true;
            }
        }

        /// <summary>
        /// The painted sprite for a layout id, or null when that piece has not been painted.
        ///
        /// <para>Resolved by the layout's own id string rather than by a switch at each call site,
        /// so adding a piece is one entry here and one in <see cref="WorldBarSheetLayout"/>.</para>
        /// </summary>
        public Sprite Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            switch (id)
            {
                case WorldBarSheetLayout.FRAME_HEALTH:   return frameHealth;
                case WorldBarSheetLayout.FRAME_RESOURCE: return frameResource;
                case WorldBarSheetLayout.PLATE_HEALTH:   return plateHealth;
                case WorldBarSheetLayout.PLATE_RESOURCE: return plateResource;
                case WorldBarSheetLayout.FILL_HEALTH:    return fillHealth;
                case WorldBarSheetLayout.FILL_RESOURCE:  return fillResource;
                case WorldBarSheetLayout.SOLID:          return solid;
                case WorldBarSheetLayout.PIP_FRAME:      return pipFrame;
                case WorldBarSheetLayout.PIP_CORE:       return pipCore;
                case WorldBarSheetLayout.CAPS_HEALTH:    return capsHealth;
                case WorldBarSheetLayout.CAPS_RESOURCE:  return capsResource;
            }

            if (id.StartsWith("icon_") && int.TryParse(id.Substring(5), out int index))
                return IconAt(index);

            return null;
        }

        /// <summary>The painted glyph for a status kind, or null.</summary>
        public Sprite IconAt(int kindIndex)
        {
            if (statusIcons == null || kindIndex < 0 || kindIndex >= statusIcons.Length) return null;
            return statusIcons[kindIndex];
        }

        /// <summary>Assign by layout id. Used by the importer; null clears a slot.</summary>
        public void Set(string id, Sprite sprite, int iconCount)
        {
            switch (id)
            {
                case WorldBarSheetLayout.FRAME_HEALTH:   frameHealth = sprite; return;
                case WorldBarSheetLayout.FRAME_RESOURCE: frameResource = sprite; return;
                case WorldBarSheetLayout.PLATE_HEALTH:   plateHealth = sprite; return;
                case WorldBarSheetLayout.PLATE_RESOURCE: plateResource = sprite; return;
                case WorldBarSheetLayout.FILL_HEALTH:    fillHealth = sprite; return;
                case WorldBarSheetLayout.FILL_RESOURCE:  fillResource = sprite; return;
                case WorldBarSheetLayout.SOLID:          solid = sprite; return;
                case WorldBarSheetLayout.PIP_FRAME:      pipFrame = sprite; return;
                case WorldBarSheetLayout.PIP_CORE:       pipCore = sprite; return;
                case WorldBarSheetLayout.CAPS_HEALTH:    capsHealth = sprite; return;
                case WorldBarSheetLayout.CAPS_RESOURCE:  capsResource = sprite; return;
            }

            if (!id.StartsWith("icon_") || !int.TryParse(id.Substring(5), out int index)) return;
            if (statusIcons == null || statusIcons.Length < iconCount)
            {
                var grown = new Sprite[Mathf.Max(iconCount, index + 1)];
                if (statusIcons != null)
                    System.Array.Copy(statusIcons, grown, Mathf.Min(statusIcons.Length, grown.Length));
                statusIcons = grown;
            }
            if (index >= 0 && index < statusIcons.Length) statusIcons[index] = sprite;
        }

        /// <summary>Drop every slot, putting the readout back on the generated art.</summary>
        public void Clear()
        {
            frameHealth = frameResource = null;
            plateHealth = plateResource = null;
            fillHealth = fillResource = null;
            solid = pipFrame = pipCore = null;
            capsHealth = capsResource = null;
            statusIcons = null;
        }
    }
}
