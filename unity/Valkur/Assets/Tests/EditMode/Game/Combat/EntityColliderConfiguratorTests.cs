using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
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
        public void ConfigureNpcBodyCollider_CreatesCenteredSquareAtHalfVisualSize()
        {
            var npc = CreateNpc("NPC");
            npc.transform.localScale = new Vector3(2f, 2f, 1f);
            var renderer = npc.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSprite("wide", 64, 32, 32f);
            npc.AddComponent<CircleCollider2D>().radius = 3f;

            var box = EntityColliderConfigurator.ConfigureNpcBodyCollider(npc, renderer);

            Assert.IsNotNull(box);
            Assert.IsNull(npc.GetComponent<CircleCollider2D>(), "Legacy circle colliders must be removed from NPC roots.");
            Assert.IsFalse(box.isTrigger);
            Assert.AreEqual(0f, box.offset.x, 0.001f);
            Assert.AreEqual(0f, box.offset.y, 0.001f);
            Assert.AreEqual(0.5f, box.size.x, 0.001f);
            Assert.AreEqual(0.5f, box.size.y, 0.001f);
            Assert.AreEqual(1f, box.bounds.size.x, 0.001f, "World body width must be 50% of the smaller visual axis.");
            Assert.AreEqual(1f, box.bounds.size.y, 0.001f, "World body height must stay square for cheap NPC physics.");
        }

        [Test]
        public void ConfigureNpcBodyCollider_UsesVisualCenterForOffset()
        {
            var npc = CreateNpc("NPC");
            var spriteChild = new GameObject("Sprite");
            _createdObjects.Add(spriteChild);
            spriteChild.transform.SetParent(npc.transform, false);
            spriteChild.transform.localPosition = new Vector3(0.25f, 0.75f, 0f);

            var renderer = spriteChild.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSprite("centered", 32, 32, 32f);

            var box = EntityColliderConfigurator.ConfigureNpcBodyCollider(npc, renderer);

            Assert.AreEqual(0.25f, box.offset.x, 0.001f);
            Assert.AreEqual(0.75f, box.offset.y, 0.001f);
            Assert.AreEqual(0.5f, box.size.x, 0.001f);
            Assert.AreEqual(0.5f, box.size.y, 0.001f);
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
        public void GetBodyCollider_PrefersConfiguredBoxCollider()
        {
            var npc = CreateNpc("NPC");
            var circle = npc.AddComponent<CircleCollider2D>();
            circle.enabled = false;
            var box = npc.AddComponent<BoxCollider2D>();
            box.isTrigger = false;

            var body = EntityColliderConfigurator.GetBodyCollider(npc);

            Assert.AreSame(box, body);
        }

        private GameObject CreateNpc(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        private Sprite CreateSprite(string name, int width, int height, float pixelsPerUnit)
        {
            var texture = new Texture2D(width, height);
            texture.name = name + "_texture";
            texture.filterMode = FilterMode.Point;
            _createdObjects.Add(texture);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            sprite.name = name;
            _createdObjects.Add(sprite);
            return sprite;
        }
    }
}
