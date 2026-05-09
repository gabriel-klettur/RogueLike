using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the bounded undo stack semantics: capacity cap, redo-clears-on-push,
    /// label round-trip, and clean Clear(). Exercised through the standalone
    /// <see cref="MapEditorUndoSystem"/> so the contract is verifiable without
    /// spinning up a MapEditorManager + ZoneManager pair.
    /// </summary>
    [TestFixture]
    public class MapEditorUndoSystemTests
    {
        [SetUp] public void SetUp() => LogAssert.ignoreFailingMessages = true;
        [TearDown] public void TearDown() => LogAssert.ignoreFailingMessages = false;

        [Test]
        public void Empty_CannotUndoOrRedo()
        {
            var sut = new MapEditorUndoSystem();
            Assert.IsFalse(sut.CanUndo);
            Assert.IsFalse(sut.CanRedo);
            Assert.IsFalse(sut.Undo(out _));
            Assert.IsFalse(sut.Redo(out _));
        }

        [Test]
        public void Push_AppliesNothingItself_OnlyRecords()
        {
            var sut = new MapEditorUndoSystem();
            int doCalls = 0, undoCalls = 0;

            sut.Push("test", () => doCalls++, () => undoCalls++);

            Assert.AreEqual(0, doCalls,   "Push must not invoke 'do' — recording happens after the editor performs it.");
            Assert.AreEqual(0, undoCalls, "Push must not invoke 'undo' either.");
            Assert.IsTrue(sut.CanUndo);
            Assert.IsFalse(sut.CanRedo);
        }

        [Test]
        public void Undo_RunsInverseAndPopulatesLabel()
        {
            var sut = new MapEditorUndoSystem();
            int undoCalls = 0;
            sut.Push("delete zone X", () => { }, () => undoCalls++);

            bool ok = sut.Undo(out var label);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, undoCalls);
            Assert.AreEqual("delete zone X", label);
            Assert.IsFalse(sut.CanUndo);
            Assert.IsTrue(sut.CanRedo);
        }

        [Test]
        public void Redo_RunsDoClosure()
        {
            var sut = new MapEditorUndoSystem();
            int doCalls = 0;
            sut.Push("place portal", () => doCalls++, () => { });

            sut.Undo(out _);
            bool ok = sut.Redo(out var label);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, doCalls, "Redo must invoke the captured 'do' closure.");
            Assert.AreEqual("place portal", label);
            Assert.IsTrue(sut.CanUndo);
            Assert.IsFalse(sut.CanRedo);
        }

        [Test]
        public void NewPushAfterUndo_ClearsRedoBranch()
        {
            var sut = new MapEditorUndoSystem();
            sut.Push("op A", () => { }, () => { });
            sut.Undo(out _);
            Assert.IsTrue(sut.CanRedo, "Sanity: redo should be available after undo.");

            sut.Push("op B", () => { }, () => { });

            Assert.IsFalse(sut.CanRedo,
                "Pushing a new op after an undo must clear the redo branch — " +
                "otherwise the user could redo into a state that no longer exists.");
        }

        [Test]
        public void Capacity_DropsOldestEntryOnOverflow()
        {
            var sut = new MapEditorUndoSystem();
            for (int i = 0; i < MapEditorUndoSystem.MaxOps + 10; i++)
            {
                int captured = i;
                sut.Push($"op-{captured}", () => { }, () => { });
            }
            Assert.AreEqual(MapEditorUndoSystem.MaxOps, sut.UndoDepth,
                "Stack must self-trim to MaxOps so a long edit session can't blow up memory.");

            // Walk back the entire stack — the labels we still see should be
            // the most recent MaxOps, not the oldest.
            string firstLabelPopped = null;
            sut.Undo(out firstLabelPopped);
            Assert.AreEqual($"op-{MapEditorUndoSystem.MaxOps + 10 - 1}", firstLabelPopped,
                "First undo after overflow must report the MOST RECENT op (LIFO).");
        }

        [Test]
        public void Clear_DropsBothStacks()
        {
            var sut = new MapEditorUndoSystem();
            sut.Push("a", () => { }, () => { });
            sut.Push("b", () => { }, () => { });
            sut.Undo(out _); // populates redo

            sut.Clear();

            Assert.IsFalse(sut.CanUndo);
            Assert.IsFalse(sut.CanRedo);
            Assert.AreEqual(0, sut.UndoDepth);
            Assert.AreEqual(0, sut.RedoDepth);
        }

        [Test]
        public void UndoOrder_IsLifo()
        {
            var sut = new MapEditorUndoSystem();
            var trace = new System.Text.StringBuilder();
            sut.Push("first",  () => trace.Append("DOf"), () => trace.Append("UNf"));
            sut.Push("second", () => trace.Append("DOs"), () => trace.Append("UNs"));

            sut.Undo(out _);
            sut.Undo(out _);

            Assert.AreEqual("UNsUNf", trace.ToString(),
                "Undos must walk back in reverse-push order.");
        }
    }
}
