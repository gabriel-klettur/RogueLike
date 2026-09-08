using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// The registry, the litter list and the save flag — the three pieces of the death flow that
    /// are pure enough to test without a scene, and each of which fails silently in production.
    /// </summary>
    public class DeathFlowStateTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();

            DeathLitter.Forget();
            DeathTuning.InvalidateCache();
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        // ── DeathLitter ─────────────────────────────────────────────────────

        [Test]
        public void Litter_CountsWhatItTracks_AndPrunesWhatWasDestroyed()
        {
            DeathLitter.Forget();

            var a = NewObject("dropA");
            var b = NewObject("dropB");
            DeathLitter.Track(a);
            DeathLitter.Track(b);
            Assert.That(DeathLitter.Count, Is.EqualTo(2));

            Object.DestroyImmediate(b);
            Assert.That(DeathLitter.Count, Is.EqualTo(1),
                "a pickup the player collected is destroyed, and must not go on being counted");
        }

        [Test]
        public void Litter_TrackingTheSameObjectTwice_CountsItOnce()
        {
            DeathLitter.Forget();

            var a = NewObject("dropA");
            DeathLitter.Track(a);
            DeathLitter.Track(a);

            Assert.That(DeathLitter.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// <c>Forget</c> and <c>ClearAll</c> are different verbs and the difference matters: a world
        /// swap must drop the LIST without destroying objects that the teardown is already taking,
        /// while the next death must actually remove the previous pile.
        /// </summary>
        [Test]
        public void Litter_ForgetDropsTheListWithoutDestroying_ClearAllDestroys()
        {
            DeathLitter.Forget();

            var kept = NewObject("kept");
            DeathLitter.Track(kept);
            DeathLitter.Forget();

            Assert.That(DeathLitter.Count, Is.Zero);
            Assert.That(kept, Is.Not.Null, "Forget must not destroy anything");

            var swept = NewObject("swept");
            DeathLitter.Track(swept);
            int removed = DeathLitter.ClearAll();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(DeathLitter.Count, Is.Zero);
        }

        // ── ResurrectionAltarRegistry ───────────────────────────────────────

        /// <summary>
        /// The registry must not hand out its backing list.
        ///
        /// <para><c>AlliedUnit</c> shipped that defect: a caller walking <c>Count</c> while a
        /// registration mutated the list skipped an entry with nothing thrown. A snapshot cannot.</para>
        /// </summary>
        [Test]
        public void Registry_SnapshotIsACopy_NotTheBackingList()
        {
            var zone = NewAltar("altarA");
            ResurrectionAltarRegistry.Register(zone);

            var first = ResurrectionAltarRegistry.Snapshot();
            int before = first.Count;

            ResurrectionAltarRegistry.Register(NewAltar("altarB"));

            Assert.That(first.Count, Is.EqualTo(before),
                "a snapshot taken before a registration must not grow underneath its reader");
        }

        [Test]
        public void Registry_ForgetsADestroyedAltar()
        {
            var zone = NewAltar("altarA");
            ResurrectionAltarRegistry.Register(zone);
            int with = ResurrectionAltarRegistry.Count;

            Object.DestroyImmediate(zone.gameObject);

            Assert.That(ResurrectionAltarRegistry.Count, Is.EqualTo(with - 1));
        }

        /// <summary>
        /// Membership is not eligibility. A deactivated altar STAYS registered — pruning it would
        /// make re-enabling it impossible outside Play Mode, where nothing calls <c>OnEnable</c> and
        /// nothing would put it back — but it must not be offered as usable.
        /// </summary>
        [Test]
        public void Registry_KeepsADeactivatedAltarListed_ButNotUsable()
        {
            var zone = NewAltar("altarA");
            ResurrectionAltarRegistry.Register(zone);

            zone.gameObject.SetActive(false);

            Assert.That(ResurrectionAltarRegistry.Count, Is.GreaterThan(0),
                "a deactivated altar stays in the registry");
            Assert.That(ResurrectionAltarRegistry.TryGetNearest(Vector3.zero, out _, out _), Is.False,
                "but it must not be offered as somewhere the player can revive");
        }

        [Test]
        public void Registry_NearestIsMeasuredFromTheFootprintCentre()
        {
            var near = NewAltar("near");
            near.transform.position = new Vector3(3f, 0f, 0f);
            var far = NewAltar("far");
            far.transform.position = new Vector3(40f, 0f, 0f);

            ResurrectionAltarRegistry.Register(far);
            ResurrectionAltarRegistry.Register(near);

            Assert.That(ResurrectionAltarRegistry.TryGetNearest(Vector3.zero, out var found, out float d), Is.True);
            Assert.That(found, Is.EqualTo(near));
            Assert.That(d, Is.EqualTo(3f).Within(0.01f));
        }

        /// <summary>
        /// A <c>ResurrectionZone</c> with no <c>BuildingObject</c> rect falls back to its transform.
        /// EditMode has no spawned buildings, so this is the path every fixture here takes — and it
        /// has to be the one production takes too when a rect is unavailable, or the two disagree.
        /// </summary>
        private Valkur.Gameplay.World.ResurrectionZone NewAltar(string name)
        {
            var go = NewObject(name);
            var building = go.AddComponent<Valkur.Gameplay.World.BuildingObject>();
            Assert.That(building, Is.Not.Null);
            return go.AddComponent<Valkur.Gameplay.World.ResurrectionZone>();
        }

        // ── DeathStateSave ──────────────────────────────────────────────────

        /// <summary>
        /// A save that predates the layer carries no key, and that has to read as "was alive" —
        /// every existing save on every machine is in that state.
        /// </summary>
        [Test]
        public void DeathStateSave_ASaveWithNoKeys_ReadsAsAlive()
        {
            var data = new GameSaveData();
            Assert.That(DeathStateSave.TryRead(data, out _), Is.False);
        }

        [Test]
        public void DeathStateSave_ReadsBackTheCorpsePosition()
        {
            var data = new GameSaveData();
            data.SetMeta(DeathStateSave.SpiritMetaKey, "1");
            data.SetMeta(DeathStateSave.CorpseXMetaKey, "205.04");
            data.SetMeta(DeathStateSave.CorpseYMetaKey, "1.62");

            Assert.That(DeathStateSave.TryRead(data, out Vector3 corpse), Is.True);
            Assert.That(corpse.x, Is.EqualTo(205.04f).Within(0.001f));
            Assert.That(corpse.y, Is.EqualTo(1.62f).Within(0.001f));
        }

        /// <summary>
        /// An explicit "0" must read as alive.
        ///
        /// <para>The metadata bag is a BAG, not a snapshot: a save taken after a revive has to
        /// overwrite the flag rather than inherit whatever an older write left there. Writing the
        /// key absent instead would make a revived player load as a spirit forever.</para>
        /// </summary>
        [Test]
        public void DeathStateSave_AnExplicitZero_ReadsAsAlive()
        {
            var data = new GameSaveData();
            data.SetMeta(DeathStateSave.SpiritMetaKey, "0");
            Assert.That(DeathStateSave.TryRead(data, out _), Is.False);
        }

        /// <summary>
        /// The corpse position is parsed with the invariant culture. On a machine with a comma
        /// decimal separator, "205.04" parsed under the local culture becomes 20504 — and the
        /// compass would point a hundred zones away.
        /// </summary>
        [Test]
        public void DeathStateSave_ParsesTheCorpsePosition_CultureInvariantly()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("es-ES");

                var data = new GameSaveData();
                data.SetMeta(DeathStateSave.SpiritMetaKey, "1");
                data.SetMeta(DeathStateSave.CorpseXMetaKey, "205.04");
                data.SetMeta(DeathStateSave.CorpseYMetaKey, "1.62");

                Assert.That(DeathStateSave.TryRead(data, out Vector3 corpse), Is.True);
                Assert.That(corpse.x, Is.EqualTo(205.04f).Within(0.001f));
            }
            finally
            {
                // Restored in a finally, always: a probe that leaves global state changed has
                // changed it for every test that follows, and the failure is invisible from the
                // fixture that reports it.
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
