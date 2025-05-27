"""
Module: ability_system.py
Sistema que procesa intenciones de lanzamiento de hechizo (WantsToCastSpell).
"""
from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity

class AbilitySystem:
    """
    Procesa eventos de WantsToCastSpell y genera solicitudes o efectos.
    """
    def __init__(self):
        pass

    def update(self, world, camera):
        print("[AbilitySystem] update called")
        # Posición del ratón en coordenadas de pantalla
        mx, my = pygame.mouse.get_pos()
        # Convertir a coordenadas del mundo según cámara
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        print(f"[AbilitySystem] Mouse world pos: ({wx:.2f}, {wy:.2f})")
        # Para cada intención de hechizo
        for eid, intent in list(world.components.get('WantsToCastSpell', {}).items()):
            print(f"[AbilitySystem] Processing intent: caster={intent.caster}, spell={intent.spell}")
            if intent.spell == 'pixel_fire':
                pos = world.components['Position'][eid]
                dx = wx - pos.x
                dy = wy - pos.y
                # Normalizar dirección y crear fireball en ECS
                length = (dx**2 + dy**2)**0.5 or 1
                ndx, ndy = dx/length, dy/length
                from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
                # Crear entidad fireball
                fid = world.create_entity()
                world.components['Position'][fid] = Position(pos.x, pos.y)
                world.components['Velocity'][fid] = Velocity(ndx * 15, ndy * 15)
                world.components['FireballComponent'][fid] = FireballComponent(ndx * 15, ndy * 15)
                # Limpiar intención
                world.remove_entity(eid)
            # Aquí podrías instanciar la lógica de hechizo (spawn de efecto, cooldowns, etc.)
            # Ejemplo: world.components.setdefault('SpawnRequest', {})[eid] = SpawnRequest(...)
            # Limpiar la intención al procesar
            # del world.components['WantsToCastSpell'][eid]
            # print(f"[AbilitySystem] Removed intent for caster={eid}")
