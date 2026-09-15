using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.LightingEditor
{
    /// <summary>
    /// Pins <see cref="WorldLightLoader.CollectActiveLights"/>, the one thing the Lighting
    /// Editor's Alt overlay reads.
    ///
    /// The overlay could have been written against <c>ActiveLightObjects</c>, and it would have
    /// been silently wrong in the case it exists for. Two independent gates deactivate a light's
    /// GameObject without the light ceasing to exist — the day/night window and the viewport
    /// cull — so an enumeration that skipped inactive objects would blank the markers at noon and
    /// off screen. That is exactly when an author has nothing else to look at, and nothing would
    /// have failed: the overlay would simply have looked like a map with no lights on it.
    ///
    /// The radius is asserted for the same reason. It is read off the live Light2D rather than
    /// off the preset, so a per-instance override cannot make the drawn ring disagree with the
    /// light it claims to measure.
    /// </summary>
    [TestFixture]
    public class LightOutlineOverlayTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static readonly Type LoaderType = typeof(WorldLightLoader);
        private static Type InstanceType => LoaderType.GetNestedType("LightInstance", Any);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private WorldLightLoader _loader;
        private readonly List<WorldLightLoader.LightHandle> _buffer =
            new List<WorldLightLoader.LightHandle>();

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("OutlineOverlayTestLoader");
            _spawned.Add(go);
            _loader = go.AddComponent<WorldLightLoader>();
            _buffer.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
            _loader = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  The inactive case — the reason this API exists
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void CollectActiveLights_ReportsLightsWhoseGameObjectIsDeactivated()
        {
            var lit    = MakeSceneLight("Light_1_Torch", new Vector3(1f, 0f, 0f), radius: 3f);
            var gated  = MakeSceneLight("Light_2_Torch", new Vector3(9f, 0f, 0f), radius: 4f);
            AddInstance(1, "Torch", lit,   persistent: true);
            AddInstance(2, "Torch", gated, persistent: true);

            // What ApplyPointLightsVisibility does at dawn, and CullLightsByViewport does the
            // moment a light leaves the frame.
            gated.SetActive(false);

            _loader.CollectActiveLights(_buffer);

            Assert.AreEqual(2, _buffer.Count,
                "A deactivated light was dropped. The day/night gate and the viewport cull both " +
                "deactivate light objects, so the overlay would go blank at noon and off screen.");
            Assert.AreEqual(2, _buffer[1].Id);
            Assert.AreSame(gated, _buffer[1].Go);
        }

        [Test]
        public void CollectActiveLights_SkipsRecordsWhoseObjectIsGone()
        {
            var alive = MakeSceneLight("Light_1_Torch", Vector3.zero, radius: 2f);
            AddInstance(1, "Torch", alive, persistent: true);
            AddInstance(2, "Torch", null,  persistent: true);

            _loader.CollectActiveLights(_buffer);

            Assert.AreEqual(1, _buffer.Count,
                "A record with no GameObject has nothing to draw a ring around.");
            Assert.AreSame(alive, _buffer[0].Go);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  What each handle carries
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void CollectActiveLights_ReportsTheLiveOuterRadius()
        {
            var go = MakeSceneLight("Light_5_Lamp", Vector3.zero, radius: 7.25f);
            AddInstance(5, "Lamp", go, persistent: true);

            _loader.CollectActiveLights(_buffer);

            Assert.AreEqual(7.25f, _buffer[0].OuterRadius, 0.0001f,
                "The ring is sized from the live Light2D, so a per-instance radius override " +
                "moves the marker with the light rather than leaving it on the preset value.");
            Assert.AreEqual("Lamp", _buffer[0].PresetId);
            Assert.AreEqual(5, _buffer[0].Id);
        }

        [Test]
        public void CollectActiveLights_ReportsZeroRadius_WhenTheRecordHasNoLight2D()
        {
            var go = MakeSceneLight("Light_6_Broken", Vector3.zero, radius: null);
            AddInstance(6, "Broken", go, persistent: true);

            _loader.CollectActiveLights(_buffer);

            Assert.AreEqual(0f, _buffer[0].OuterRadius, 0.0001f,
                "A record with no Light2D must report 0 rather than throw; the overlay falls " +
                "back to its own default so a ring is still drawn.");
        }

        [Test]
        public void CollectActiveLights_DistinguishesDerivedFromAuthored()
        {
            var authored = MakeSceneLight("Light_1_Torch", Vector3.zero,          radius: 3f);
            var derived  = MakeSceneLight("Light_0_Lamp",  new Vector3(4f, 0f, 0f), radius: 3f);
            AddInstance(1, "Torch", authored, persistent: true);
            AddInstance(0, "Lamp",  derived,  persistent: false);

            _loader.CollectActiveLights(_buffer);

            Assert.IsTrue(_buffer[0].Persistent, "An authored light must report as persistent.");
            Assert.IsFalse(_buffer[1].Persistent,
                "A building-owned light must report as derived. The editor refuses every edit " +
                "on one, so the overlay paints it differently — a marker that looked identical " +
                "would promise an edit that cannot happen.");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Buffer contract — it is called every frame
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void CollectActiveLights_ClearsTheBufferFirst()
        {
            var go = MakeSceneLight("Light_1_Torch", Vector3.zero, radius: 3f);
            AddInstance(1, "Torch", go, persistent: true);

            _loader.CollectActiveLights(_buffer);
            _loader.CollectActiveLights(_buffer);

            Assert.AreEqual(1, _buffer.Count,
                "The buffer is reused every frame; not clearing it grows one marker per light " +
                "per frame until the pool is the whole session's history.");
        }

        [Test]
        public void CollectActiveLights_NullBufferIsIgnored()
        {
            Assert.DoesNotThrow(() => _loader.CollectActiveLights(null));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private GameObject MakeSceneLight(string name, Vector3 pos, float? radius)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            _spawned.Add(go);
            if (radius.HasValue)
            {
                var l = go.AddComponent<Light2D>();
                l.lightType = Light2D.LightType.Point;
                l.pointLightOuterRadius = radius.Value;
            }
            return go;
        }

        private IList Instances()
            => (IList)LoaderType.GetField("_activeLights", Any).GetValue(_loader);

        private void AddInstance(int id, string presetId, GameObject go, bool persistent)
        {
            object inst = Activator.CreateInstance(InstanceType);
            InstanceType.GetField("id").SetValue(inst, id);
            InstanceType.GetField("presetId").SetValue(inst, presetId);
            InstanceType.GetField("zone").SetValue(inst, "");
            InstanceType.GetField("go").SetValue(inst, go);
            InstanceType.GetField("persistent").SetValue(inst, persistent);
            if (go != null)
                InstanceType.GetField("light2D").SetValue(inst, go.GetComponent<Light2D>());
            Instances().Add(inst);
        }
    }
}
