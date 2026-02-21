import pygame
from pygame import Surface
from roguelike_editors.map.map_title_panel.map_title_view import MapTitleView

from roguelike_editors.map.view import (
    default_palette,
    make_fonts,
    ZonesView,
    CollidersView,
    DialogsView,
    ProgressView,
)
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_editors.map.events.utils import screen_to_world

class MapEditorView:
    """
    Vista para el Map Editor: gestiona el dibujo de zonas, etiquetas,
    resaltados, toolbar, dropdown y diálogos de confirmación.
    """

    def __init__(self, controller, state, map_manager):
        self.controller = controller
        self.state = state
        self.map_manager = map_manager

        # Centralizar fuentes y paleta
        self.fonts = make_fonts(base_size=16)
        self.palette = default_palette()

        # Subvistas
        self.title_view = MapTitleView(None, state)
        self.zones_view = ZonesView(self.fonts, self.palette)
        self.colliders_view = CollidersView(self.palette)
        self.dialogs_view = DialogsView(self.fonts, self.palette)
        self.progress_view = ProgressView(self.fonts, self.palette)

    def render(self, screen: Surface, camera, map_manager) -> None:
        """Punto de entrada para dibujar todo el Map Editor."""
        if not self.state.active:
            return

        # 1. Barra de título
        if self.title_view:
            self.title_view.state = self.state
            try:
                self._last_title_rect = self.title_view.render(screen)
            except Exception:
                pass

        # 2. Zonas y (opcional) colliders
        self._draw_zones(screen, camera)
        if self.state.show_colliders:
            self._draw_colliders_overlay(screen, camera)

        # 3. Toolbar y dropdown de capas
        self._draw_toolbar(screen)
        if self.state.layers_view_open:
            try:
                self.controller.toolbar.view_layers.view.render_dropdown(screen)
            except Exception:
                pass
        # 3.1. Tile Picker (Paint Tiles): render floating panel to the right of toolbar
        try:
            self.controller.toolbar.paint_tiles.render(screen)
        except Exception:
            pass

        # 4. Diálogos de confirmación
        self._draw_confirmation_dialogs(screen)

        # 5. Progreso inferior mientras corren herramientas asíncronas
        if self.state.executing_tool:
            self._draw_progress_bar(screen)

        # 6. Overlay de depuración de coordenadas (tile interno/lógico/zona)
        if getattr(self.state, "show_debug_overlay", False):
            try:
                self._draw_debug_coords(screen, camera)
            except Exception:
                # Nunca permitir que el overlay de debug rompa el render principal
                pass

    # -------------------------------------------------------------
    # 1. Carga y Barra de Progreso (pantalla completa)
    # -------------------------------------------------------------
    def _draw_loading_overlay(self, screen: Surface) -> None:
        """Overlay de carga a pantalla completa."""
        self.progress_view.draw_loading_overlay(screen, self.state)

    # -------------------------------------------------------------
    # 2. Zonas: dibujo de rectángulos, etiquetas y modo renombrar
    # -------------------------------------------------------------
    def _draw_zones(self, screen: Surface, camera) -> None:
        """Dibuja zonas delegando en `ZonesView` y cachea el último rect seleccionado."""
        try:
            self._last_selected_zone_rect = self.zones_view.render(screen, camera, self.state)
        except Exception:
            # Evitar que un error en zonas rompa el render completo del editor
            pass

    # -------------------------------------------------------------
    # 3. Colisiones: overlay de colisiones sobre cada tile sólido
    # -------------------------------------------------------------
    def _draw_colliders_overlay(self, screen: Surface, camera) -> None:
        try:
            self.colliders_view.render(screen, camera, self.map_manager)
        except Exception:
            pass

    # -------------------------------------------------------------
    # 4. Toolbar: icono principal y botones de zona
    # -------------------------------------------------------------
    def _draw_toolbar(self, screen: Surface) -> None:
        # Delegar dibujo en el widget compartido a través de la vista del toolbar
        # Auto-ubicar bajo el título si aún está en la posición por defecto
        try:
            widget = self.controller.toolbar.view.widget
            panel = widget.panel
            title_rect = getattr(self, "_last_title_rect", None)
            if title_rect is not None:
                default_pos = (getattr(widget, "x", 10), getattr(widget, "y", 10))
                current_pos = getattr(panel, "pos", None) or default_pos
                if current_pos == default_pos:
                    panel.pos = (title_rect.x, title_rect.bottom + 10)
        except Exception:
            pass
        self.controller.toolbar.view.render(screen)

    # -------------------------------------------------------------
    # 5. Dropdown: la lógica y el render ahora viven en ViewLayersView
    # -------------------------------------------------------------

    # -------------------------------------------------------------
    # 5.1. Diálogos de confirmación (Delete, Paint Tiles, Clear Colliders, Paint Colliders, Add Zone)
    # -------------------------------------------------------------
    def _draw_confirmation_dialogs(self, screen: Surface) -> None:
        """Dibuja diálogos de confirmación delegando en `DialogsView`."""
        try:
            self.dialogs_view.render(screen, self.state)
        except Exception:
            pass

    # -------------------------------------------------------------
    # 6. Barra de progreso inferior (para herramientas asíncronas)
    # -------------------------------------------------------------
    def _draw_progress_bar(self, screen: Surface) -> None:
        """Barra de progreso inferior delegada en `ProgressView`."""
        try:
            self.progress_view.draw_bottom_bar(screen, self.state)
        except Exception:
            pass

    # -------------------------------------------------------------
    # 7. Overlay de depuración de coordenadas (internas/lógicas/zona)
    # -------------------------------------------------------------
    def _draw_debug_coords(self, screen: Surface, camera) -> None:
        """Dibuja un pequeño panel con coordenadas bajo el ratón.

        Muestra:
          - tile interna (tx, ty) en la malla world 0-based
          - tile lógica (lx, ly) en espacio world_origin-aware
          - zona y offset lógico de la zona
        """
        try:
            mx, my = pygame.mouse.get_pos()
        except Exception:
            return

        try:
            world_x, world_y = screen_to_world((mx, my), camera)
        except Exception:
            return

        try:
            tx = int(world_x) // TILE_SIZE
            ty = int(world_y) // TILE_SIZE
        except Exception:
            return

        # Coordenadas lógicas de tile usando world_origin
        try:
            lx, ly = global_map_settings.internal_to_logical_tile(tx, ty)
        except Exception:
            lx, ly = tx, ty

        # Zona y offset lógico de zona
        try:
            zone = get_zone_for_tile(tx, ty)
        except Exception:
            zone = "no zone"
        try:
            zx, zy = global_map_settings.logical_zone_offsets.get(zone, (0, 0))
        except Exception:
            zx, zy = 0, 0

        line1 = f"tile={tx},{ty} logical={lx},{ly}"
        line2 = f"zone={zone} z_off={zx},{zy}"
        # Info global de mapa: origen lógico y dimensiones internas
        try:
            ox0 = getattr(global_map_settings, "world_origin_x", 0)
            oy0 = getattr(global_map_settings, "world_origin_y", 0)
            gw = int(getattr(global_map_settings, "global_width", 0))
            gh = int(getattr(global_map_settings, "global_height", 0))
            line3 = f"origin={ox0},{oy0} map={gw}x{gh}"
        except Exception:
            line3 = ""

        font = self.fonts.small
        try:
            surf1 = font.render(line1, True, self.palette.text)
            surf2 = font.render(line2, True, self.palette.text)
            surf3 = font.render(line3, True, self.palette.text) if line3 else None
        except Exception:
            return

        heights = [surf1.get_height(), surf2.get_height()] + ([surf3.get_height()] if surf3 else [])
        w = max(
            surf1.get_width(),
            surf2.get_width(),
            (surf3.get_width() if surf3 else 0),
        ) + 8
        h = sum(heights) + 8

        try:
            sw, sh = screen.get_size()
        except Exception:
            sw, sh = 800, 600

        # Panel en esquina inferior izquierda
        x = 10
        y = sh - h - 10
        bg_rect = pygame.Rect(x, y, w, h)

        try:
            # Fondo semioscuro para legibilidad
            pygame.draw.rect(screen, (0, 0, 0), bg_rect)
            pygame.draw.rect(screen, self.palette.border_default, bg_rect, 1)
            cy = y + 2
            screen.blit(surf1, (x + 4, cy))
            cy += surf1.get_height()
            screen.blit(surf2, (x + 4, cy))
            if surf3 is not None:
                cy += surf2.get_height()
                screen.blit(surf3, (x + 4, cy))

        except Exception:
            # Best-effort: si algo falla al dibujar, no hacemos nada más
            return