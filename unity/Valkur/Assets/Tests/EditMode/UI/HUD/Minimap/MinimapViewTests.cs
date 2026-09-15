using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Minimap
{
    /// <summary>
    /// The view end to end, minus the GPU: what it draws, what it pins to the rim, and what it
    /// will name under the pointer. Built on real graphics with explicit rect sizes, because
    /// uGUI performs no layout in Edit Mode.
    /// </summary>
    public class MinimapViewTests
    {
        private const string Channel = "tests.minimap.view";
        private readonly List<Object> _made = new List<Object>();
        private MinimapView _view;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            var root = new GameObject("MapRoot", typeof(RectTransform));
            _made.Add(root);
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(176f, 176f);
            var map = root.AddComponent<RawImage>();
            var mat = new Material(MinimapStyle.ResolveShader(MinimapStyle.Active.compositeShader, "Valkur/UI/MinimapComposite"));
            _made.Add(mat);
            _view = new MinimapView(map, Layer(rt, "Under"), Layer(rt, "Glyphs"), Layer(rt, "Over"), mat, circle: true)
            {
                Centre = Vector2.zero,
                HalfHeightWorld = 24f,
            };
        }

        private MinimapQuadGraphic Layer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            return go.AddComponent<MinimapQuadGraphic>();
        }

        [TearDown]
        public void TearDown()
        {
            WorldMarkerBoard.Clear(Channel);
            MinimapWaypoint.Clear();
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private MinimapFrame Frame() => new MinimapFrame
        {
            Style = MinimapStyle.Active,
            HasPlayer = true,
            Player = Vector2.zero,
            Facing = Vector2.up,
            DayTint = Color.white,
            SightRadius = 16f,
        };

        [Test]
        public void AFarDestination_IsPinnedToTheRim_WithItsDistance()
        {
            MinimapWaypoint.Set(new Vector2(300f, 0f));
            var scene = new MinimapScene();
            scene.Collect(MinimapStyle.Active, 0f);
            var f = Frame();
            _view.Draw(in f, scene, null);

            Assert.AreEqual(1, _view.Pins.Count, "one pin for the one destination off the dial");
            var pin = _view.Pins[0];
            Assert.That(pin.Distance, Is.EqualTo(300f).Within(0.01f));
            Assert.That(pin.Local.magnitude, Is.LessThanOrEqualTo(_view.Radius), "the pin sits inside the rim");
            Assert.That(pin.Local.x, Is.GreaterThan(0f), "on the side the destination is on");
        }

        [Test]
        public void AGlyphUnderThePointer_IsNamed()
        {
            WorldMarkerBoard.Publish(Channel, new[] { new WorldMarker(new Vector2(6f, 0f), WorldMarkerKind.QuestTurnIn, "Gatita") });
            var scene = new MinimapScene();
            scene.Collect(MinimapStyle.Active, 0f);
            var f = Frame();
            _view.Draw(in f, scene, null);

            // A turn-in is drawn as a badge, lifted above its spot and bobbing; pick near it.
            Vector2 at = _view.WorldToLocal(new Vector2(6f, 0f));
            bool found = false;
            for (float dy = 0f; dy <= 16f && !found; dy += 1f)
                if (_view.TryPick(at + new Vector2(0f, dy), out var label, out _))
                {
                    found = true;
                    StringAssert.Contains("Entregar", label);
                    StringAssert.Contains("Gatita", label);
                }
            Assert.IsTrue(found, "the pointer over the mark names it");
            Assert.IsFalse(_view.TryPick(new Vector2(-70f, -70f), out _, out _), "empty ground names nothing");
        }
    }
}
