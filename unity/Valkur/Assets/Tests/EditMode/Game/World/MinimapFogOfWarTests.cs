using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.World
{
    public class MinimapFogOfWarTests
    {
        [SetUp]
        public void Ignore() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void RevealAround_MarksNearbyCellsExplored()
        {
            var go = new GameObject("Minimap", typeof(RectTransform));
            var raw = go.AddComponent<RawImage>();
            var mm = go.AddComponent<MinimapManager>();
            typeof(MinimapManager).GetField("rawImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(mm, raw);

            mm.RevealAround(new Vector2(0f, 0f), 3f);
            Assert.IsTrue(mm.IsExplored(new Vector2(0f, 0f)));
            Assert.IsTrue(mm.IsExplored(new Vector2(2.5f, 0f)));
            Assert.IsFalse(mm.IsExplored(new Vector2(50f, 0f)));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ClearFog_ForgetsExplored()
        {
            var go = new GameObject("Minimap", typeof(RectTransform));
            var raw = go.AddComponent<RawImage>();
            var mm = go.AddComponent<MinimapManager>();
            typeof(MinimapManager).GetField("rawImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(mm, raw);

            mm.RevealAround(Vector2.zero, 5f);
            mm.ClearFog();
            Assert.IsFalse(mm.IsExplored(Vector2.zero));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MinimapMarker_RegistersAndUnregisters()
        {
            var go = new GameObject("Marker");
            var m = go.AddComponent<MinimapMarker>();
            m.enabled = true;
            Assert.AreEqual(m.color, m.EffectiveColor); // no pulse
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MinimapMarker_Pulse_ReturnsModulatedColor()
        {
            var go = new GameObject("Marker");
            var m = go.AddComponent<MinimapMarker>();
            m.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            m.pulse = true;
            m.pulsePeriod = 1f;
            var c = m.EffectiveColor;
            Assert.IsTrue(c.r >= m.color.r - 0.001f && c.r <= m.color.r + 0.3f);
            Object.DestroyImmediate(go);
        }
    }
}
