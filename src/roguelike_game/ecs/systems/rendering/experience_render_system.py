import pygame
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent


class ExperienceRenderSystem:
    """
    Sistema para renderizar la barra de experiencia en HUD.
    """
    def __init__(self, perf_log=None):
        # Inicializar fuente
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 20)

    def update(self, world, screen, camera):
        # Ocultar barra de experiencia si hay UIs que deben tomar el foco
        state = getattr(world, 'state', None)
        # Entities Editor activo
        if state and getattr(state, 'entities_editor_state', None) and state.entities_editor_state.visible:
            return
        # Selector de clase visible
        if state and getattr(state, 'class_selector_visible', False):
            return
        # Buscar el jugador con ExperienceComponent
        xp_comps = world.components.get('ExperienceComponent', {})
        tags = world.components.get('PlayerTagComponent', {})
        for eid, xp_comp in xp_comps.items():
            if eid not in tags:
                continue
            # Dimensiones de pantalla
            screen_w, screen_h = screen.get_size()
            margin = 20
            bar_height = 10
            # Usar mitad del ancho de la pantalla y centrar
            bar_width = int(screen_w * 0.5)
            x = (screen_w - bar_width) // 2
            y = screen_h - bar_height - margin
            # Fondo
            pygame.draw.rect(screen, (50, 50, 50), (x, y, bar_width, bar_height))
            # Relleno
            ratio = xp_comp.xp / xp_comp.xp_to_next_level if xp_comp.xp_to_next_level else 0
            ratio = max(0.0, min(ratio, 1.0))
            fill_w = int(bar_width * ratio)
            pygame.draw.rect(screen, (0, 0, 255), (x, y, fill_w, bar_height))
            # Borde
            pygame.draw.rect(screen, (0, 0, 0), (x, y, bar_width, bar_height), 1)
            # Texto de nivel y XP
            text = f"Lvl {xp_comp.level}  {xp_comp.xp}/{xp_comp.xp_to_next_level}"
            text_surf = self.font.render(text, True, (255, 255, 255))
            text_rect = text_surf.get_rect(center=(screen_w // 2, y - bar_height))
            screen.blit(text_surf, text_rect)
            break  # solo jugador
