import pygame
from roguelike_ui.widgets.text_input import TextInput
from pathlib import Path
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.grid import ScrollableGrid
# Config mode border colors
CONFIG_HOVER_COLOR = (128, 0, 128)
CONFIG_SELECTED_COLOR = (255, 0, 0)
# Tileset button hover color
TILESET_BTN_HOVER_COLOR = (255, 255, 0)

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
        # Hover overlay surface for tile grid cells
        self.hover_surface = pygame.Surface((THUMB, THUMB), pygame.SRCALPHA)
        self.hover_surface.fill((255, 255, 0, 100))
        # TextInput para tamaño de grid tileset
        self.tileset_text_input = TextInput(self.label_font)
        # Panel draggable
        self.panel = None

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
        """Compute grid parameters via ScrollableGrid"""
        grid = ScrollableGrid(THUMB, PAD, len(self.assets), self.picker_state.scroll_offset, cols=COLS*3)
        cols, rows, w, h_grid = grid.compute()
        h = h_grid + PAD + BTN_H + PAD
        return grid, cols, rows, w, h_grid, h

    def _init_panel(self, w, h):
        """Initialize DraggablePanel with size and background."""
        if self.panel is None:
            self.panel = DraggablePanel(w, h)
        else:
            self.panel.resize(w, h)
        # background fill handled by PanelSurface

    def _get_local_coords(self):
        """Compute local mouse coords and y offset based on scroll."""
        y0 = PAD - self.picker_state.scroll_offset
        mx, my = pygame.mouse.get_pos()
        # relative to panel position
        px, py = self.panel.pos if self.panel and self.panel.pos else (0, 0)
        lx = mx - px
        ly = my - py
        return lx, ly, y0

    def _draw_assets_grid(self, grid):
        """Use ScrollableGrid to draw assets, return hovered and orig size."""
        panel_pos = self.panel.pos or (0, 0)

        def draw_fn(surf, rect, asset, idx):
            value, thumb, is_dir, orig_size = asset
            surf.blit(thumb, rect)
            mx, my = pygame.mouse.get_pos()
            lx, ly = mx - panel_pos[0], my - panel_pos[1]
            if self.picker_state.config_mode:
                if idx == self.picker_state.config_src_idx:
                    pygame.draw.rect(surf, CONFIG_SELECTED_COLOR, rect, 3)
                elif rect.collidepoint((lx, ly)):
                    surf.blit(self.hover_surface, rect.topleft)
                    pygame.draw.rect(surf, CONFIG_HOVER_COLOR, rect, 3)
                elif self.picker_state.current_choice == value:
                    pygame.draw.rect(surf, CLR_SELECTION, rect, 3)
            else:
                if rect.collidepoint((lx, ly)):
                    surf.blit(self.hover_surface, rect.topleft)
                    pygame.draw.rect(surf, CLR_HOVER, rect, 3)
                elif self.picker_state.current_choice == value:
                    pygame.draw.rect(surf, CLR_SELECTION, rect, 3)
            if is_dir and value != "..":
                t = self._ellipsize(value, self.icon_font, THUMB - 4)
                label = self.icon_font.render(t, True, (0, 0, 0))
                surf.blit(label, label.get_rect(center=rect.center))

        hovered_asset = grid.draw_items(self.panel.surface, self.assets, panel_pos, draw_fn)
        if hovered_asset:
            value, _, _, size = hovered_asset
        else:
            value, size = None, None
        return value, size

    def _draw_toolbar_and_labels(self, hovered_value, hovered_orig_size, w, h_grid):
        """Draw delete/default buttons and display hovered/selected labels."""


        # Configurar Posición Tiles button
        cfg_text = "Configurar Posición Tiles"
        cfg_txt_surf = self.label_font.render(cfg_text, True, CLR_BORDER)
        cfg_bw = cfg_txt_surf.get_width() + PAD*2
        cfg_x = PAD
        cfg_rect = pygame.Rect(cfg_x, PAD + h_grid, cfg_bw, BTN_H)
        # Draw button background
        pygame.draw.rect(self.panel.surface, (60, 60, 60), cfg_rect)
        # Hover overlay for Configurar Posición Tiles button
        mouse_pos = pygame.mouse.get_pos()
        pos_x, pos_y = self.picker_state.pos or (0, 0)
        local_mouse = (mouse_pos[0] - pos_x, mouse_pos[1] - pos_y)
        if cfg_rect.collidepoint(local_mouse):
            hover_surf = pygame.Surface((cfg_rect.width, cfg_rect.height), pygame.SRCALPHA)
            hover_surf.fill((255, 255, 0, 100))
            self.panel.surface.blit(hover_surf, (cfg_rect.x, cfg_rect.y))
        # Border and text
        pygame.draw.rect(self.panel.surface, CLR_BORDER, cfg_rect, 1)
        self.panel.surface.blit(cfg_txt_surf, cfg_txt_surf.get_rect(center=cfg_rect.center))
        self.picker_state.btn_config_rect = cfg_rect
        if self.picker_state.config_mode:
            pygame.draw.rect(self.panel.surface, CLR_SELECTION, cfg_rect, 3)
        label_text = hovered_value if hovered_value else (self.picker_state.current_choice or "")
        if label_text:
            base_name = Path(label_text).name
            label_start_x = cfg_rect.right + PAD
            max_label_width = w - label_start_x - PAD
            disp = self._ellipsize(base_name, self.label_font, max_label_width)
            rend = self.label_font.render(disp, True, CLR_BORDER)
            pos = (label_start_x, PAD + h_grid + BTN_H//2 - rend.get_height()//2)
            self.panel.surface.blit(rend, pos)
            if not hovered_value and not hovered_orig_size and self.picker_state.current_choice:
                for v, _, _, o in self.assets:
                    if v == self.picker_state.current_choice:
                        hovered_orig_size = o
                        break
            if hovered_orig_size:
                text = f"{hovered_orig_size[0]}x{hovered_orig_size[1]}"
                dr = self.label_font.render(text, True, CLR_SELECTION)
                self.panel.surface.blit(dr, (pos[0] + rend.get_width() + PAD, pos[1]))

    def _draw_close_button(self, w):
        """Draw top-right close button and set its rect."""
        close_size = BTN_H
        rect = pygame.Rect(w - PAD - close_size, PAD, close_size, close_size)
        pygame.draw.rect(self.panel.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.panel.surface, CLR_BORDER, rect, 1)
        pygame.draw.line(self.panel.surface, CLR_BORDER, (rect.left+4, rect.top+4), (rect.right-4, rect.bottom-4), 2)
        pygame.draw.line(self.panel.surface, CLR_BORDER, (rect.right-4, rect.top+4), (rect.left+4, rect.bottom-4), 2)
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
        # Hover overlay for Tileset checkbox
        mouse_pos = pygame.mouse.get_pos()
        pos_x, pos_y = self.picker_state.pos or (0, 0)
        local_mouse = (mouse_pos[0] - pos_x, mouse_pos[1] - pos_y)
        if cr.collidepoint(local_mouse):
            hover_surf = pygame.Surface((cr.width, cr.height), pygame.SRCALPHA)
            hover_surf.fill((255, 255, 0, 100))
            self.panel.surface.blit(hover_surf, (cr.x, cr.y))
        # Draw checkbox border
        pygame.draw.rect(self.panel.surface, CLR_BORDER, cr, 1)
        if self.picker_state.tileset_filter:
            pygame.draw.line(self.panel.surface, CLR_SELECTION, (start+3, y_cb+3), (start+cb-3, y_cb+cb-3), 2)
            pygame.draw.line(self.panel.surface, CLR_SELECTION, (start+3, y_cb+cb-3), (start+cb-3, y_cb+3), 2)
        self.picker_state.tileset_checkbox_rect = cr
        # labels & input
        ox = start + cb + PAD
        self.panel.surface.blit(lab, (ox, y_cb + (cb-lh)//2))
        if self.picker_state.tileset_filter:
            gx = ox + lw + PAD
            self.panel.surface.blit(glab, (gx, y_cb + (cb-gh)//2))
            ix = gx + gw + PAD
            ir = pygame.Rect(ix, y_cb, iw, cb)
            pygame.draw.rect(self.panel.surface, (60,60,60), ir)
            pygame.draw.rect(self.panel.surface, CLR_BORDER, ir, 1)
            if self.picker_state.tileset_input_active:
                self.tileset_text_input.text = self.picker_state.tileset_grid_size_text
            self.tileset_text_input.draw(self.panel.surface, ix+4, y_cb + (cb-self.label_font.get_height())//2, CLR_BORDER)
            self.picker_state.tileset_input_rect = ir
            bx = ix + iw + PAD
            br = pygame.Rect(bx, y_cb, bw, cb)
            pygame.draw.rect(self.panel.surface, (60,60,60), br)
            # Hover effect: yellow border cuando se pasa por encima
            mouse_pos = pygame.mouse.get_pos()
            pos_x, pos_y = self.picker_state.pos or (0, 0)
            local_mouse = (mouse_pos[0] - pos_x, mouse_pos[1] - pos_y)
            if br.collidepoint(local_mouse):
                pygame.draw.rect(self.panel.surface, TILESET_BTN_HOVER_COLOR, br, 2)
            else:
                pygame.draw.rect(self.panel.surface, CLR_BORDER, br, 1)
            self.picker_state.btn_tileset_rect = br
            self.panel.surface.blit(btn, (br.x + (bw-btn.get_width())//2, y_cb + (cb-btn.get_height())//2))

    def render(self, screen):
        """Render the tile picker by orchestrating layout, drawing assets and UI elements."""
        if not self.picker_state.open:
            return

        # Compute layout and initialize picker surface
        grid, cols, rows, w, h_grid, h = self._compute_layout()
        self._init_panel(w, h)
        # Update state surface reference for event handling
        self.picker_state.surface = self.panel.surface
        # Draw assets grid via ScrollableGrid
        hovered_value, hovered_orig_size = self._draw_assets_grid(grid)
        # Draw toolbar buttons and labels
        self._draw_toolbar_and_labels(hovered_value, hovered_orig_size, w, h_grid)
        # Draw close button
        self._draw_close_button(w)
        # Draw tileset filter UI
        self._draw_tileset_filter_ui(w, h, h_grid)

        # Initialize position if undefined
        if self.picker_state.pos is None:
            # In brush mode, align picker to the right of size panel
            if self.controller.editor_state.current_tool == "brush" and self.controller.editor_controller.size_panel_controller.state.visible:
                sp_state = self.controller.editor_controller.size_panel_controller.state
                if sp_state.pos:
                    xsp, ysp = sp_state.pos
                else:
                    toolbar = self.controller.editor_controller.toolbar
                    xsp = toolbar.x + toolbar.size + toolbar.padding
                    ysp = toolbar.y
                margin = PAD
                self.picker_state.pos = (xsp + BTN_W + margin, ysp)
            else:
                # Default: align picker beside view panel
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
                # update panel position and blit
        self.panel.pos = self.picker_state.pos
        screen.blit(self.panel.surface, self.panel.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.panel.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.panel.surface, CLR_BORDER, rect, 1)
        txt = self.label_font.render(text, True, CLR_BORDER)
        self.panel.surface.blit(txt, txt.get_rect(center=rect.center))