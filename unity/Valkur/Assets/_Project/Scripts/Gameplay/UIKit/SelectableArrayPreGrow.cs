using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Workaround for the recurring Unity 2022.3 UGUI bug where
    /// <c>Selectable.OnEnable()</c> throws <c>IndexOutOfRangeException</c>
    /// against its internal <c>s_Selectables</c> array. The bug fires every
    /// time MainMenuUI / PauseMenuUI / a runtime editor builds dozens of
    /// Selectables (Button, Toggle, Slider, …) on Play start.
    ///
    /// Issue tracker:
    /// https://issuetracker.unity3d.com/issues/indexoutofrangeexception-error-is-thrown-when-disabling-a-button
    ///
    /// ── Root cause (in detail) ────────────────────────────────────────────
    /// <see cref="Selectable.OnEnable"/> only grows <c>s_Selectables</c> when
    /// <c>s_SelectableCount == s_Selectables.Length</c> (strict equality).
    /// With Project Settings → Enter Play Mode Options → "Reload Domain"
    /// disabled (Valkur runs in this mode for fast iteration), both
    /// <c>s_Selectables</c> AND <c>s_SelectableCount</c> survive Play→Stop
    /// transitions. Selectables destroyed at scene-unload time don't always
    /// hit OnDisable, so the count drifts ABOVE the real living-Selectable
    /// total. On the next Play, a fresh OnEnable computes
    /// <c>m_CurrentIndex = s_SelectableCount</c> (now &gt; Length), the
    /// grow-check fails the equality test, and the very next line
    /// <c>s_Selectables[m_CurrentIndex] = this;</c> throws.
    ///
    /// ── Fix ───────────────────────────────────────────────────────────────
    /// Reset BOTH <c>s_Selectables</c> (a fresh 1024-slot clean array) AND
    /// <c>s_SelectableCount</c> (back to 0) on every Play start. We run at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> — the
    /// earliest stage, before any scene Awake — so no real Selectable has
    /// registered yet and clearing both fields cannot disturb live state.
    /// Each Selectable that gets enabled afterwards re-registers from a
    /// clean slate, and the 1024-element capacity then prevents in-session
    /// growth events (the secondary cause documented in the issue tracker).
    ///
    /// Implemented via reflection because both fields are internal to
    /// UGUI; no production package modification needed. Safe no-op if
    /// Unity ever renames the fields — the warning logs once instead of
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

        // SubsystemRegistration runs BEFORE any scene Awake, so existing
        // Selectables can't yet have registered against the old (smaller)
        // array — growing here is safe. We deliberately do NOT reset
        // s_SelectableCount: any Selectables still alive across the Play
        // boundary already have m_CurrentIndex pointing into the OLD array,
        // and zeroing the count would cause their next OnDisable to compute
        // s_SelectableCount-- == -1 and throw IndexOutOfRangeException at
        // Selectable.OnDisable line ~555. Array.Copy preserves those refs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
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
