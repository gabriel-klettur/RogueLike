using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// The shape of a built weather effect: how many depth slices it has, what they draw
    /// with, and where they sort.
    ///
    /// Three of these assertions are regressions rather than preferences:
    ///
    ///   • <b>Depth.</b> Each effect must build more than one slice. A single-system weather
    ///     gives the eye no way to resolve distance, which is why the previous one read as a
    ///     decal on the lens no matter how the drops were tuned.
    ///   • <b>Stretch orientation.</b> Unity's stretched billboard aligns the quad's U axis
    ///     with velocity, so a streak texture must be WIDER than it is tall. The rain used to
    ///     ship a 4x16 vertical strip and every drop rendered as a smear across its own fall.
    ///   • <b>Shared materials.</b> Every renderer must be on a material from the shared cache,
    ///     which also means it is on the URP particle shader. The old code built a
    ///     <c>Particles/Standard Unlit</c> material — a built-in-pipeline shader — per effect
    ///     and assigned it to <c>renderer.material</c>, cloning it once more per renderer.
    /// </summary>
    [TestFixture]
    public class WeatherEffectLayerTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private T Build<T>() where T : WeatherEffect
        {
            var go = new GameObject($"Test_{typeof(T).Name}");
            _spawned.Add(go);
            var fx = go.AddComponent<T>();
            // Unity does not call Awake on a component added in Edit Mode, so the build is
            // requested explicitly. Same entry point Awake uses, so this is the Play Mode object.
            fx.EnsureBuilt();
            return fx;
        }

        private static ParticleSystem[] SlicesOf(WeatherEffect fx)
            => fx.GetComponentsInChildren<ParticleSystem>();

        // ── depth ────────────────────────────────────────────────────────────────────

        [Test]
        public void Rain_BuildsAFullDepthStack()
        {
            // far / mid / near streaks, plus ground splashes and the haze between them.
            Assert.That(SlicesOf(Build<RainEffect>()).Length, Is.EqualTo(5));
        }

        [Test]
        public void Snow_BuildsAFullDepthStack()
        {
            // far / mid / near flakes, plus settled specks.
            Assert.That(SlicesOf(Build<SnowEffect>()).Length, Is.EqualTo(4));
        }

        [Test]
        public void Wind_BuildsAFullDepthStack()
        {
            // dust / streaks / near blur, plus leaves.
            Assert.That(SlicesOf(Build<WindEffect>()).Length, Is.EqualTo(4));
        }

        [Test]
        public void EveryEffect_HasMoreThanOneSlice()
        {
            Assert.That(SlicesOf(Build<RainEffect>()).Length, Is.GreaterThan(1));
            Assert.That(SlicesOf(Build<SnowEffect>()).Length, Is.GreaterThan(1));
            Assert.That(SlicesOf(Build<WindEffect>()).Length, Is.GreaterThan(1));
        }

        // ── renderers ────────────────────────────────────────────────────────────────

        [Test]
        public void EverySlice_DrawsWithASharedCachedMaterial()
        {
            foreach (var ps in AllSlices())
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.That(r.sharedMaterial, Is.Not.Null, $"{ps.name} has no material");
                Assert.That(r.sharedMaterial.mainTexture, Is.Not.Null,
                    $"{ps.name} would draw an untextured white quad");
            }
        }

        [Test]
        public void TwoInstancesOfTheSameEffect_ShareTheirMaterials()
        {
            var a = SlicesOf(Build<RainEffect>());
            var b = SlicesOf(Build<RainEffect>());
            Assert.That(a.Length, Is.EqualTo(b.Length));

            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(a[i].GetComponent<ParticleSystemRenderer>().sharedMaterial,
                    Is.SameAs(b[i].GetComponent<ParticleSystemRenderer>().sharedMaterial),
                    $"slice {a[i].name} built its own material instead of taking the cached one");
            }
        }

        [Test]
        public void StretchedSlices_UseAHorizontalTexture()
        {
            foreach (var ps in AllSlices())
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r.renderMode != ParticleSystemRenderMode.Stretch) continue;

                var tex = r.sharedMaterial.mainTexture;
                Assert.That(tex.width, Is.GreaterThan(tex.height),
                    $"{ps.name} is stretched along velocity but its texture is drawn across it");
            }
        }

        [Test]
        public void EverySlice_SortsAboveTheWorld()
        {
            // VFX is the highest sorting layer below the UI ones — precipitation belongs
            // between the camera and the world, wall tops included. "Default" is the fallback
            // for a project whose sorting layers have not been set up.
            foreach (var ps in AllSlices())
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.That(r.sortingLayerName, Is.EqualTo("VFX").Or.EqualTo("Default"), ps.name);
            }
        }

        [Test]
        public void EverySlice_EmitsFromABox()
        {
            // A bare ParticleSystem defaults to a Cone, which in a 2D ortho scene sprays a
            // wedge out of one point instead of covering the frame. Every slice must have
            // overridden it — the layout helpers write a Box's scale and position and would
            // otherwise be sizing a shape nothing reads.
            foreach (var ps in AllSlices())
            {
                Assert.That(ps.shape.enabled, Is.True, ps.name);
                Assert.That(ps.shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Box), ps.name);
            }
        }

        [Test]
        public void NearSlicesSortAboveFarOnes()
        {
            AssertOrdered(Build<RainEffect>(), "Rain_Far",  "Rain_Mid",  "Rain_Near");
            AssertOrdered(Build<SnowEffect>(), "Snow_Far",  "Snow_Mid",  "Snow_Near");
            AssertOrdered(Build<WindEffect>(), "Wind_Dust", "Wind_Streak", "Wind_Near");
        }

        private static void AssertOrdered(WeatherEffect fx, params string[] farToNear)
        {
            int previous = int.MinValue;
            foreach (var name in farToNear)
            {
                var child = fx.transform.Find(name);
                Assert.That(child, Is.Not.Null, $"missing slice {name}");
                int order = child.GetComponent<ParticleSystemRenderer>().sortingOrder;
                Assert.That(order, Is.GreaterThan(previous),
                    $"{name} does not sort above the slice behind it");
                previous = order;
            }
        }

        // ── initial state ────────────────────────────────────────────────────────────

        [Test]
        public void AFreshEffect_IsOffAndEmitsNothing()
        {
            var fx = Build<RainEffect>();
            Assert.That(fx.Level, Is.EqualTo(WeatherIntensity.Off));
            Assert.That(fx.IsActive, Is.False);
            Assert.That(fx.Density, Is.EqualTo(0f));

            foreach (var ps in SlicesOf(fx))
                Assert.That(ps.emission.rateOverTime.constant, Is.EqualTo(0f),
                    $"{ps.name} started emitting before anything asked for weather");
        }

        [Test]
        public void SetIntensity_IsRecordedImmediately_EvenThoughTheFadeIsGradual()
        {
            var fx = Build<SnowEffect>();
            fx.SetIntensity(WeatherIntensity.Heavy);

            Assert.That(fx.Level, Is.EqualTo(WeatherIntensity.Heavy));
            Assert.That(fx.IsActive, Is.True);
            // Density is the SMOOTHED value and only moves in Update, so it is still 0 here.
            // That separation is the point: the request is instant, the look ramps.
            Assert.That(fx.Density, Is.EqualTo(0f));
        }

        [Test]
        public void Snow_IsSilent()
        {
            // Falling snow makes no sound. A bed under it would be inventing one.
            Assert.That(Build<SnowEffect>().GetComponent<AudioSource>(), Is.Null);
        }

        [Test]
        public void RainAndWind_CarryALoopingBed()
        {
            foreach (var fx in new WeatherEffect[] { Build<RainEffect>(), Build<WindEffect>() })
            {
                var src = fx.GetComponent<AudioSource>();
                Assert.That(src, Is.Not.Null, $"{fx.GetType().Name} has no audio bed");
                Assert.That(src.loop, Is.True);
                Assert.That(src.playOnAwake, Is.False);
                Assert.That(src.spatialBlend, Is.EqualTo(0f), "a weather bed is not positional");
                Assert.That(src.volume, Is.EqualTo(0f), "the bed must start silent and fade in");
                Assert.That(src.clip, Is.Not.Null);
                Assert.That(src.clip.length, Is.GreaterThan(1f));
            }
        }

        private IEnumerable<ParticleSystem> AllSlices()
        {
            foreach (var ps in SlicesOf(Build<RainEffect>())) yield return ps;
            foreach (var ps in SlicesOf(Build<SnowEffect>())) yield return ps;
            foreach (var ps in SlicesOf(Build<WindEffect>())) yield return ps;
        }
    }
}
