import pygame
from roguelike_ui.widgets.text_input import TextInput
from pathlib import Path
from roguelike_ui.panel import DraggablePanel
from roguelike_ui.widgets.grid import ScrollableGrid
import time
import logging
logger = logging.getLogger(__name__)
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
        self.assets = [(value, thumb.convert_alpha(), is_dir, orig_size)
                       for value, thumb, is_dir, orig_size in assets]  # optimize surfaces
        self.icon_font = pygame.font.SysFont("Arial", 12)
        self.label_font = pygame.font.SysFont("Arial", 16)
        # Hover overlay surface for tile grid cells
        self.hover_surface = pygame.Surface((THUMB, THUMB), pygame.SRCALPHA)
        self.hover_surface.fill((255, 255, 0, 100))
        self.hover_surface = self.hover_surface.convert_alpha()
        self.hover_surface = pygame.Surface((THUMB, THUMB), pygame.SRCALPHA)
        self.hover_surface.fill((255, 255, 0, 100))
        # TextInput para tamaño de grid tileset
        self.tileset_text_input = TextInput(self.label_font)
        # Panel draggable
        self.panel = None
        # Static panel cache for render skipping
        self._last_state = None
        self.static_panel_surf = None

        # Cache static surfaces and hover overlays for toolbar and filter UI
        self.cfg_txt_surf = self.label_font.render("Configurar Posición Tiles", True, CLR_BORDER)
        self.cfg_bw = self.cfg_txt_surf.get_width() + PAD*2
        self.tileset_label_surf = self.label_font.render("tileset", True, CLR_BORDER)
        self.tileset_grid_label_surf = self.label_font.render("tile size grid", True, CLR_BORDER)
        self.tileset_btn_surf = self.label_font.render("Crear tiles", True, CLR_BORDER)
        self.tileset_btn_bw = self.tileset_btn_surf.get_width() + PAD
        # Hover overlays
        self.config_hover_surf = pygame.Surface((self.cfg_bw, BTN_H), pygame.SRCALPHA)
        self.config_hover_surf.fill((255, 255, 0, 100))
        self.config_hover_surf = self.config_hover_surf.convert_alpha()
        self.checkbox_hover_surf = pygame.Surface((16, 16), pygame.SRCALPHA)
        self.checkbox_hover_surf.fill((255, 255, 0, 100))
        self.checkbox_hover_surf = self.checkbox_hover_surf.convert_alpha()
        # Cache directory label surfaces and pre-render asset name and size surfaces

        self.asset_name_surfs = {}
        self.asset_size_surfs = {}
        # Compute max label width for toolbar
        grid_init = ScrollableGrid(THUMB, PAD, len(self.assets), 0, cols=COLS*3)
        _, _, w0, h_grid0 = grid_init.compute()
        label_start_x = PAD + self.cfg_bw + PAD
        max_width = w0 - label_start_x - PAD
        self.max_label_width = max_width
        for value, thumb, is_dir, orig_size in self.assets:
            name = Path(value).name
            disp = self._ellipsize(name, self.label_font, self.max_label_width)
            self.asset_name_surfs[value] = self.label_font.render(disp, True, CLR_BORDER)
            if orig_size is not None:
                size_text = f"{orig_size[0]}x{orig_size[1]}"
                self.asset_size_surfs[orig_size] = self.label_font.render(size_text, True, CLR_SELECTION)
        self.dir_label_surfs = {}
        for value, thumb, is_dir, orig_size in self.assets:
            if is_dir and value != "..":
                t = self._ellipsize(value, self.icon_font, THUMB - 4)
                self.dir_label_surfs[value] = self.icon_font.render(t, True, (0, 0, 0))

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
            # Optimize panel surface for fast blit
            self.panel.surface = self.panel.surface.convert_alpha()
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
        cols, rows, w, h_grid = grid.compute()
        cell_size, cell_pad = THUMB, PAD
        # Build/update static cache surface
        if not hasattr(self, 'assets_cache_surf') or self.assets_cache_size != (cols, rows):
            total_h = rows * (cell_size + cell_pad)
            # Create and optimize the static grid surface
            self.assets_cache_surf = pygame.Surface((w, total_h), pygame.SRCALPHA).convert_alpha()
            for idx, (value, thumb, is_dir, orig_size) in enumerate(self.assets):
                col_idx = idx % cols
                row_idx = idx // cols
                x0 = col_idx * (cell_size + cell_pad)
                y0 = row_idx * (cell_size + cell_pad)
                self.assets_cache_surf.blit(thumb, (x0, y0))
                if is_dir and value != '..':
                    label = self.dir_label_surfs.get(value)
                    if label:
                        self.assets_cache_surf.blit(label, label.get_rect(center=(x0 + cell_size//2, y0 + cell_size//2)))
            self.assets_cache_size = (cols, rows)
        # Draw static grid
        surf = self.panel.surface
        scroll = self.picker_state.scroll_offset
        surf.blit(self.assets_cache_surf, (0, 0), area=pygame.Rect(0, scroll, w, h_grid))
        # Compute dynamic overlay indices
        mx, my = pygame.mouse.get_pos()
        px, py = self.panel.pos or (0, 0)
        lx, ly = mx - px, my - py
        y_grid = ly + scroll
        hovered_idx = None
        if 0 <= lx < w and 0 <= y_grid < self.assets_cache_surf.get_height():
            col_c = lx // (cell_size + cell_pad)
            row_c = y_grid // (cell_size + cell_pad)
            idx = row_c * cols + col_c
            if 0 <= idx < len(self.assets):
                hovered_idx = idx
        # Find selected index
        current = self.picker_state.current_choice
        selected_idx = None
        if current:
            for i, (v, *_ ) in enumerate(self.assets):
                if v == current:
                    selected_idx = i
                    break
        # Draw overlays
        if self.picker_state.config_mode:
            src = self.picker_state.config_src_idx
            # selected source outline
            if src is not None:
                cs = src % cols; rs = src // cols
                rect = pygame.Rect(cs*(cell_size+cell_pad), rs*(cell_size+cell_pad)-scroll, cell_size, cell_size)
                pygame.draw.rect(surf, CONFIG_SELECTED_COLOR, rect, 3)
            # hover outline
            if hovered_idx is not None and hovered_idx != src:
                cs = hovered_idx % cols; rs = hovered_idx // cols
                rect = pygame.Rect(cs*(cell_size+cell_pad), rs*(cell_size+cell_pad)-scroll, cell_size, cell_size)
                surf.blit(self.hover_surface, rect.topleft)
                pygame.draw.rect(surf, CONFIG_HOVER_COLOR, rect, 3)
            # current choice outline
            if selected_idx is not None and selected_idx != src:
                cs = selected_idx % cols; rs = selected_idx // cols
                rect = pygame.Rect(cs*(cell_size+cell_pad), rs*(cell_size+cell_pad)-scroll, cell_size, cell_size)
                surf.blit(self.selection_overlay, rect.topleft)
            # return hovered info
            if hovered_idx is not None:
                v, _, _, sz = self.assets[hovered_idx]
                return v, sz
        else:
            # hover outline
            if hovered_idx is not None:
                cs = hovered_idx % cols; rs = hovered_idx // cols
                rect = pygame.Rect(cs*(cell_size+cell_pad), rs*(cell_size+cell_pad)-scroll, cell_size, cell_size)
                surf.blit(self.hover_surface, rect.topleft)
                pygame.draw.rect(surf, CLR_HOVER, rect, 3)
                v, _, _, sz = self.assets[hovered_idx]
                hovered_val, hovered_sz = v, sz
            else:
                hovered_val, hovered_sz = None, None
            # selection outline
            if selected_idx is not None:
                cs = selected_idx % cols; rs = selected_idx // cols
                rect = pygame.Rect(cs*(cell_size+cell_pad), rs*(cell_size+cell_pad)-scroll, cell_size, cell_size)
                surf.blit(self.selection_overlay, rect.topleft)
            return hovered_val, hovered_sz
        """Use ScrollableGrid to draw assets, return hovered and orig size."""
        panel_pos = self.panel.pos or (0, 0)
        # Precompute mouse and state for draw_fn
        mouse_pos = pygame.mouse.get_pos()
        local_mouse = (mouse_pos[0] - panel_pos[0], mouse_pos[1] - panel_pos[1])
        config_mode = self.picker_state.config_mode
        config_src_idx = self.picker_state.config_src_idx
        current_choice = self.picker_state.current_choice

        def draw_fn(surf, rect, asset, idx):
            value, thumb, is_dir, orig_size = asset
            surf.blit(thumb, rect)

            if config_mode:
                if idx == self.picker_state.config_src_idx:
                    pygame.draw.rect(surf, CONFIG_SELECTED_COLOR, rect, 3)
                elif rect.collidepoint(local_mouse):
                    surf.blit(self.hover_surface, rect.topleft)
                    pygame.draw.rect(surf, CONFIG_HOVER_COLOR, rect, 3)
                elif current_choice == value:
                    surf.blit(self.selection_overlay, rect.topleft)
            else:
                if rect.collidepoint(local_mouse):
                    surf.blit(self.hover_surface, rect.topleft)
                    pygame.draw.rect(surf, CLR_HOVER, rect, 3)
                elif current_choice == value:
                    surf.blit(self.selection_overlay, rect.topleft)
            if is_dir and value != "..":
                label = self.dir_label_surfs.get(value)
                if label:
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
        cfg_txt_surf = self.cfg_txt_surf
        cfg_bw = self.cfg_bw
        cfg_x = PAD
        cfg_rect = pygame.Rect(cfg_x, PAD + h_grid, cfg_bw, BTN_H)
        # Draw button background
        pygame.draw.rect(self.panel.surface, (60, 60, 60), cfg_rect)
        # Hover overlay for Configurar Posición Tiles button
        # Draw asset name and size using cached surfaces
        label_start_x = cfg_rect.right + PAD
        y_center = PAD + h_grid + BTN_H//2
        # Determine which asset to display
        key_value = hovered_value or self.picker_state.current_choice
        name_surf = self.asset_name_surfs.get(key_value)
        if name_surf:
            pos = (label_start_x, y_center - name_surf.get_height()//2)
            self.panel.surface.blit(name_surf, pos)
            # Draw size
            size = hovered_orig_size or next((o for v, _, _, o in self.assets if v == key_value), None)
            size_surf = self.asset_size_surfs.get(size)
            if size_surf:
                self.panel.surface.blit(size_surf, (pos[0] + name_surf.get_width() + PAD, pos[1]))
        # Else: no asset name to draw
        mouse_pos = pygame.mouse.get_pos()
        pos_x, pos_y = self.picker_state.pos or (0, 0)
        local_mouse = (mouse_pos[0] - pos_x, mouse_pos[1] - pos_y)
        if cfg_rect.collidepoint(local_mouse):
            self.panel.surface.blit(self.config_hover_surf, (cfg_rect.x, cfg_rect.y))
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
        lab = self.tileset_label_surf
        glab = self.tileset_grid_label_surf
        btn = self.tileset_btn_surf
        lw, lh = self.tileset_label_surf.get_size()
        gw, gh = self.tileset_grid_label_surf.get_size()
        iw = 50
        bw = self.tileset_btn_bw
        total = cb + PAD + lw + (PAD + gw + PAD + iw + PAD + bw if self.picker_state.tileset_filter else 0)
        start = w - PAD - total
        # checkbox
        cr = pygame.Rect(start, y_cb, cb, cb)
        # Hover overlay for Tileset checkbox
        mouse_pos = pygame.mouse.get_pos()
        pos_x, pos_y = self.picker_state.pos or (0, 0)
        local_mouse = (mouse_pos[0] - pos_x, mouse_pos[1] - pos_y)
        if cr.collidepoint(local_mouse):
            self.panel.surface.blit(self.checkbox_hover_surf, (cr.x, cr.y))
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
            self.panel.surface.blit(self.tileset_btn_surf, (br.x + (bw - self.tileset_btn_surf.get_width())//2, y_cb + (cb - self.tileset_btn_surf.get_height())//2))

    def render(self, screen):
        """Render the tile picker by orchestrating layout, drawing assets and UI elements."""
        if not self.picker_state.open:
            return

        # Compute layout and initialize picker surface
        grid, cols, rows, w, h_grid, h = self._compute_layout()
        self._init_panel(w, h)
        # Skip full redraw if state unchanged
        state = (
            self.picker_state.scroll_offset,
            self.picker_state.current_choice,
            self.picker_state.config_mode,
            getattr(self.picker_state, 'config_src_idx', None),
            getattr(self.picker_state, 'tileset_filter', None),
            self.picker_state.pos
        )
        if self.static_panel_surf is not None and state == self._last_state:
            # Blit cached surface and return
            self.panel.pos = self.picker_state.pos
            screen.blit(self.static_panel_surf, self.panel.pos)
            return
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
        # Cache last rendered panel surface and state
        self._last_state = state
        self.static_panel_surf = self.panel.surface.copy()

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.panel.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.panel.surface, CLR_BORDER, rect, 1)
        txt = self.label_font.render(text, True, CLR_BORDER)
        self.panel.surface.blit(txt, txt.get_rect(center=rect.center))