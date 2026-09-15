using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Gameplay.Quests
{
    /// <summary>
    /// The tracker as a WINDOW: a dropped quest leaves it, the drop takes two clicks, and the
    /// three window verbs mean what they say.
    ///
    /// <para><b>The defect this fixture exists for.</b> <c>QuestManager.AbandonQuest</c> raised
    /// no event at all — there were two, start and complete, and dropping is neither. So the
    /// tracker, which is event-driven, never heard: a quest dropped from the conversation panel
    /// stayed listed in the corner with its counters live, for the rest of the session, with no
    /// way to be rid of it. The minimap healed itself because its publisher re-scans on a timer;
    /// this panel had nothing to re-scan from.</para>
    ///
    /// <para><b>PlayerPrefs is MACHINE state, not fixture state.</b> The window's geometry and
    /// its minimized/closed flags live there and survive the run, the Editor and the reboot — so
    /// a developer who once closed the tracker by hand would otherwise fail these forever, on
    /// that machine only, for a reason nothing in the test name mentions. Cleared in BOTH
    /// SetUp and TearDown.</para>
    /// </summary>
    [TestFixture]
    public class QuestTrackerWindowTests
    {
        private static readonly string[] PrefKeys =
        {
            "valkur.questlog.x", "valkur.questlog.y",
            "valkur.questlog.width", "valkur.questlog.height",
            "valkur.questlog.minimized", "valkur.questlog.closed",
        };

        private GameObject _hudGo;
        private QuestLogHUD _hud;
        private GameObject _mgrGo;
        private QuestManager _mgr;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            ClearPrefs();

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
            ClearPrefs();
            GameEvents.Clear();
        }

        private static void ClearPrefs()
        {
            foreach (var k in PrefKeys) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }

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

        private int RowCount() => _hud.Rows.Count(r => r != null);

        // ── The abandon hole ────────────────────────────────────────────────────

        [Test]
        public void AbandoningAQuest_RemovesItsRow()
        {
            _mgr.StartQuest(MakeQuest("q.keep", "Kept", 3));
            _mgr.StartQuest(MakeQuest("q.drop", "Dropped", 3));
            Assert.AreEqual(2, RowCount(), "Both accepted quests should be listed.");

            _mgr.AbandonQuest("q.drop");

            Assert.AreEqual(1, RowCount(),
                "A dropped quest must leave the tracker. It did not, for the life of the panel: " +
                "AbandonQuest raised no event and the tracker is event-driven.");
            StringAssert.DoesNotContain("Dropped", _hud.ComputeLogText());
            StringAssert.Contains("Kept", _hud.ComputeLogText(),
                "Dropping one quest must not disturb the others.");
        }

        [Test]
        public void AbandonEvent_FiresAfterTheQuestHasAlreadyLeftTheActiveLog()
        {
            // The listener asks the manager what is active the moment it is told, so an event
            // raised mid-removal would have it redraw the very row it was notified about.
            bool stillActiveWhenNotified = true;
            _mgr.OnQuestAbandoned += id => stillActiveWhenNotified = _mgr.IsActive(id);

            _mgr.StartQuest(MakeQuest("q.order", "Order", 1));
            _mgr.AbandonQuest("q.order");

            Assert.IsFalse(stillActiveWhenNotified,
                "OnQuestAbandoned must fire AFTER the quest leaves _active.");
        }

        [Test]
        public void AbandonEvent_IsNotTheCompletionEvent()
        {
            // They must stay separate: QuestService subscribes AnnounceCompletion to the
            // completion event, so sharing one would toast the rewards of a quest nobody
            // finished — and pay nothing, which is worse than saying nothing.
            bool completed = false;
            _mgr.OnQuestCompleted += _ => completed = true;

            _mgr.StartQuest(MakeQuest("q.sep", "Separate", 2));
            _mgr.AbandonQuest("q.sep");

            Assert.IsFalse(completed, "Dropping a quest must never report it as completed.");
            Assert.IsFalse(_mgr.IsCompleted("q.sep"),
                "A dropped quest is not finished — it goes back on offer.");
        }

        // ── Dropping from the tracker ───────────────────────────────────────────

        [Test]
        public void DroppingFromTheTracker_TakesTwoClicks()
        {
            _mgr.StartQuest(MakeQuest("q.two", "TwoClick", 4));
            Assert.AreEqual(1, RowCount());

            _hud.DropQuest("q.two");
            Assert.IsTrue(_mgr.IsActive("q.two"),
                "The first click ARMS the button. One misclick in a corner nobody is looking " +
                "at must not throw away an errand the player walked across the map for.");
            Assert.AreEqual(1, RowCount(), "The row stays, repainted as armed.");

            _hud.DropQuest("q.two");
            Assert.IsFalse(_mgr.IsActive("q.two"), "The second click drops it.");
            Assert.AreEqual(0, RowCount());
        }

        [Test]
        public void ArmingOneQuest_DisarmsTheOther()
        {
            _mgr.StartQuest(MakeQuest("q.a", "Alpha", 1));
            _mgr.StartQuest(MakeQuest("q.b", "Beta", 1));

            _hud.DropQuest("q.a");   // arms A
            _hud.DropQuest("q.b");   // arms B, must disarm A
            _hud.DropQuest("q.a");   // therefore ARMS A again rather than dropping it

            Assert.IsTrue(_mgr.IsActive("q.a"),
                "Arming is exclusive, so a click that lands on a different row cannot complete " +
                "a confirmation the player started somewhere else.");
            Assert.IsTrue(_mgr.IsActive("q.b"));
        }

        // ── The window verbs ────────────────────────────────────────────────────

        [Test]
        public void Minimizing_IsRememberedAndDoesNotChangeTheRememberedSize()
        {
            _mgr.StartQuest(MakeQuest("q.min", "Mini", 1));
            float heightBefore = _hud.WindowRect.height;

            _hud.SetMinimized(true);

            Assert.IsTrue(_hud.IsMinimized);
            Assert.AreEqual(heightBefore, _hud.WindowRect.height, 0.01f,
                "Collapsing is a VIEW, not a resize: expanding must restore the height the " +
                "player chose rather than a title bar's worth of it.");
            Assert.AreEqual(1, PlayerPrefs.GetInt("valkur.questlog.minimized", 0));
        }

        [Test]
        public void Closing_HidesTheTracker_AndAcceptingAQuestBringsItBack()
        {
            _mgr.StartQuest(MakeQuest("q.close", "Closer", 1));

            _hud.SetClosed(true);
            Assert.IsFalse(_hud.IsWindowVisible);
            Assert.AreEqual(1, PlayerPrefs.GetInt("valkur.questlog.closed", 0),
                "Closing is remembered, so it survives a restart.");

            _mgr.StartQuest(MakeQuest("q.next", "Next errand", 1));

            Assert.IsTrue(_hud.IsWindowVisible,
                "A new quest is the one moment a closed tracker has to come back. Without it " +
                "CLOSE is a one-way door in a shipped build: the only panel that can reopen " +
                "this one is a runtime editor, and those are gated out of a player build.");
        }

        [Test]
        public void TheWindow_OpensWhereTheOldFixedPanelSat()
        {
            // DERIVED from HudLayout rather than pinned to a literal, so this stays true when
            // the minimap moves — which is the whole reason BelowMinimapTop exists.
            var rect = _hud.WindowRect;
            Assert.AreEqual(
                Valkur.Core.UI.HudLayout.ReferenceWidth - Valkur.Core.UI.HudLayout.ScreenMargin,
                rect.x + rect.width, 0.01f,
                "Right edge flush with the screen margin.");
            Assert.AreEqual(-Valkur.Core.UI.HudLayout.BelowMinimapTop, rect.y, 0.01f,
                "Top edge directly under the minimap block.");
        }
    }
}
