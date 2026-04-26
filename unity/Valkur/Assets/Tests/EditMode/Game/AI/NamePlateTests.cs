using NUnit.Framework;
using UnityEngine;
using TMPro;
using Valkur.UI;

namespace Valkur.Tests.EditMode.Game.AI
{
    public class NamePlateTests
    {
        private NamePlate CreateNamePlate()
        {
            var go = new GameObject("TestNPC");
            go.AddComponent<SpriteRenderer>();
            return go.AddComponent<NamePlate>();
        }

        private void Cleanup(NamePlate np)
        {
            Object.DestroyImmediate(np.gameObject);
        }

        [Test]
        public void Initialize_SetsText()
        {
            var np = CreateNamePlate();
            np.Initialize("TestMonster", "EVIL");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            Assert.IsNotNull(tmp);
            Assert.AreEqual("TestMonster", tmp.text);
            Cleanup(np);
        }

        [Test]
        public void Initialize_EvilFaction_SetsRedColor()
        {
            var np = CreateNamePlate();
            np.Initialize("Goblin", "EVIL");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            // EVIL = (255, 80, 80) / 255
            Assert.AreEqual(1f, tmp.color.r, 0.01f);
            Assert.AreEqual(80f / 255f, tmp.color.g, 0.01f);
            Assert.AreEqual(80f / 255f, tmp.color.b, 0.01f);
            Cleanup(np);
        }

        [Test]
        public void Initialize_GoodFaction_SetsBlueColor()
        {
            var np = CreateNamePlate();
            np.Initialize("Guard", "GOOD");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            Assert.AreEqual(90f / 255f, tmp.color.r, 0.01f);
            Assert.AreEqual(160f / 255f, tmp.color.g, 0.01f);
            Assert.AreEqual(1f, tmp.color.b, 0.01f);
            Cleanup(np);
        }

        [Test]
        public void Initialize_NeutralFaction_SetsWhiteishColor()
        {
            var np = CreateNamePlate();
            np.Initialize("Villager", "NEUTRAL");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            Assert.AreEqual(245f / 255f, tmp.color.r, 0.01f);
            Assert.AreEqual(245f / 255f, tmp.color.g, 0.01f);
            Assert.AreEqual(245f / 255f, tmp.color.b, 0.01f);
            Cleanup(np);
        }

        [Test]
        public void Initialize_EmptyFaction_DefaultsToNeutral()
        {
            var np = CreateNamePlate();
            np.Initialize("Unknown", "");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            Assert.AreEqual(245f / 255f, tmp.color.r, 0.01f);
            Cleanup(np);
        }

        [Test]
        public void Initialize_NullName_UsesGameObjectName()
        {
            var np = CreateNamePlate();
            np.Initialize(null, "EVIL");
            var tmp = np.GetComponentInChildren<TextMeshPro>();
            Assert.AreEqual("TestNPC", tmp.text);
            Cleanup(np);
        }
    }
}
