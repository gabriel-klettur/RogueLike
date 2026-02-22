"""Call a simple function as a spell release handler."""

from __future__ import annotations

from typing import Callable

from .base import SpellReleaseHandler, SupportsSpellContext


class FunctionSpellReleaseHandler(SpellReleaseHandler):
    """Execute a user-provided callable for the release logic."""

    def __init__(self, callback: Callable[[SupportsSpellContext], None]) -> None:
        self._callback = callback

    def handle(self, context: SupportsSpellContext) -> None:
        self._callback(context)
