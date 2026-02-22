import pytest
from roguelike_editors.entities.services.history import HistoryManager, Command


class IncrCmd(Command):
    def __init__(self, box):
        self.box = box
    def apply(self):
        self.box[0] += 1
    def undo(self):
        self.box[0] -= 1


def test_history_push_undo_redo_and_capacity():
    hist = HistoryManager(max_size=2)
    box = [0]

    # push/apply 1,2
    hist.push(IncrCmd(box))
    hist.push(IncrCmd(box))
    assert box[0] == 2
    assert hist.can_undo is True and hist.can_redo is False

    # undo twice
    assert hist.undo() is True
    assert box[0] == 1
    assert hist.undo() is True
    assert box[0] == 0
    assert hist.can_undo is False and hist.can_redo is True

    # redo twice
    assert hist.redo() is True
    assert hist.redo() is True
    assert box[0] == 2

    # capacity: pushing a third drops the oldest
    hist.push(IncrCmd(box))  # now undo stack has size 2 (the last two commands)
    # Undo thrice should only undo 2 actions
    assert hist.undo() is True
    assert hist.undo() is True
    assert hist.undo() is False
