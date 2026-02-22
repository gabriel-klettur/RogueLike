from __future__ import annotations

from typing import Protocol, List


class Command(Protocol):
    def do(self) -> None: ...
    def undo(self) -> None: ...


class UndoRedoStack:
    """Minimal undo/redo stack placeholder."""

    def __init__(self) -> None:
        self._undo: List[Command] = []
        self._redo: List[Command] = []

    def push(self, cmd: Command) -> None:
        self._undo.append(cmd)
        self._redo.clear()

    def undo(self) -> None:
        if not self._undo:
            return
        cmd = self._undo.pop()
        cmd.undo()
        self._redo.append(cmd)

    def redo(self) -> None:
        if not self._redo:
            return
        cmd = self._redo.pop()
        cmd.do()
        self._undo.append(cmd)
