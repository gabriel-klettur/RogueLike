"""FSM robustness tests: ensure state tracking uses classes, not instances.

These tests guard against regressions like:
TypeError: unhashable type: 'AttackState'
by verifying that FiniteStateMachine stores state classes in _seen_states
and _history, so unhashable dataclass state instances won't break it.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import pygame
import pytest

from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.systems.fsm.state import State


class _DummyEntity:
    """Minimal entity stub providing an id used by logging."""

    def __init__(self, eid: int = 1) -> None:
        self.id = eid


class _Idle(State):
    """Trivial state used as initial state for tests."""

    def enter(self, entity: Any) -> None:  # noqa: D401 - FSM API
        pass

    def execute(self, entity: Any, dt: float) -> None:  # noqa: D401 - FSM API
        pass

    def exit(self, entity: Any) -> None:  # noqa: D401 - FSM API
        pass


@dataclass
class _UnhashableDataclassState(State):
    """Dataclass state that is intentionally unhashable (default dataclass eq sets __hash__=None)."""

    some_field: int = 0

    def enter(self, entity: Any) -> None:
        pass

    def execute(self, entity: Any, dt: float) -> None:
        pass

    def exit(self, entity: Any) -> None:
        pass


def test_change_state_with_unhashable_dataclass_instance_tracks_by_type() -> None:
    """Ensure FSM change_state works even if the new state instance is unhashable.

    Asserts that _seen_states and _history hold classes, not instances.
    """
    fsm = FiniteStateMachine(initial_state=_Idle())
    ent = _DummyEntity(42)

    # Sanity: dataclass instances like this are typically unhashable (hash() raises TypeError)
    with pytest.raises(TypeError):
        hash(_UnhashableDataclassState())

    new_state = _UnhashableDataclassState()

    # Should not raise
    fsm.change_state(new_state, ent)

    # Types (classes) are recorded
    assert _Idle in fsm._seen_states  # type: ignore[attr-defined]
    assert _UnhashableDataclassState in fsm._seen_states  # type: ignore[attr-defined]

    # History stores class-to-class transitions
    assert (type(_Idle()), type(_UnhashableDataclassState())) in fsm._history  # type: ignore[attr-defined]


def test_debug_draw_smoke_does_not_crash_with_class_tracking() -> None:
    """Smoke test: debug_draw should run without exceptions on a simple surface."""
    fsm = FiniteStateMachine(initial_state=_Idle())
    ent = _DummyEntity(7)
    fsm.change_state(_UnhashableDataclassState(), ent)

    # Use a tiny surface; conftest ensures pygame is initialized headless
    surf = pygame.Surface((200, 200))

    # Should not raise; no assertion needed beyond not crashing
    fsm.debug_draw(surf)
