using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The footprint an NPC stands on. It replaced one centred square half the sprite's shorter
    /// side, which collided with walls at chest height and was the only thing a spell could hit.
    /// </summary>
    public class EntityColliderConfiguratorTests
    {
        private readonly List<Object> _createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                    Object.DestroyImmediate(_createdObjects[i]);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void Footprint_IsAHorizontalCapsuleAtTheFeet_AndReplacesLegacyColliders()
        {
            var npc = CreateNpc("NPC");
            var renderer = npc.AddComponent<SpriteRenderer>();
            // 1.25 x 2 units, pivot at the feet: the shape of every shipped character.
            renderer.sprite = CreateSprite("tall", 40, 64, 32f, new Vector2(0.5f, 0f));
            npc.AddComponent<CircleCollider2D>().radius = 3f;

            var footprint = EntityColliderConfigurator.ConfigureNpcFootprint(npc, renderer);

            Assert.IsNotNull(footprint);
            Assert.IsNull(npc.GetComponent<CircleCollider2D>(), "Legacy colliders must be removed from NPC roots.");
            Assert.IsFalse(footprint.isTrigger);
            Assert.AreEqual(CapsuleDirection2D.Horizontal, footprint.direction);
            Assert.AreEqual(Vector2.zero, footprint.offset, "The footprint sits on the pivot, which is the feet.");

            Vector2 expected = EntityCollisionProfile.AutoFootprintSize(1.25f, 2f);
            Assert.AreEqual(expected.x, footprint.size.x, 0.001f);
            Assert.AreEqual(expected.y, footprint.size.y, 0.001f);
            Assert.Less(footprint.bounds.max.y, 0.2f, "A footprint must stay at the feet, never at chest height.");
        }

        [Test]
        public void Footprint_HonoursTheAuthoredSize_ScaledByTheEntity()
        {
            var npc = CreateNpc("NPC");
            npc.transform.localScale = new Vector3(2f, 2f, 1f);
            var renderer = npc.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSprite("any", 32, 32, 32f, new Vector2(0.5f, 0f));
            var profile = new EntityCollisionProfile { footprintSize = new Vector2(1.2f, 0.4f), footprintOffset = new Vector2(0f, 0.1f) };

            var footprint = EntityColliderConfigurator.ConfigureNpcFootprint(npc, renderer, profile);

            Assert.AreEqual(1.2f, footprint.size.x, 0.001f, "Local size is the authored size; the root scale doubles it in the world.");
            Assert.AreEqual(0.4f, footprint.size.y, 0.001f);
            Assert.AreEqual(2.4f, footprint.bounds.size.x, 0.01f);
            Assert.AreEqual(0.1f, footprint.offset.y, 0.001f);
        }

        [Test]
        public void AutoFootprint_MatchesThePlayersHandTunedBox_OnTheShippedBody()
        {
            // The player prefab's box is 0.5 x 0.3, tuned by hand for the 1.22 x 1.86 dwarf.
            Vector2 size = EntityCollisionProfile.AutoFootprintSize(1.22f, 1.86f);
            Assert.That(size.x, Is.InRange(0.45f, 0.65f));
            Assert.That(size.y, Is.InRange(0.2f, 0.32f));
        }

        [Test]
        public void ApplyLayerRecursively_SetsVisualChildrenToNpcLayer()
        {
            var npc = CreateNpc("NPC");
            var child = new GameObject("Visual");
            _createdObjects.Add(child);
            child.transform.SetParent(npc.transform, false);

            EntityColliderConfigurator.ApplyLayerRecursively(npc, 9);

            Assert.AreEqual(9, npc.layer);
            Assert.AreEqual(9, child.layer);
        }

        [Test]
        public void GetBodyCollider_PrefersTheRigFootprint_NeverAHurtbox()
        {
            var npc = CreateNpc("NPC");
            var renderer = npc.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSprite("tall", 40, 64, 32f, new Vector2(0.5f, 0f));
            var footprint = EntityColliderConfigurator.ConfigureNpcFootprint(npc, renderer);
            var rig = EntityColliderConfigurator.InstallRig(npc, null, renderer, footprint);

            Assert.Greater(rig.HurtboxCount, 0);
            Assert.AreSame(footprint, EntityColliderConfigurator.GetBodyCollider(npc));
        }

        [Test]
        public void GetBodyCollider_FallsBackToTheFirstSolidRootCollider()
        {
            var npc = CreateNpc("NPC");
            var circle = npc.AddComponent<CircleCollider2D>();
            circle.enabled = false;
            var box = npc.AddComponent<BoxCollider2D>();
            box.isTrigger = false;

            Assert.AreSame(box, EntityColliderConfigurator.GetBodyCollider(npc));
        }

        private GameObject CreateNpc(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        private Sprite CreateSprite(string name, int width, int height, float pixelsPerUnit, Vector2 pivot)
        {
            var texture = new Texture2D(width, height);
            texture.name = name + "_texture";
            texture.filterMode = FilterMode.Point;
            _createdObjects.Add(texture);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, pixelsPerUnit);
            sprite.name = name;
            _createdObjects.Add(sprite);
            return sprite;
        }
    }
}
