# Path: tests/test_fsm_integration.py

from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState
from roguelike_game.ecs.systems.fsm.states.monster.flee_state import FleeState
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState

class DummyEntity:
    """
    Entidad dummy para pruebas de FSM.
    """
    def __init__(self):
        self.world = None
        self.id = 0


def test_full_fsm_cycle(world):
    """
    Test de integración completo de ciclo de FSM: Idle -> Patrol -> Aggro -> Attack -> Flee -> Death.
    """
    entity = DummyEntity()
    # Asignar mundo y jugador para AttackState
    entity.world = world
    world.player_entity = entity.id
    fsm = FiniteStateMachine(IdleState())
    # Entrar a Idle
    fsm.current_state.enter(entity)
    # Cambiar a Patrol
    fsm.change_state(PatrolState(), entity)
    assert isinstance(fsm.current_state, PatrolState)
    # Cambiar a Aggro
    fsm.change_state(AggroState(), entity)
    assert isinstance(fsm.current_state, AggroState)
    # Cambiar a Attack
    fsm.change_state(AttackState(), entity)
    assert isinstance(fsm.current_state, AttackState)
    # Cambiar a Flee
    fsm.change_state(FleeState(), entity)
    assert isinstance(fsm.current_state, FleeState)
    # Cambiar a Death
    fsm.change_state(DeathState(), entity)
    assert isinstance(fsm.current_state, DeathState)