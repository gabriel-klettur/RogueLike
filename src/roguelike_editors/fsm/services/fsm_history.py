"""Undo/Redo history using Command pattern (skeleton)."""
from __future__ import annotations
from typing import Protocol, List


class Command(Protocol):
    def apply(self) -> None: ...
    def undo(self) -> None: ...


class History:
    def __init__(self) -> None:
        self._done: List[Command] = []
        self._undone: List[Command] = []

    def do(self, cmd: Command) -> None:
        cmd.apply()
        self._done.append(cmd)
        self._undone.clear()

    def undo(self) -> None:
        if not self._done:
            return
        cmd = self._done.pop()
        cmd.undo()
        self._undone.append(cmd)

    def redo(self) -> None:
        if not self._undone:
            return
        cmd = self._undone.pop()
        cmd.apply()
        self._done.append(cmd)


__all__ = ["Command", "History"]
