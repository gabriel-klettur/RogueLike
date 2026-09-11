using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The explored map. The defect it was rebuilt out of: the fog was cleared on every
    /// OnZoneChanged, and zones are 50x50 tiles laid edge to edge — walking across town wiped
    /// the street the player had just come down.
    /// </summary>
    public class MinimapFogMapTests
    {
        private MinimapFogMap _fog;

        [SetUp] public void SetUp() => _fog = new MinimapFogMap();
        [TearDown] public void TearDown() => _fog.Dispose();

        [Test]
        public void EverythingInsideTheRadius_IsExplored_AndFarGroundIsNot()
        {
            _fog.Reveal(new Vector2(10f, 10f), 5f);
            Assert.IsTrue(_fog.IsExplored(new Vector2(10f, 10f)));
            Assert.IsTrue(_fog.IsExplored(new Vector2(14.2f, 10f)), "the soft ramp lies OUTSIDE the radius");
            Assert.IsFalse(_fog.IsExplored(new Vector2(40f, 10f)));
        }

        [Test]
        public void EachWorld_KeepsItsOwnFog_AndReturningRestoresIt()
        {
            _fog.Reveal(new Vector2(0f, 0f), 6f);
            _fog.SetActiveKey("interior:house");
            Assert.IsFalse(_fog.IsExplored(Vector2.zero), "an interior starts unexplored");
            _fog.Reveal(new Vector2(0f, 0f), 3f);
            _fog.SetActiveKey(MinimapFogMap.WorldKey);
            Assert.IsTrue(_fog.IsExplored(Vector2.zero), "the town's fog survived the visit");
            Assert.IsTrue(_fog.IsExplored(new Vector2(5f, 0f)));
        }

        [Test]
        public void SettingTheSameKey_ForgetsNothing()
        {
            _fog.Reveal(new Vector2(100f, 50f), 8f);
            _fog.SetActiveKey(MinimapFogMap.WorldKey);
            Assert.IsTrue(_fog.IsExplored(new Vector2(100f, 50f)));
        }

        [Test]
        public void Reveal_ReportsOnlyCellsThatCrossedIntoExplored()
        {
            var fresh = new List<Vector2>();
            int first = _fog.Reveal(Vector2.zero, 4f, fresh, 1000);
            Assert.That(first, Is.GreaterThan(40));
            Assert.AreEqual(first, fresh.Count);

            fresh.Clear();
            int again = _fog.Reveal(Vector2.zero, 4f, fresh, 1000);
            Assert.AreEqual(0, again, "standing still reveals nothing new");
            Assert.AreEqual(0, fresh.Count);
        }

        [Test]
        public void GrowingBeyondTheFirstLayer_KeepsWhatWasExplored()
        {
            _fog.Reveal(Vector2.zero, 5f);
            _fog.Reveal(new Vector2(400f, -300f), 5f);
            Assert.IsTrue(_fog.IsExplored(Vector2.zero));
            Assert.IsTrue(_fog.IsExplored(new Vector2(400f, -300f)));
        }

        [Test]
        public void ExploredFraction_MeasuresARect()
        {
            _fog.Reveal(new Vector2(25f, 25f), 100f);
            Assert.That(_fog.ExploredFraction(new RectInt(0, 0, 50, 50)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(_fog.ExploredFraction(new RectInt(1000, 1000, 50, 50)), Is.EqualTo(0f));
        }

        [Test]
        public void Json_RoundTrips_AndMergeNeverUnexplores()
        {
            _fog.Reveal(new Vector2(3f, 3f), 4f);
            _fog.SetActiveKey("interior:cellar");
            _fog.Reveal(new Vector2(1f, 1f), 2f);
            _fog.SetActiveKey(MinimapFogMap.WorldKey);
            string json = _fog.ToJson();

            var other = new MinimapFogMap();
            try
            {
                other.Reveal(new Vector2(60f, 60f), 3f);            // explored before the load
                Assert.IsTrue(other.MergeJson(json));
                Assert.IsTrue(other.IsExplored(new Vector2(3f, 3f)), "loaded");
                Assert.IsTrue(other.IsExplored(new Vector2(60f, 60f)), "a merge never forgets");
                other.SetActiveKey("interior:cellar");
                Assert.IsTrue(other.IsExplored(new Vector2(1f, 1f)), "every world's fog travels");
            }
            finally { other.Dispose(); }
        }

        [Test]
        public void MergeJson_RefusesGarbage()
        {
            Assert.IsFalse(_fog.MergeJson(null));
            Assert.IsFalse(_fog.MergeJson("{ not json"));
        }

        [Test]
        public void Texture_IsR8_AndSizedToTheLayer()
        {
            _fog.Reveal(Vector2.zero, 5f);
            var tex = (Texture2D)_fog.GetTexture();
            Assert.AreEqual(TextureFormat.R8, tex.format);
            var rect = _fog.WorldRect;
            Assert.AreEqual((int)rect.z, tex.width);
            Assert.AreEqual((int)rect.w, tex.height);
            Assert.AreEqual(FilterMode.Bilinear, tex.filterMode, "the filter is what makes the frontier soft");
        }
    }
}
