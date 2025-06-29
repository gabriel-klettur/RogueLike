# Path: src/roguelike_game/ecs/systems/rendering/fsm/states_debug_render_system.py
import pygame
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_engine.utils.benchmark import benchmark
import roguelike_engine.config.config as config

class StatesDebugRenderSystem:
    """
    Dibuja una etiqueta con el nombre del estado FSM sobre cada NPC.
    """
    def __init__(self, perf_log):
        self.font = pygame.font.SysFont(None, 14)
        self.perf_log = perf_log
        # cache rendered labels per state
        self.text_cache = {}

    @benchmark(lambda self: self.perf_log, "4.2.2.StatesDebugRenderSystem.update")
    def update(self, world, screen, camera):
        # view frustum culling
        view_rect = pygame.Rect(0, 0, camera.screen_width, camera.screen_height)

        # Solo debug de entidades (F12)
        if not config.DEBUG_ENTITIES:
            return

        comps = world.components
        for eid in world.get_entities_with('NPCState', 'Position', 'Sprite'):
            state_name = comps['NPCState'][eid].fsm.current_state.__class__.__name__
            pos = comps['Position'][eid]
            sprite_cmp = comps['Sprite'][eid]
            w, h = sprite_cmp.image.get_size()
            # Ajustar por escala del sprite
            scale_cmp = comps.get('Scale', {}).get(eid, Scale(scale=1.0))
            w *= scale_cmp.scale
            h *= scale_cmp.scale
            # calcular posición en pantalla
            x = (pos.x - camera.offset_x + w/2) * camera.zoom
            y = (pos.y - camera.offset_y) * camera.zoom
            # culling labels off-screen
            if not view_rect.collidepoint(x, y):
                continue
            label = self.text_cache.get(state_name)
            if label is None:
                label = self.font.render(state_name, True, (255, 255, 255))
                self.text_cache[state_name] = label
            lw, lh = label.get_size()
            screen.blit(label, (x - lw/2, y - lh))