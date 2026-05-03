using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Edit-mode-safe destruction helper. <see cref="Object.Destroy(Object)"/>
    /// is a no-op in EditMode and logs an error in newer Unity test runners,
    /// so any code path that runs both at runtime AND in EditMode tests
    /// (editor utilities, services with TearDown teardown, lifecycle
    /// resets, etc.) needs to branch on <see cref="Application.isPlaying"/>.
    ///
    /// Centralising the branch in one helper keeps the convention identical
    /// across the codebase — replace ad-hoc <c>Application.isPlaying ?
    /// Destroy : DestroyImmediate</c> ternaries with <see cref="Of(Object)"/>
    /// so future regressions can't drift.
    /// </summary>
    public static class SafeDestroy
    {
        /// <summary>
        /// Destroys <paramref name="obj"/> using the appropriate API for
        /// the current playmode state. Null inputs are a no-op so callers
        /// don't need a separate guard.
        /// </summary>
        public static void Of(Object obj)
        {
            if (obj == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }

        /// <summary>
        /// Convenience overload that destroys the GameObject of a Component
        /// — the most common pattern for editor cleanup. Null-safe.
        /// </summary>
        public static void GameObjectOf(Component component)
        {
            if (component == null) return;
            Of(component.gameObject);
        }
    }
}
