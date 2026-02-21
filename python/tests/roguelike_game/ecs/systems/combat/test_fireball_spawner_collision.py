import pygame
import pytest

from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent


class _Model:
    def __init__(self, w, h):
        self.image = pygame.Surface((w, h), pygame.SRCALPHA)
        self.image.fill((255, 255, 255, 255))
        self._mask = pygame.mask.from_surface(self.image)

    def get_full_mask(self):
        return self._mask


class _SpawnerVisual:
    def __init__(self, x, y, w, h, eid):
        self.x = float(x)
        self.y = float(y)
        self.runtime_hidden = False
        self._is_spawner_visual = True
        self._spawner_visual_life_cfg = {"damageable": True}
        self.model = _Model(w, h)
        self._spawner_eid = eid


@pytest.fixture()
def sys_fb():
    return FireballSystem(perf_log=None)


def _ensure_maps(world):
    world.components.setdefault('Position', {})
    world.components.setdefault('Velocity', {})
    world.components.setdefault('FireballComponent', {})
    world.components.setdefault('SpawnerDamageEvents', [])


def test_fireball_hits_spawner_visual_appends_event_and_removes(world, sys_fb):
    _ensure_maps(world)
    # Attach a spawner visual building to the world
    se = 999  # arbitrary spawner id
    b = _SpawnerVisual(x=180, y=90, w=40, h=40, eid=se)
    world.buildings = [b]

    # Fireball moving right into the visual area
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(30, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=30, dy=0, damage=7, lifespan=120, caster=None, spell_key='t_spawner', spawn_pos=(120, 100), hit_radius=3.0)

    # Step until removal
    for _ in range(10):
        sys_fb.update(world)
        if pid not in world.components.get('FireballComponent', {}):
            break

    assert pid not in world.components.get('FireballComponent', {})
    events = world.components.get('SpawnerDamageEvents', [])
    assert events, "Expected a spawner damage event"
    ev = events[-1]
    assert ev.get('spawner_eid') == se
    assert ev.get('damage') == 7
