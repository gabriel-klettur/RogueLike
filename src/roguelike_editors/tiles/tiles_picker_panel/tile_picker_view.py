import pygame
from roguelike_ui.widgets.text_input import TextInput
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
        # TextInput para tamaño de grid tileset
        self.tileset_text_input = TextInput(self.label_font)

    def _ellipsize(self, text, font, max_width):
        ellipsis = "..."
        if font.size(text)[0] <= max_width:
            return text
        for i in range(len(text), 0, -1):
            candidate = text[:i] + ellipsis
            if font.size(candidate)[0] <= max_width:
                return candidate
        return ellipsis

    def _compute_layout(self):
        """Compute columns, rows and panel dimensions"""
        cols = COLS * 3
        rows = (len(self.assets) + cols - 1) // cols
        w = cols * (THUMB + PAD) + PAD
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + BTN_H + PAD
        return cols, rows, w, h_grid, h

    def _init_surface(self, w, h):
        """Initialize or resize the picker surface."""
        if self.picker_state.surface is None or self.picker_state.surface.get_size() != (w, h):
            self.picker_state.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.picker_state.surface.fill((20, 20, 20, 235))

    def _get_local_coords(self):
        """Compute local mouse coords and y offset based on scroll."""
        y0 = PAD - self.picker_state.scroll_offset
        mx, my = pygame.mouse.get_pos()
        lx = mx - (self.picker_state.pos[0] if self.picker_state.pos else 0)
        ly = my - (self.picker_state.pos[1] if self.picker_state.pos else 0)
        return lx, ly, y0

    def _draw_assets_grid(self, lx, ly, y0, cols, h_grid):
        """Draw thumbnails grid, highlight hover/selection, return hovered value and orig size."""
        hovered_value = None
        hovered_orig_size = None
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
            if is_dir and value != "..":
                text = self._ellipsize(value, self.icon_font, THUMB - 4)
                label = self.icon_font.render(text, True, (0, 0, 0))
                label_rect = label.get_rect(center=(x + THUMB // 2, y + THUMB // 2))
                self.picker_state.surface.blit(label, label_rect)
        return hovered_value, hovered_orig_size

    def _draw_toolbar_and_labels(self, hovered_value, hovered_orig_size, w, h_grid):
        """Draw delete/default buttons and display hovered/selected labels."""
        self.picker_state.btn_delete_rect = pygame.Rect(PAD, PAD + h_grid, BTN_W, BTN_H)
        self.picker_state.btn_default_rect = pygame.Rect(PAD*2 + BTN_W, PAD + h_grid, BTN_W, BTN_H)
        self._draw_button(self.picker_state.btn_delete_rect, "Borrar")
        self._draw_button(self.picker_state.btn_default_rect, "Default")
        label_text = hovered_value if hovered_value else (self.picker_state.current_choice or "")
        if label_text:
            base_name = Path(label_text).name
            max_label_width = w - (PAD * 3 + BTN_W * 2) - PAD
            disp = self._ellipsize(base_name, self.label_font, max_label_width)
            rend = self.label_font.render(disp, True, CLR_BORDER)
            pos = (PAD * 3 + BTN_W * 2, PAD + h_grid + BTN_H//2 - rend.get_height()//2)
            self.picker_state.surface.blit(rend, pos)
            if not hovered_value and not hovered_orig_size and self.picker_state.current_choice:
                for v, _, _, o in self.assets:
                    if v == self.picker_state.current_choice:
                        hovered_orig_size = o
                        break
            if hovered_orig_size:
                text = f"{hovered_orig_size[0]}x{hovered_orig_size[1]}"
                dr = self.label_font.render(text, True, CLR_SELECTION)
                self.picker_state.surface.blit(dr, (pos[0] + rend.get_width() + PAD, pos[1]))

    def _draw_close_button(self, w):
        """Draw top-right close button and set its rect."""
        close_size = BTN_H
        rect = pygame.Rect(w - PAD - close_size, PAD, close_size, close_size)
        pygame.draw.rect(self.picker_state.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, rect, 1)
        pygame.draw.line(self.picker_state.surface, CLR_BORDER, (rect.left+4, rect.top+4), (rect.right-4, rect.bottom-4), 2)
        pygame.draw.line(self.picker_state.surface, CLR_BORDER, (rect.right-4, rect.top+4), (rect.left+4, rect.bottom-4), 2)
        self.picker_state.btn_close_rect = rect

    def _draw_tileset_filter_ui(self, w, h, h_grid):
        """Draw tileset checkbox, input, and "Crear tiles" button for grid slicing."""
        y_cb = h - PAD - 16
        cb = 16
        # reset button
        self.picker_state.btn_tileset_rect = None
        lab = self.label_font.render('tileset', True, CLR_BORDER)
        glab = self.label_font.render('tile size grid', True, CLR_BORDER)
        btn = self.label_font.render('Crear tiles', True, CLR_BORDER)
        lw, lh = lab.get_size()
        gw, gh = glab.get_size()
        iw = 50
        bw = btn.get_width() + PAD
        total = cb + PAD + lw + (PAD + gw + PAD + iw + PAD + bw if self.picker_state.tileset_filter else 0)
        start = w - PAD - total
        # checkbox
        cr = pygame.Rect(start, y_cb, cb, cb)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, cr, 1)
        if self.picker_state.tileset_filter:
            pygame.draw.line(self.picker_state.surface, CLR_SELECTION, (start+3, y_cb+3), (start+cb-3, y_cb+cb-3), 2)
            pygame.draw.line(self.picker_state.surface, CLR_SELECTION, (start+3, y_cb+cb-3), (start+cb-3, y_cb+3), 2)
        self.picker_state.tileset_checkbox_rect = cr
        # labels & input
        ox = start + cb + PAD
        self.picker_state.surface.blit(lab, (ox, y_cb + (cb-lh)//2))
        if self.picker_state.tileset_filter:
            gx = ox + lw + PAD
            self.picker_state.surface.blit(glab, (gx, y_cb + (cb-gh)//2))
            ix = gx + gw + PAD
            ir = pygame.Rect(ix, y_cb, iw, cb)
            pygame.draw.rect(self.picker_state.surface, (60,60,60), ir)
            pygame.draw.rect(self.picker_state.surface, CLR_BORDER, ir, 1)
            if self.picker_state.tileset_input_active:
                self.tileset_text_input.text = self.picker_state.tileset_grid_size_text
            self.tileset_text_input.draw(self.picker_state.surface, ix+4, y_cb + (cb-self.label_font.get_height())//2, CLR_BORDER)
            self.picker_state.tileset_input_rect = ir
            bx = ix + iw + PAD
            br = pygame.Rect(bx, y_cb, bw, cb)
            pygame.draw.rect(self.picker_state.surface, (60,60,60), br)
            pygame.draw.rect(self.picker_state.surface, CLR_BORDER, br, 1)
            self.picker_state.btn_tileset_rect = br
            self.picker_state.surface.blit(btn, (br.x + (bw-btn.get_width())//2, y_cb + (cb-btn.get_height())//2))

    def render(self, screen):
        """Render the tile picker by orchestrating layout, drawing assets and UI elements."""
        if not self.picker_state.open:
            return

        # Compute layout and initialize picker surface
        cols, rows, w, h_grid, h = self._compute_layout()
        self._init_surface(w, h)
        # Compute local coords and draw assets grid
        lx, ly, y0 = self._get_local_coords()
        hovered_value, hovered_orig_size = self._draw_assets_grid(lx, ly, y0, cols, h_grid)
        # Draw toolbar buttons and labels
        self._draw_toolbar_and_labels(hovered_value, hovered_orig_size, w, h_grid)
        # Draw close button
        self._draw_close_button(w)
        # Draw tileset filter UI
        self._draw_tileset_filter_ui(w, h, h_grid)

        # Initialize position if undefined
        if self.picker_state.pos is None:
            vp_state = self.controller.editor_controller.view_panel_controller.state
            if hasattr(vp_state, 'pos') and hasattr(vp_state, 'size') and vp_state.pos and vp_state.size:
                xvp, yvp = vp_state.pos
                wvp, hvp = vp_state.size
                margin = PAD
                self.picker_state.pos = (xvp + wvp + margin, yvp)
            else:
                sw, sh = screen.get_size()
                self.picker_state.pos = ((sw - w) // 2, (sh - h) // 2)

        # Blit picker surface
        screen.blit(self.picker_state.surface, self.picker_state.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.picker_state.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, rect, 1)
        txt = self.label_font.render(text, True, CLR_BORDER)
        self.picker_state.surface.blit(txt, txt.get_rect(center=rect.center))