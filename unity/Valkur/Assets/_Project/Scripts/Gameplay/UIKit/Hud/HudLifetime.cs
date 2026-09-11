using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Releases a runtime-made texture, material or object the way the current mode allows.
    /// <c>Object.Destroy</c> is an ERROR in Edit Mode — not a warning — and the panel is built and
    /// rebuilt by EditMode fixtures, so every release in it goes through here rather than choosing
    /// at each call site and getting one of them wrong.
    /// </summary>
    public static class HudLifetime
    {
        public static void Release(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
