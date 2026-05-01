using NUnit.Framework;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    public class UndoStackTests
    {
        [Test]
        public void Do_ExecutesCommandAndRecords()
        {
            var stack = new UndoStack();
            int x = 0;
            stack.Do("inc", () => x++, () => x--);
            Assert.AreEqual(1, x);
            Assert.AreEqual(1, stack.UndoCount);
            Assert.AreEqual(0, stack.RedoCount);
        }

        [Test]
        public void Undo_RestoresState()
        {
            var stack = new UndoStack();
            int x = 0;
            stack.Do("inc", () => x++, () => x--);
            Assert.IsTrue(stack.Undo());
            Assert.AreEqual(0, x);
            Assert.AreEqual(0, stack.UndoCount);
            Assert.AreEqual(1, stack.RedoCount);
        }

        [Test]
        public void Redo_ReappliesCommand()
        {
            var stack = new UndoStack();
            int x = 0;
            stack.Do("inc", () => x++, () => x--);
            stack.Undo();
            Assert.IsTrue(stack.Redo());
            Assert.AreEqual(1, x);
        }

        [Test]
        public void NewCommandAfterUndo_ClearsRedoStack()
        {
            var stack = new UndoStack();
            int x = 0;
            stack.Do("a", () => x += 1, () => x -= 1);
            stack.Undo();
            Assert.AreEqual(1, stack.RedoCount);
            stack.Do("b", () => x += 10, () => x -= 10);
            Assert.AreEqual(0, stack.RedoCount);
            Assert.AreEqual(10, x);
        }

        [Test]
        public void CapacityLimit_DropsOldestUndo()
        {
            var stack = new UndoStack(capacity: 3);
            int x = 0;
            for (int i = 0; i < 5; i++)
                stack.Do("op" + i, () => x++, () => x--);
            Assert.AreEqual(3, stack.UndoCount);
            Assert.AreEqual(5, x);
        }

        [Test]
        public void Undo_WhenEmpty_ReturnsFalse()
        {
            var stack = new UndoStack();
            Assert.IsFalse(stack.Undo());
        }

        [Test]
        public void Redo_WhenEmpty_ReturnsFalse()
        {
            var stack = new UndoStack();
            Assert.IsFalse(stack.Redo());
        }

        [Test]
        public void Clear_EmptiesBothStacks()
        {
            var stack = new UndoStack();
            int x = 0;
            stack.Do("a", () => x++, () => x--);
            stack.Undo();
            stack.Clear();
            Assert.AreEqual(0, stack.UndoCount);
            Assert.AreEqual(0, stack.RedoCount);
        }

        [Test]
        public void PeekLabels_ReturnsLastCommandName()
        {
            var stack = new UndoStack();
            stack.Do("alpha", () => { }, () => { });
            stack.Do("beta",  () => { }, () => { });
            Assert.AreEqual("beta", stack.PeekUndoLabel());
            stack.Undo();
            Assert.AreEqual("beta", stack.PeekRedoLabel());
            Assert.AreEqual("alpha", stack.PeekUndoLabel());
        }

        [Test]
        public void ChangedEvent_FiresOnDoUndoRedoClear()
        {
            var stack = new UndoStack();
            int fires = 0;
            stack.Changed += () => fires++;
            stack.Do("x", () => { }, () => { });
            stack.Undo();
            stack.Redo();
            stack.Clear();
            Assert.AreEqual(4, fires);
        }
    }
}
