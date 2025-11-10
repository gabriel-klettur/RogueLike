import pytest

from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.combat.health import Health


@pytest.fixture()
def sys_fb():
    return FireballSystem(perf_log=None)


def _ensure_maps(world):
    world.components.setdefault('Position', {})
    world.components.setdefault('Velocity', {})
    world.components.setdefault('FireballComponent', {})
    world.components.setdefault('MultiCollider', {})
    world.components.setdefault('Health', {})
    world.components.setdefault('PlayerTagComponent', {})
    world.components.setdefault('FSMEventQueue', {})
    world.components.setdefault('ComboEventQueue', [])


def test_onhit_event_when_player_hits_npc(world, sys_fb):
    _ensure_maps(world)
    # Mark caster as player
    caster = world.create_entity()
    world.components['Position'][caster] = Position(100, 100)
    world.components['PlayerTagComponent'][caster] = object()

    # Target in front
    target = world.create_entity()
    world.components['Position'][target] = Position(160, 100)
    world.components['MultiCollider'][target] = MultiCollider({'body': Collider(20, 20, -10, -10)})
    world.components['Health'][target] = Health(current_hp=10, max_hp=10)

    # Projectile from caster towards target
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(20, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=20, dy=0, damage=3, lifespan=60, caster=caster, spell_key='t_evt', spawn_pos=(120, 100), hit_radius=3.0)

    # Step until removal
    for _ in range(10):
        sys_fb.update(world)
        if pid not in world.components.get('FireballComponent', {}):
            break

    # Check OnHit queued for target
    qmap = world.components.get('FSMEventQueue', {})
    q = qmap.get(target, [])
    assert q and any(e.get('type') == 'OnHit' for e in q)


def test_godmode_attacker_oneshots(world, sys_fb):
    _ensure_maps(world)
    # Player caster with godmode flag in world.state
    caster = world.create_entity()
    world.components['Position'][caster] = Position(100, 100)
    world.components['PlayerTagComponent'][caster] = object()
    class _State: pass
    world.state = _State()
    world.state.godmode = True

    target = world.create_entity()
    world.components['Position'][target] = Position(160, 100)
    world.components['MultiCollider'][target] = MultiCollider({'body': Collider(20, 20, -10, -10)})
    world.components['Health'][target] = Health(current_hp=15, max_hp=15)

    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(20, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=20, dy=0, damage=3, lifespan=60, caster=caster, spell_key='t_gm', spawn_pos=(120, 100), hit_radius=3.0)

    for _ in range(10):
        sys_fb.update(world)
        if pid not in world.components.get('FireballComponent', {}):
            break

    assert world.components['Health'][target].current_hp == 0
