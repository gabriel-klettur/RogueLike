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
    /// </summary>
    public partial class BuildingObject : MonoBehaviour
    {
        /// <summary>
        /// (Re)attach the casters after the template or the sprites changed. Called at the end
        /// of <c>Apply</c> beside the light refresh. Cheap when nothing changed: the caster is
        /// idempotent and re-syncs from the renderer it already has.
        /// </summary>
        private void RefreshShadowCasters()
        {
            if (!SkyStyle.Active.buildingShadows) return;
            if (_bottomRenderer == null) return;

            SunShadowCaster.Attach(_bottomRenderer, withBlob: false, sortUnder: _bottomRenderer, groundReference: _bottomRenderer);
            if (_topRenderer != null)
                SunShadowCaster.Attach(_topRenderer, withBlob: false, sortUnder: _bottomRenderer, groundReference: _bottomRenderer);
        }
    }
}
