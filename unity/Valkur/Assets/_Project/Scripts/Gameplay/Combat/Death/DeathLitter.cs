using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Everything the player's last death left lying on the ground, so the NEXT death can sweep it.
    ///
    /// <para><b>Why it is needed.</b> <c>PlayerDeathDropSystem</c> scatters the whole inventory and
    /// the whole purse, and its own comment said the items "live until revive… but does not clean
    /// up dropped items" — which is true and is the bug: nothing cleaned them up ever. Ten deaths
    /// in one session left ten piles, each of them a full bag, and the loot the player was walking
    /// back for was indistinguishable from the loot they had already collected around it.</para>
    ///
    /// <para><b>It sweeps on the NEXT DEATH, not on revive.</b> Sweeping at revive would delete
    /// the pile the player has just walked all the way back to reach, which is the whole point of
    /// the corpse run. Sweeping at the next death is the moment a fresh pile appears and the old
    /// one stops being the thing they were going back for.</para>
    ///
    /// <para><b>It tracks GameObjects, not item data.</b> A player who picked an item back up must
    /// not have it deleted out of their bag later, and a destroyed pickup simply reads as null
    /// here — the same "membership is not eligibility" split <c>ResurrectionAltarRegistry</c> makes.</para>
    /// </summary>
    public static class DeathLitter
    {
        private static readonly List<GameObject> _tracked = new List<GameObject>(32);

        /// <summary>
        /// Domain Reload is OFF. <c>field.Clear()</c> is one of the two reset shapes
        /// <c>DomainReloadStaticResetTests</c> recognises — it reads the hook's raw IL, so
        /// passing the field to <c>Array.Clear</c> would count as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tracked.Clear();
        }

        /// <summary>How many of the previous death's drops are still on the ground.</summary>
        public static int Count
        {
            get
            {
                Prune();
                return _tracked.Count;
            }
        }

        public static void Track(GameObject go)
        {
            if (go == null) return;
            Prune();
            if (!_tracked.Contains(go)) _tracked.Add(go);
        }

        public static void Track(Component component)
        {
            if (component != null) Track(component.gameObject);
        }

        /// <summary>
        /// Destroy every surviving drop from the previous death and forget the list. Returns how
        /// many were actually removed, which is what the console probe and the editor report.
        /// </summary>
        public static int ClearAll()
        {
            int removed = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                var go = _tracked[i];
                if (go == null) continue;

                // Object.Destroy is an outright ERROR in Edit Mode, and the death flow is driven
                // from EditMode fixtures.
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
                removed++;
            }
            _tracked.Clear();
            return removed;
        }

        /// <summary>Forget the list WITHOUT destroying anything. Used when the world itself is torn down.</summary>
        public static void Forget()
        {
            _tracked.Clear();
        }

        private static void Prune()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i] == null) _tracked.RemoveAt(i);
        }
    }
}
