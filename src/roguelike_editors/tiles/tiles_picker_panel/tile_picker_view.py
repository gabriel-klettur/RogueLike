import pygame
from pathlib import Path
from roguelike_editors.tiles.tiles_editor_config import (
    CLR_HOVER,
    CLR_SELECTION,
    THUMB,
    COLS,
    PAD,
    BTN_H,
    BTN_W,
    CLR_BORDER
)

class TilePickerView:
    """
    Vista del explorador de tiles:
    - Muestra miniaturas (archivos y carpetas) en una rejilla.
    - Destaca con borde cian al pasar el ratón.
    - Borde amarillo indica selección actual.
    - Botones Borrar y Default para funcionalidad de overlay.
    - Muestra el nombre de la carpeta sobre cada icono de directorio.
    """
    def __init__(self, controller, picker_state, assets):
        self.controller = controller
        self.picker_state = picker_state
        self.assets = assets
        self.icon_font = pygame.font.SysFont("Arial", 12)
        self.label_font = pygame.font.SysFont("Arial", 16)

    def _ellipsize(self, text, font, max_width):
        ellipsis = "..."
        if font.size(text)[0] <= max_width:
            return text
        for i in range(len(text), 0, -1):
            candidate = text[:i] + ellipsis
            if font.size(candidate)[0] <= max_width:
                return candidate
        return ellipsis

    def render(self, screen):
        if not self.picker_state.open:
            return
        hovered_value = None
        hovered_orig_size = None

        # Expand width to 3x columns
        cols = COLS * 3
        w = cols * (THUMB + PAD) + PAD
        rows = (len(self.assets) + cols - 1) // cols
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + BTN_H + PAD

        if self.picker_state.surface is None or self.picker_state.surface.get_size() != (w, h):
            self.picker_state.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.picker_state.surface.fill((20, 20, 20, 235))

        y0 = PAD - self.picker_state.scroll_offset
        mx, my = pygame.mouse.get_pos()
        lx = mx - (self.picker_state.pos[0] if self.picker_state.pos else 0)
        ly = my - (self.picker_state.pos[1] if self.picker_state.pos else 0)

        for idx, (value, thumb, is_dir, orig_size) in enumerate(self.assets):
            row, col = divmod(idx, cols)
            x = PAD + col * (THUMB + PAD)
            y = y0 + row * (THUMB + PAD)
            rect = pygame.Rect(x, y, THUMB, THUMB)
            if rect.bottom < PAD or rect.top > h_grid:
                continue

            self.picker_state.surface.blit(thumb, rect)
            if rect.collidepoint((lx, ly)):
                hovered_value = value
                hovered_orig_size = orig_size
                pygame.draw.rect(self.picker_state.surface, CLR_HOVER, rect, 3)
            elif self.picker_state.current_choice == value:
                pygame.draw.rect(self.picker_state.surface, CLR_SELECTION, rect, 3)

            # Si es carpeta y no la flecha "..", dibujamos el nombre encima
            if is_dir and value != "..":
                text = self._ellipsize(value, self.icon_font, THUMB - 4)
                label = self.icon_font.render(text, True, (0, 0, 0))
                label_rect = label.get_rect(center=(x + THUMB // 2, y + THUMB // 2))
                self.picker_state.surface.blit(label, label_rect)

        self.picker_state.btn_delete_rect = pygame.Rect(PAD, PAD + h_grid, BTN_W, BTN_H)
        self.picker_state.btn_default_rect = pygame.Rect(PAD*2 + BTN_W, PAD + h_grid, BTN_W, BTN_H)
        self._draw_button(self.picker_state.btn_delete_rect,  "Borrar")
        self._draw_button(self.picker_state.btn_default_rect, "Default")
        # Label for hovered or selected asset
        label_text = hovered_value if hovered_value else (self.picker_state.current_choice or "")
        if label_text:
            # Mostrar solo nombre del archivo/carpeta
            base_name = Path(label_text).name
            max_label_width = w - (PAD * 3 + BTN_W * 2) - PAD
            display_text = self._ellipsize(base_name, self.label_font, max_label_width)
            label_render = self.label_font.render(display_text, True, CLR_BORDER)
            label_pos = (PAD * 3 + BTN_W * 2, PAD + h_grid + BTN_H // 2 - label_render.get_height() // 2)
            self.picker_state.surface.blit(label_render, label_pos)
            # Dimensions label
            orig_size_label = hovered_orig_size
            if not hovered_value and not orig_size_label and self.picker_state.current_choice:
                # find orig_size for selected choice
                for v, _, _, o in self.assets:
                    if v == self.picker_state.current_choice:
                        orig_size_label = o
                        break
            if orig_size_label:
                dims_text = f"{orig_size_label[0]}x{orig_size_label[1]}"
                dims_render = self.label_font.render(dims_text, True, CLR_SELECTION)
                dims_pos = (label_pos[0] + label_render.get_width() + PAD, label_pos[1])
                self.picker_state.surface.blit(dims_render, dims_pos)

        # Close button at top-right
        close_size = BTN_H
        close_rect = pygame.Rect(w - PAD - close_size, PAD, close_size, close_size)
        # Draw close button
        pygame.draw.rect(self.picker_state.surface, (60, 60, 60), close_rect)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, close_rect, 1)
        # 'X' mark
        pygame.draw.line(self.picker_state.surface, CLR_BORDER, (close_rect.left+4, close_rect.top+4), (close_rect.right-4, close_rect.bottom-4), 2)
        pygame.draw.line(self.picker_state.surface, CLR_BORDER, (close_rect.right-4, close_rect.top+4), (close_rect.left+4, close_rect.bottom-4), 2)
        self.picker_state.btn_close_rect = close_rect

        if self.picker_state.pos is None:
            # Align to the right of TilesViewPanel
            vp_state = self.controller.editor_controller.view_panel_controller.state
            wvp, hvp = None, None
            if hasattr(vp_state, 'pos') and hasattr(vp_state, 'size') and vp_state.pos and vp_state.size:
                xvp, yvp = vp_state.pos
                wvp, hvp = vp_state.size
                margin = PAD
                self.picker_state.pos = (xvp + wvp + margin, yvp)
            else:
                sw, sh = screen.get_size()
                self.picker_state.pos = ((sw - w) // 2, (sh - h) // 2)
   
   


        # Checkbox for tileset filter
        cb_size = 16
        x_cb = w - PAD - cb_size
        y_cb = h - PAD - cb_size
        checkbox_rect = pygame.Rect(x_cb, y_cb, cb_size, cb_size)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, checkbox_rect, 1)
        # Label for tileset checkbox
        label_surf_cb = self.label_font.render('tileset', True, CLR_BORDER)
        label_pos_cb = (x_cb - PAD - label_surf_cb.get_width(), y_cb + (cb_size - label_surf_cb.get_height()) // 2)
        self.picker_state.surface.blit(label_surf_cb, label_pos_cb)
        screen.blit(self.picker_state.surface, self.picker_state.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.picker_state.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, rect, 1)
        txt = self.label_font.render(text, True, CLR_BORDER)
        self.picker_state.surface.blit(txt, txt.get_rect(center=rect.center))