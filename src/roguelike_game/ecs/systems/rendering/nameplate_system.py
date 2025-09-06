import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.transform.scale import Scale
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.factories.monster.config import MONSTER_DEFS
from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState

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
        - name_cache: cachea superficies compuestas (texto+contorno+fondo) del nombre.
        - title_cache: cachea superficies compuestas para el título.
        """
        pygame.font.init()
        # Ligero aumento de tamaño para mejor lectura
        self.name_font = pygame.font.SysFont(None, 32)
        self.title_font = pygame.font.SysFont(None, 26)
        # Caches: clave incluye parámetros de estilo para evitar colisiones
        self.name_cache: dict[tuple, pygame.Surface] = {}
        self.title_cache: dict[tuple, pygame.Surface] = {}
        self.perf_log = perf_log
    
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

            # 4) Elegir color según facción (más brillante para mejorar contraste)
            if id_comp.faction == Faction.GOOD:
                color = (90, 160, 255)   # azul brillante
            elif id_comp.faction == Faction.EVIL:
                color = (255, 80, 80)    # rojo brillante
            else:
                color = (245, 245, 245)  # neutro casi blanco

            # 5) Preparar texto del nombre (incluye ID en DEBUG). Si el nombre actual coincide con
            #    una clase conocida y ésta define default_name, usarlo como display.
            base_name = id_comp.name
            try:
                alt = MONSTER_DEFS.get(base_name, {}).get("default_name")
                if alt:
                    base_name = str(alt)
            except Exception:
                pass
            display_name = (f"{eid} {base_name}" if config.DEBUG else base_name)
            # Estilo común para mejorar legibilidad
            outline_color = (0, 0, 0)
            outline_w = 2
            bg_rgba = (0, 0, 0, 110)  # fondo oscuro semitransparente

            # 6) Obtener o generar Surface compuesta para el nombre
            name_key = (display_name, color, outline_color, outline_w, bg_rgba, self.name_font.get_height())
            if name_key not in self.name_cache:
                self.name_cache[name_key] = self._render_label_surface(
                    self.name_font, display_name, color, outline_color, outline_w, bg_rgba
                )
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
                title_key = (id_comp.title, color, outline_color, outline_w, bg_rgba, self.title_font.get_height())
                if title_key not in self.title_cache:
                    self.title_cache[title_key] = self._render_label_surface(
                        self.title_font, id_comp.title, color, outline_color, outline_w, bg_rgba
                    )
                title_surf = self.title_cache[title_key]
                title_rect = title_surf.get_rect()

                # 10) Alineación horizontal igual al nombre
                title_rect.centerx = name_rect.centerx

                # 11) Posicionar título justo encima del nombre
                title_rect.bottom = name_rect.top - 1
                screen.blit(title_surf, title_rect)

            # 12) Mostrar estado '(Inconsciente)' por encima de título o nombre si aplica
            npc_state = comps.get('NPCState', {}).get(eid)
            is_unconscious = bool(npc_state and isinstance(npc_state.fsm.current_state, UnconsciousState))
            if is_unconscious:
                status_text = "(Inconsciente)"
                status_key = (status_text, color, outline_color, outline_w, bg_rgba, self.title_font.get_height())
                if status_key not in self.title_cache:
                    self.title_cache[status_key] = self._render_label_surface(
                        self.title_font, status_text, color, outline_color, outline_w, bg_rgba
                    )
                status_surf = self.title_cache[status_key]
                status_rect = status_surf.get_rect()
                # Alinear con el nombre
                status_rect.centerx = name_rect.centerx
                # Colocar por encima del elemento más alto (título si existe, si no el nombre)
                top_anchor = name_rect.top
                if 'title_rect' in locals():
                    top_anchor = min(top_anchor, title_rect.top)
                status_rect.bottom = top_anchor - 1
                screen.blit(status_surf, status_rect)

    def _render_label_surface(self, font: pygame.font.Font, text: str,
                               fg: tuple[int, int, int],
                               outline: tuple[int, int, int] = (0, 0, 0),
                               outline_w: int = 2,
                               bg_rgba: tuple[int, int, int, int] = (0, 0, 0, 110),
                               pad: int = 4,
                               border_radius: int = 4) -> pygame.Surface:
        """Genera una Surface con texto, contorno y fondo semitransparente.

        - Dibuja primero el fondo (rect redondeado), luego texto con contorno
          (mediante múltiples blits desplazados) y finalmente el texto principal.
        """
        # Render base del texto (para medir)
        base_text = font.render(text, True, fg)
        tw, th = base_text.get_size()

        # Superficie final con padding y posible contorno
        w = tw + pad * 2 + outline_w * 2
        h = th + pad * 2 + outline_w * 2
        surf = pygame.Surface((w, h), pygame.SRCALPHA)

        # Fondo
        rect = surf.get_rect()
        try:
            pygame.draw.rect(surf, bg_rgba, rect, border_radius=border_radius)
        except TypeError:
            # Compatibilidad si border_radius no está disponible
            pygame.draw.rect(surf, bg_rgba, rect)

        # Texto con contorno (dibujar offset alrededor)
        if outline_w > 0:
            outline_text = font.render(text, True, outline)
            for ox in range(-outline_w, outline_w + 1):
                for oy in range(-outline_w, outline_w + 1):
                    if ox == 0 and oy == 0:
                        continue
                    surf.blit(outline_text, (pad + outline_w + ox, pad + outline_w + oy))

        # Texto principal
        surf.blit(base_text, (pad + outline_w, pad + outline_w))
        return surf