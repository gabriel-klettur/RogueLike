from __future__ import annotations
from typing import List, Optional

class Command:
    """
    Minimal command protocol for HistoryManager.
    Subclasses must implement apply() and undo().
    """
    description: str = ""

    def apply(self) -> None:  # pragma: no cover - interface
        raise NotImplementedError

    def undo(self) -> None:  # pragma: no cover - interface
        raise NotImplementedError


class HistoryManager:
    """
    Simple undo/redo history with capacity limit.
    push(cmd) applies the command and appends it to the undo stack,
    clearing the redo stack.
    """
    def __init__(self, max_size: int = 200) -> None:
        self._undo_stack: List[Command] = []
        self._redo_stack: List[Command] = []
        self._max_size = max(1, int(max_size))

    @property
    def can_undo(self) -> bool:
        return len(self._undo_stack) > 0

    @property
    def can_redo(self) -> bool:
        return len(self._redo_stack) > 0

    def clear(self) -> None:
        self._undo_stack.clear()
        self._redo_stack.clear()

    def push(self, cmd: Command) -> None:
        """Apply command and push onto undo stack. Clears redo stack."""
        cmd.apply()
        self._undo_stack.append(cmd)
        if len(self._undo_stack) > self._max_size:
            # Drop oldest
            self._undo_stack.pop(0)
        # New action invalidates redo history
        self._redo_stack.clear()

    def undo(self) -> bool:
        if not self._undo_stack:
            return False
        cmd = self._undo_stack.pop()
        cmd.undo()
        self._redo_stack.append(cmd)
        return True

    def redo(self) -> bool:
        if not self._redo_stack:
            return False
        cmd = self._redo_stack.pop()
        cmd.apply()
        self._undo_stack.append(cmd)
        return True
