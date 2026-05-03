using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="QuestLogHUD"/>: text reflects active quests with
    /// objective progress, refreshes on quest start / completion / kill
    /// progress events, and produces a sensible default empty string
    /// when no manager is bound.
    /// </summary>
    [TestFixture]
    public class QuestLogHUDTests
    {
        private GameObject _hudGo;
        private QuestLogHUD _hud;
        private GameObject _mgrGo;
        private QuestManager _mgr;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();

            _hudGo = new GameObject("QuestLogHUD");
            _hud = _hudGo.AddComponent<QuestLogHUD>();
            _hud.EnsureBuilt();

            _mgrGo = new GameObject("QuestManager");
            _mgr = _mgrGo.AddComponent<QuestManager>();

            _hud.BindManager(_mgr);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_mgrGo != null) Object.DestroyImmediate(_mgrGo);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static QuestDefinition MakeQuest(string id, string name, int killCount)
        {
            var d = ScriptableObject.CreateInstance<QuestDefinition>();
            d.questId = id;
            d.displayName = name;
            d.objectives = new[]
            {
                new ObjectiveEntry { kind = ObjectiveKind.KillCount, count = killCount }
            };
            return d;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void EmptyManager_ProducesEmptyText()
        {
            Assert.AreEqual(string.Empty, _hud.ComputeLogText(),
                "No active quests = empty log text. The Canvas hides itself when this is empty.");
        }

        [Test]
        public void ActiveQuest_AppearsInLogWithObjectiveProgress()
        {
            var def = MakeQuest("kill_wolves", "Wolfpack", killCount: 5);
            try
            {
                _mgr.StartQuest(def);
                string log = _hud.ComputeLogText();
                StringAssert.Contains("Wolfpack", log,
                    "Quest displayName must appear in the log.");
                StringAssert.Contains("0/5", log,
                    "Objective progress must render as 'Current/Target'.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void ProgressUpdate_LogReflectsNewCounter()
        {
            var def = MakeQuest("kc", "Wolves", killCount: 3);
            try
            {
                _mgr.StartQuest(def);
                Assert.IsTrue(_hud.ComputeLogText().Contains("0/3"));

                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);

                Assert.IsTrue(_hud.ComputeLogText().Contains("1/3"),
                    "After one kill the log must show 1/3.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void Completion_DropsOutOfActiveLog()
        {
            var def = MakeQuest("done", "OneShot", killCount: 1);
            try
            {
                _mgr.StartQuest(def);
                Assert.IsTrue(_hud.ComputeLogText().Contains("OneShot"));

                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);

                Assert.IsFalse(_hud.ComputeLogText().Contains("OneShot"),
                    "Completed quest must drop out of the active log — completed " +
                    "quests live in the manager's completed set, not the active log.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void RebindToNewManager_ClearsOldSubscriptions()
        {
            // Set up an active quest on the first manager.
            var def = MakeQuest("a", "Alpha", killCount: 5);
            try
            {
                _mgr.StartQuest(def);
                Assert.IsTrue(_hud.ComputeLogText().Contains("Alpha"));

                // Bind to a fresh manager — log must clear.
                var mgr2Go = new GameObject("Mgr2");
                var mgr2 = mgr2Go.AddComponent<QuestManager>();
                try
                {
                    _hud.BindManager(mgr2);
                    Assert.AreEqual(string.Empty, _hud.ComputeLogText(),
                        "Re-binding to a fresh manager must produce an empty log.");
                }
                finally { Object.DestroyImmediate(mgr2Go); }
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void EnsureBuilt_Idempotent()
        {
            var canvasesBefore = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            _hud.EnsureBuilt();
            _hud.EnsureBuilt();
            var canvasesAfter = _hudGo.GetComponentsInChildren<Canvas>(true).Length;
            Assert.AreEqual(canvasesBefore, canvasesAfter,
                "Multiple EnsureBuilt calls must not stack Canvases.");
        }
    }
}
