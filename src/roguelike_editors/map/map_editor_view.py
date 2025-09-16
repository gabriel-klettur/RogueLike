import pygame
from pygame import Surface, Rect
from roguelike_editors.map.map_title_panel.map_title_view import MapTitleView

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H
from roguelike_engine.map.model.layer import Layer


class MapEditorView:
    """
    Vista para el Map Editor: gestiona el dibujo de zonas, etiquetas,
    resaltados, toolbar, dropdown y diálogos de confirmación.
    """

    # Colores reutilizables
    COLOR_OVERLAY_BG = (0, 0, 0, 150)
    COLOR_PROGRESS_BG = (50, 50, 50)
    COLOR_PROGRESS_FILL = (0, 150, 215)
    COLOR_BORDER_DEFAULT = (0, 128, 255)
    COLOR_BORDER_SELECTED = (0, 255, 0)
    COLOR_BORDER_HIDDEN = (100, 100, 100)
    COLOR_BORDER_DELETE = (255, 0, 0)
    COLOR_COLLIDER_FILL = (255, 0, 0, 80)
    COLOR_COLLIDER_BORDER = (255, 0, 0)
    COLOR_INPUT_BG = (255, 255, 255)
    COLOR_INPUT_BORDER = (0, 0, 0)
    COLOR_BUTTON_BG = (0, 120, 215)
    COLOR_BUTTON_BORDER = (255, 255, 255)
    COLOR_BUTTON_TEXT = (255, 255, 255)
    COLOR_TEXT = (255, 255, 255)
    COLOR_DIALOG_BG = (0, 0, 0)
    COLOR_DIALOG_BORDER = (255, 255, 255)
    COLOR_YES_BG = (0, 200, 0)
    COLOR_NO_BG = (200, 0, 0)

    def __init__(self, controller, state, map_manager):
        self.controller = controller
        self.state = state
        self.map_manager = map_manager

        # Fuentes
        pygame.font.init()
        self.base_font_size = 16
        self.font_large = pygame.font.SysFont(None, self.base_font_size * 3)
        self.font_medium = pygame.font.SysFont(None, 24)
        self.font_small = pygame.font.SysFont(None, 20)
        self.font_dropdown = pygame.font.SysFont("Arial", 14)
        # Professional title bar
        self.title_view = MapTitleView(None, state)

    def render(self, screen: Surface, camera, map_manager) -> None:
        """
        Punto de entrada para dibujar todo el Map Editor.
        """
        if not self.state.active:
            return

        # 1. Si hay herramienta asíncrona ejecutándose, mostrar overlay de carga y barra de progreso
        if self.state.executing_tool:
            self._draw_loading_overlay(screen)
            # Title above the overlay for consistent branding
            if self.title_view:
                # Keep state reference up to date
                self.title_view.state = self.state
                self.title_view.render(screen)
            return

        # Title bar in normal rendering path
        title_rect = None
        if self.title_view:
            self.title_view.state = self.state
            title_rect = self.title_view.render(screen)
            # Cache for overlays (e.g., tutorial positioning)
            try:
                self._last_title_rect = title_rect
            except Exception:
                pass
            # Align toolbar panel flush left (matching title's left) and just below the title (preserve dragging later)
            try:
                toolbar = getattr(self.controller, "toolbar", None)
                tv = getattr(getattr(toolbar, "view", None), "widget", None)
                panel = getattr(tv, "panel", None)
                if tv and panel and not getattr(panel, "dragging", False):
                    # If toolbar is still at its initial position, snap it under the title
                    initial_pos = (getattr(tv, "x", 10), getattr(tv, "y", 10))
                    if getattr(panel, "pos", None) in (None, initial_pos):
                        gap = 8
                        snap_x = int(title_rect.left)
                        snap_y = int(title_rect.bottom + gap)
                        panel.pos = (snap_x, snap_y)
            except Exception:
                # Non-fatal: keep previous toolbar position
                pass

        # 2. Dibujar zonas
        self._draw_zones(screen, camera)

        # 3. Dibujar overlay de colisiones, si está habilitado
        if self.state.show_colliders:
            self._draw_colliders_overlay(screen, camera)

        # 4. Dibujar toolbar y dropdown (si está abierto)
        self._draw_toolbar(screen)

        if self.state.layers_view_open:
            # Delegar dropdown al MVC de ViewLayers
            try:
                self.controller.toolbar.view_layers.view.render_dropdown(screen)
            except Exception:
                # Falla no fatal: evita romper el render completo si algo ocurre en la vista de dropdown
                pass

        # 5. Dibujar los diálogos de confirmación, si corresponde
        self._draw_confirmation_dialogs(screen)

        # 6. Si la herramienta asíncrona volvió a activarse en medio de diálogos, dibujar barra de progreso inferior
        if self.state.executing_tool:
            self._draw_progress_bar(screen)

    # -------------------------------------------------------------
    # 1. Carga y Barra de Progreso (pantalla completa)
    # -------------------------------------------------------------
    def _draw_loading_overlay(self, screen: Surface) -> None:
        sw, sh = screen.get_size()

        # Fondo semi-transparente
        overlay = Surface((sw, sh), pygame.SRCALPHA)
        overlay.fill(self.COLOR_OVERLAY_BG)
        screen.blit(overlay, (0, 0))

        # Barra de progreso central
        bar_w, bar_h = sw * 0.5, 20
        bar_x = (sw - bar_w) / 2
        bar_y = (sh - bar_h) / 2
        pygame.draw.rect(screen, self.COLOR_PROGRESS_BG, (bar_x, bar_y, bar_w, bar_h))

        total = max(self.state.execution_total, 1)
        progress = self.state.execution_index / total
        fill_w = bar_w * progress
        pygame.draw.rect(screen, self.COLOR_PROGRESS_FILL, (bar_x, bar_y, fill_w, bar_h))
        pygame.draw.rect(screen, self.COLOR_TEXT, (bar_x, bar_y, bar_w, bar_h), 2)

        # Texto de porcentaje
        label = f"{self.state.executing_tool.replace('_', ' ').title()}: {int(progress * 100)}%"
        text_surf = self.font_medium.render(label, True, self.COLOR_TEXT)
        text_x = bar_x + (bar_w - text_surf.get_width()) / 2
        text_y = bar_y + (bar_h - text_surf.get_height()) / 2
        screen.blit(text_surf, (text_x, text_y))

    # -------------------------------------------------------------
    # 2. Zonas: dibujo de rectángulos, etiquetas y modo renombrar
    # -------------------------------------------------------------
    def _draw_zones(self, screen: Surface, camera) -> None:
        zones = global_map_settings.zone_offsets
        zone_w, zone_h = global_map_settings.zone_size

        for zone_name, (ox, oy) in zones.items():
            # Skip sentinel zone entries that represent "outside any defined zone"
            if zone_name in ("no zone", "no-zone"):
                continue
            # Determinar colores según estado
            hidden = zone_name in self.state.hidden_zones
            if hidden:
                outline_color = self.COLOR_BORDER_HIDDEN
                fill_color = (*outline_color, 50)
            else:
                if zone_name == self.state.selected_zone:
                    outline_color = self.COLOR_BORDER_SELECTED
                else:
                    outline_color = self.COLOR_BORDER_DEFAULT
                fill_color = (*outline_color, 50)

            # Posición en píxeles del rectángulo de zona
            px, py = ox * TILE_SIZE, oy * TILE_SIZE
            pw, ph = zone_w * TILE_SIZE, zone_h * TILE_SIZE

            # Convertir a coordenadas de pantalla
            screen_tl = camera.apply((px, py))
            screen_size = camera.scale((pw, ph))

            # Dibujar relleno semitransparente
            surf = Surface(screen_size, pygame.SRCALPHA)
            surf.fill(fill_color)
            screen.blit(surf, screen_tl)

            # Dibujar borde de zona
            pygame.draw.rect(screen, outline_color, (*screen_tl, *screen_size), 2)

            # Cachear rect de zona seleccionada para overlays externos (tutorial)
            try:
                if zone_name == self.state.selected_zone:
                    self._last_selected_zone_rect = pygame.Rect(*screen_tl, *screen_size)
            except Exception:
                pass

            # Si está en modo de pendiente de borrado, resaltar en rojo
            if self.state.pending_delete_zone == zone_name:
                red_outline = self.COLOR_BORDER_DELETE
                red_fill = (255, 0, 0, 50)
                surf_del = Surface(screen_size, pygame.SRCALPHA)
                surf_del.fill(red_fill)
                screen.blit(surf_del, screen_tl)
                pygame.draw.rect(screen, red_outline, (*screen_tl, *screen_size), 2)
                # Continuar para no dibujar la etiqueta normal
                continue

            # Dibujar etiqueta o modo renombrar
            if self.state.renaming_zone == zone_name:
                self._draw_renaming_overlay(screen, camera, zone_name, screen_tl, screen_size)
            else:
                self._draw_zone_label(screen, screen_tl, screen_size, zone_name)

    def _draw_zone_label(
        self, screen: Surface, screen_tl: tuple[float, float], screen_size: tuple[int, int], text: str
    ) -> None:
        """
        Dibuja el nombre de la zona centrado dentro del rectángulo.
        """
        label_surf = self.font_large.render(text, True, self.COLOR_TEXT)
        label_w, label_h = label_surf.get_size()
        max_w, max_h = screen_size

        if label_w > max_w or label_h > max_h:
            scale = min(max_w / label_w, max_h / label_h)
            new_size = (int(label_w * scale), int(label_h * scale))
            label_surf = pygame.transform.smoothscale(label_surf, new_size)
            label_w, label_h = new_size

        x = screen_tl[0] + (screen_size[0] - label_w) / 2
        y = screen_tl[1] + (screen_size[1] - label_h) / 2
        screen.blit(label_surf, (x, y))

    def _draw_renaming_overlay(
        self,
        screen: Surface,
        camera,
        zone_name: str,
        screen_tl: tuple[float, float],
        screen_size: tuple[int, int],
    ) -> None:
        """
        Dibuja la caja de texto y botón "Aceptar" para renombrar una zona.
        """
        # Preparar texto actual
        text_input = self.state.rename_input or ""
        input_surf = self.font_large.render(text_input, True, (0, 0, 0))
        text_h = input_surf.get_height()
        padding_y = 4

        # Altura de la caja de input (mínimo BTN_H)
        box_h = max(text_h + padding_y * 2, BTN_H)
        total_w = screen_size[0]

        # Dimensiones de input y botón "Aceptar"
        accept_w = box_h * 2
        input_w = max(20, total_w - accept_w - 5)
        input_x = screen_tl[0]
        input_y = screen_tl[1] + screen_size[1] - box_h - 5

        # Dibujar caja de entrada
        input_rect = Rect(input_x, input_y, input_w, box_h)
        pygame.draw.rect(screen, self.COLOR_INPUT_BG, input_rect)
        pygame.draw.rect(screen, self.COLOR_INPUT_BORDER, input_rect, 2)
        screen.blit(input_surf, (input_x + 5, input_y + (box_h - text_h) // 2))
        self.state.rename_input_rect = input_rect

        # Dibujar botón "Aceptar"
        accept_rect = Rect(input_rect.right + 5, input_y, accept_w, box_h)
        pygame.draw.rect(screen, self.COLOR_BUTTON_BG, accept_rect)
        pygame.draw.rect(screen, self.COLOR_BUTTON_BORDER, accept_rect, 2)
        btn_font = pygame.font.SysFont(None, int(box_h * 0.6))
        ok_surf = btn_font.render("Aceptar", True, self.COLOR_BUTTON_TEXT)
        screen.blit(
            ok_surf,
            (
                accept_rect.centerx - ok_surf.get_width() // 2,
                accept_rect.centery - ok_surf.get_height() // 2,
            ),
        )
        self.state.rename_accept_rect = accept_rect

        # Dibujar cursor parpadeante
        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            caret_x = input_x + 5 + input_surf.get_width()
            caret_y1 = input_y + padding_y
            caret_y2 = input_y + box_h - padding_y
            pygame.draw.line(screen, (0, 0, 0), (caret_x, caret_y1), (caret_x, caret_y2), 2)

    # -------------------------------------------------------------
    # 3. Colisiones: overlay de colisiones sobre cada tile sólido
    # -------------------------------------------------------------
    def _draw_colliders_overlay(self, screen: Surface, camera) -> None:
        for tile in self.map_manager.solid_tiles:
            tl = camera.apply((tile.x, tile.y))
            size = camera.scale((TILE_SIZE, TILE_SIZE))
            overlay = Surface(size, pygame.SRCALPHA)
            overlay.fill(self.COLOR_COLLIDER_FILL)
            screen.blit(overlay, tl)
            pygame.draw.rect(screen, self.COLOR_COLLIDER_BORDER, (*tl, *size), 1)

    # -------------------------------------------------------------
    # 4. Toolbar: icono principal y botones de zona
    # -------------------------------------------------------------
    def _draw_toolbar(self, screen: Surface) -> None:
        # Delegar dibujo en el widget compartido a través de la vista del toolbar
        self.controller.toolbar.view.render(screen)

    # -------------------------------------------------------------
    # 5. Dropdown: la lógica y el render ahora viven en ViewLayersView
    # -------------------------------------------------------------

    # -------------------------------------------------------------
    # 5.1. Diálogos de confirmación (Delete, Paint Tiles, Clear Colliders, Paint Colliders, Add Zone)
    # -------------------------------------------------------------
    def _draw_confirmation_dialogs(self, screen: Surface) -> None:
        """
        Verifica distintos flags de estado y dibuja el diálogo correspondiente.
        """
        if self.state.confirm_delete_zone and self.state.pending_delete_zone:
            self._draw_generic_dialog(
                screen,
                f"Eliminar zona {self.state.pending_delete_zone}?",
                yes_callback_attr="confirm_yes_rect",
                no_callback_attr="confirm_no_rect",
            )

        if self.state.confirm_paint_tiles and self.state.pending_paint_tiles_zone:
            self._draw_generic_dialog(
                screen,
                f"Pintar tiles de zona {self.state.pending_paint_tiles_zone}?",
                yes_callback_attr="confirm_paint_yes_rect",
                no_callback_attr="confirm_paint_no_rect",
            )

        if self.state.confirm_clear_colliders and self.state.pending_clear_colliders_zone:
            self._draw_generic_dialog(
                screen,
                f"Vaciar colliders de zona {self.state.pending_clear_colliders_zone}?",
                yes_callback_attr="confirm_clear_colliders_yes_rect",
                no_callback_attr="confirm_clear_colliders_no_rect",
            )

        if self.state.confirm_paint_colliders and self.state.pending_paint_colliders_zone:
            self._draw_generic_dialog(
                screen,
                f"Pintar colliders de zona {self.state.pending_paint_colliders_zone}?",
                yes_callback_attr="confirm_paint_colliders_yes_rect",
                no_callback_attr="confirm_paint_colliders_no_rect",
            )

        if self.state.confirm_add_zone and self.state.pending_add_zone_coords:
            tx, ty = self.state.pending_add_zone_coords
            self._draw_generic_dialog(
                screen,
                f"Agregar zona en ({tx},{ty})?",
                yes_callback_attr="confirm_add_yes_rect",
                no_callback_attr="confirm_add_no_rect",
            )

    def _draw_generic_dialog(
        self,
        screen: Surface,
        message: str,
        yes_callback_attr: str,
        no_callback_attr: str,
    ) -> None:
        """
        Dibuja un diálogo central con mensaje, botón "Sí" y "No".
        Guarda los rects de los botones en los atributos indicados de self.state.
        """
        sw, sh = screen.get_size()
        text_surf = self.font_medium.render(message, True, self.COLOR_TEXT)
        box_w = text_surf.get_width() + 20
        box_h = text_surf.get_height() + 60
        box_x = (sw - box_w) // 2
        box_y = (sh - box_h) // 2
        box_rect = Rect(box_x, box_y, box_w, box_h)

        # Fondo y borde del diálogo
        pygame.draw.rect(screen, self.COLOR_DIALOG_BG, box_rect)
        pygame.draw.rect(screen, self.COLOR_DIALOG_BORDER, box_rect, 2)
        screen.blit(text_surf, (box_x + 10, box_y + 10))

        # Botón "Sí"
        yes_w, yes_h = 60, 30
        yes_x = box_x + 10
        yes_y = box_y + box_h - yes_h - 10
        yes_rect = Rect(yes_x, yes_y, yes_w, yes_h)
        pygame.draw.rect(screen, self.COLOR_YES_BG, yes_rect)
        pygame.draw.rect(screen, self.COLOR_DIALOG_BORDER, yes_rect, 2)
        yes_surf = self.font_medium.render("Sí", True, self.COLOR_TEXT)
        screen.blit(
            yes_surf,
            (yes_rect.centerx - yes_surf.get_width() // 2,
             yes_rect.centery - yes_surf.get_height() // 2),
        )
        setattr(self.state, yes_callback_attr, yes_rect)

        # Botón "No"
        no_w, no_h = 60, 30
        no_x = yes_rect.right + 10
        no_y = yes_y
        no_rect = Rect(no_x, no_y, no_w, no_h)
        pygame.draw.rect(screen, self.COLOR_NO_BG, no_rect)
        pygame.draw.rect(screen, self.COLOR_DIALOG_BORDER, no_rect, 2)
        no_surf = self.font_medium.render("No", True, self.COLOR_TEXT)
        screen.blit(
            no_surf,
            (no_rect.centerx - no_surf.get_width() // 2,
             no_rect.centery - no_surf.get_height() // 2),
        )
        setattr(self.state, no_callback_attr, no_rect)

    # -------------------------------------------------------------
    # 6. Barra de progreso inferior (para herramientas asíncronas)
    # -------------------------------------------------------------
    def _draw_progress_bar(self, screen: Surface) -> None:
        sw, sh = screen.get_size()
        bar_w, bar_h = sw * 0.5, 8
        bar_x = (sw - bar_w) / 2
        bar_y = sh * 0.85

        pygame.draw.rect(screen, self.COLOR_PROGRESS_BG, (bar_x, bar_y, bar_w, bar_h))

        total = max(self.state.execution_total, 1)
        progress = self.state.execution_index / total
        fill_w = bar_w * progress
        pygame.draw.rect(screen, self.COLOR_PROGRESS_FILL, (bar_x, bar_y, fill_w, bar_h))
        pygame.draw.rect(screen, self.COLOR_TEXT, (bar_x, bar_y, bar_w, bar_h), 1)

        label = f"{self.state.executing_tool.replace('_', ' ').title()}: {int(progress * 100)}%"
        text_surf = self.font_small.render(label, True, self.COLOR_TEXT)
        screen.blit(text_surf, (bar_x, bar_y - bar_h - 2))