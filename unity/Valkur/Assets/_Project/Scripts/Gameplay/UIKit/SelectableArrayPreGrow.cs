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
        private static void ResetOnPlayStart() => Reset();

        /// <summary>
        /// Force UGUI's Selectable static array back to a fresh
        /// <see cref="InitialCapacity"/>-slot buffer and zero the count. Public
        /// API but ONLY safe to call when no <see cref="Selectable"/> instances
        /// exist anywhere in the scene tree — e.g. the very start of Play Mode
        /// (the runtime <c>SubsystemRegistration</c> hook below) where every
        /// editor-scene Selectable has already been destroyed and no runtime-
        /// scene Selectable has registered yet.
        ///
        /// DO NOT call from EditMode tests: the editor stays alive during the
        /// test session and its permanent UI (Inspector, Project window, the
        /// Test Runner itself) keeps Selectables registered against the
        /// pre-Reset array. Zeroing the count behind their back makes their
        /// next <see cref="Selectable.OnDisable"/> underflow against a fresh
        /// array — the exact <c>IndexOutOfRangeException</c> cascade we are
        /// trying to prevent. Use <see cref="EnsureCapacity(int)"/> instead.
        /// </summary>
        public static void Reset()
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

        /// <summary>
        /// Safe-at-any-time variant: grows <c>s_Selectables</c> so its
        /// length is at least <c>max(requestedCapacity, s_SelectableCount × 2)</c>.
        /// The <c>count × 2</c> term is the critical defence: if drift has
        /// already pushed <c>s_SelectableCount</c> above the requested
        /// capacity (a Selectable's OnEnable threw earlier in the session and
        /// the matching OnDisable skipped its decrement), a fixed capacity
        /// floor wouldn't recover — the next OnEnable would still try to
        /// write past the array end because UGUI's grow-check fires only on
        /// strict equality (<c>count == length</c>). Doubling above the live
        /// count guarantees the strict-equality check triggers before any
        /// out-of-bounds write.
        ///
        /// Never zeroes the count and never drops references — any live
        /// <see cref="Selectable"/> (including the editor's permanent UI:
        /// Inspector, Project window, Test Runner) keeps its valid
        /// <c>m_CurrentIndex</c> after the grow.
        ///
        /// Designed for the EditMode test runner: calling <see cref="Reset"/>
        /// mid-session orphans live Selectables and triggers an
        /// <c>IndexOutOfRangeException</c> cascade in the editor's own UI
        /// (which then surfaces as "unhandled log message" failures in
        /// dozens of unrelated tests). <c>EnsureCapacity</c> avoids that.
        /// </summary>
        public static void EnsureCapacity(int requestedCapacity)
        {
            if (requestedCapacity <= 0) return;
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
                        "not found — EnsureCapacity inactive.");
                    return;
                }

                var current = (Selectable[])arrField.GetValue(null);
                int liveCount = (int)countField.GetValue(null);
                // Drift safety: if the count has crept above requestedCapacity
                // (failed-OnEnable cascade), the floor doesn't help — grow
                // explicitly to 2× the live count so UGUI's strict-equality
                // grow-check has room to fire before any out-of-bounds write.
                int target = Mathf.Max(requestedCapacity, liveCount * 2);

                if (current != null && current.Length >= target) return;

                var bigger = new Selectable[target];
                if (current != null)
                    Array.Copy(current, bigger, current.Length);
                arrField.SetValue(null, bigger);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SelectableArrayPreGrow] EnsureCapacity skipped — reflection threw: {ex.Message}");
            }
        }
    }
}
