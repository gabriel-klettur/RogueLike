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
    }
}
