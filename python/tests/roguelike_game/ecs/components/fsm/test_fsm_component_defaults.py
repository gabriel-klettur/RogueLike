from types import SimpleNamespace

from roguelike_game.ecs.components.fsm.npc_state import NPCState


def test_npc_state_holds_fsm_and_current_token():
    dummy_fsm = SimpleNamespace(name="dummy_fsm")
    st = NPCState(fsm=dummy_fsm, current="idle")
    assert st.fsm is dummy_fsm
    assert st.current == "idle"
