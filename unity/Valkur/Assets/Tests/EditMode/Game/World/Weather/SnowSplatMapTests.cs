using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// The world-space accumulation buffer — the half of snow accumulation that knows WHERE.
    ///
    /// The one property that matters more than any other here is that the buffer is indexed by
    /// WORLD position and published as such. Everything downstream is a shader read against
    /// <c>_ValkurSnowMapRect</c>, so a rect that does not describe the texture's actual
    /// coverage does not fail loudly — it silently samples the drift at the wrong place, which
    /// looks like snow lying somewhere nobody walked.
    /// </summary>
    [TestFixture]
    public class SnowSplatMapTests
    {
        private static readonly int MapRectId = Shader.PropertyToID("_ValkurSnowMapRect");

        private GameObject _go;
        private SnowSplatMap _map;

        /// <summary>
        /// The buffer is a RenderTexture and both of its operations are draws, so there is
        /// nothing to measure on a null graphics device. Skipped rather than failed: a
        /// <c>-nographics</c> batch run is a legitimate way to run this suite.
        /// </summary>
        private static void RequireGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device — the accumulation buffer cannot be exercised.");
        }

        [SetUp]
        public void SetUp()
        {
            RequireGraphics();
            _go  = new GameObject("Test_SnowSplatMap");
            _map = _go.AddComponent<SnowSplatMap>();
            _map.EnsureBuilt();   // Edit Mode never calls Awake
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void ItBuildsAndPublishesItself()
        {
            Assert.That(_map.IsReady, Is.True, "the buffer failed to allocate");
            Assert.That(SnowSplatMap.Instance, Is.SameAs(_map));
        }

        [Test]
        public void ThePublishedRect_DescribesTheBuffersActualCoverage()
        {
            // The shader divides a world position by this rect to get its UV. A rect that
            // disagrees with the buffer samples the drift somewhere it was never laid down,
            // and nothing anywhere reports an error.
            var rect = _map.WorldRect;
            var published = Shader.GetGlobalVector(MapRectId);

            Assert.That(published.x, Is.EqualTo(rect.xMin).Within(1e-3f));
            Assert.That(published.y, Is.EqualTo(rect.yMin).Within(1e-3f));
            Assert.That(published.z, Is.EqualTo(rect.width).Within(1e-3f));
            Assert.That(published.w, Is.EqualTo(rect.height).Within(1e-3f));
        }

        [Test]
        public void TheBuffer_CoversSeveralViewports()
        {
            // It has to be big enough that ordinary walking does not reach an edge, or the
            // scroll would run constantly and every scroll discards the drift it uncovers.
            // The game plays at a 20 x 10 viewport.
            Assert.That(_map.WorldRect.width,  Is.GreaterThan(60f));
            Assert.That(_map.WorldRect.height, Is.GreaterThan(60f));
        }

        [Test]
        public void Landings_AreQueuedAndFlushedTogether()
        {
            // One draw per frame, not one per flake: sixty render-target switches a frame
            // would cost far more than the snow is worth.
            for (int i = 0; i < 24; i++) _map.Stamp(new Vector2(i * 0.1f, 0f));
            Assert.That(_map.PendingCount, Is.EqualTo(24));

            _map.Tick(0.016f, meltPerSecond: 0f);
            Assert.That(_map.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Fill_DropsPendingLandings()
        {
            // Fill rewrites the whole buffer; replaying stamps queued against the state it
            // replaced would double-count them on top of it.
            _map.Stamp(Vector2.zero);
            _map.Fill(1f);
            Assert.That(_map.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void ManyLandingsInAFrame_DoNotThrow()
        {
            // A gust lands hundreds at once, and the mesh buffers grow to the high-water mark.
            for (int i = 0; i < 900; i++)
                _map.Stamp(new Vector2(Random.Range(-20f, 20f), Random.Range(-20f, 20f)));

            Assert.DoesNotThrow(() => _map.Tick(0.016f, 0f));
            Assert.That(_map.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Ticking_WithNoCameraAndNoLandings_IsHarmless()
        {
            // The manager ticks it every frame from boot, long before a camera exists and
            // whether or not it has ever snowed.
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 30; i++) _map.Tick(0.05f, 0.01f);
            });
        }

        [Test]
        public void Destroying_PointsTheGlobalAtASafeFallback()
        {
            // Stated as a precondition rather than assumed: if a previous fixture left a live
            // buffer behind, this one never became the publisher and has nothing to hand back,
            // and the failure below would otherwise read as the reset being broken.
            Assert.That(_map.IsReady, Is.True, "this fixture's buffer never became the publisher");
            Assert.That(SnowSplatMap.Instance, Is.SameAs(_map));

            // ReleaseBuffer rather than DestroyImmediate: in Edit Mode a component whose Awake
            // never ran does not receive OnDestroy either, so destroying the object here would
            // run no teardown at all and the assertion below would be measuring nothing. Play
            // Mode reaches the same method through OnDestroy.
            _map.ReleaseBuffer();

            // A shader global outlives the object that set it. Left dangling it would be a
            // destroyed RenderTexture sampled by every world sprite; the 1x1 white fallback
            // is what makes the shader's uniform path take over instead.
            var published = Shader.GetGlobalVector(MapRectId);
            Assert.That(published, Is.EqualTo(new Vector4(0f, 0f, 1f, 1f)));
            Assert.That(SnowSplatMap.Instance == null, Is.True);

            // Idempotent: OnDestroy will reach it again when TearDown destroys the object.
            Assert.DoesNotThrow(() => _map.ReleaseBuffer());
        }

        [Test]
        public void ASecondInstance_DoesNotStealTheSingleton()
        {
            var second = new GameObject("Test_SnowSplatMap_Second");
            try
            {
                // The refusal is loud on purpose — only WeatherManager creates this, so a
                // second one is a wiring mistake and the warning is how it gets noticed.
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("second buffer"));

                var duplicate = second.AddComponent<SnowSplatMap>();
                duplicate.EnsureBuilt();
                Assert.That(SnowSplatMap.Instance, Is.SameAs(_map));
                Assert.That(duplicate.IsReady, Is.False, "the duplicate allocated a second buffer");
            }
            finally
            {
                Object.DestroyImmediate(second);
            }
        }
    }
}
