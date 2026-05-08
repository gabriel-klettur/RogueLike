using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Tiny pointer-enter / pointer-exit probe attached to the Particles Editor View
    /// panel's preview surface. Exposes <see cref="IsHovered"/> so the editor's
    /// Update loop can apply mouse-wheel zoom only while the cursor is actually
    /// over the preview area.
    ///
    /// Mirrors <c>SpellsViewHoverProbe</c> in the Spells Editor.
    ///
    /// Requires the host RectTransform to have <see cref="UnityEngine.UI.Graphic.raycastTarget"/>
    /// = true on a sibling Image (the View panel's background fills that role).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParticlesViewHoverProbe : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public bool IsHovered { get; private set; }

        public void OnPointerEnter(PointerEventData _) => IsHovered = true;
        public void OnPointerExit(PointerEventData _)  => IsHovered = false;

        private void OnDisable() => IsHovered = false;
    }
}
