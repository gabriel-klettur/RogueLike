import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.transform.scale import Scale
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark

class NamePlateSystem:
    """
    Sistema para renderizar nombre y título sobre entidades con Identity.

    • Dibuja el nombre centrado sobre la entidad, usando color según facción.
    • En modo DEBUG, antepone el ID interno al nombre.
    • Si la entidad tiene un título, lo muestra encima del nombre.
    • Cachea superficies de texto para mejorar el rendimiento.
    """

    def __init__(self, perf_log):
        """
        Inicializa fuentes y caches.

        - name_font: fuente para nombres.
        - title_font: fuente para títulos.
        - name_cache: almacena (texto, color) → Surface.
        - title_cache: almacena (texto, color) → Surface.
        """
        pygame.font.init()
        self.name_font = pygame.font.SysFont(None, 30)
        self.title_font = pygame.font.SysFont(None, 24)
        self.name_cache: dict[tuple[str, tuple[int,int,int]], pygame.Surface] = {}
        self.title_cache: dict[tuple[str, tuple[int,int,int]], pygame.Surface] = {}
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.NamePlateSystem.update")
    def update(self, world, screen, camera):
        """
        Recorre todas las entidades con Position + Identity y dibuja:
         - Barra de salud ya posicionada (NamePlateSystem solo ajusta encima).
         - Nombre y, opcionalmente, título.

        Args:
            world: ECS con componentes y entidades.
            screen: superficie de Pygame donde dibujar.
            camera: cámara para transformación de coordenadas.
        """
        # Obtener conjuntos de componentes para acceder por clave
        comps = world.components

        # Renderizar solo entidades dentro del área de cámara
        for eid in world.get_entities_in_camera(camera, 'Position', 'Identity'):
            # 1) Recuperar componentes
            pos: Position    = comps['Position'][eid]
            id_comp: Identity = comps['Identity'][eid]

            # 2) Convertir posición del mundo a pantalla
            screen_x, screen_y = camera.apply((pos.x, pos.y))

            # 3) Calcular la altura del health bar para posicionar texto encima
            bar_margin = 2
            bar_height = 5
            bar_top_y = screen_y - bar_margin - bar_height

            # 4) Elegir color según facción
            if id_comp.faction == Faction.GOOD:
                color = (0, 0, 255)
            elif id_comp.faction == Faction.EVIL:
                color = (255, 0, 0)
            else:
                color = (128, 128, 128)

            # 5) Preparar texto del nombre (incluye ID en DEBUG)
            display_name = (
                f"{eid} {id_comp.name}" if config.DEBUG
                else id_comp.name
            )
            name_key = (display_name, color)

            # 6) Obtener o generar Surface para el nombre
            if name_key not in self.name_cache:
                self.name_cache[name_key] = self.name_font.render(display_name, True, color)
            name_surf = self.name_cache[name_key]
            name_rect = name_surf.get_rect()

            # 7) Ajustar posición horizontal: centrar sobre el sprite
            sprite = comps['Sprite'][eid]
            scale_comp: Scale = comps.get('Scale', {}).get(eid, Scale())
            sprite_width = int(sprite.image.get_width() * scale_comp.scale)
            name_rect.centerx = screen_x + sprite_width // 2

            # 8) Posicionar el nombre justo encima de la barra de salud
            name_rect.bottom = bar_top_y - 2
            screen.blit(name_surf, name_rect)

            # 9) Si la entidad tiene título, dibujarlo encima del nombre
            if id_comp.title:
                title_key = (id_comp.title, color)
                if title_key not in self.title_cache:
                    self.title_cache[title_key] = self.title_font.render(id_comp.title, True, color)
                title_surf = self.title_cache[title_key]
                title_rect = title_surf.get_rect()

                # 10) Alineación horizontal igual al nombre
                title_rect.centerx = name_rect.centerx

                # 11) Posicionar título justo encima del nombre
                title_rect.bottom = name_rect.top - 1
                screen.blit(title_surf, title_rect)