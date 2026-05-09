using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Workaround for the recurring Unity 2022.3 UGUI bug where
    /// <c>Selectable.OnEnable()</c> throws <c>IndexOutOfRangeException</c>
    /// against its internal <c>s_Selectables</c> array when many
    /// Selectables (Button, Toggle, Slider, …) enable in close succession
    /// — a hot path for any in-game editor that constructs its UI on F-key
    /// press, and for the Main / Pause menus that build dozens of rows.
    ///
    /// Issue tracker:
    /// https://issuetracker.unity3d.com/issues/indexoutofrangeexception-error-is-thrown-when-disabling-a-button
    ///
    /// Root cause: <c>s_Selectables</c> grows lazily by doubling
    /// (initial capacity 10). Every Add that crosses a power-of-two boundary
    /// reallocates the backing array; the bug surfaces when an OnEnable
    /// reads a stale length AFTER the reallocation but BEFORE the assignment.
    /// Pre-growing the array to a comfortable upper bound eliminates the
    /// growth events for the lifetime of the play session, so the bug
    /// cannot fire — Selectables come and go but the array never resizes.
    ///
    /// Done via reflection because <c>s_Selectables</c> is internal to
    /// UGUI; no production package modification needed. Safe no-op if
    /// Unity ever renames the field — the warning logs once instead of
    /// crashing.
    /// </summary>
    public static class SelectableArrayPreGrow
    {
        // Capacity sized for "every button across every menu + every
        // editor's UI built from BuildUI() at once" with significant
        // headroom. Bumps to higher values are a one-line edit if a future
        // editor adds another panel-load of Selectables. 4 KiB of pointers
        // is a fraction of a typical scene's UI memory.
        private const int InitialCapacity = 1024;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreGrow()
        {
            try
            {
                var t = typeof(Selectable);
                var field = t.GetField("s_Selectables",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                {
                    Debug.LogWarning(
                        "[SelectableArrayPreGrow] Selectable.s_Selectables not " +
                        "found — Unity may have renamed the field. The UGUI " +
                        "IndexOutOfRangeException workaround is inactive.");
                    return;
                }

                if (!(field.GetValue(null) is Selectable[] current))
                {
                    Debug.LogWarning(
                        "[SelectableArrayPreGrow] s_Selectables is not a " +
                        "Selectable[]. Workaround skipped.");
                    return;
                }

                if (current.Length >= InitialCapacity) return; // already big enough

                var grown = new Selectable[InitialCapacity];
                System.Array.Copy(current, grown, current.Length);
                field.SetValue(null, grown);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[SelectableArrayPreGrow] Skipped — reflection threw: {ex.Message}");
            }
        }
    }
}
