from __future__ import annotations

from roguelike_editors.fsm.services import fsm_registry as reg
from roguelike_game.ecs.systems.fsm.state import State


def test_registry_resolves_core_classes():
    cls = reg.get_state_class("IdleState")
    assert cls is not None
    assert issubclass(cls, State)
    inst = cls()  # type: ignore[call-arg]
    # Instance should expose enter/execute/exit methods
    assert hasattr(inst, "enter") and hasattr(inst, "execute") and hasattr(inst, "exit")


def test_registry_unknown_returns_none():
    assert reg.get_state_class("NonExistentStateClassXYZ") is None
