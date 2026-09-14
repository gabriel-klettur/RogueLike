using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// What one walk or run cycle was DRAWN to do: where its loop starts, which frames plant a
    /// foot, and how long one step is. Measured by <c>tools/atlas/locomotion_cycles.py</c> and
    /// reviewed on a contact sheet before it is imported.
    /// </summary>
    [Serializable]
    public sealed class LocomotionCycle
    {
        [Tooltip("Sheet prefix the frames are named by, e.g. 'dwarf_armed_running' (frames 'dwarf_armed_running_e0'...).")]
        public string key;

        [Tooltip("'walk' or 'chase'. Informational: the same sheet always plays the same state.")]
        public string state;

        [Tooltip("First frame of the loop. 0 plays every frame; 1 skips an opening pose that is not part of the cycle.")]
        [Range(0, 1)] public int loopStart;

        [Tooltip("The frame each foot lands on, one per foot, half a cycle apart.")]
        public int[] contactFrames = Array.Empty<int>();

        [Tooltip("One step, in world units, as drawn.")]
        [Min(0f)] public float strideUnits;

        [Tooltip("Per frame, how many pixels the lowest ink floats above the canvas bottom. The " +
                 "airborne frames of a run are the ones worth reading: the contact shadow shrinks " +
                 "and fades while the body is off the ground.")]
        public int[] groundLiftPx = Array.Empty<int>();

        /// <summary>
        /// Pixels frame <paramref name="frame"/> is off the ground, above the cycle's own lowest
        /// frame — so a cycle drawn floating a few pixels throughout reads as grounded, and only a
        /// real hop counts.
        /// </summary>
        public int LiftAbovePlanted(int frame)
        {
            if (groundLiftPx == null || frame < 0 || frame >= groundLiftPx.Length) return 0;
            int min = int.MaxValue;
            for (int i = 0; i < groundLiftPx.Length; i++) if (groundLiftPx[i] < min) min = groundLiftPx[i];
            return Math.Max(0, groundLiftPx[frame] - min);
        }

        public bool HasContacts => contactFrames != null && contactFrames.Length > 0;

        /// <summary>True when <paramref name="frame"/> plants a foot; <paramref name="foot"/> says which.</summary>
        public bool IsContact(int frame, out int foot)
        {
            foot = -1;
            if (contactFrames == null) return false;
            for (int i = 0; i < contactFrames.Length; i++)
                if (contactFrames[i] == frame) { foot = i; return true; }
            return false;
        }
    }

    /// <summary>
    /// Every measured locomotion cycle, keyed by SHEET rather than by character, state or loadout.
    ///
    /// <para><b>WHY BY SHEET.</b> One drawing is reached from a base state, a state variant, a
    /// loadout and a Dark twin (whose config is a copy of the class's). The cycle is a fact about
    /// the drawing, so the animator asks by the name of the frames it is showing and every one of
    /// those routes gets the same answer with no copy to keep in sync.</para>
    ///
    /// <para>A sheet with no entry (every monster today) keeps the historical behaviour: skip frame 0,
    /// no contact events, speed-derived pacing.</para>
    ///
    /// <para>Under <c>Resources/Skills/</c> beside <see cref="LocomotionTuning"/>, for the same
    /// reason: its reader, <c>DirectionalAnimator</c>, is added by code on every entity.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "LocomotionCycleCatalog", menuName = "Valkur/Player/Locomotion Cycle Catalog")]
    public sealed class LocomotionCycleCatalog : ScriptableObject
    {
        public const string ResourcePath = "Skills/LocomotionCycleCatalog";

        public List<LocomotionCycle> cycles = new List<LocomotionCycle>();

        [NonSerialized] private Dictionary<string, LocomotionCycle> _byKey;

        public LocomotionCycle Find(string key)
        {
            if (string.IsNullOrEmpty(key) || cycles == null) return null;
            if (_byKey == null || _byKey.Count != cycles.Count)
            {
                _byKey = new Dictionary<string, LocomotionCycle>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in cycles)
                    if (c != null && !string.IsNullOrEmpty(c.key)) _byKey[c.key] = c;
            }
            return _byKey.TryGetValue(key, out var found) ? found : null;
        }

        /// <summary>
        /// The sheet key a frame is named by: "dwarf_armed_running_e3" -> "dwarf_armed_running".
        /// Packed sprites keep their names, which is what makes this lookup survive the atlas.
        /// </summary>
        public static string SheetKeyOf(Sprite frame)
        {
            if (frame == null) return null;
            string name = frame.name;
            int cut = name.LastIndexOf('_');
            if (cut <= 0 || cut >= name.Length - 1) return null;
            char side = name[cut + 1];
            return side == 'e' || side == 'w' ? name.Substring(0, cut) : null;
        }

        public void InvalidateLookup() => _byKey = null;

        // ── Resolution ───────────────────────────────────────────────────────

        private static LocomotionCycleCatalog s_cached;
        private static bool s_looked;
        private static LocomotionCycleCatalog s_override;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
            s_override = null;
        }

        /// <summary>Test seam: a catalog to answer instead of the shipped one. Null restores it.
        /// Animators cache per frame array, so set it before building the rig under test.</summary>
        public static LocomotionCycleCatalog OverrideForTests
        {
            get => s_override;
            set => s_override = value;
        }

        /// <summary>The shipped catalog, or null when none was imported. Null is a valid answer:
        /// every caller falls back to the historical cycle behaviour.</summary>
        public static LocomotionCycleCatalog Active
        {
            get
            {
                if (s_override != null) return s_override;
                if (!s_looked)
                {
                    s_cached = Resources.Load<LocomotionCycleCatalog>(ResourcePath);
                    s_looked = true;
                }
                return s_cached;
            }
        }

        public static void InvalidateCache()
        {
            s_cached = null;
            s_looked = false;
        }
    }
}
