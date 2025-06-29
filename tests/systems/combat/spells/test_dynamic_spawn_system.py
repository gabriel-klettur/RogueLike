# Path: tests/systems/combat/spells/test_dynamic_spawn_system.py
import pygame
import pytest
from roguelike_game.ecs.systems.combat.spells.resolvers import ProjectileResolver
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent


def test_dynamic_spawn_creates_fireball(world, camera, monkeypatch):
    # Simular posición del ratón
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (0, 0))
    resolver = ProjectileResolver()
    # Crear caster con posición inicial
    caster = world.create_entity()
    world.components['Position'][caster] = Position(0, 0)
    # Metadatos de spawn
    spawn_meta = {'offset': 0, 'spell': 'fireball'}
    # Configuración mínima para resolver
    cfg = {'speed': 10, 'damage': 5, 'lifespan': 1, 'range': None, 'sprite': None, 'type': 'projectile'}
    before = set(world.entities)
    resolver.resolve(world, caster, spawn_meta, cfg, camera)
    spawned = set(world.entities) - before
    assert spawned, "Debe crear al menos una nueva entidad de proyectil"
    # Verificar que se creó el componente FireballComponent
    assert any(isinstance(comp, FireballComponent) for comp in world.components.get('FireballComponent', {}).values()), "FireballComponent no fue añadido"