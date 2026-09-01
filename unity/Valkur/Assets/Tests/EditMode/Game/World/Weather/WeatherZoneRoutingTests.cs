using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// Weather is stored per ZONE and rendered once.
    ///
    /// The distinction those two halves draw is the whole point, and each half has its own
    /// failure mode. Store it globally and "it snows in the forest" is unsayable. Render it
    /// per zone and you have allocated three or four ParticleSystems and a synthesised audio
    /// clip for every named region in the world, most of which nobody is looking at.
    ///
    /// So the contract asserted here is: authoring goes to the zone the player is standing in,
    /// only the zone being rendered may move the live effects, and no effect is ever built for
    /// a zone that has nothing to show.
    /// </summary>
    [TestFixture]
    public class WeatherZoneRoutingTests
    {
        private GameObject _go;
        private WeatherManager _manager;
        private readonly List<(string zone, WeatherType type, WeatherIntensity level)> _events =
            new List<(string, WeatherType, WeatherIntensity)>();

        [SetUp]
        public void SetUp()
        {
            _events.Clear();
            _go = new GameObject("Test_WeatherManager");
            // Awake does not run in Edit Mode, which is exactly what this fixture wants: no
            // singleton registration, no SnowSplatMap child, no RenderTexture. The zone routing
            // is plain state and needs none of it.
            _manager = _go.AddComponent<WeatherManager>();
            WeatherManager.OnWeatherChanged += Record;
        }

        [TearDown]
        public void TearDown()
        {
            WeatherManager.OnWeatherChanged -= Record;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private void Record(string zone, WeatherType type, WeatherIntensity level)
            => _events.Add((zone, type, level));

        private int LiveEffectCount() => _go.GetComponentsInChildren<WeatherEffect>().Length;

        // ── authoring goes where the player is ───────────────────────────────────────

        [Test]
        public void WithNoZoneDetected_AuthoringIsRefusedRatherThanSilentlyDropped()
        {
            // An empty zone key would author weather into a region that does not exist and
            // could never be reached again. The caller has to be able to say so — the F2 panel
            // prints a reason instead of reporting a level it did not set.
            Assert.That(_manager.HasActiveZone, Is.False);
            Assert.That(_manager.Set(WeatherType.Rain, WeatherIntensity.Heavy), Is.False);
            Assert.That(_manager.ZonesWithWeather(), Is.Empty);
        }

        [Test]
        public void AuthoringLandsInTheActiveZone()
        {
            _manager.SetActiveZone("forest");
            Assert.That(_manager.Set(WeatherType.Snow, WeatherIntensity.Heavy), Is.True);

            Assert.That(_manager.LevelOf(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Heavy));
            Assert.That(_manager.LevelOfZone("forest", WeatherType.Snow), Is.EqualTo(WeatherIntensity.Heavy));
        }

        [Test]
        public void WeatherBelongsToItsZone_NotToTheSession()
        {
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Heavy);

            _manager.SetActiveZone("desert");
            Assert.That(_manager.LevelOf(WeatherType.Rain), Is.EqualTo(WeatherIntensity.Off),
                "the desert inherited the forest's rain");

            _manager.SetActiveZone("forest");
            Assert.That(_manager.LevelOf(WeatherType.Rain), Is.EqualTo(WeatherIntensity.Heavy),
                "walking away and back lost the forest's rain");
        }

        [Test]
        public void ZoneNames_AreMatchedTheWayZoneManagerMatchesThem()
        {
            // ZoneManager's own lookup is OrdinalIgnoreCase. A weather keyed by a differently
            // cased name would not be wrong in any visible way — it would just be unreachable.
            _manager.SetActiveZone("Forest");
            _manager.Set(WeatherType.Wind, WeatherIntensity.Light);

            Assert.That(_manager.LevelOfZone("forest", WeatherType.Wind), Is.EqualTo(WeatherIntensity.Light));
            Assert.That(_manager.LevelOfZone("FOREST", WeatherType.Wind), Is.EqualTo(WeatherIntensity.Light));
        }

        [Test]
        public void Cycle_WalksTheLevelsOfTheActiveZone()
        {
            _manager.SetActiveZone("tundra");
            Assert.That(_manager.Cycle(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Light));
            Assert.That(_manager.Cycle(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Medium));
            Assert.That(_manager.Cycle(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Heavy));
            Assert.That(_manager.Cycle(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Off));
        }

        // ── clearing is scoped ───────────────────────────────────────────────────────

        [Test]
        public void ClearAll_ClearsOnlyTheZoneTheAuthorCanSee()
        {
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Heavy);
            _manager.SetActiveZone("desert");
            _manager.Set(WeatherType.Wind, WeatherIntensity.Medium);

            _manager.ClearAll();

            Assert.That(_manager.LevelOfZone("desert", WeatherType.Wind), Is.EqualTo(WeatherIntensity.Off));
            Assert.That(_manager.LevelOfZone("forest", WeatherType.Rain), Is.EqualTo(WeatherIntensity.Heavy),
                "the OFF row cleared a zone the author was not standing in");
        }

        [Test]
        public void ClearEveryZone_WipesTheWholeWorld()
        {
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Heavy);
            _manager.SetActiveZone("desert");
            _manager.Set(WeatherType.Wind, WeatherIntensity.Medium);

            _manager.ClearEveryZone();
            Assert.That(_manager.ZonesWithWeather(), Is.Empty);
        }

        [Test]
        public void ZonesWithWeather_ListsOnlyZonesThatHaveSome()
        {
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Light);
            _manager.SetActiveZone("desert");   // authored nothing

            var zones = _manager.ZonesWithWeather();
            Assert.That(zones, Is.EquivalentTo(new[] { "forest" }));
        }

        // ── indoors ──────────────────────────────────────────────────────────────────

        [Test]
        public void GoingIndoors_SuspendsTheWeatherWithoutForgettingIt()
        {
            // ZoneManager suspends detection inside an interior, so there is no zone to author
            // and no weather to show — you are under a roof. What must NOT happen is the
            // zone's authored weather being cleared on the way in.
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Snow, WeatherIntensity.Heavy);

            _manager.SetActiveZone("house_interior", indoors: true);
            Assert.That(_manager.IsIndoors, Is.True);
            Assert.That(_manager.Set(WeatherType.Rain, WeatherIntensity.Heavy), Is.True,
                "the manager itself does not refuse indoor authoring — the UI does");

            _manager.SetActiveZone("forest");
            Assert.That(_manager.IsIndoors, Is.False);
            Assert.That(_manager.LevelOf(WeatherType.Snow), Is.EqualTo(WeatherIntensity.Heavy),
                "stepping inside and out lost the zone's snow");
        }

        // ── allocation ───────────────────────────────────────────────────────────────

        [Test]
        public void AClearWorld_BuildsNoEffectsAtAll()
        {
            // Turning weather OFF must never be the thing that constructs it. An effect is
            // four or five ParticleSystems plus, for rain and wind, a synthesised audio clip.
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Off);
            _manager.Set(WeatherType.Snow, WeatherIntensity.Off);
            _manager.ClearAll();

            Assert.That(LiveEffectCount(), Is.EqualTo(0));
        }

        [Test]
        public void AuthoringAZoneYouAreNotIn_RendersNothing()
        {
            // Walking through a hundred clear zones must not allocate the weather of any of
            // them. Only the zone being rendered may move the live effects.
            _manager.SetActiveZone("forest");
            _manager.SetInZone("desert", WeatherType.Rain, WeatherIntensity.Heavy);

            Assert.That(_manager.LevelOfZone("desert", WeatherType.Rain), Is.EqualTo(WeatherIntensity.Heavy));
            Assert.That(LiveEffectCount(), Is.EqualTo(0));
            Assert.That(_manager.DensityOf(WeatherType.Rain), Is.EqualTo(0f));
        }

        [Test]
        public void EnteringAZoneWithWeather_BuildsExactlyThatEffect()
        {
            _manager.SetInZone("desert", WeatherType.Wind, WeatherIntensity.Medium);
            Assert.That(LiveEffectCount(), Is.EqualTo(0));

            _manager.SetActiveZone("desert");

            Assert.That(LiveEffectCount(), Is.EqualTo(1), "wind alone should have been built");
            var effect = _go.GetComponentInChildren<WindEffect>();
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.Level, Is.EqualTo(WeatherIntensity.Medium));
        }

        // ── events ───────────────────────────────────────────────────────────────────

        [Test]
        public void TheChangeEvent_SaysWhichZoneChanged()
        {
            // Subscribers have to be able to ignore a change to a zone that is not on screen;
            // without the zone in the payload the F2 panel would repaint for edits it cannot show.
            _manager.SetActiveZone("forest");
            _manager.Set(WeatherType.Rain, WeatherIntensity.Medium);
            _manager.SetInZone("desert", WeatherType.Snow, WeatherIntensity.Light);

            Assert.That(_events, Does.Contain(("forest", WeatherType.Rain, WeatherIntensity.Medium)));
            Assert.That(_events, Does.Contain(("desert", WeatherType.Snow, WeatherIntensity.Light)));
        }
    }
}
