using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World.Sky;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A building's sun shadow.
    ///
    /// Both halves cast — the footprint AND the canopy — from ONE foot line, the footprint's
    /// bottom edge, and both sort just under the footprint. Casting from the canopy's own
    /// bottom would put a tree crown's shadow starting three units up its trunk; sorting the
    /// canopy's shadow with the canopy would draw it over the player walking under the crown.
    ///
    /// No contact blob: a building's footprint IS its contact with the ground.
    ///
    /// <para>TWO facts about the ART decide the shadow, and neither can be read off the sprite
    /// at runtime — an atlas-packed texture is not readable — so both are baked onto the
    /// template by <c>Valkur > Buildings > Bake Sprite Ink Bounds</c>: where the ink's bottom is
    /// (<c>inkBottomNormalized</c>), and whether the piece has a base at all
    /// (<see cref="BuildingProjectedShadow"/>).</para>
    /// </summary>
    public partial class BuildingObject : MonoBehaviour
    {
        /// <summary>
        /// The distance, in the CHILDREN's own local units, from the sprite rect's bottom row up
        /// to the lowest row of solid ink. Both children sit at localScale 1 under the root and
        /// both sprites are cut at <c>PPU</c>, so one number serves the footprint and the canopy;
        /// the root's scale then carries it into the world exactly as it carries the art.
        /// </summary>
        private float _inkBottomLocal;

        /// <summary>
        /// (Re)attach the casters after the template or the sprites changed. Called at the end
        /// of <c>Apply</c> beside the light refresh. Cheap when nothing changed: the caster is
        /// idempotent and re-syncs from the renderer it already has.
        /// </summary>
        private void RefreshShadowCasters()
        {
            if (!SkyStyle.Active.buildingShadows) return;
            if (_bottomRenderer == null) return;

            // A piece drawn in plan has no silhouette to shear. Any caster already on it is
            // taken off rather than left standing: a template can be re-authored in the
            // Buildings editor, and a shadow that stayed attached would come back with it.
            if (!BuildingProjectedShadow.Resolve(_template))
            {
                RemoveCaster(_bottomRenderer);
                RemoveCaster(_topRenderer);
                return;
            }

            SunShadowCaster.Attach(_bottomRenderer, withBlob: false, sortUnder: _bottomRenderer,
                                   groundReference: _bottomRenderer, groundOffsetLocal: _inkBottomLocal);
            if (_topRenderer != null)
                SunShadowCaster.Attach(_topRenderer, withBlob: false, sortUnder: _bottomRenderer,
                                       groundReference: _bottomRenderer, groundOffsetLocal: _inkBottomLocal);
        }

        private static void RemoveCaster(SpriteRenderer sr)
        {
            if (sr == null) return;
            var caster = sr.GetComponent<SunShadowCaster>();
            if (caster == null) return;
            if (caster.Shadow != null) caster.Shadow.enabled = false;
            if (caster.Blob != null) caster.Blob.enabled = false;
            // Destroy is an error in Edit Mode, and the Buildings editor runs there.
            if (Application.isPlaying) Destroy(caster); else caster.enabled = false;
        }
    }
}
