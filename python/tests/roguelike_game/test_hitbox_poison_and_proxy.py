import types
import pygame
import pytest

from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem
from roguelike_game.ecs.components.status.poison_component import PoisonComponent
from roguelike_game.managers.core.render.npc_render_proxy import _NPCWrapper


class _C:
    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


class _Sprite:
    def __init__(self, w=2, h=2, color=(10, 10, 10)):
        self.image = pygame.Surface((w, h), pygame.SRCALPHA)
        self.image.fill(color)


@pytest.mark.parametrize("use_element", [False, True])
def test_hitbox_applies_poison_on_status_or_element(world, camera, use_element):
    target = world.create_entity()
    world.components.setdefault("Health", {})[target] = types.SimpleNamespace(current_hp=50)
    world.components.setdefault("Position", {})[target] = types.SimpleNamespace(x=5.0, y=0.0)

    hb_eid = world.create_entity()
    world.components.setdefault("Position", {})[hb_eid] = types.SimpleNamespace(x=0.0, y=0.0)

    status = {"poison": {"dps": 3, "duration": 2.0, "tick_period": 0.5}} if not use_element else None
    element = "poison" if use_element else ""

    hb = _C(
        owner=hb_eid,
        direction=(1.0, 0.0),
        arc_angle=6.28318530718,
        radius=32.0,
        damage=0,
        lifespan=2,
        hit_targets=set(),
        follow_owner=False,
        rotate_with_owner=False,
        offset=0.0,
        status=status,
        element=element,
    )
    world.components.setdefault("HitboxComponent", {})[hb_eid] = hb

    HitboxSystem().update(world, camera)

    poisons = world.components.get("PoisonComponent", {})
    assert target in poisons, "Hitbox should add PoisonComponent from status or element=poison"
    pc = poisons[target]
    assert isinstance(pc, PoisonComponent)
    assert pc.damage_per_tick >= 3
    assert pc.duration >= 2.0
    assert pc.tick_period <= 0.5


def test_npc_render_proxy_not_tinting_burn_without_godmode(world, camera):
    eid = world.create_entity()
    world.components.setdefault("Position", {})[eid] = types.SimpleNamespace(x=0.0, y=0.0)
    world.components.setdefault("Sprite", {})[eid] = _Sprite(color=(10, 10, 10))
    # Add BurnComponent to entity and ensure proxy does not apply red tint by itself
    world.components.setdefault("BurnComponent", {})[eid] = types.SimpleNamespace(
        start_time=0.0, tick_period=0.5, duration=5.0
    )

    screen = pygame.Surface((8, 8), pygame.SRCALPHA)

    proxy = _NPCWrapper(world, eid)
    proxy.render(screen, camera)

    # The pixel where the sprite was blitted should remain the base color (no red tint)
    px = screen.get_at((0, 0))
    assert (px.r, px.g, px.b) == (10, 10, 10)
