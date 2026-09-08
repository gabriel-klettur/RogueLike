using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// The single answer to "where can the player revive".
    ///
    /// <para><b>Why it exists.</b> Three systems each asked that question their own way and one
    /// of them asked it sixty times a second: <c>SpiritAltarPathHighlighter</c> ran
    /// <c>FindObjectsOfType&lt;ResurrectionZone&gt;</c> on every path rebuild,
    /// <c>SpiritWorldGrayscale</c> swept every <c>BuildingObject</c> in the scene comparing a
    /// hard-coded template id, and <c>ResurrectionZoneAutoBinder</c> polled the loader. Three
    /// scans, two copies of the magic number, and no single place that could say "there are no
    /// altars" — which is exactly the sentence the shipped build needed to say and could not.</para>
    ///
    /// <para><b>Membership is not eligibility.</b> A destroyed zone leaves the registry; a
    /// DEACTIVATED one stays in it and is filtered by whoever asks. That split is the lesson
    /// <c>AlliedUnit</c> paid for: pruning inactive entries makes re-enabling one impossible
    /// outside Play Mode, because nothing calls <c>OnEnable</c> there and nothing puts it back.</para>
    ///
    /// <para><b>It never hands out its backing list.</b> <see cref="Snapshot"/> copies, and
    /// returns a shared empty array in the case that is almost always true, so the common path
    /// allocates nothing. Handing out <c>_live</c> while three paths mutate it is how an index
    /// walk silently skips an entry — the other half of the same <c>AlliedUnit</c> defect.</para>
    /// </summary>
    public static class ResurrectionAltarRegistry
    {
        private static readonly List<ResurrectionZone> _live = new List<ResurrectionZone>(4);
        private static ResurrectionZone[] _empty = new ResurrectionZone[0];

        /// <summary>
        /// Domain Reload is OFF. <c>field.Clear()</c> and a plain <c>stsfld</c> are the only two
        /// reset shapes <c>DomainReloadStaticResetTests</c> recognises — it reads the hook's raw
        /// IL, so <c>Array.Clear(_empty, …)</c> would count as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _live.Clear();
            _empty = new ResurrectionZone[0];
        }

        /// <summary>How many altars are registered, destroyed ones excluded.</summary>
        public static int Count
        {
            get
            {
                Prune();
                return _live.Count;
            }
        }

        /// <summary>True when at least one altar is registered AND active in the hierarchy.</summary>
        public static bool AnyUsable
        {
            get
            {
                Prune();
                for (int i = 0; i < _live.Count; i++)
                    if (_live[i] != null && _live[i].isActiveAndEnabled) return true;
                return false;
            }
        }

        public static void Register(ResurrectionZone zone)
        {
            if (zone == null) return;
            Prune();
            if (!_live.Contains(zone)) _live.Add(zone);
        }

        /// <summary>
        /// Only a DESTROYED zone leaves. Called from <c>OnDestroy</c>, never from
        /// <c>OnDisable</c>: a deactivated altar must stay listed so re-enabling it needs no
        /// second mechanism to put it back.
        /// </summary>
        public static void Unregister(ResurrectionZone zone)
        {
            if (zone == null) { Prune(); return; }
            _live.Remove(zone);
        }

        /// <summary>A copy of the live altars. Shared empty array when there are none.</summary>
        public static IReadOnlyList<ResurrectionZone> Snapshot()
        {
            Prune();
            if (_live.Count == 0) return _empty;
            return _live.ToArray();
        }

        /// <summary>
        /// The nearest usable altar to <paramref name="from"/>, measured against the building's
        /// own rect centre rather than its transform — a building's pivot sits at a corner of a
        /// footprint that can be several tiles across, so two altars can rank in the wrong order
        /// on the transform alone.
        /// </summary>
        public static bool TryGetNearest(Vector3 from, out ResurrectionZone nearest, out float distance)
        {
            Prune();
            nearest = null;
            distance = float.PositiveInfinity;

            for (int i = 0; i < _live.Count; i++)
            {
                var zone = _live[i];
                if (zone == null || !zone.isActiveAndEnabled) continue;

                float d = Vector2.Distance(from, zone.AnchorPoint);
                if (d < distance) { distance = d; nearest = zone; }
            }

            return nearest != null;
        }

        /// <summary>
        /// THE predicate: is this art an altar.
        ///
        /// <para>Two inputs, ONE answer, and the precedence is documented because two models that
        /// can disagree is what this subsystem was rebuilt out of. The FLAG on the template is the
        /// model — it is a fact about the building, it is visible from the building, and it cannot
        /// drift from itself. <c>DeathTuning.altarTemplateIds</c> is a legacy bridge kept so no
        /// already-shipped world loses its altar, and nothing writes to it any more.</para>
        ///
        /// <para>They are OR-ed rather than ranked. A ranking would need a way to say "this
        /// template is NOT an altar despite the list", i.e. a third state on a bool, which is how a
        /// compatibility shim turns into a second model.</para>
        /// </summary>
        public static bool IsAltar(BuildingTemplateData template)
        {
            if (template == null) return false;
            if (template.isResurrectionAltar) return true;
            return Valkur.Data.DeathTuning.Active.IsAltarTemplate(template.templateId);
        }

        /// <summary>
        /// Attach a <see cref="ResurrectionZone"/> to <paramref name="building"/> if its art is an
        /// altar and it has none yet. Returns true when one was added.
        /// </summary>
        public static bool TryBind(BuildingObject building)
        {
            if (building == null || building.Template == null) return false;
            if (!IsAltar(building.Template)) return false;
            if (building.GetComponent<ResurrectionZone>() != null) return false;

            building.gameObject.AddComponent<ResurrectionZone>();
            return true;
        }

        /// <summary>
        /// Scan every spawned building and bind the ones whose template says altar. Returns how
        /// many were added. Safe to call repeatedly — already-bound buildings are skipped.
        /// </summary>
        public static int BindAll(BuildingLoader loader)
        {
            if (loader == null) return 0;
            int bound = 0;
            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
                if (TryBind(spawned[i])) bound++;
            return bound;
        }

        /// <summary>
        /// Unbind every altar whose template is no longer listed, and bind every one that now is.
        /// Called by the Death Editor when the author edits the template list, so the change is
        /// visible in the same frame instead of on the next world load.
        /// </summary>
        public static void Rebind(BuildingLoader loader)
        {
            if (loader == null) return;

            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
            {
                var b = spawned[i];
                if (b == null || b.Template == null) continue;

                bool shouldBe = IsAltar(b.Template);
                var zone = b.GetComponent<ResurrectionZone>();

                if (shouldBe && zone == null) b.gameObject.AddComponent<ResurrectionZone>();
                else if (!shouldBe && zone != null)
                {
                    // Object.Destroy is an outright ERROR in Edit Mode, and an editor rebind is
                    // one of the paths that can run there.
                    if (Application.isPlaying) Object.Destroy(zone);
                    else Object.DestroyImmediate(zone);
                }
            }
        }

        private static void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i] == null) _live.RemoveAt(i);
        }
    }
}
