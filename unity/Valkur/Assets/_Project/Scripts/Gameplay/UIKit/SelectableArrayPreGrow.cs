using System;
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
    /// hit OnDisable (UGUI bug — happens when an OnEnable earlier threw and
    /// <c>m_EnableCalled</c> never got set, plus a handful of other edge
    /// cases), so the count drifts ABOVE the real living-Selectable total.
    /// On the next Play, a fresh OnEnable computes
    /// <c>m_CurrentIndex = s_SelectableCount</c> (now &gt; Length), the
    /// grow-check fails the strict-equality test, and the very next line
    /// <c>s_Selectables[m_CurrentIndex] = this;</c> throws.
    ///
    /// ── Fix ───────────────────────────────────────────────────────────────
    /// Reset BOTH <c>s_Selectables</c> (a fresh <see cref="InitialCapacity"/>
    /// clean array) AND <c>s_SelectableCount</c> (back to 0) on every Play
    /// start. We run at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> — the
    /// earliest stage, before any scene Awake — so:
    ///   • All editor-scene Selectables have already been destroyed by the
    ///     play-mode transition (any drift they introduced is dead state),
    ///   • Runtime-scene Selectables have not yet had a chance to register,
    ///     so resetting the count cannot underflow any live OnDisable.
    ///
    /// The previous version of this workaround grew the array but left
    /// <c>s_SelectableCount</c> alone out of an unfounded fear that resetting
    /// it would cause a live Selectable's OnDisable to underflow. At
    /// SubsystemRegistration time there are no live Selectables, so the
    /// reset is safe and is the only way to recover from drift &gt;
    /// <see cref="InitialCapacity"/> (which long editor sessions hit).
    ///
    /// Implemented via reflection because both fields are protected statics
    /// on UGUI's Selectable; no production package modification needed. Safe
    /// no-op if Unity ever renames the fields — the warning logs once
    /// instead of crashing.
    /// </summary>
    public static class SelectableArrayPreGrow
    {
        // Capacity sized for "every button across every menu + every
        // editor's UI built from BuildUI() at once" with significant
        // headroom. Bumps to higher values are a one-line edit if a future
        // editor adds another panel-load of Selectables. 4 KiB of pointers
        // is a fraction of a typical scene's UI memory.
        public const int InitialCapacity = 1024;

        private const string SelectablesFieldName = "s_Selectables";
        private const string SelectableCountFieldName = "s_SelectableCount";

        // SubsystemRegistration runs BEFORE any scene Awake. The play-mode
        // transition has already destroyed every editor-scene Selectable, so
        // the static array can be replaced wholesale without dangling any
        // live OnDisable swap-pop logic. New Selectables register against
        // the fresh array from index 0 with no risk of overflow.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            try
            {
                var t = typeof(Selectable);
                var arrField = t.GetField(SelectablesFieldName,
                    BindingFlags.NonPublic | BindingFlags.Static);
                var countField = t.GetField(SelectableCountFieldName,
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (arrField == null || countField == null)
                {
                    Debug.LogWarning(
                        "[SelectableArrayPreGrow] UGUI Selectable static fields " +
                        "not found — Unity may have renamed them. Workaround inactive.");
                    return;
                }

                arrField.SetValue(null, new Selectable[InitialCapacity]);
                countField.SetValue(null, 0);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SelectableArrayPreGrow] Skipped — reflection threw: {ex.Message}");
            }
        }
    }
}
