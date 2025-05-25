import pygame
from ..components.position import Position
from ..components.identity import Identity, Faction
from ..components.scale import Scale
import roguelike_engine.config.config as config

class NamePlateSystem:
    """
    Sistema para renderizar nombre y título sobre entidades con Identity.
    """
    def __init__(self):
        pygame.font.init()
        self.name_font = pygame.font.SysFont(None, 30)
        self.title_font = pygame.font.SysFont(None, 24)
        # Cache de superficies de texto para nombres y títulos
        self.name_cache: dict[tuple[str, tuple[int,int,int]], pygame.Surface] = {}
        self.title_cache: dict[tuple[str, tuple[int,int,int]], pygame.Surface] = {}

    def update(self, world, screen, camera):
        for eid in world.get_entities_with('Position', 'Identity'):
            pos: Position = world.components['Position'][eid]
            id_comp: Identity = world.components['Identity'][eid]
            # Posición en pantalla
            screen_x, screen_y = camera.apply((pos.x, pos.y))
            # Determinar top de health bar para ubicar textos encima
            bar_margin = 2
            bar_height = 5
            bar_y = screen_y - bar_margin - bar_height
            # Color según facción
            if id_comp.faction == Faction.GOOD:
                color = (0, 0, 255)
            elif id_comp.faction == Faction.EVIL:
                color = (255, 0, 0)
            else:
                color = (128, 128, 128)
            # Obtener o renderizar nombre desde cache
            display_name = f"{id_comp.id} {id_comp.name}" if config.DEBUG else id_comp.name
            name_key = (display_name, color)
            if name_key not in self.name_cache:
                self.name_cache[name_key] = self.name_font.render(display_name, True, color)
            name_surf = self.name_cache[name_key]
            name_rect = name_surf.get_rect()
            name_rect.centerx = screen_x + (world.components['Scale'].get(eid, Scale()).scale * world.components['Sprite'][eid].image.get_width()) // 2
            # Ubicar nombre justo encima de la barra de salud
            name_rect.bottom = bar_y - 2
            screen.blit(name_surf, name_rect)
            # Renderizar título encima del nombre
            if id_comp.title:
                # Obtener o renderizar título desde cache
                title_key = (id_comp.title, color)
                if title_key not in self.title_cache:
                    self.title_cache[title_key] = self.title_font.render(id_comp.title, True, color)
                title_surf = self.title_cache[title_key]
                title_rect = title_surf.get_rect()
                title_rect.centerx = name_rect.centerx
                # Ubicar título encima del nombre
                title_rect.bottom = name_rect.top - 1
                screen.blit(title_surf, title_rect)
