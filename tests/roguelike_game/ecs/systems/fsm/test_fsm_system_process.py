import types

import roguelike_game.ecs.systems.fsm.fsm_system as fsm_mod
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState


def test_fsm_process_after_attack_json_transition(monkeypatch):
    # Controlar tiempo para cumplir condición after_attack
    t0 = 10000.0
    monkeypatch.setattr('time.time', lambda: t0 + 0.2)

    # Stub get_state_class para resolver 'IdleState'
    class IdleStateStub:
        pass
    monkeypatch.setattr(fsm_mod, 'get_state_class', lambda name: IdleStateStub if name == 'IdleState' else None)

    # FSM con contexto de transición JSON
    class FakeFSM:
        def __init__(self):
            self.current_state = AttackState()
            self.context = {
                'transitions': [
                    {'when': 'after_attack', 'from': 'ATT', 'to': 'IDLE'}
                ],
                'class_to_id': {'AttackState': 'ATT'},
                'id_to_class': {'IDLE': 'IdleState'},
                'attack_start': t0,
                'attack_duration': 0.1,
            }
        def update(self, entity, dt):
            pass
        def change_state(self, state, entity):
            self.current_state = state

    eid = 1
    fsm = FakeFSM()
    world = types.SimpleNamespace(components={
        'NPCState': {eid: types.SimpleNamespace(fsm=fsm)},
        'FSMEventQueue': {},
    })

    sys = fsm_mod.FSMSystem(perf_log=None)
    sys.update(world)

    # Debe haber transicionado a IdleStateStub
    assert isinstance(world.components['NPCState'][eid].fsm.current_state, IdleStateStub)
