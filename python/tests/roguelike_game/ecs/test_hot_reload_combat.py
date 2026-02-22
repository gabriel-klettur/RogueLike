import math
import pygame
import pytest

from roguelike_game.config.hot_reload import reload_all_game_data_and_code
from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem


class _Pos:
    __slots__ = ("x", "y")
    def __init__(self, x: float, y: float):
        self.x = float(x)
        self.y = float(y)


class _Vel:
    __slots__ = ("vx", "vy")
    def __init__(self, vx: float, vy: float):
        self.vx = float(vx)
        self.vy = float(vy)


class _Health:
    __slots__ = ("current_hp", "max_hp")
    def __init__(self, hp: float):
        self.current_hp = float(hp)
        self.max_hp = float(hp)


class _RectCol:
    __slots__ = ("offset_x", "offset_y", "width", "height")
    def __init__(self, ox: float, oy: float, w: int, h: int):
        self.offset_x = float(ox)
        self.offset_y = float(oy)
        self.width = int(w)
        self.height = int(h)


class _MultiCol:
    __slots__ = ("colliders",)
    def __init__(self, colliders: dict):
        self.colliders = colliders


class _Camera:
    def __init__(self, w=800, h=600, zoom=1.0):
        self.screen_width = w
        self.screen_height = h
        self.zoom = zoom
        self.offset_x = 0
        self.offset_y = 0
    def apply(self, pos):
        # Identity mapping for tests
        return (float(pos[0]), float(pos[1]))


class _DummyMap:
    def __init__(self):
        self.solid_tiles = []


def _make_world():
    screen = pygame.Surface((64, 64))
    world = ECSWorld(screen, _DummyMap(), buildings=[])
    # Provide minimal world.state used by some systems for flags
    class _State: pass
    world.state = _State()
    return world


class _GameLike:
    def __init__(self, world, screen):
        class _EcsWrap:
            def __init__(self, w):
                self.ecs_world = w
        self.ecs = _EcsWrap(world)
        self.screen = screen
        # Minimal attributes referenced by loaders are optional and guarded by try/except


def _spawn_target(world, x=100, y=100, w=24, h=24):
    t = world.create_entity()
    world.components['Position'][t] = _Pos(x, y)
    world.components['MultiCollider'][t] = _MultiCol({'body': _RectCol(-w/2, -h/2, w, h)})
    world.components['Health'][t] = _Health(100)
    return t


def _spawn_fireball(world, x, y, vx, vy, damage=10, caster=None):
    # Avoid importing component class; systems use attribute access only
    class _Fireball:
        __slots__ = ("damage", "lifespan", "age", "caster", "spell_key", "spawn_pos")
        def __init__(self):
            self.damage = float(damage)
            self.lifespan = 120
            self.age = 0
            self.caster = caster
            self.spell_key = "test_fireball"
            self.spawn_pos = (float(x), float(y))
    eid = world.create_entity()
    world.components['Position'][eid] = _Pos(x, y)
    world.components['Velocity'][eid] = _Vel(vx, vy)
    world.components['FireballComponent'][eid] = _Fireball()
    return eid


def _spawn_player_hitbox(world, owner_eid, x, y, radius=40, arc_deg=120, direction=(1.0, 0.0), damage=999):
    from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
    eid = world.create_entity()
    world.components['Position'][eid] = _Pos(x, y)
    hb = HitboxComponent(owner=owner_eid, offset=0.0, radius=float(radius), arc_angle=math.radians(arc_deg), direction=direction, lifespan=5, damage=float(damage))
    hb.follow_owner = False
    hb.rotate_with_owner = False
    world.components['HitboxComponent'][eid] = hb
    return eid


@pytest.mark.timeout(10)
def test_hot_reload_keeps_fireball_hits_working(monkeypatch):
    # Patch ECS to include only the minimal systems needed
    import roguelike_game.ecs.core.manager as mngr
    monkeypatch.setattr(mngr, 'get_update_system_classes', lambda: [FireballSystem, HitboxSystem], raising=False)
    monkeypatch.setattr(mngr, 'get_render_system_classes', lambda: [], raising=False)
    world = _make_world()
    camera = _Camera()
    screen = pygame.Surface((64, 64))
    game = _GameLike(world, screen)

    # Spawn target and a colliding fireball
    target = _spawn_target(world, x=100, y=100, w=30, h=30)
    fb = _spawn_fireball(world, x=100, y=100, vx=0, vy=0, damage=15)

    # Two updates so fireball.age >= 1 and collision is processed
    world.update(camera)
    world.update(camera)

    hp_after_1 = world.components['Health'][target].current_hp
    assert hp_after_1 < 100.0, "Fireball should damage target before reload"

    # Hot-reload (force): patch internals to avoid heavy I/O and focus on system reinit
    import roguelike_game.config.hot_reload as hr
    monkeypatch.setattr(hr, 'reload_all_game_data', lambda _g, force=False: {}, raising=False)
    monkeypatch.setattr(hr, 'reload_changed_python_modules', lambda force=False: 1, raising=False)
    reload_all_game_data_and_code(game, force=True)

    # Spawn another fireball and repeat
    fb2 = _spawn_fireball(world, x=100, y=100, vx=0, vy=0, damage=15)
    world.update(camera)
    world.update(camera)

    hp_after_2 = world.components['Health'][target].current_hp
    assert hp_after_2 < hp_after_1, "Fireball should still damage after hot-reload"


@pytest.mark.timeout(10)
def test_melee_hitbox_clears_fireballs(monkeypatch):
    # Patch ECS to include only the minimal systems needed
    import roguelike_game.ecs.core.manager as mngr
    monkeypatch.setattr(mngr, 'get_update_system_classes', lambda: [FireballSystem, HitboxSystem], raising=False)
    monkeypatch.setattr(mngr, 'get_render_system_classes', lambda: [], raising=False)
    world = _make_world()
    camera = _Camera()

    # Create player entity (owner of hitbox)
    player = world.create_entity()
    world.components['Position'][player] = _Pos(50, 50)
    world.components.setdefault('PlayerTagComponent', {})[player] = object()

    # Fireball near player, inside melee arc
    fb = _spawn_fireball(world, x=70, y=50, vx=0, vy=0, damage=1)

    # Create a melee hitbox oriented to +X with enough radius
    hb = _spawn_player_hitbox(world, owner_eid=player, x=50, y=50, radius=40, arc_deg=160, direction=(1.0, 0.0), damage=0)

    # Update once: the hitbox arc should intersect the fireball and remove it
    world.update(camera)

    assert fb not in world.components.get('FireballComponent', {}), "Melee hitbox should destroy fireball in arc"
