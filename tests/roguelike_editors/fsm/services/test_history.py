from __future__ import annotations

from dataclasses import dataclass

from roguelike_editors.fsm.services import fsm_history as h


@dataclass
class _Counter:
    value: int = 0


class _IncCmd:
    def __init__(self, c: _Counter, delta: int) -> None:
        self.c = c
        self.d = delta

    def apply(self) -> None:
        self.c.value += self.d

    def undo(self) -> None:
        self.c.value -= self.d


def test_history_do_undo_redo_and_clear_redo():
    hist = h.History()
    c = _Counter()

    cmd1 = _IncCmd(c, 2)
    hist.do(cmd1)
    assert c.value == 2

    cmd2 = _IncCmd(c, 3)
    hist.do(cmd2)
    assert c.value == 5

    # Undo twice
    hist.undo()
    assert c.value == 2
    hist.undo()
    assert c.value == 0

    # Redo once
    hist.redo()
    assert c.value == 2

    # New do should clear redo stack
    hist.do(_IncCmd(c, 10))
    assert c.value == 12
    # Further redo should be no-op (cleared)
    hist.redo()
    assert c.value == 12
