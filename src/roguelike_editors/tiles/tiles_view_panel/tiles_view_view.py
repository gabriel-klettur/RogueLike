import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import (
    OUTLINE_CHOICE,
    OUTLINE_HOVER,
    OUTLINE_SEL,
    BTN_H,
)
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer


class TilesViewPanelView:
    """
    Vista del panel de vista de tiles.

    Muestra información de los tiles:
      - Tile sobre el que está el cursor.
      - Tile seleccionado.
      - Tile actualmente escogido para pintar.
      - Layer del tile hovered y del tile seleccionado.

    Provee layout dinámico y soporta distintos modos de posicionamiento.
    """
    def __init__(self, controller, state):
        """
        Args:
            controller: Controlador asociado, para acceder a lógica de detección de tiles y estado.
            state: Objeto de estado que guarda posición, tamaño y rects clicables.
        """
        self.controller = controller
        self.state = state

    def render(self, screen, camera, game_map):
        """
        Dibuja el panel de información de tiles en pantalla.

        Args:
            screen: Surface de pygame donde dibujar el panel.
            camera: Objeto cámara para conversión de coordenadas.
            game_map: Mapa de juego, para obtener tiles y layers.
        """
        # Preparar datos de tile y layer
        mouse_pos = pygame.mouse.get_pos()
        hovered_tile, hovered_layer = self._detect_hovered(mouse_pos, camera, game_map)
        selected_tile = self.controller.editor_state.selected_tile
        choice_sprite = self._load_choice_sprite()

        # Construir items a renderizar
        sprite_items = [
            ("Hovered", getattr(hovered_tile, 'sprite', None), OUTLINE_HOVER),
            ("Selected", getattr(selected_tile, 'sprite', None), OUTLINE_SEL),
            ("Choice", choice_sprite, OUTLINE_CHOICE),
        ]
        layer_items = [
            ("Layer Hovered", f"{hovered_layer.value}: {hovered_layer.name}"),
            ("Layer Selected", f"{self.controller.editor_state.current_layer.value}: {self.controller.editor_state.current_layer.name}"),
        ]

        # Calcular dimensiones y posición del panel
        font = pygame.font.SysFont("Arial", 14)
        panel_w, panel_h, row_dims = self._compute_panel_size(sprite_items, layer_items, font)
        x0, y0 = self._compute_panel_position(screen, panel_w, panel_h)

        # Dibujar panel de fondo y borde
        panel_surf = self._create_panel_surface(panel_w, panel_h)
        pygame.draw.rect(panel_surf, OUTLINE_CHOICE, panel_surf.get_rect(), 2)

        # Renderizar filas de sprite
        self._draw_sprite_rows(panel_surf, sprite_items, font, row_dims, x0, y0)
        # Renderizar filas de layer
        self._draw_layer_rows(panel_surf, layer_items, font, row_dims, x0, y0)

        # Actualizar estado y blit final
        self.state.size = (panel_w, panel_h)
        screen.blit(panel_surf, (x0, y0))

    def _screen_to_world(self, mouse_pos, camera):
        """
        Convierte coordenadas de pantalla a índices de tile en el mapa.

        Returns:
            Tupla (col, row) de tile bajo el ratón.
        """
        mx, my = mouse_pos
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        return int(wx) // TILE_SIZE, int(wy) // TILE_SIZE

    def _detect_hovered(self, mouse_pos, camera, game_map):
        """
        Determina el tile y layer sobre los que está el ratón.

        Recorre los layers en orden de dibujo inverso, el primero con overlay gana.
        """
        hovered = self.controller._tile_under_mouse(mouse_pos, camera, game_map)
        col, row = self._screen_to_world(mouse_pos, camera)
        chosen_layer = Layer.Ground
        for layer in sorted(game_map.tiles_by_layer.keys(), key=lambda l: -l.value):
            grid = game_map.tiles_by_layer[layer]
            if 0 <= row < len(grid) and 0 <= col < len(grid[0]):
                t = grid[row][col]
                if t and getattr(t, 'overlay_code', ''):
                    chosen_layer = layer
                    break
        return hovered, chosen_layer

    def _load_choice_sprite(self):
        """
        Carga la imagen del asset actualmente elegido como brush.

        Returns:
            Surface o None si no hay choice_path.
        """
        choice_path = self.controller.editor_state.current_choice
        if not choice_path:
            return None
        return load_image(choice_path, (TILE_SIZE, TILE_SIZE))

    def _compute_panel_size(self, sprite_rows, layer_rows, font):
        """
        Calcula ancho, alto y dimensiones de filas para el panel.

        Returns:
            (panel_w, panel_h, row_dimensions)
        """
        margin_x, margin_y = 12, 12
        padding_x = 8
        spacing_y = 6
        row_widths, row_heights = [], []
        # Sprites
        for label, sprite, _ in sprite_rows:
            tw, th = font.size(label)
            w = TILE_SIZE + padding_x + tw
            h = max(TILE_SIZE, th)
            row_widths.append(w)
            row_heights.append(h)
        # Layers
        for label, val in layer_rows:
            lw, lh = font.size(label)
            vw, vh = font.size(val)
            w = lw + padding_x + vw
            h = lh + vh + spacing_y
            row_widths.append(w)
            row_heights.append(h)
        content_w = max(row_widths)
        content_h = sum(row_heights) + spacing_y * (len(row_heights) - 1)
        panel_w = content_w + margin_x * 2
        panel_h = content_h + margin_y * 2
        return panel_w, panel_h, {'widths': row_widths, 'heights': row_heights}

    def _compute_panel_position(self, screen, panel_w, panel_h):
        """
        Determina la posición del panel en pantalla.

        Usa override draggable o ancla en función de la herramienta activa.
        """
        if self.state.pos:
            return self.state.pos
        current = self.controller.editor_state.current_tool
        sw, sh = screen.get_size()
        margin = 12
        # Modos que muestran en top-right
        if current in ("brush", "select", "eyedropper", "delete", "default"):
            return sw - panel_w - margin, margin
        # Si no, al lado de la toolbar
        tb = self.controller.editor_controller.toolbar
        return tb.x + tb.size + tb.padding, tb.y

    def _create_panel_surface(self, w, h):
        """
        Genera la Surface semitransparente del panel.
        """
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill((30, 30, 30, 220))
        return surf

    def _draw_sprite_rows(self, panel_surf, sprite_rows, font, row_dims, screen_offset_x, screen_offset_y):
        """
        Dibuja las filas de sprites (hovered, selected, choice).
        Cada fila muestra el sprite y su etiqueta con outline.
        """
        margin_x, margin_y = 12, 12
        padding_x = 8
        spacing_y = 6
        y = margin_y
        for i, (label, sprite, outline) in enumerate(sprite_rows):
            # Sprite
            sx = margin_x
            sy = y + (row_dims['heights'][i] - TILE_SIZE) // 2
            if sprite:
                panel_surf.blit(sprite, (sx, sy))
            rect = pygame.Rect(sx, sy, TILE_SIZE, TILE_SIZE)
            pygame.draw.rect(panel_surf, outline, rect, 2)
            # Etiqueta
            text_surf = font.render(label, True, (245, 245, 245))
            tx = sx + TILE_SIZE + padding_x
            ty = y + (row_dims['heights'][i] - text_surf.get_height()) // 2
            panel_surf.blit(text_surf, (tx, ty))
            y += row_dims['heights'][i] + spacing_y

    def _draw_layer_rows(self, panel_surf, layer_rows, font, row_dims, screen_offset_x, screen_offset_y):
        """
        Dibuja las filas de layers con etiqueta y valor.
        """
        margin_x, margin_y, spacing_y = 12, 12, 6
        # Offset vertical: saltar filas de sprite
        y = margin_y + sum(row_dims['heights']) + spacing_y * len(row_dims['heights'])
        for label, val in layer_rows:
            lbl_surf = font.render(label, True, (245, 245, 245))
            panel_surf.blit(lbl_surf, (margin_x, y))
            y += lbl_surf.get_height() + spacing_y // 2
            val_surf = font.render(val, True, OUTLINE_CHOICE)
            panel_surf.blit(val_surf, (margin_x + 8, y))
            y += val_surf.get_height() + spacing_y
