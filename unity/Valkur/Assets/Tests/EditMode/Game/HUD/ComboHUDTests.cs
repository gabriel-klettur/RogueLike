using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="ComboHUD"/>: it mirrors the bound <see cref="ComboCounter"/>,
    /// climbs the tier ladder as the streak grows, stays hidden below the show
    /// threshold, holds on screen after a break, and accepts a replacement
    /// ladder at runtime.
    /// </summary>
    [TestFixture]
    public class ComboHUDTests
    {
        private const int NpcLayer = 9;

        private GameObject _hudGo;
        private ComboHUD _hud;

        private GameObject _playerGo;
        private ComboCounter _combo;
        private readonly List<GameObject> _targets = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();

            _hudGo = new GameObject("ComboHUD", typeof(RectTransform));
            _hud = _hudGo.AddComponent<ComboHUD>();
            // Awake does not fire reliably outside play mode.
            _hud.EnsureBuilt();

            _playerGo = new GameObject("Player");
            _combo = _playerGo.AddComponent<ComboCounter>();

            _hud.Bind(_combo);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var target in _targets)
                if (target != null) Object.DestroyImmediate(target);
            _targets.Clear();

            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Each hit needs a fresh victim: the counter rejects consecutive hits on
        // the same target (require_unique_target + same-target cooldown).
        private void LandHits(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var target = new GameObject("Target_" + _targets.Count) { layer = NpcLayer };
                _targets.Add(target);
                _combo.RegisterHit(target, 10f);
            }
        }

        private void TickFor(float seconds, float step = 0.05f)
        {
            for (float t = 0f; t < seconds; t += step) _hud.Tick(step);
        }

        // ── Behaviours ──────────────────────────────────────────────────────

        [Test]
        public void Bind_MirrorsCounterState()
        {
            Assert.AreSame(_combo, _hud.BoundCounter);
            Assert.AreEqual(0, _hud.DisplayedCount, "Sanity: nothing shown before the first hit.");
        }

        [Test]
        public void Hits_AdvanceDisplayedCount()
        {
            LandHits(3);
            Assert.AreEqual(3, _combo.Current, "Sanity: the counter itself must have advanced.");
            Assert.AreEqual(3, _hud.DisplayedCount,
                "The badge must follow OnComboChanged without polling.");
        }

        [Test]
        public void TierClimbsWithTheStreak()
        {
            LandHits(2);
            Assert.AreEqual("COMBO", _hud.CurrentTier.Title, "2 hits is the first rung.");

            LandHits(3);   // total 5
            Assert.AreEqual("GREAT", _hud.CurrentTier.Title, "5 hits crosses into the second rung.");

            LandHits(5);   // total 10
            Assert.AreEqual("SAVAGE", _hud.CurrentTier.Title, "10 hits crosses into the third rung.");
        }

        [Test]
        public void SingleHit_StaysHidden()
        {
            LandHits(1);
            TickFor(0.5f);

            Assert.AreEqual(0f, _hud.Alpha, 0.01f,
                "A lone hit must not flash the badge — that is what minCountToShow is for.");
        }

        [Test]
        public void ActiveStreak_FadesIn()
        {
            LandHits(2);
            TickFor(0.6f);

            Assert.Greater(_hud.Alpha, 0.9f,
                "With a live streak above the threshold the badge must be visible.");
        }

        [Test]
        public void BrokenStreak_HoldsThenFadesOut()
        {
            LandHits(4);
            TickFor(0.4f);
            Assert.Greater(_hud.Alpha, 0.5f, "Sanity: visible while the streak runs.");

            _combo.ForceBreak();

            // Still readable right after the break — the player gets to see the
            // number they earned.
            _hud.Tick(0.05f);
            Assert.Greater(_hud.Alpha, 0.5f, "The badge must hold briefly after a break.");
            Assert.AreEqual(4, _hud.DisplayedCount, "The final count stays on screen during the hold.");

            TickFor(2f);
            Assert.AreEqual(0f, _hud.Alpha, 0.01f, "Once the hold expires the badge must fade out fully.");
        }

        [Test]
        public void SetTiers_ReplacesAndSortsTheLadder()
        {
            var unsorted = new List<ComboTier>
            {
                new ComboTier(20, "HIGH", Color.red, Color.red, 0.5f, 1.3f),
                new ComboTier(3,  "LOW",  Color.green, Color.green, 0.2f, 1.1f),
            };

            _hud.SetTiers(unsorted);
            Assert.AreEqual(2, _hud.TierCount, "The ladder must be exactly what was handed in.");

            LandHits(4);
            Assert.AreEqual("LOW", _hud.CurrentTier.Title,
                "4 hits must resolve to the rung at 3, proving the list was sorted ascending.");

            LandHits(16);   // total 20
            Assert.AreEqual("HIGH", _hud.CurrentTier.Title);
        }

        [Test]
        public void SetTiers_EmptyFallsBackToDefaults()
        {
            _hud.SetTiers(new List<ComboTier>());
            Assert.Greater(_hud.TierCount, 0,
                "An empty ladder must fall back to the built-in one rather than leave the badge blank.");
        }

        [Test]
        public void BindNull_IsSafeAndReleasesTheCounter()
        {
            LandHits(3);
            _hud.Bind(null);

            Assert.IsNull(_hud.BoundCounter);

            // The released counter must no longer drive the badge.
            int before = _hud.DisplayedCount;
            LandHits(2);
            Assert.AreEqual(before, _hud.DisplayedCount,
                "After unbinding, further hits must not reach the badge.");
        }
    }
}
