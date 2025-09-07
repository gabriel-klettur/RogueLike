import pygame
from typing import Tuple, Iterable, Optional
from roguelike_engine.config.config_tiles import TILE_SIZE, TILE_COLORS
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.tile.model.tile_model import Tile


from roguelike_engine.config.config_minimap import (
    MINIMAP_WIDTH,
    MINIMAP_HEIGHT,
    MINIMAP_ZOOM,
    MINIMAP_PADDING,
    MINIMAP_BG_ALPHA,
    MINIMAP_TILE_UPDATE_MS,
    MINIMAP_BUILDINGS_UPDATE_MS,
    MINIMAP_ENTITIES_UPDATE_MS,
    MINIMAP_MAX_ENTITIES,
    MINIMAP_COLORS,
    MINIMAP_ZONE_COLORS,
    MINIMAP_ZONE_BORDER_WIDTH,
    MINIMAP_BTN_SIZE,
    MINIMAP_BTN_MARGIN,
    MINIMAP_BTN_BG,
    MINIMAP_BTN_BG_ACTIVE,
    MINIMAP_BTN_BG_INACTIVE,
    MINIMAP_BTN_BORDER,
    MINIMAP_BTN_BORDER_HOVER,
    MINIMAP_BTN_TEXT,
)
from roguelike_game.ecs.components.core.identity import Faction

class Minimap:
    def __init__(self):
        self.width = MINIMAP_WIDTH
        self.height = MINIMAP_HEIGHT
        self.zoom = MINIMAP_ZOOM
        self.pad_x, self.pad_y = MINIMAP_PADDING

        # Superficie final donde dibujamos:
        self.surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.surface.set_alpha(MINIMAP_BG_ALPHA)  # transparencia de fondo

        # Capas: tiles (bg), edificios y entidades
        self.bg_tiles_surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.buildings_surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.entities_surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.zones_surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)

        # Tiempos de última actualización por capa
        self._last_tiles_ms = 0
        self._last_buildings_ms = 0
        self._last_entities_ms = 0
        self._last_zones_ms = 0

        # Cache de últimos parámetros relevantes
        self._last_player_tile: Optional[Tuple[int, int]] = None
        self._visible_half_tiles: Tuple[int, int] = (
            (self.width // self.zoom) // 2,
            (self.height // self.zoom) // 2,
        )

        self.visible_tiles: list[Tile] = []

        # Flags de visibilidad de capas
        self.show_tiles = True
        self.show_buildings = True
        self.show_entities = True
        self.show_zones = True

        # UI: botones de capas (se recalculan en render)
        self._btn_rects = {}
        self._btn_hover: Optional[str] = None
        self._font = None

    def update(self,
               player_pos: Tuple[float, float],
               tiles: Iterable[Tile],
               buildings: Optional[Iterable] = None,
               world: Optional[object] = None):
        """
        Actualiza las capas del minimapa con rate-limits por capa.
        - tiles: fondo (usa TILE_COLORS)
        - buildings: footprint aproximado por tamaño de asset
        - world: ECS world para ubicar entidades por Position y colorear por Faction
        """
        now = pygame.time.get_ticks()
        px = int(player_pos[0]) // TILE_SIZE
        py = int(player_pos[1]) // TILE_SIZE
        half_x, half_y = self._visible_half_tiles

        # 1) Tiles (fondo) - más pesado, cada ~1s o si cambió el tile-centro
        if (now - self._last_tiles_ms >= MINIMAP_TILE_UPDATE_MS) or (self._last_player_tile != (px, py)):
            self._last_tiles_ms = now
            # Filtrar tiles visibles en la ventana del minimapa
            vis = []
            for t in tiles:
                tx = (t.x // TILE_SIZE)
                ty = (t.y // TILE_SIZE)
                if abs(tx - px) <= half_x and abs(ty - py) <= half_y:
                    vis.append(t)
            self.visible_tiles = vis
            self.bg_tiles_surface.fill(MINIMAP_COLORS["bg"])
            for t in self.visible_tiles:
                tx = (t.x // TILE_SIZE) - px
                ty = (t.y // TILE_SIZE) - py
                x = self.width // 2 + tx * self.zoom
                y = self.height // 2 + ty * self.zoom
                color = TILE_COLORS.get(t.tile_type, (255, 0, 255))
                pygame.draw.rect(self.bg_tiles_surface, color, (x, y, self.zoom, self.zoom))

        # 2) Edificios (semi-estático) - cada ~1.5s o si cambió el tile-centro
        if buildings is not None and ((now - self._last_buildings_ms >= MINIMAP_BUILDINGS_UPDATE_MS) or (self._last_player_tile != (px, py))):
            self._last_buildings_ms = now
            self.buildings_surface.fill((0, 0, 0, 0))  # limpiar con transparente
            for b in buildings:
                try:
                    bx = b.x // TILE_SIZE
                    by = b.y // TILE_SIZE
                    # Aproximar footprint por tamaño de imagen en tiles
                    img = getattr(b, 'image', None)
                    if img is None:
                        continue
                    bw = max(1, img.get_width() // TILE_SIZE)
                    bh = max(1, img.get_height() // TILE_SIZE)
                    # Filtrado por ventana visible del minimapa
                    if abs(bx - px) > (half_x + bw) or abs(by - py) > (half_y + bh):
                        continue
                    rel_x = self.width // 2 + (bx - px) * self.zoom
                    rel_y = self.height // 2 + (by - py) * self.zoom
                    pygame.draw.rect(self.buildings_surface,
                                     MINIMAP_COLORS["building"],
                                     (rel_x, rel_y, bw * self.zoom, bh * self.zoom),
                                     width=1)
                except Exception:
                    # Nunca romper por un edificio mal formado
                    pass

        # 2.5) Zonas (semi-estático) - usar mismo ritmo que edificios o al cambiar centro
        if (now - self._last_zones_ms >= MINIMAP_BUILDINGS_UPDATE_MS) or (self._last_player_tile != (px, py)):
            self._last_zones_ms = now
            self.zones_surface.fill((0, 0, 0, 0))
            try:
                zone_w = int(getattr(global_map_settings, 'zone_width', 50))
                zone_h = int(getattr(global_map_settings, 'zone_height', 50))
                half_x, half_y = self._visible_half_tiles
                for name, (ox, oy) in dict(getattr(global_map_settings, 'zone_offsets', {})).items():
                    low = str(name).lower()
                    if low in ('no zone', 'no-zone'):
                        continue
                    # Filtrar por intersección con la ventana del minimapa en coordenadas de tiles
                    if (ox + zone_w) < (px - half_x) or ox > (px + half_x):
                        continue
                    if (oy + zone_h) < (py - half_y) or oy > (py + half_y):
                        continue
                    # Convertir a coords del minimapa
                    rel_tx = ox - px
                    rel_ty = oy - py
                    x = self.width // 2 + rel_tx * self.zoom
                    y = self.height // 2 + rel_ty * self.zoom
                    w = max(1, zone_w * self.zoom)
                    h = max(1, zone_h * self.zoom)
                    color = MINIMAP_ZONE_COLORS.get(low, MINIMAP_ZONE_COLORS.get('default', (200, 200, 200)))
                    try:
                        pygame.draw.rect(self.zones_surface, color, pygame.Rect(x, y, w, h), width=int(MINIMAP_ZONE_BORDER_WIDTH))
                    except Exception:
                        pass
            except Exception:
                pass

        # 3) Entidades (dinámico) - frecuente (~150ms) o si cambió el tile-centro
        if world is not None and ((now - self._last_entities_ms >= MINIMAP_ENTITIES_UPDATE_MS) or (self._last_player_tile != (px, py))):
            self._last_entities_ms = now
            self.entities_surface.fill((0, 0, 0, 0))
            try:
                pos_map = world.components.get('Position', {})
                id_map = world.components.get('Identity', {})
                player_eid = getattr(world, 'player_entity', None)
                count = 0
                for eid, pos in pos_map.items():
                    if eid == player_eid:
                        continue  # el jugador se dibuja al centro luego
                    ex = int(pos.x) // TILE_SIZE
                    ey = int(pos.y) // TILE_SIZE
                    if abs(ex - px) > half_x or abs(ey - py) > half_y:
                        continue
                    # Color por facción si disponible
                    color = MINIMAP_COLORS["neutral"]
                    ident = id_map.get(eid)
                    if ident is not None:
                        try:
                            if ident.faction == Faction.GOOD:
                                color = MINIMAP_COLORS["ally"]
                            elif ident.faction == Faction.EVIL:
                                color = MINIMAP_COLORS["enemy"]
                            else:
                                color = MINIMAP_COLORS["neutral"]
                        except Exception:
                            pass
                    rx = self.width // 2 + (ex - px) * self.zoom
                    ry = self.height // 2 + (ey - py) * self.zoom
                    pygame.draw.rect(self.entities_surface, color, (rx, ry, self.zoom, self.zoom))
                    count += 1
                    if count >= MINIMAP_MAX_ENTITIES:
                        break
            except Exception:
                pass
        # Actualizar última posición de centro usada
        self._last_player_tile = (px, py)

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        # Componer capas en la surface final
        # Limpiar la surface final (con alpha)
        self.surface.fill((0, 0, 0, 0))
        if self.show_tiles:
            self.surface.blit(self.bg_tiles_surface, (0, 0))
        if self.show_buildings:
            self.surface.blit(self.buildings_surface, (0, 0))
        if self.show_zones:
            self.surface.blit(self.zones_surface, (0, 0))
        if self.show_entities:
            self.surface.blit(self.entities_surface, (0, 0))

        # jugador
        pygame.draw.rect(self.surface,
                         MINIMAP_COLORS["player"],
                         (self.width // 2, self.height // 2, self.zoom, self.zoom))

        # Borde y UI de botones
        if self._font is None:
            try:
                self._font = pygame.font.SysFont("Arial", 12)
            except Exception:
                self._font = pygame.font.Font(None, 12)

        # Dibujar botones en la esquina superior-izquierda del minimapa
        btn_w, btn_h = MINIMAP_BTN_SIZE
        x0, y0 = MINIMAP_BTN_MARGIN, MINIMAP_BTN_MARGIN
        buttons = [
            ("tiles", "T", self.show_tiles),
            ("buildings", "B", self.show_buildings),
            ("zones", "Z", self.show_zones),
            ("entities", "E", self.show_entities),
        ]
        self._btn_rects.clear()
        cur_x = x0
        for key, label, active in buttons:
            rect = pygame.Rect(cur_x, y0, btn_w, btn_h)
            self._btn_rects[key] = rect
            bg = MINIMAP_BTN_BG_ACTIVE if active else MINIMAP_BTN_BG_INACTIVE
            pygame.draw.rect(self.surface, bg, rect, border_radius=3)
            border_col = MINIMAP_BTN_BORDER_HOVER if self._btn_hover == key else MINIMAP_BTN_BORDER
            pygame.draw.rect(self.surface, border_col, rect, width=1, border_radius=3)
            # Etiqueta
            try:
                txt = self._font.render(label, True, MINIMAP_BTN_TEXT)
                tr = txt.get_rect(center=rect.center)
                self.surface.blit(txt, tr)
            except Exception:
                pass
            cur_x += btn_w + MINIMAP_BTN_MARGIN

        # Posición final y dibujo del minimapa completo
        dest = (screen.get_width() - self.width - self.pad_x, self.pad_y)
        screen.blit(self.surface, dest)
        # Guardar última rect para hit-test
        self._last_rect = pygame.Rect(dest, (self.width, self.height))
        return self._last_rect

    def get_rect(self, screen: pygame.Surface) -> pygame.Rect:
        """Rect del minimapa en pantalla acorde a render()."""
        return pygame.Rect((screen.get_width() - self.width - self.pad_x, self.pad_y), (self.width, self.height))

    def handle_event(self, event: pygame.event.Event, screen: pygame.Surface) -> bool:
        """Maneja hover y clic en los botones del minimapa. Devuelve True si consume el evento."""
        et = getattr(event, 'type', None)
        if et not in (pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN):
            return False
        # Hit-test área del minimapa
        rect = getattr(self, '_last_rect', None) or self.get_rect(screen)
        pos = getattr(event, 'pos', None)
        if not pos or not rect.collidepoint(*pos):
            # Limpiar hover si el mouse salió
            if et == pygame.MOUSEMOTION:
                self._btn_hover = None
            return False
        # Posición relativa dentro del minimapa
        rel_x = pos[0] - rect.x
        rel_y = pos[1] - rect.y
        # Hover
        if et == pygame.MOUSEMOTION:
            self._btn_hover = None
            for key, brect in self._btn_rects.items():
                r = pygame.Rect(rect.x + brect.x, rect.y + brect.y, brect.w, brect.h)
                if r.collidepoint(*pos):
                    self._btn_hover = key
                    break
            return False  # hover no consume click
        # Click izquierdo: toggle botón
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            for key, brect in self._btn_rects.items():
                r = pygame.Rect(rect.x + brect.x, rect.y + brect.y, brect.w, brect.h)
                if r.collidepoint(*pos):
                    if key == 'tiles':
                        self.show_tiles = not self.show_tiles
                    elif key == 'buildings':
                        self.show_buildings = not self.show_buildings
                    elif key == 'zones':
                        self.show_zones = not self.show_zones
                    elif key == 'entities':
                        self.show_entities = not self.show_entities
                    return True
        return False