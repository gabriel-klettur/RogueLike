using UnityEngine;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Marks a subtree of the player that <see cref="PlayerSpiritVisuals"/> must leave alone.
    ///
    /// <para>The spirit look tints EVERY sprite renderer under the player to a black silhouette,
    /// which is right for the body and whatever it is holding and wrong for a readout: the bars
    /// over the head are deliberately kept on screen while the player is a spirit — it is the one
    /// moment the run is in danger — and tinting them black kept them on screen as unreadable
    /// shapes. Worse, the tint was written through a <c>MaterialPropertyBlock</c> <c>_Color</c>
    /// that the revive restored to a colour cached at death, so every bar colour after the first
    /// death was multiplied by a frozen one: the low-health amber rendered as dark olive for the
    /// rest of the session. A readout owns its own colours; this marker says so.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpiritTintExempt : MonoBehaviour
    {
    }
}
