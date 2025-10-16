import types

import roguelike_game.ecs.systems.fsm.fsm_system as fsm_mod
from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.systems.fsm.states.damage_state import DamageState
from roguelike_game.ecs.components.rendering.flash_component import FlashComponent


class FakeFSM:
    def __init__(self, state):
        self.current_state = state
        self.changed_to = None
        self.context = {}
    def update(self, entity, dt):
        pass
    def change_state(self, state, entity):
        self.changed_to = state
        self.current_state = state


def test_on_death_transitions_to_unconscious():
    sys = fsm_mod.FSMSystem(perf_log=None)
    eid = 1
    world = types.SimpleNamespace(components={
        'NPCState': {eid: types.SimpleNamespace(fsm=FakeFSM(state=AttackState()))},
        'FSMEventQueue': {eid: [{'type': 'OnDeath'}]},
    })

    sys.update(world)

    fsm = world.components['NPCState'][eid].fsm
    assert isinstance(fsm.current_state, UnconsciousState)


def test_on_hit_either_damage_or_flash(monkeypatch):
    # Forzar que random.random() < stop_probability para entrar en DamageState
    monkeypatch.setattr(fsm_mod, 'random', types.SimpleNamespace(random=lambda: 0.0))

    sys = fsm_mod.FSMSystem(perf_log=None)
    eid = 2
    fsm = FakeFSM(state=AttackState())
    world = types.SimpleNamespace(components={
        'NPCState': {eid: types.SimpleNamespace(fsm=fsm)},
        'FSMEventQueue': {eid: [{'type': 'OnHit', 'from_left': True}]},
        'DamageConfig': {eid: types.SimpleNamespace(stop_probability=1.0, duration=0.2)},
    })

    sys.update(world)

    assert isinstance(fsm.current_state, DamageState)

    # Ahora probar la ruta sin stun: debe aplicar FlashComponent y no cambiar de estado
    monkeypatch.setattr(fsm_mod, 'random', types.SimpleNamespace(random=lambda: 0.99))
    fsm = FakeFSM(state=AttackState())
    world = types.SimpleNamespace(components={
        'NPCState': {eid: types.SimpleNamespace(fsm=fsm)},
        'FSMEventQueue': {eid: [{'type': 'OnHit', 'from_left': False}]},
        'DamageConfig': {eid: types.SimpleNamespace(stop_probability=0.0, duration=0.1)},
    })

    sys.update(world)

    # Debe existir FlashComponent y el estado seguir siendo AttackState
    assert eid in world.components.get('FlashComponent', {})
    fc = world.components['FlashComponent'][eid]
    assert isinstance(fc, FlashComponent)
    assert isinstance(fsm.current_state, AttackState)
