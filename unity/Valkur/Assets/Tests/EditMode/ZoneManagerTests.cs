using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    public class ZoneManagerTests
    {
        private ZoneManager CreateZoneManager()
        {
            var go = new GameObject("ZoneManager");
            var zm = go.AddComponent<ZoneManager>();
            return zm;
        }

        private void Cleanup(ZoneManager zm)
        {
            Object.DestroyImmediate(zm.gameObject);
        }

        [Test]
        public void DetectZone_NoZonesDefined_ReturnsCurrent()
        {
            var zm = CreateZoneManager();
            // No zones configured — should return the default currentZone
            string result = zm.DetectZone(new Vector2(5f, 5f));
            Assert.AreEqual("Lobby", result);
            Cleanup(zm);
        }

        [Test]
        public void CurrentZone_DefaultIsLobby()
        {
            var zm = CreateZoneManager();
            Assert.AreEqual("Lobby", zm.CurrentZone);
            Cleanup(zm);
        }

        [Test]
        public void GetZoneCenter_UnknownZone_ReturnsZero()
        {
            var zm = CreateZoneManager();
            Vector2 center = zm.GetZoneCenter("NonExistent");
            Assert.AreEqual(Vector2.zero, center);
            Cleanup(zm);
        }

        [Test]
        public void TryGetZone_UnknownZone_ReturnsFalse()
        {
            var zm = CreateZoneManager();
            bool found = zm.TryGetZone("NonExistent", out _);
            Assert.IsFalse(found);
            Cleanup(zm);
        }

        [Test]
        public void AddZone_ThenTryGetZone_ReturnsTrue()
        {
            var zm = CreateZoneManager();

            bool created = zm.AddZone("zone_test", new Vector2Int(10, 20), editableInTileEditor: true);
            bool found = zm.TryGetZone("zone_test", out var zone);

            Assert.IsTrue(created);
            Assert.IsTrue(found);
            Assert.AreEqual(new Vector2Int(10, 20), zone.gridOffset);
            Assert.IsTrue(zone.editableInTileEditor);
            Cleanup(zm);
        }

        [Test]
        public void RenameZone_UpdatesLookup()
        {
            var zm = CreateZoneManager();
            zm.AddZone("zone_old", Vector2Int.zero, editableInTileEditor: true);

            bool renamed = zm.RenameZone("zone_old", "zone_new");

            Assert.IsTrue(renamed);
            Assert.IsFalse(zm.TryGetZone("zone_old", out _));
            Assert.IsTrue(zm.TryGetZone("zone_new", out _));
            Cleanup(zm);
        }

        [Test]
        public void SetZoneEditable_False_BlocksTileEditCheck()
        {
            var zm = CreateZoneManager();
            zm.AddZone("zone_lock", Vector2Int.zero, editableInTileEditor: true);

            bool changed = zm.SetZoneEditable("zone_lock", false);
            bool canEdit = zm.IsTileInEditableZone(new Vector3Int(0, 0, 0));

            Assert.IsTrue(changed);
            Assert.IsFalse(canEdit);
            Cleanup(zm);
        }

        [Test]
        public void MoveZone_ShiftsDetectionArea()
        {
            var zm = CreateZoneManager();
            zm.AddZone("zone_move", Vector2Int.zero, editableInTileEditor: true);

            bool moved = zm.MoveZone("zone_move", new Vector2Int(50, 0));
            bool foundOldPos = zm.TryGetZoneAtTile(Vector2Int.zero, out _);
            bool foundNewPos = zm.TryGetZoneAtTile(new Vector2Int(50, 0), out var zone);

            Assert.IsTrue(moved);
            Assert.IsFalse(foundOldPos);
            Assert.IsTrue(foundNewPos);
            Assert.AreEqual("zone_move", zone.zoneName);
            Cleanup(zm);
        }
    }
}
