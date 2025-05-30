"""
Sistema ECS que detecta 'WantsToCastSpell' y arranca el FSM de hechizos.
"""
from roguelike_game.ecs.fsm.states.cast_state import CastState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy
import pygame
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite

class SpellCastingSystem:
    def update(self, world, camera=None):
        #print(f"[ECS][SpellCastingSystem] Procesando intenciones de hechizo...")
        # Procesar intenciones de hechizo (AI y jugador)
        wants = world.components.get('WantsToCastSpell', {})
        npcs = world.components.get('NPCState', {})
        for eid in list(wants.keys()):
            intent = wants[eid]
            if eid in npcs:
                print(f"[ECS][SpellCastingSystem] Procesando intención de hechizo del NPC...")
                # AI: iniciar sub-FSM de hechizo
                npc_state = npcs[eid]
                entity = _EntityProxy(world, eid)
                npc_state.fsm.change_state(CastState(), entity)
            else:
                # Player: instant fireball hacia mouse
                print(f"[ECS][SpellCastingSystem] Procesando intención de hechizo del jugador...")

                #!-------------------- Esto no deberia moverse a la FSM????? ------------------------
                pos_cmp = world.components['Position'][eid]
                mx, my = pygame.mouse.get_pos()
                wx = mx / camera.zoom + camera.offset_x
                wy = my / camera.zoom + camera.offset_y
                dx = wx - pos_cmp.x; dy = wy - pos_cmp.y
                length = (dx*dx + dy*dy)**0.5 or 1
                dir_x, dir_y = dx/length, dy/length
                spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
                sprite_cmp = world.components['Sprite'].get(eid)
                if sprite_cmp:
                    w, h = sprite_cmp.image.get_size()
                    spawn_x += w/2; spawn_y += h/2
                cfg = SPELLS.get(intent.spell, {})
                speed = cfg.get('speed', 0)
                fid = world.create_entity()
                world.components['Position'][fid] = Position(spawn_x, spawn_y)
                world.components['Velocity'][fid] = Velocity(dir_x*speed, dir_y*speed)
                world.components['FireballComponent'][fid] = FireballComponent(
                    dir_x*speed, dir_y*speed,
                    damage=cfg.get('damage', 0), lifespan=cfg.get('lifespan', 0), caster=eid
                )
                sprite_path = cfg.get('sprite', "assets/projectiles/fireball.png")
                world.components['Sprite'][fid] = Sprite(sprite_path)
            # Eliminar intención procesada
            wants.pop(eid, None)
