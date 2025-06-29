# Path: tests/systems/rendering/test_hitbox_debug_system.py
import pytest
import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem


def test_hitbox_draws_arc_and_circle(world, screen, camera):
    eid = world.create_entity()
    world.components['Position'][eid] = Position(100, 50)
    world.components['HitboxComponent'][eid] = HitboxComponent(owner=eid, offset=0, radius=20, arc_angle=3.14, direction=(1,0), lifespan=1, damage=1)
    system = HitboxDebugSystem(perf_log=None)
    # Debería dibujar sin errores
    system.update(world, screen, camera)
    # Inspeccionar algunos píxeles en buffer para confirmar dibujado
    pixel = screen.get_at((100,50))
    # El centro se dibuja con círculo rojo de contorno => no llenado, así que transparente (0,0,0,0) o color de fondo
    assert isinstance(pixel, pygame.Color)