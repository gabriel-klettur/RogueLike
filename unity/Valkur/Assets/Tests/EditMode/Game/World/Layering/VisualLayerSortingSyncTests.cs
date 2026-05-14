using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Pin the per-visual-layer → SpriteRenderer.sortingLayerName mapping owned by
    /// <see cref="VisualLayerSortingSync"/>. The mapping is the only thing that keeps
    /// an elevated player (e.g. <c>CurrentVisualLayer = 8</c>) from rendering BEHIND
    /// the tilemap layers they have logically climbed onto — a regression here
    /// reverts the symptom that prompted this feature (player on layer 8 was drawn
    /// underneath Decorations / WallsTop / ObjectsHigh / Overhead tiles).
    /// </summary>
    [TestFixture]
    public class VisualLayerSortingSyncTests
    {
        private GameObject _host;
        private VisualLayerOccupant _occ;
        private SpriteRenderer _sr;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SortingSyncHost");
            _occ = _host.AddComponent<VisualLayerOccupant>();
            _sr = _host.AddComponent<SpriteRenderer>();
            var sync = _host.AddComponent<VisualLayerSortingSync>();
            // EditMode does not invoke Awake/OnEnable on AddComponent reliably —
            // force them so the sync subscribes to OnLayerChanged AND snaps the
            // initial sorting layer to match the occupant's CurrentVisualLayer.
            ForceLifecycle(sync);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        private static void ForceLifecycle(VisualLayerSortingSync sync)
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(VisualLayerSortingSync).GetMethod("Awake", Flags)?.Invoke(sync, null);
            typeof(VisualLayerSortingSync).GetMethod("OnEnable", Flags)?.Invoke(sync, null);
        }

        [Test]
        public void DefaultLayer_PlacesPlayerOn_EntitiesSortingLayer()
        {
            // Layer 0 (Ground) → "Entities" — preserves the historical default so
            // existing zones that never elevate the player render identically.
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES, _sr.sortingLayerName,
                "Default visual layer (0 = Ground) must place the renderer on " +
                "'Entities' so the project-wide YSort baseline keeps working.");
        }

        [Test]
        public void Layers0To4_StayOn_EntitiesSortingLayer()
        {
            // The first half of the layer enum (Ground … WallsBottom) all sit BELOW
            // the existing "Entities" sortingLayer in TagManager order, so the
            // historical Entities placement is already correct for them.
            for (int layer = 0; layer <= 4; layer++)
            {
                _occ.SetVisualLayer(layer);
                Assert.AreEqual(SortingConfig.LAYER_ENTITIES, _sr.sortingLayerName,
                    $"VisualLayer {layer} must remain on '{SortingConfig.LAYER_ENTITIES}' — " +
                    $"this is the historical default for low layers.");
            }
        }

        [Test]
        public void Layer5_Decorations_PlacesPlayerOn_WallsTop()
        {
            _occ.SetVisualLayer(5);
            Assert.AreEqual(SortingConfig.LAYER_WALLS_TOP, _sr.sortingLayerName,
                "Layer 5 (Decorations) must put the player on the next sortingLayer " +
                "above Decorations ('WallsTop'), so the player draws on top of " +
                "Decorations tiles painted at their cell.");
        }

        [Test]
        public void Layer6_WallsTop_PlacesPlayerOn_ObjectsHigh()
        {
            _occ.SetVisualLayer(6);
            Assert.AreEqual(SortingConfig.LAYER_OBJECTS_HIGH, _sr.sortingLayerName);
        }

        [Test]
        public void Layer7_ObjectsHigh_PlacesPlayerOn_Projectiles()
        {
            _occ.SetVisualLayer(7);
            Assert.AreEqual(SortingConfig.LAYER_PROJECTILES, _sr.sortingLayerName);
        }

        [Test]
        public void Layer8_OverheadDetails_PlacesPlayerOn_EntitiesOverhead()
        {
            // THE bug this feature fixes: when the player's CurrentVisualLayer is 8,
            // the renderer must move to "EntitiesOverhead" (the sortingLayer added
            // specifically for this purpose). Otherwise the player draws BEHIND
            // every painted Decorations / WallsTop / ObjectsHigh / Overhead tile.
            _occ.SetVisualLayer(8);
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD, _sr.sortingLayerName,
                "Layer 8 (OverheadDetails) MUST use the new 'EntitiesOverhead' " +
                "sortingLayer so the elevated player renders above every tilemap.");
        }

        [Test]
        public void SortingLayerName_Updates_OnEveryTransition()
        {
            // The contract is "follow the occupant's events" — a stale renderer state
            // after a SetVisualLayer call is the failure mode the user reported.
            _occ.SetVisualLayer(8);
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD, _sr.sortingLayerName);

            _occ.SetVisualLayer(0);
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES, _sr.sortingLayerName,
                "Returning to layer 0 must move the renderer back to Entities.");

            _occ.SetVisualLayer(5);
            Assert.AreEqual(SortingConfig.LAYER_WALLS_TOP, _sr.sortingLayerName);
        }

        [Test]
        public void NewlyEnabled_SnapsRendererTo_CurrentLayer()
        {
            // Mirrors VisualLayerColliderSync.OnEnable: snapping on Enable is what
            // lets a saved-and-restored elevated entity be visually correct from
            // its first frame, without waiting for a layer-change event.
            var go = new GameObject("LateEnableHost");
            try
            {
                var occ = go.AddComponent<VisualLayerOccupant>();
                occ.SetVisualLayer(8);
                var sr = go.AddComponent<SpriteRenderer>();
                var sync = go.AddComponent<VisualLayerSortingSync>();
                ForceLifecycle(sync);

                Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD, sr.sortingLayerName,
                    "OnEnable must apply the current visual layer immediately — " +
                    "otherwise restored-save players spawn on the wrong sortingLayer.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ════════════════════════════════════════════════════════════════════
        // RUNTIME INVARIANTS — the contract that actually matters at render time
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// THE root invariant of the whole feature: for every visual layer N where a
        /// tile is rendered, the player's sortingLayer must rank STRICTLY ABOVE the
        /// tile sortingLayer in Unity's runtime <see cref="SortingLayer.layers"/>
        /// order. If this fails the player draws under tiles even though the name
        /// mapping looks correct — the symptom the user originally reported.
        ///
        /// Captures TagManager.asset reorders, mapping table edits and the historical
        /// "we have the right names but they're in the wrong slots" failure mode the
        /// integrity suite already guards for physics layers.
        /// </summary>
        [Test]
        public void EveryVisualLayer_PlayerSortingLayer_RanksStrictlyAbove_TileSortingLayer()
        {
            // (visualLayerIdx, tileSortingLayerName) — must match
            // TilemapLayerSetup.ApplyLayerSettings. Layer 2 (Collision) is excluded
            // because its renderer is disabled so depth doesn't apply.
            var tileLayers = new (int idx, string tileSortingLayer)[]
            {
                (0, SortingConfig.LAYER_GROUND),
                (1, SortingConfig.LAYER_FLOOR_DECALS),
                (3, SortingConfig.LAYER_OBJECTS_LOW),
                (4, SortingConfig.LAYER_WALLS_BOTTOM),
                (5, SortingConfig.LAYER_DECORATIONS),
                (6, SortingConfig.LAYER_WALLS_TOP),
                (7, SortingConfig.LAYER_OBJECTS_HIGH),
                (8, SortingConfig.LAYER_OVERHEAD),
            };

            foreach (var (idx, tileLayer) in tileLayers)
            {
                _occ.SetVisualLayer(idx);
                string playerLayer = _sr.sortingLayerName;

                int playerOrder = LayerOrderInTagManager(playerLayer);
                int tileOrder   = LayerOrderInTagManager(tileLayer);

                Assert.Greater(playerOrder, tileOrder,
                    $"VisualLayer {idx}: player sortingLayer '{playerLayer}' (slot " +
                    $"{playerOrder}) must rank STRICTLY ABOVE the tile sortingLayer " +
                    $"'{tileLayer}' (slot {tileOrder}) in TagManager. If this fails the " +
                    $"player draws UNDER tiles at this visual layer.");
            }
        }

        /// <summary>
        /// Companion invariant for <c>EntitiesOverhead</c>: it must sit BELOW UI_World
        /// so in-world UI elements (health bars, mana bars on the player) keep
        /// rendering above the player sprite even when the player is on layer 8.
        /// Otherwise we'd ship a regression where the player covers their own HUD.
        /// </summary>
        [Test]
        public void EntitiesOverhead_RanksBelow_UIWorld_AndAbove_Overhead()
        {
            int overheadOrder         = LayerOrderInTagManager(SortingConfig.LAYER_OVERHEAD);
            int entitiesOverheadOrder = LayerOrderInTagManager(SortingConfig.LAYER_ENTITIES_OVERHEAD);
            int uiWorldOrder          = LayerOrderInTagManager(SortingConfig.LAYER_UI_WORLD);

            Assert.Greater(entitiesOverheadOrder, overheadOrder,
                "EntitiesOverhead must rank above Overhead (the layer 8 tile sortingLayer).");
            Assert.Less(entitiesOverheadOrder, uiWorldOrder,
                "EntitiesOverhead must rank below UI_World so in-world HUD elements " +
                "(WorldHealthBar, WorldManaBar, WorldDashBar) keep rendering above " +
                "the elevated player sprite.");
        }

        [Test]
        public void SortingConfig_LAYER_ENTITIES_OVERHEAD_Constant_MatchesTagManagerEntry()
        {
            // Catches a typo in the constant. SortingLayer.NameToID returns a non-zero
            // value only when the name exists in TagManager.
            Assert.AreEqual("EntitiesOverhead", SortingConfig.LAYER_ENTITIES_OVERHEAD,
                "Constant must match the literal sortingLayer name added to TagManager.");
            Assert.IsTrue(
                SortingLayer.layers.Any(l => l.name == SortingConfig.LAYER_ENTITIES_OVERHEAD),
                "SortingLayer.layers must contain 'EntitiesOverhead' — required by " +
                "VisualLayerSortingSync's layer 8 mapping. Add it via " +
                "Edit > Project Settings > Tags & Layers if missing.");
        }

        // ════════════════════════════════════════════════════════════════════
        // ROBUSTNESS — defensive contracts
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Out-of-range values must clamp instead of indexing past the mapping
        /// table. Negative inputs come from defensive code paths (save-restore
        /// receiving corrupt JSON), values ≥ 9 come from future enum additions
        /// that haven't been wired into the mapping yet.
        /// </summary>
        [Test]
        public void OutOfRangeVisualLayer_ClampsTo_NearestValidSortingLayer()
        {
            // VisualLayerOccupant clamps to [0..8] itself, so we hit the sortingsync
            // clamp by talking directly to its private apply method (no occupant
            // change → no event → must call the private API). The clamp guards
            // against any future caller that bypasses the occupant.
            var apply = typeof(VisualLayerSortingSync).GetMethod(
                "ApplySortingLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(apply, "Private ApplySortingLayer must exist.");

            var sync = _host.GetComponent<VisualLayerSortingSync>();
            apply.Invoke(sync, new object[] { -5 });
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES, _sr.sortingLayerName,
                "Negative visual layer must clamp to slot 0 (Entities), not throw.");

            apply.Invoke(sync, new object[] { 99 });
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD, _sr.sortingLayerName,
                "Above-range visual layer must clamp to slot 8 (EntitiesOverhead).");
        }

        /// <summary>
        /// OnEnable subscribes, OnDisable unsubscribes — symmetric. Without the
        /// unsubscribe a destroyed sync would still receive events from a long-lived
        /// occupant (boss / persistent NPC), leaking and eventually NRE'ing the
        /// renderer-of-a-destroyed-GO.
        /// </summary>
        [Test]
        public void OnDisable_Unsubscribes_FromOccupantEvent()
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var sync = _host.GetComponent<VisualLayerSortingSync>();

            typeof(VisualLayerSortingSync).GetMethod("OnDisable", Flags)
                ?.Invoke(sync, null);

            // After OnDisable, mutating the occupant must NOT touch the renderer.
            string before = _sr.sortingLayerName;
            _occ.SetVisualLayer(8);
            Assert.AreEqual(before, _sr.sortingLayerName,
                "After OnDisable, SetVisualLayer must not update the sortingLayer — " +
                "indicates the OnLayerChanged subscription leaked across the disable.");
        }

        /// <summary>
        /// Re-enabling re-subscribes — covers the "disable + enable to swap colliders"
        /// pattern that the broader VisualLayer system uses for equipment swaps.
        /// </summary>
        [Test]
        public void OnEnable_AfterOnDisable_RestoresSubscription()
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var sync = _host.GetComponent<VisualLayerSortingSync>();

            typeof(VisualLayerSortingSync).GetMethod("OnDisable", Flags)?.Invoke(sync, null);
            typeof(VisualLayerSortingSync).GetMethod("OnEnable", Flags)?.Invoke(sync, null);

            _occ.SetVisualLayer(7);
            Assert.AreEqual(SortingConfig.LAYER_PROJECTILES, _sr.sortingLayerName,
                "OnEnable must re-subscribe so SetVisualLayer events resume updating " +
                "the renderer.");
        }

        /// <summary>
        /// One subscription per OnEnable, regardless of how many times it fires
        /// — the canonical "+= without paired -=" leak protection. A double-fire
        /// would still produce the right end state here (idempotent assignment),
        /// but it would also leak callbacks on every disable/enable cycle.
        /// </summary>
        [Test]
        public void RepeatedOnEnable_DoesNotDoubleSubscribe()
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var sync = _host.GetComponent<VisualLayerSortingSync>();
            var onEnable = typeof(VisualLayerSortingSync).GetMethod("OnEnable", Flags);

            // Count how many handlers the event has BEFORE — extracted via reflection
            // on the private OnLayerChanged backing delegate.
            int before = CountOnLayerChangedSubscribers(_occ);

            // Re-firing OnEnable without a matching OnDisable would double-subscribe
            // a naive implementation. The component must guard against this (or use
            // the "-= then +=" idiom).
            onEnable?.Invoke(sync, null);
            onEnable?.Invoke(sync, null);

            int after = CountOnLayerChangedSubscribers(_occ);
            Assert.LessOrEqual(after - before, 1,
                "OnEnable invoked multiple times without paired OnDisable must not " +
                "add multiple handlers to OnLayerChanged. Found " +
                $"{after - before} extra subscriber(s).");
        }

        // ════════════════════════════════════════════════════════════════════
        // INTEGRATION — PlayerController auto-wiring
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// PlayerController declares <see cref="VisualLayerSortingSync"/> via
        /// <see cref="UnityEngine.RequireComponent"/>. Adding the controller to a
        /// fresh GameObject must auto-mount the sync component — otherwise every
        /// player spawn would have to remember to add it manually.
        /// </summary>
        [Test]
        public void PlayerController_RequireComponent_AutoMounts_VisualLayerSortingSync()
        {
            var go = new GameObject("PlayerRequireCheck");
            try
            {
                go.AddComponent<Valkur.Gameplay.PlayerController>();
                Assert.IsNotNull(go.GetComponent<VisualLayerSortingSync>(),
                    "PlayerController must auto-mount VisualLayerSortingSync via " +
                    "RequireComponent — otherwise elevated-layer players would not " +
                    "remap their sortingLayer.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// The two siblings sharing the same OnLayerChanged subscription must both
        /// react to a SetVisualLayer call — collider includeLayers AND sortingLayer
        /// flip together. A previous bug shipped only one and produced "you collide
        /// with the right layer but render under it" or the inverse.
        /// </summary>
        [Test]
        public void ColliderSync_And_SortingSync_Both_RespondTo_SameLayerChange()
        {
            var go = new GameObject("BothSyncsHost");
            try
            {
                var occ = go.AddComponent<VisualLayerOccupant>();
                var col = go.AddComponent<BoxCollider2D>();
                go.AddComponent<SpriteRenderer>();
                var collSync = go.AddComponent<VisualLayerColliderSync>();
                var sortSync = go.AddComponent<VisualLayerSortingSync>();

                const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(VisualLayerColliderSync).GetMethod("Awake", Flags)?.Invoke(collSync, null);
                typeof(VisualLayerColliderSync).GetMethod("OnEnable", Flags)?.Invoke(collSync, null);
                ForceLifecycle(sortSync);

                occ.SetVisualLayer(8);

                Assert.AreEqual(SortingConfig.LAYER_ENTITIES_OVERHEAD,
                    go.GetComponent<SpriteRenderer>().sortingLayerName,
                    "SortingSync must react to the layer change.");
                // ColliderSync writes includeLayers. Any non-zero mask proves the
                // event reached it; exact mask is the ColliderSync's own contract.
                Assert.AreNotEqual(0, col.includeLayers.value,
                    "ColliderSync must also have reacted (includeLayers must be non-empty).");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Order-of-rendering index for <paramref name="sortingLayerName"/> in
        /// Unity's runtime <see cref="SortingLayer.layers"/> table (= position in
        /// TagManager.asset). Higher value = renders LATER = renders ON TOP.
        /// Asserts when the name is unknown so a typo fails the test rather than
        /// silently passing.
        /// </summary>
        private static int LayerOrderInTagManager(string sortingLayerName)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == sortingLayerName) return i;
            Assert.Fail($"SortingLayer '{sortingLayerName}' not registered in TagManager.");
            return -1;
        }

        /// <summary>Count handlers attached to <see cref="VisualLayerOccupant.OnLayerChanged"/>.</summary>
        private static int CountOnLayerChangedSubscribers(VisualLayerOccupant occ)
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(VisualLayerOccupant).GetField("OnLayerChanged", Flags);
            if (field == null) return 0;
            var del = field.GetValue(occ) as System.Delegate;
            return del?.GetInvocationList().Length ?? 0;
        }
    }
}
