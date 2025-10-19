import dataclasses
from types import SimpleNamespace

from roguelike_game.ecs.components.fsm.npc_state import NPCState


def test_npc_state_asdict_serialization():
    dummy_fsm = SimpleNamespace(name="fsm")
    st = NPCState(fsm=dummy_fsm, current="patrol")
    data = dataclasses.asdict(st)
    assert data["current"] == "patrol"
    # The FSM object is embedded as-is; type check is sufficient
    assert isinstance(data["fsm"], SimpleNamespace)
