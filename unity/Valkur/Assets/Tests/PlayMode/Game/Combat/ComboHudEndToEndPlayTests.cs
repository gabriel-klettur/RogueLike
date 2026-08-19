using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.PlayMode.Game.Combat
{
    /// <summary>
    /// Runs the whole combo chain live: a spell reports a hit on the global event
    /// channel, <see cref="ComboCounter"/> picks it up, and <see cref="ComboHUD"/>
    /// becomes visible.
    ///
    /// This has to be a PlayMode test. The link that was broken for the entire
    /// life of the feature is <c>ComboCounter.OnEnable</c> subscribing to
    /// <c>GameEvents.OnHitDealt</c> — and OnEnable does not run outside play mode,
    /// so an EditMode test can call RegisterHit directly and pass while the real
    /// game shows nothing. Everything here goes through the event.
    /// </summary>
    public class ComboHudEndToEndPlayTests
    {
        private const int NpcLayer = 9;

        private GameObject _player;
        private ComboCounter _combo;
        private GameObject _hudGo;
        private ComboHUD _hud;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();

            _player = new GameObject("Player");
            _combo = _player.AddComponent<ComboCounter>();

            _hudGo = new GameObject("ComboHUD", typeof(RectTransform));
            _hud = _hudGo.AddComponent<ComboHUD>();
            _hud.Bind(_combo);

            // One frame so OnEnable has run on both components.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.Destroy(go);
            _spawned.Clear();

            if (_hudGo != null) Object.Destroy(_hudGo);
            if (_player != null) Object.Destroy(_player);
            GameEvents.Clear();
            yield return null;
        }

        // Each hit needs its own victim: the counter rejects consecutive hits on
        // the same target, so reusing one enemy never builds a streak.
        private GameObject NewEnemy()
        {
            var enemy = new GameObject("Enemy" + _spawned.Count) { layer = NpcLayer };
            _spawned.Add(enemy);
            return enemy;
        }

        private void ReportHit(GameObject attacker, GameObject victim, int damage = 10)
            => GameEvents.FireHitDealt(attacker, victim, damage);

        // Wait on elapsed time, not on a frame count. The test runner renders far
        // faster than the game, so "30 frames" can be a couple of milliseconds and
        // the fade has hardly moved. The deadlines stay well inside the 2 s combo
        // window so the streak does not expire underneath the assertion.
        private IEnumerator WaitUntilAlphaAbove(float threshold, float timeoutSeconds = 1.5f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (_hud.Alpha <= threshold && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        // ── The chain ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AReportedHitReachesTheCounter()
        {
            ReportHit(_player, NewEnemy());
            yield return null;

            Assert.AreEqual(1, _combo.Current,
                "GameEvents.OnHitDealt must reach ComboCounter. This subscription is the link " +
                "that was dead: no spell raised the event, so the counter never moved.");
        }

        [UnityTest]
        public IEnumerator TwoEnemiesInARowMakeTheBadgeVisible()
        {
            ReportHit(_player, NewEnemy());
            ReportHit(_player, NewEnemy());
            Assert.AreEqual(2, _combo.Current);

            yield return WaitUntilAlphaAbove(0.9f);

            Assert.Greater(_hud.Alpha, 0.9f,
                "Two hits is the streak the badge is supposed to appear on. If this ever " +
                "fails the HUD is invisible in ordinary play again.");
            Assert.AreEqual(2, _hud.DisplayedCount);
        }

        [UnityTest]
        public IEnumerator AHitReportedByAChildTransformStillCounts()
        {
            // Spells report the transform they were cast from. The laser fires from
            // the player's hands, which are a child object — an exact-equality check
            // on the attacker throws those hits away.
            var hand = new GameObject("Hand");
            hand.transform.SetParent(_player.transform, false);
            _spawned.Add(hand);

            ReportHit(_player, NewEnemy());
            ReportHit(hand, NewEnemy());
            yield return null;

            Assert.AreEqual(2, _combo.Current,
                "A hit reported from inside the player's own hierarchy is the player's hit.");
        }

        [UnityTest]
        public IEnumerator AnotherAttackersHitIsIgnored()
        {
            var stranger = new GameObject("Stranger");
            _spawned.Add(stranger);

            ReportHit(_player, NewEnemy());
            ReportHit(stranger, NewEnemy());
            yield return null;

            Assert.AreEqual(1, _combo.Current,
                "Damage dealt by someone else must never feed the player's combo.");
        }

        [UnityTest]
        public IEnumerator TheSameEnemyHitTwiceDoesNotDoubleCount()
        {
            var enemy = NewEnemy();
            ReportHit(_player, enemy);
            ReportHit(_player, enemy);
            yield return null;

            Assert.AreEqual(1, _combo.Current,
                "requireUniqueTarget is what stops a single enemy inflating the streak. " +
                "If this changes, the badge's meaning changes with it.");
        }

        // ── Coming back down ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator TheBadgeHidesAgainAfterTheStreakBreaks()
        {
            ReportHit(_player, NewEnemy());
            ReportHit(_player, NewEnemy());
            yield return WaitUntilAlphaAbove(0.9f);
            Assert.Greater(_hud.Alpha, 0.9f, "Sanity: visible while the streak runs.");

            _combo.ForceBreak();

            // Long enough to outlast the post-break hold plus the fade.
            float deadline = Time.realtimeSinceStartup + 4f;
            while (_hud.Alpha > 0.01f && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(0f, _hud.Alpha, 0.01f,
                "A badge that never fades out sits on top of the HP bars forever.");
        }

        [UnityTest]
        public IEnumerator TheDrainBarTracksTheRealComboWindow()
        {
            ReportHit(_player, NewEnemy());
            ReportHit(_player, NewEnemy());
            yield return null;

            Assert.Greater(_combo.WindowRemaining01, 0f);
            Assert.LessOrEqual(_combo.WindowRemaining01, 1f,
                "The badge divides by the CURRENT window, which shrinks as the streak grows. " +
                "A value above 1 means it is dividing by the base window again and the bar lies.");
        }
    }
}
