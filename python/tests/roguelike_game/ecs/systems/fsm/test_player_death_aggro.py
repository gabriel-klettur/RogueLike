import types
import time
import pytest

from types import SimpleNamespace

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.components.fsm.patrol_route import PatrolRoute
from roguelike_game.ecs.components.combat.death_timer import DeathTimer

from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState


class DummyFSM:
    """Minimal FSM stub that records transitions without calling enter/exit."""
    def __init__(self):
        self.transitions = []
        self.context = {}
        self.current_state = None

    def change_state(self, new_state, entity):
        self.transitions.append(new_state.__class__.__name__)
        self.current_state = new_state


class EntityProxy:
    """Hashable proxy with id equality compatible with int keys (like production _EntityProxy)."""
    def __init__(self, world, entity_id):
        self.world = world
        self.id = entity_id

    def __hash__(self):
        return hash(self.id)

    def __eq__(self, other):
        if isinstance(other, EntityProxy):
            return self.id == other.id
        return other == self.id


class World:
    """Minimal world to satisfy state expectations in tests."""
    def __init__(self):
        # component_name -> {eid: component}
        self.components = {
            'Velocity': {},
            'Position': {},
            'MovementSpeed': {},
            'PatrolRoute': {},
            'Health': {},
            'AggroRange': {},
            'NPCState': {},
            'DeathTimer': {},
        }
        self._next_id = 1
        self.player_entity = None
        self.player_position = None

    def create_entity(self):
        eid = self._next_id
        self._next_id += 1
        return eid


def build_npc_and_player(world: World, player_alive: bool, with_death_timer: bool, in_range: bool = True):
    # Create player
    pid = world.create_entity()
    world.player_entity = pid
    # Player position near NPC if in_range else far
    px, py = (100, 100)
    world.player_position = Position(px, py)
    # Player health
    if player_alive:
        world.components['Health'][pid] = Health(current_hp=100, max_hp=100)
    else:
        world.components['Health'][pid] = Health(current_hp=0, max_hp=100)
    # Player death timer if requested
    if with_death_timer:
        world.components['DeathTimer'][pid] = DeathTimer(start_time=time.time(), duration=60.0)

    # Create NPC
    eid = world.create_entity()
    world.components['Health'][eid] = Health(current_hp=50, max_hp=50)
    world.components['Velocity'][eid] = Velocity(0, 0)
    # NPC position: set near or far from player
    if in_range:
        world.components['Position'][eid] = Position(px + 8, py + 8)  # close
    else:
        world.components['Position'][eid] = Position(px + 10_000, py + 10_000)  # far
    world.components['MovementSpeed'][eid] = MovementSpeed(speed=50.0)
    # Patrol route with a single point so PatrolState has required component
    world.components['PatrolRoute'][eid] = PatrolRoute(points=[(px + 8, py + 8)], dwell_times=None)
    # Aggro range in tiles (use radius 10 to easily be in range)
    world.components['AggroRange'][eid] = AggroRange(radius=10)

    # Minimal NPCState with DummyFSM
    fsm = DummyFSM()
    world.components['NPCState'][eid] = SimpleNamespace(fsm=fsm)

    # Entity proxy passed into states (hashable, int-compatible)
    entity = EntityProxy(world, eid)
    return eid, entity, fsm


@pytest.mark.parametrize(
    "player_alive,with_dt",
    [
        (False, False),  # dead, no death timer
        (False, True),   # dead, has death timer
        (True, True),    # alive but has death timer (treated as KO)
    ],
)
def test_patrol_does_not_aggro_when_player_is_ko_or_has_death_timer(player_alive, with_dt):
    world = World()
    eid, entity, fsm = build_npc_and_player(world, player_alive=player_alive, with_death_timer=with_dt, in_range=True)

    state = PatrolState()
    # Execute once; if guard works, there is no Aggro transition
    state.execute(entity, dt=0.016)

    assert 'AggroState' not in fsm.transitions, "PatrolState should not enter Aggro when player is KO or has DeathTimer"


def test_patrol_enters_aggro_when_player_alive_and_in_range():
    world = World()
    eid, entity, fsm = build_npc_and_player(world, player_alive=True, with_death_timer=False, in_range=True)

    state = PatrolState()
    state.execute(entity, dt=0.016)

    assert 'AggroState' in fsm.transitions, "PatrolState should enter Aggro when player is alive and within AggroRange"


def test_attack_state_returns_to_patrol_if_player_becomes_ko():
    world = World()
    eid, entity, fsm = build_npc_and_player(world, player_alive=False, with_death_timer=True, in_range=True)

    # AttackState should immediately detect KO/DeathTimer and switch back to Patrol
    state = AttackState()
    state.execute(entity, dt=0.016)

    assert 'PatrolState' in fsm.transitions, "AttackState should return to Patrol when player is KO/has DeathTimer"


def test_aggro_state_returns_to_patrol_if_player_is_ko():
    world = World()
    eid, entity, fsm = build_npc_and_player(world, player_alive=False, with_death_timer=True, in_range=True)

    # AggroState should detect KO/DeathTimer and go back to Patrol
    state = AggroState()
    state.execute(entity, dt=0.016)

    assert 'PatrolState' in fsm.transitions, "AggroState should return to Patrol when player is KO/has DeathTimer"


def test_chase_state_returns_to_patrol_if_player_is_ko():
    world = World()
    eid, entity, fsm = build_npc_and_player(world, player_alive=False, with_death_timer=True, in_range=True)

    # ChaseState should detect KO/DeathTimer and go back to Patrol
    state = ChaseState()
    state.execute(entity, dt=0.016)

    assert 'PatrolState' in fsm.transitions, "ChaseState should return to Patrol when player is KO/has DeathTimer"
