using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.WorldDrops;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// TTL / despawn-reason behaviour for <see cref="WorldPickup"/>:
    ///   • Ephemeral pickups (Initialize) never expose persistence metadata.
    ///   • Persistent pickups carry dropId / TTL and clamp negatives to 0.
    ///   • <see cref="WorldPickup.OnDestroyed"/> fires the right reason for
    ///     PickedUp / Expired / Manual / SceneUnload.
    ///
    /// EditMode tests can't actually advance Time.time, so we either: (a) verify
    /// the metadata wiring directly via reflection, or (b) trigger Update by
    /// making Time.time look elapsed via _spawnTime mutation.
    /// </summary>
    [TestFixture]
    public class WorldPickupTtlTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
            // Reset static event so leaked subscribers from a previous test
            // can't poison the next one.
            var evField = typeof(WorldPickup).GetField("OnDestroyed",
                BindingFlags.Static | BindingFlags.NonPublic);
            evField?.SetValue(null, null);
        }

        private ItemDefinition CreateItem(string id)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = id;
            def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _runtimeAssets.Add(def.icon);
            _runtimeAssets.Add(def);
            return def;
        }

        private WorldPickup BuildPickup(ItemDefinition def, Vector3 pos)
        {
            var p = DropSystem.BuildPickupShell(def, pos);
            _scene.Add(p.gameObject);
            return p;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void Ephemeral_Initialize_LeavesPersistenceFlagsOff()
        {
            LogAssert.ignoreFailingMessages = true;
            var def = CreateItem("ephemeral");
            var p = BuildPickup(def, Vector3.zero);
            p.Initialize(def, 2, Vector3.zero);

            Assert.IsFalse(p.IsPersistent);
            Assert.IsTrue(string.IsNullOrEmpty(p.DropId));
            Assert.IsTrue(p.IsInfiniteTtl);
            Assert.AreEqual(ItemDropSource.Unknown, p.Source);
        }

        [Test]
        public void Persistent_InitializeStoresMetadata()
        {
            LogAssert.ignoreFailingMessages = true;
            var def = CreateItem("persistent");
            var p = BuildPickup(def, new Vector3(3f, 4f, 0f));
            p.InitializePersistent(def, 5, new Vector3(3f, 4f, 0f),
                dropId: "drop-uuid", despawnTtlSeconds: 60f, createdAtUnixMs: 42L,
                zoneId: "zone-A", source: ItemDropSource.Editor);

            Assert.IsTrue(p.IsPersistent);
            Assert.AreEqual("drop-uuid", p.DropId);
            Assert.AreEqual(60f, p.DespawnTtlSeconds);
            Assert.AreEqual(42L, p.CreatedAtUnixMs);
            Assert.AreEqual("zone-A", p.ZoneId);
            Assert.AreEqual(ItemDropSource.Editor, p.Source);
            Assert.IsFalse(p.IsInfiniteTtl);
        }

        [Test]
        public void Persistent_NegativeTtlClampedToZero_ImpliesInfinite()
        {
            LogAssert.ignoreFailingMessages = true;
            var def = CreateItem("inf");
            var p = BuildPickup(def, Vector3.zero);
            p.InitializePersistent(def, 1, Vector3.zero, "id", -10f, 0L, "", ItemDropSource.Editor);
            Assert.AreEqual(0f, p.DespawnTtlSeconds);
            Assert.IsTrue(p.IsInfiniteTtl);
            Assert.AreEqual(float.PositiveInfinity, p.SecondsUntilExpiry);
        }

        // Read the private _pendingReason field — direct verification dodges the
        // global static event whose behaviour can vary based on test order.
        private static WorldPickup.DestructionReason ReadPendingReason(WorldPickup p)
        {
            var f = typeof(WorldPickup).GetField("_pendingReason",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Could not reflect _pendingReason field.");
            return (WorldPickup.DestructionReason)f.GetValue(p);
        }

        [Test]
        public void TryPickup_StampsPickedUpReasonOnPickup()
        {
            LogAssert.ignoreFailingMessages = true;
            var player = new GameObject("Player");
            _scene.Add(player);
            player.tag = "Player";
            player.AddComponent<Valkur.Gameplay.Inventory.Inventory>();

            var def = CreateItem("torch");
            def.stackable = true;
            def.maxStack = 99;
            var p = BuildPickup(def, Vector3.zero);
            p.InitializePersistent(def, 1, Vector3.zero, "torch-1", 0f, 0L, "", ItemDropSource.Editor);

            Assert.IsTrue(p.TryPickup(player), "Player must succeed at picking up the torch.");
            Assert.AreEqual(WorldPickup.DestructionReason.PickedUp, ReadPendingReason(p),
                "TryPickup must stamp the destruction reason before requesting Destroy().");
        }

        [Test]
        public void SetWorldPosition_UpdatesTransformAndBobBaseline()
        {
            // The bob animation in WorldPickup.Update() recomputes Y from the
            // private _baseY field every frame. Mutating only transform.position
            // would let the next bob frame snap Y back to the spawn baseline —
            // exactly what broke RMB drag-to-move before this method existed.
            LogAssert.ignoreFailingMessages = true;
            var def = CreateItem("rune");
            var p = BuildPickup(def, Vector3.zero);
            p.InitializePersistent(def, 1, Vector3.zero, "rune-1", 0f, 0L, "", ItemDropSource.Editor);

            p.SetWorldPosition(new Vector3(7f, 9f, 0f));
            Assert.AreEqual(new Vector3(7f, 9f, 0f), p.transform.position);

            var baseY = typeof(WorldPickup).GetField("_baseY",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(baseY);
            Assert.AreEqual(9f, (float)baseY.GetValue(p), 0.0001f,
                "_baseY must follow the new world position so the bob doesn't fight the move.");
        }

        [Test]
        public void MarkManualDelete_StampsManualReason()
        {
            LogAssert.ignoreFailingMessages = true;
            var def = CreateItem("staff");
            var p = BuildPickup(def, Vector3.zero);
            p.InitializePersistent(def, 1, Vector3.zero, "staff-1", 0f, 0L, "", ItemDropSource.Editor);

            Assert.AreEqual(WorldPickup.DestructionReason.SceneUnload, ReadPendingReason(p),
                "Default reason must be SceneUnload until something marks otherwise.");

            p.MarkManualDelete();

            Assert.AreEqual(WorldPickup.DestructionReason.Manual, ReadPendingReason(p));
        }

        // Note: end-to-end coverage of OnDestroyed event flow lives in
        // ItemDropServiceTests.RemoveByDropId_DropsFromCacheAndKillsLivePickup,
        // which proves the event actually wires through to the persistence
        // service. We don't duplicate that here because the static-event
        // backing field can be poisoned by other test fixtures.
    }
}
