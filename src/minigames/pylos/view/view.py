from __future__ import annotations

import math
from dataclasses import dataclass
import os
from typing import Dict, Optional, Tuple, List

import pygame

from pylos.model import Board, Cell, GameState

Color = Tuple[int, int, int]


@dataclass
class ViewConfig:
    """Configuration for rendering the Pylos board."""

    width: int = 900
    height: int = 750
    bg_color: Color = (22, 22, 28)
    grid_color: Color = (70, 70, 90)
    layer_outline: Color = (120, 120, 150)
    player1_color: Color = (234, 93, 40)  # orange
    player2_color: Color = (80, 180, 255)  # sky blue
    empty_color: Color = (170, 170, 190)
    highlight_color: Color = (255, 235, 59)  # amber
    text_color: Color = (230, 230, 240)

    # Board geometry
    cell_size: int = 64
    gap: int = 10
    top_margin: int = 60
    bottom_panel_height: int = 80
    # Per-layer vertical offset (in pixels). Keep at 0 for perfect symmetry.
    layer_vertical_offset: int = 0
    # Vertical gap between the bottom status panel and the overlay toggle buttons
    toggle_gap: int = 48
    # --- Asset rendering options ---
    use_assets: bool = False
    assets_dir: str = os.path.join(os.path.dirname(__file__), "..", "assets")
    board_image: str = os.path.join(os.path.dirname(__file__), "..", "assets", "board.png")
    marble_a_image: str = os.path.join(os.path.dirname(__file__), "..", "assets", "bola_a.png")
    marble_b_image: str = os.path.join(os.path.dirname(__file__), "..", "assets", "bola_b.png")
    # Normalized rect inside board image that spans from the FIRST to LAST center of the 4x4 holes
    # Tune these if alignment looks off for your board asset
    grid_norm_left: float = 0.20
    grid_norm_top: float = 0.20
    grid_norm_width: float = 0.60
    grid_norm_height: float = 0.60
    # Marble visual scale factor relative to logical hole size (derived from grid)
    marble_scale: float = 1.00  # 1.0 fits snug; <1 smaller, >1 larger
    marble_scale_min: float = 0.60
    marble_scale_max: float = 1.40


class PylosView:
    """Responsible for rendering and screen-to-board picking.

    Maps each logical Cell to a screen-space circle (center, radius) for simple
    picking and drawing. Smaller layers are automatically centered over larger.
    """

    def __init__(self, screen: pygame.Surface, config: Optional[ViewConfig] = None) -> None:
        self.screen = screen
        self.cfg = config or ViewConfig()
        self.font = pygame.font.SysFont("consolas", 22)
        self.font_small = pygame.font.SysFont("consolas", 16)
        self.font_big = pygame.font.SysFont("consolas", 32)
        # Precompute positions for each cell as centers with a radius
        self.cell_centers: Dict[Cell, Tuple[int, int]] = {}
        self.pick_radius: int = max(14, self.cfg.cell_size // 2 - 6)
        self._bottom_panel_h: int = self.cfg.bottom_panel_height
        # Asset surfaces and rects
        self._board_img: Optional[pygame.Surface] = None
        self._marble_img_p1: Optional[pygame.Surface] = None
        self._marble_img_p2: Optional[pygame.Surface] = None
        self._board_rect: Optional[pygame.Rect] = None
        self._board_rect_manual: Optional[pygame.Rect] = None  # override when calibrating board placement/size
        # Load assets if enabled
        if self.cfg.use_assets:
            try:
                if os.path.exists(self.cfg.board_image):
                    self._board_img = pygame.image.load(self.cfg.board_image).convert_alpha()
                if os.path.exists(self.cfg.marble_a_image):
                    self._marble_img_p1 = pygame.image.load(self.cfg.marble_a_image).convert_alpha()
                if os.path.exists(self.cfg.marble_b_image):
                    self._marble_img_p2 = pygame.image.load(self.cfg.marble_b_image).convert_alpha()
            except Exception:
                # Fallback to shapes if load fails
                self._board_img = None
                self._marble_img_p1 = None
                self._marble_img_p2 = None
        self._compute_layout()
        # Last computed End Removal button rects (left=P1, right=P2)
        self._last_end_btn_left: Optional[pygame.Rect] = None
        self._last_end_btn_right: Optional[pygame.Rect] = None
        # Last computed Show Holes button rect
        self._last_showholes_btn: Optional[pygame.Rect] = None
        # Last computed Show Indices button rect
        self._last_indices_btn: Optional[pygame.Rect] = None
        # Last computed Show Labels button rect
        self._last_labels_btn: Optional[pygame.Rect] = None
        # Last computed win banner rect (for restart click)
        self._last_win_banner_rect: Optional[pygame.Rect] = None

    # --- Layout and picking ---
    def _compute_layer_top_left(self, layer_size: int) -> Tuple[int, int]:
        total_w = layer_size * self.cfg.cell_size + (layer_size - 1) * self.cfg.gap
        total_h = layer_size * self.cfg.cell_size + (layer_size - 1) * self.cfg.gap
        cx = self.cfg.width / 2.0
        # Respect dynamic bottom panel height
        cy = self.cfg.top_margin + (self.cfg.height - self._bottom_panel_h - self.cfg.top_margin) / 2.0
        x0 = int(round(cx - total_w / 2.0))
        y0 = int(round(cy - total_h / 2.0))
        return x0, y0

    def _compute_layout(self) -> None:
        self.cell_centers.clear()
        if self.cfg.use_assets and self._board_img is not None:
            # Compute board rect to fit between top margin and above bottom panel, centered horizontally
            if self._board_rect_manual is not None:
                self._board_rect = self._board_rect_manual.copy()
            else:
                avail_h = self.cfg.height - self._bottom_panel_h - self.cfg.top_margin - 20
                avail_w = self.cfg.width - 40
                img_w, img_h = self._board_img.get_width(), self._board_img.get_height()
                scale = min(avail_w / img_w, avail_h / img_h)
                bw = int(img_w * scale)
                bh = int(img_h * scale)
                bx = (self.cfg.width - bw) // 2
                by = self.cfg.top_margin + (self.cfg.height - self._bottom_panel_h - self.cfg.top_margin - bh) // 2
                self._board_rect = pygame.Rect(bx, by, bw, bh)
            # Compute grid screen rect using normalized values (first-to-last centers span)
            bx = self._board_rect.x
            by = self._board_rect.y
            bw = self._board_rect.width
            bh = self._board_rect.height
            gx = bx + int(self.cfg.grid_norm_left * bw)
            gy = by + int(self.cfg.grid_norm_top * bh)
            gw = int(self.cfg.grid_norm_width * bw)
            gh = int(self.cfg.grid_norm_height * bh)
            # Base step between centers on the 4x4 grid: 3 intervals
            step_x = gw / 3.0
            step_y = gh / 3.0
            # Derive pick radius from smaller step
            self.pick_radius = max(10, int(min(step_x, step_y) * 0.35))
            # Fill centers per layer, centered within the same span using offsets
            for layer, size in enumerate(Board.LAYER_SIZES):
                off_mul = (4 - size) / 2.0
                ox = gx + int(off_mul * step_x)
                oy = gy + int(off_mul * step_y)
                for y in range(size):
                    for x in range(size):
                        cx = int(round(ox + x * step_x))
                        cy = int(round(oy + y * step_y))
                        self.cell_centers[Cell(layer, x, y)] = (cx, cy)
        else:
            # Fallback: synthetic geometry
            for layer, size in enumerate(Board.LAYER_SIZES):
                x0, y0 = self._compute_layer_top_left(size)
                # Symmetric layout: no vertical staggering unless configured
                y0 -= layer * self.cfg.layer_vertical_offset
                for y in range(size):
                    for x in range(size):
                        cx = int(round(x0 + x * (self.cfg.cell_size + self.cfg.gap) + self.cfg.cell_size / 2.0))
                        cy = int(round(y0 + y * (self.cfg.cell_size + self.cfg.gap) + self.cfg.cell_size / 2.0))
                        self.cell_centers[Cell(layer, x, y)] = (cx, cy)

    def pick_cell(self, pos: Tuple[int, int], board: Board) -> Optional[Cell]:
        mx, my = pos
        # Prefer top layers first
        for layer in reversed(range(4)):
            size = Board.LAYER_SIZES[layer]
            for y in range(size):
                for x in range(size):
                    c = Cell(layer, x, y)
                    cx, cy = self.cell_centers[c]
                    if math.hypot(mx - cx, my - cy) <= self.pick_radius:
                        # Only allow picking empty and supported cells
                        if board.valid_placement(c):
                            return c
        return None

    def pick_cell_any(self, pos: Tuple[int, int]) -> Optional[Cell]:
        """Pick any cell (occupied or not) purely by proximity."""
        mx, my = pos
        for layer in reversed(range(4)):
            size = Board.LAYER_SIZES[layer]
            for y in range(size):
                for x in range(size):
                    c = Cell(layer, x, y)
                    cx, cy = self.cell_centers[c]
                    if math.hypot(mx - cx, my - cy) <= self.pick_radius:
                        return c
        return None

    def pick_removal_candidate(self, pos: Tuple[int, int], state: GameState) -> Optional[Cell]:
        """Pick the nearest own free marble under the cursor for REMOVAL.

        Scans all layers and returns the closest candidate within the pick radius,
        avoiding empty upper-layer cells capturing hover.
        """
        mx, my = pos
        board = state.board
        best: Optional[Tuple[float, Cell]] = None
        for cell, (cx, cy) in self.cell_centers.items():
            owner = board.get(cell)
            if owner != state.current_player:
                continue
            if not board.is_free(cell):
                continue
            d = math.hypot(mx - cx, my - cy)
            if d <= self.pick_radius:
                if best is None or d < best[0]:
                    best = (d, cell)
        return best[1] if best else None

    # --- Drawing ---
    def draw(
        self,
        state: GameState,
        hovered: Optional[Cell],
        info: str = "",
        is_ai_p2: bool = True,
        selected_src: Optional[Cell] = None,
        preview_player_id: Optional[int] = None,
        removal_preview_player_id: Optional[int] = None,
        show_holes: bool = False,
        show_indices: bool = False,
        show_labels: bool = True,
        calibration_mode: bool = False,
    ) -> None:
        self.screen.fill(self.cfg.bg_color)
        # Measure dynamic status panel height for this frame and recompute layout accordingly
        help_text = "  |  [LMB] Place  [RMB] Select  [H] Huecos  [I] Índices  [Space] End Removal  [R] Reset  [1-5] Depth  [Esc] Quit"
        full_text = state.status_text() + help_text + (f"  |  {info}" if info else "")
        padding_x = 24
        padding_y = 16
        max_width = self.cfg.width - 2 * padding_x
        lines = self._wrap_text(full_text, max_width)
        line_height = self.font.get_height()
        needed_h = padding_y * 2 + line_height * len(lines)
        self._bottom_panel_h = max(self.cfg.bottom_panel_height, needed_h)
        self._compute_layout()
        self._draw_layers()
        # Draw the bottom status panel first so other UI (buttons) can render above it
        self._draw_status(state, info)
        # Toggle overlays buttons (positioned just above the bottom panel), responsive to panel width
        self._draw_toggle_buttons(show_labels, show_holes, show_indices, calibration_mode)
        self._draw_marbles(state, show_labels)
        # Calibration overlay (white guides) to adjust board grid mapping
        if calibration_mode:
            self._draw_calibration_overlay()
        if show_holes:
            self._draw_available_holes(state.board)
        if show_indices:
            self._draw_cell_indices()
        # Preview for removal takes precedence over placement preview
        if hovered is not None and removal_preview_player_id in (1, 2):
            self._draw_removal_preview(hovered, removal_preview_player_id)
        elif hovered is not None and preview_player_id in (1, 2):
            self._draw_preview(hovered, preview_player_id)
        self._draw_hover(hovered)
        if selected_src is not None:
            self._draw_selected(selected_src)
        self._draw_side_counters(state, is_ai_p2)
        # Show win banner centered above the board if the game ended
        self._draw_win_banner(state)
        # Draw cursor ghost last so it stays on top (only for placement preview)
        if preview_player_id in (1, 2) and removal_preview_player_id is None:
            self._draw_cursor_preview(preview_player_id)

    def _draw_layers(self) -> None:
        if self.cfg.use_assets and self._board_img is not None and self._board_rect is not None:
            board_scaled = pygame.transform.smoothscale(self._board_img, (self._board_rect.width, self._board_rect.height))
            self.screen.blit(board_scaled, self._board_rect.topleft)
        else:
            # Draw layer outlines to convey pyramid levels (synthetic)
            for layer, size in enumerate(Board.LAYER_SIZES):
                x0, y0 = self._compute_layer_top_left(size)
                y0 -= layer * self.cfg.layer_vertical_offset
                w = size * self.cfg.cell_size + (size - 1) * self.cfg.gap
                h = w
                rect = pygame.Rect(x0 - 8, y0 - 8, w + 16, h + 16)
                pygame.draw.rect(self.screen, self.cfg.layer_outline, rect, width=2, border_radius=8)
                # Grid dots for the cells
                for y in range(size):
                    for x in range(size):
                        cx = int(round(x0 + x * (self.cfg.cell_size + self.cfg.gap) + self.cfg.cell_size / 2.0))
                        cy = int(round(y0 + y * (self.cfg.cell_size + self.cfg.gap) + self.cfg.cell_size / 2.0))
                        pygame.draw.circle(self.screen, self.cfg.grid_color, (cx, cy), 4)

    def _draw_available_holes(self, board: Board) -> None:
        """Draw semi-transparent green overlays on all supported empty cells."""
        holes = board.valid_moves()
        if not holes:
            return
        fill = (76, 175, 80, 90)  # green with alpha
        rim = (56, 142, 60, 180)
        for cell in holes:
            cx, cy = self.cell_centers[cell]
            r = self.pick_radius - 2
            d = r * 2 + 6
            ghost = pygame.Surface((d, d), pygame.SRCALPHA)
            center = (d // 2, d // 2)
            pygame.draw.circle(ghost, fill, center, r)
            pygame.draw.circle(ghost, rim, center, r, width=2)
            self.screen.blit(ghost, (cx - d // 2, cy - d // 2))

    def _draw_toggle_buttons(self, labels_enabled: bool, holes_enabled: bool, indices_enabled: bool, calibration_enabled: bool) -> None:
        """Responsive layout for 'Mostrar huecos' and 'Mostrar índices' above the bottom panel.

        If both button widths fit within the legend content width, draw them side-by-side centered.
        Otherwise, stack them vertically (Índices arriba, Huecos abajo) centrados.
        """
        # Labels and text surfaces
        holes_label = "Ocultar huecos" if holes_enabled else "Mostrar huecos"
        idx_label = "Ocultar índices" if indices_enabled else "Mostrar índices"
        cfg_label = "Salir config" if calibration_enabled else "Configuración"
        labels_label = "Ocultar info" if labels_enabled else "Mostrar info"
        holes_txt = self.font_small.render(holes_label, True, (250, 250, 255))
        idx_txt = self.font_small.render(idx_label, True, (250, 250, 255))
        cfg_txt = self.font_small.render(cfg_label, True, (250, 250, 255))
        labels_txt = self.font_small.render(labels_label, True, (250, 250, 255))

        pad_x, pad_y = 10, 6
        holes_w = holes_txt.get_width() + 2 * pad_x
        holes_h = holes_txt.get_height() + 2 * pad_y
        idx_w = idx_txt.get_width() + 2 * pad_x
        idx_h = idx_txt.get_height() + 2 * pad_y
        cfg_w = cfg_txt.get_width() + 2 * pad_x
        cfg_h = cfg_txt.get_height() + 2 * pad_y
        lab_w = labels_txt.get_width() + 2 * pad_x
        lab_h = labels_txt.get_height() + 2 * pad_y

        panel_top = self.cfg.height - self._bottom_panel_h
        gap = max(12, self.cfg.toggle_gap)
        # Use the same inner padding width as status panel word-wrap
        legend_pad_x = 24
        available_w = self.cfg.width - 2 * legend_pad_x
        center_x = self.cfg.width // 2

        # Decide layout: inline or stacked
        inline = (lab_w + gap + holes_w + gap + idx_w + gap + cfg_w) <= available_w

        bg_off = (40, 40, 50)
        bg_on_holes = (56, 142, 60)
        bg_on_idx = (84, 110, 122)
        bg_cfg = (90, 90, 100)
        bg_on_labels = (123, 31, 162)
        outline = (20, 20, 26)

        if inline:
            group_w = lab_w + gap + holes_w + gap + idx_w + gap + cfg_w
            start_x = center_x - group_w // 2
            by = max(8, panel_top - max(holes_h, idx_h) - gap)
            # Labels button (left-most)
            lab_rect = pygame.Rect(start_x, by, lab_w, lab_h)
            # Holes button
            holes_rect = pygame.Rect(lab_rect.right + gap, by, holes_w, holes_h)
            # Indices button
            idx_rect = pygame.Rect(holes_rect.right + gap, by, idx_w, idx_h)
            # Config button to the right
            cfg_rect = pygame.Rect(idx_rect.right + gap, by, cfg_w, max(idx_h, holes_h))
        else:
            # Stack: índices arriba, huecos abajo
            lab_by = max(8, panel_top - lab_h - gap)
            lab_rect = pygame.Rect(center_x - lab_w // 2, lab_by, lab_w, lab_h)
            holes_by = max(8, lab_rect.y - gap - holes_h)
            holes_rect = pygame.Rect(center_x - holes_w // 2, holes_by, holes_w, holes_h)
            idx_by = max(8, holes_rect.y - gap - idx_h)
            idx_rect = pygame.Rect(center_x - idx_w // 2, idx_by, idx_w, idx_h)
            # Place config button above indices if stacked
            cfg_by = max(8, idx_rect.y - gap - cfg_h)
            cfg_rect = pygame.Rect(center_x - cfg_w // 2, cfg_by, cfg_w, cfg_h)

        # Store rects for controller hit-tests
        self._last_labels_btn = lab_rect
        self._last_showholes_btn = holes_rect
        self._last_indices_btn = idx_rect
        self._last_config_btn = cfg_rect

        # Draw labels button
        pygame.draw.rect(self.screen, bg_on_labels if labels_enabled else bg_off, lab_rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, lab_rect, width=2, border_radius=8)
        self.screen.blit(labels_txt, (lab_rect.centerx - labels_txt.get_width() // 2, lab_rect.centery - labels_txt.get_height() // 2))

        # Draw indices button
        pygame.draw.rect(self.screen, bg_on_idx if indices_enabled else bg_off, idx_rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, idx_rect, width=2, border_radius=8)
        self.screen.blit(idx_txt, (idx_rect.centerx - idx_txt.get_width() // 2, idx_rect.centery - idx_txt.get_height() // 2))

        # Draw holes button
        pygame.draw.rect(self.screen, bg_on_holes if holes_enabled else bg_off, holes_rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, holes_rect, width=2, border_radius=8)
        self.screen.blit(holes_txt, (holes_rect.centerx - holes_txt.get_width() // 2, holes_rect.centery - holes_txt.get_height() // 2))
        # Draw config button: match other buttons when OFF, turn gray when ON
        cfg_bg = bg_cfg if calibration_enabled else bg_off
        pygame.draw.rect(self.screen, cfg_bg, cfg_rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, cfg_rect, width=2, border_radius=8)
        self.screen.blit(cfg_txt, (cfg_rect.centerx - cfg_txt.get_width() // 2, cfg_rect.centery - cfg_txt.get_height() // 2))

    def get_config_button_rect(self) -> Optional[pygame.Rect]:
        return getattr(self, "_last_config_btn", None)

    def get_labels_button_rect(self) -> Optional[pygame.Rect]:
        return self._last_labels_btn

    def get_board_rect(self) -> Optional[pygame.Rect]:
        return self._board_rect

    def get_grid_rect(self) -> Optional[pygame.Rect]:
        # Return the current 4x4 span rect in screen space (first-to-last centers)
        if self._board_rect is None:
            return None
        bw, bh = self._board_rect.width, self._board_rect.height
        gx = self._board_rect.x + int(self.cfg.grid_norm_left * bw)
        gy = self._board_rect.y + int(self.cfg.grid_norm_top * bh)
        gw = int(self.cfg.grid_norm_width * bw)
        gh = int(self.cfg.grid_norm_height * bh)
        return pygame.Rect(gx, gy, gw, gh)

    def pick_calibration_handle(self, pos: Tuple[int, int]) -> Optional[str]:
        """Return which calibration handle is under pos: one of
        'move', 'tl', 'tr', 'bl', 'br', 'l', 'r', 't', 'b' or None.
        """
        rect = self.get_grid_rect()
        if rect is None:
            return None
        x, y = pos
        # Corner handle size
        hs = 14
        tl = pygame.Rect(rect.left - hs // 2, rect.top - hs // 2, hs, hs)
        tr = pygame.Rect(rect.right - hs // 2, rect.top - hs // 2, hs, hs)
        bl = pygame.Rect(rect.left - hs // 2, rect.bottom - hs // 2, hs, hs)
        br = pygame.Rect(rect.right - hs // 2, rect.bottom - hs // 2, hs, hs)
        if tl.collidepoint((x, y)):
            return "tl"
        if tr.collidepoint((x, y)):
            return "tr"
        if bl.collidepoint((x, y)):
            return "bl"
        if br.collidepoint((x, y)):
            return "br"
        if rect.collidepoint((x, y)):
            return "move"
        return None

    def _draw_calibration_overlay(self) -> None:
        """Draw white guides for the 4x4 grid span, handles, and hole centers."""
        # 1) Board rect overlay (green)
        if self._board_rect is not None:
            b = self._board_rect
            green = (76, 175, 80)
            pygame.draw.rect(self.screen, green, b, width=3)
            # corner handles
            hs = 14
            for (hx, hy) in [
                (b.left, b.top),
                (b.right, b.top),
                (b.left, b.bottom),
                (b.right, b.bottom),
            ]:
                hrect = pygame.Rect(hx - hs // 2, hy - hs // 2, hs, hs)
                pygame.draw.rect(self.screen, green, hrect)
                pygame.draw.rect(self.screen, (20, 20, 26), hrect, width=2)
            # cache for picking
            self._last_board_handle_rects = {
                "tl": pygame.Rect(b.left - hs // 2, b.top - hs // 2, hs, hs),
                "tr": pygame.Rect(b.right - hs // 2, b.top - hs // 2, hs, hs),
                "bl": pygame.Rect(b.left - hs // 2, b.bottom - hs // 2, hs, hs),
                "br": pygame.Rect(b.right - hs // 2, b.bottom - hs // 2, hs, hs),
                "move": b.copy(),
            }

        # 2) Grid rect overlay (yellow + centers + size knob)
        rect = self.get_grid_rect()
        if rect is None:
            return
        # Outline (yellow) over the board image
        pygame.draw.rect(self.screen, self.cfg.highlight_color, rect, width=3)
        # Corner handles
        hs = 14
        for (hx, hy) in [
            (rect.left, rect.top),
            (rect.right, rect.top),
            (rect.left, rect.bottom),
            (rect.right, rect.bottom),
        ]:
            handle = pygame.Rect(hx - hs // 2, hy - hs // 2, hs, hs)
            pygame.draw.rect(self.screen, self.cfg.highlight_color, handle)
            pygame.draw.rect(self.screen, (20, 20, 26), handle, width=2)
        # White hole centers (4x4)
        step_x = rect.width / 3.0
        step_y = rect.height / 3.0
        r = max(6, int(min(step_x, step_y) * 0.28))
        for j in range(4):
            for i in range(4):
                cx = int(round(rect.left + i * step_x))
                cy = int(round(rect.top + j * step_y))
                pygame.draw.circle(self.screen, (255, 255, 255), (cx, cy), r, width=2)
        # Size preview/handle: use the (1,1) center as anchor and draw a draggable circle with a small knob
        anchor_cx = int(round(rect.left + 1 * step_x))
        anchor_cy = int(round(rect.top + 1 * step_y))
        logical_r = min(step_x, step_y) * 0.5
        vis_r = int(logical_r * self.cfg.marble_scale)
        # Main preview circle (semi-transparent white)
        pygame.draw.circle(self.screen, (255, 255, 255, 40), (anchor_cx, anchor_cy), vis_r)
        pygame.draw.circle(self.screen, self.cfg.highlight_color, (anchor_cx, anchor_cy), vis_r, width=2)
        # Knob at the rightmost point
        knob_r = max(6, int(vis_r * 0.15))
        knob_x = anchor_cx + vis_r
        knob_y = anchor_cy
        knob_rect = pygame.Rect(knob_x - knob_r, knob_y - knob_r, knob_r * 2, knob_r * 2)
        pygame.draw.circle(self.screen, self.cfg.highlight_color, (knob_x, knob_y), knob_r)
        pygame.draw.circle(self.screen, (20, 20, 26), (knob_x, knob_y), knob_r, width=2)
        # Store for picking this frame
        self._last_size_handle = {
            "center": (anchor_cx, anchor_cy),
            "radius": vis_r,
            "knob_rect": knob_rect,
            "logical_r": logical_r,
        }

    def pick_size_handle(self, pos: Tuple[int, int]) -> bool:
        data = getattr(self, "_last_size_handle", None)
        if not data:
            return False
        return data["knob_rect"].collidepoint(pos)

    def size_scale_from_pos(self, pos: Tuple[int, int]) -> Optional[float]:
        data = getattr(self, "_last_size_handle", None)
        if not data:
            return None
        cx, cy = data["center"]
        logical_r: float = float(data["logical_r"]) if data.get("logical_r") else 0.0
        if logical_r <= 0:
            return None
        dx = pos[0] - cx
        dy = pos[1] - cy
        dist = (dx * dx + dy * dy) ** 0.5
        scale = max(self.cfg.marble_scale_min, min(self.cfg.marble_scale_max, dist / logical_r))
        return scale

    # --- Board rect editing helpers ---
    def ensure_board_manual(self) -> None:
        if self._board_rect is not None and self._board_rect_manual is None:
            self._board_rect_manual = self._board_rect.copy()

    def set_board_manual_rect(self, rect: pygame.Rect) -> None:
        # keep it clamped to screen and above bottom panel
        rect = rect.copy()
        rect.width = max(80, rect.width)
        rect.height = max(80, rect.height)
        rect.x = max(0, min(self.cfg.width - rect.width, rect.x))
        max_h = self.cfg.height - self._bottom_panel_h - 4
        rect.y = max(0, min(max_h - rect.height, rect.y))
        self._board_rect_manual = rect

    def pick_board_handle(self, pos: Tuple[int, int]) -> Optional[str]:
        data = getattr(self, "_last_board_handle_rects", None)
        if not data:
            return None
        for key in ("tl", "tr", "bl", "br"):
            if data[key].collidepoint(pos):
                return f"board_{key}"
        if data["move"].collidepoint(pos):
            return "board_move"
        return None

    def _draw_showholes_button(self, enabled: bool) -> None:
        """Draw the Show Holes toggle button just above the bottom status panel."""
        label = "Ocultar huecos" if enabled else "Mostrar huecos"
        text = self.font_small.render(label, True, (250, 250, 255))
        pad_x, pad_y = 10, 6
        w = text.get_width() + 2 * pad_x
        h = text.get_height() + 2 * pad_y
        panel_top = self.cfg.height - self._bottom_panel_h
        bx = self.cfg.width // 2 - w // 2
        by = max(8, panel_top - h - 8)
        rect = pygame.Rect(bx, by, w, h)
        self._last_showholes_btn = rect
        # Visuals
        bg = (40, 40, 50)
        bg_on = (56, 142, 60)
        outline = (20, 20, 26)
        pygame.draw.rect(self.screen, bg_on if enabled else bg, rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, rect, width=2, border_radius=8)
        self.screen.blit(text, (rect.centerx - text.get_width() // 2, rect.centery - text.get_height() // 2))

    def get_showholes_button_rect(self) -> Optional[pygame.Rect]:
        return self._last_showholes_btn

    def _draw_indices_button(self, enabled: bool) -> None:
        """Draw a toggle button next to the holes button, above the bottom panel."""
        holes_rect = self._last_showholes_btn
        if holes_rect is None:
            # Fallback to top-center if holes button hasn't been drawn yet
            x0, y0 = self._compute_layer_top_left(Board.LAYER_SIZES[0])
            board_w = Board.LAYER_SIZES[0] * self.cfg.cell_size + (Board.LAYER_SIZES[0] - 1) * self.cfg.gap
            cx = x0 + board_w // 2
            pad_x, pad_y = 10, 6
            label = "Ocultar índices" if enabled else "Mostrar índices"
            text = self.font_small.render(label, True, (250, 250, 255))
            w = text.get_width() + 2 * pad_x
            h = text.get_height() + 2 * pad_y
            panel_top = self.cfg.height - self._bottom_panel_h
            rect = pygame.Rect(cx + 8, max(8, panel_top - h - 8), w, h)
        else:
            label = "Ocultar índices" if enabled else "Mostrar índices"
            text = self.font_small.render(label, True, (250, 250, 255))
            pad_x, pad_y = 10, 6
            w = text.get_width() + 2 * pad_x
            h = holes_rect.height
            rect = pygame.Rect(holes_rect.right + 8, holes_rect.y, w, h)
        self._last_indices_btn = rect
        bg = (40, 40, 50)
        bg_on = (84, 110, 122)
        outline = (20, 20, 26)
        pygame.draw.rect(self.screen, bg_on if enabled else bg, rect, border_radius=8)
        pygame.draw.rect(self.screen, outline, rect, width=2, border_radius=8)
        self.screen.blit(text, (rect.centerx - text.get_width() // 2, rect.centery - text.get_height() // 2))

    def get_indices_button_rect(self) -> Optional[pygame.Rect]:
        return self._last_indices_btn

    def _draw_cell_indices(self) -> None:
        """Overlay matrix indices (row,col) on every cell center for all layers (base-1)."""
        for cell, (cx, cy) in self.cell_centers.items():
            # row = y, col = x (display 1-based)
            label = f"{cell.y + 1},{cell.x + 1}"
            txt = self.font_small.render(label, True, (250, 250, 255))
            sh = self.font_small.render(label, True, (20, 20, 26))
            # place near top-left inside the marble circle
            r = self.pick_radius
            x = cx - r + 4
            y = cy - r + 2
            self.screen.blit(sh, (x + 1, y + 1))
            self.screen.blit(txt, (x, y))

    def _draw_marbles(self, state: GameState, show_labels: bool) -> None:
        board = state.board
        removal_active = getattr(state, "subphase", None) is not None and state.subphase.name == "REMOVAL"
        # Determine base diameter from grid if assets are active
        grid_rect = self.get_grid_rect() if (self.cfg.use_assets and self._board_img is not None) else None
        if grid_rect is not None:
            step_d = min(grid_rect.width / 3.0, grid_rect.height / 3.0)
            base_d = int(step_d * 0.70)
        else:
            base_d = int(self.pick_radius * 2)
        for cell, (cx, cy) in self.cell_centers.items():
            owner = board.get(cell)
            if owner is None:
                # Draw faint circle for empty cells on bottom layer only to avoid clutter
                if cell.layer == 0 and not (self.cfg.use_assets and self._board_img is not None):
                    pygame.draw.circle(self.screen, self.cfg.empty_color, (cx, cy), int(self.pick_radius * self.cfg.marble_scale), width=2)
                continue
            if self.cfg.use_assets and (self._marble_img_p1 is not None and self._marble_img_p2 is not None):
                img = self._marble_img_p1 if owner == 1 else self._marble_img_p2
                d = max(6, int(base_d * self.cfg.marble_scale))
                sprite = pygame.transform.smoothscale(img, (d, d))
                self.screen.blit(sprite, (cx - d // 2, cy - d // 2))
            else:
                color = self.cfg.player1_color if owner == 1 else self.cfg.player2_color
                pygame.draw.circle(self.screen, color, (cx, cy), int(self.pick_radius * self.cfg.marble_scale))
                # subtle rim
                pygame.draw.circle(self.screen, (20, 20, 26), (cx, cy), int(self.pick_radius * self.cfg.marble_scale), width=2)
            # Overlays: 'L' (free) and level in parentheses (conditional)
            is_free = board.is_free(cell)
            if show_labels:
                if is_free:
                    label = self.font_small.render("L", True, (240, 240, 250))
                    shadow = self.font_small.render("L", True, (20, 20, 26))
                    lx = cx - label.get_width() // 2
                    ly = cy - label.get_height() // 2
                    # shadow first
                    self.screen.blit(shadow, (lx + 1, ly + 1))
                    self.screen.blit(label, (lx, ly))

                level_text = f"({cell.layer + 1})"  # Display levels as 1..4 (base-1)
                lvl = self.font_small.render(level_text, True, (240, 240, 250))
                lvl_sh = self.font_small.render(level_text, True, (20, 20, 26))
                if is_free:
                    # Place level just below the centered 'L'
                    lvy = cy + (label.get_height() // 2) + 2
                    lvx = cx - lvl.get_width() // 2
                else:
                    # Center the level on the marble
                    lvx = cx - lvl.get_width() // 2
                    lvy = cy - lvl.get_height() // 2
                self.screen.blit(lvl_sh, (lvx + 1, lvy + 1))
                self.screen.blit(lvl, (lvx, lvy))
            # If in REMOVAL subphase, highlight all candidates (own & free)
            if removal_active and owner == state.current_player and board.is_free(cell):
                pygame.draw.circle(self.screen, (220, 68, 68), (cx, cy), self.pick_radius + 2, width=3)

    def _draw_hover(self, hovered: Optional[Cell]) -> None:
        if hovered is None:
            return
        cx, cy = self.cell_centers[hovered]
        pygame.draw.circle(self.screen, self.cfg.highlight_color, (cx, cy), self.pick_radius + 4, width=3)

    def _draw_selected(self, cell: Cell) -> None:
        cx, cy = self.cell_centers[cell]
        pygame.draw.circle(self.screen, (76, 175, 80), (cx, cy), self.pick_radius + 6, width=3)  # green

    def _draw_preview(self, cell: Cell, player_id: int) -> None:
        """Draw a semi-transparent preview marble at the hovered cell.

        Helps clarify which color will be placed by the human player.
        """
        cx, cy = self.cell_centers[cell]
        color = self.cfg.player1_color if player_id == 1 else self.cfg.player2_color
        r = self.pick_radius
        # Create a small SRCALPHA surface for soft transparency
        d = r * 2 + 8
        ghost = pygame.Surface((d, d), pygame.SRCALPHA)
        center = (d // 2, d // 2)
        fill = (*color, 120)  # semi-transparent fill
        rim = (20, 20, 26, 180)
        pygame.draw.circle(ghost, fill, center, r)
        pygame.draw.circle(ghost, rim, center, r, width=2)
        self.screen.blit(ghost, (cx - d // 2, cy - d // 2))

    def _draw_removal_preview(self, cell: Cell, player_id: int) -> None:
        """Draw a removal preview: tinted overlay with an X to indicate removal."""
        cx, cy = self.cell_centers[cell]
        r = self.pick_radius
        d = r * 2 + 10
        ghost = pygame.Surface((d, d), pygame.SRCALPHA)
        center = (d // 2, d // 2)
        # Use player's color but with red tint for removal intent
        base = self.cfg.player1_color if player_id == 1 else self.cfg.player2_color
        tint = (220, 68, 68)
        fill = (
            min(255, (base[0] + tint[0]) // 2),
            min(255, (base[1] + tint[1]) // 2),
            min(255, (base[2] + tint[2]) // 2),
            110,
        )
        rim = (220, 68, 68, 200)
        pygame.draw.circle(ghost, fill, center, r)
        pygame.draw.circle(ghost, rim, center, r, width=3)
        # Draw an X in the center
        arm = int(r * 0.75)
        x1 = center[0] - arm
        y1 = center[1] - arm
        x2 = center[0] + arm
        y2 = center[1] + arm
        x3 = center[0] - arm
        y3 = center[1] + arm
        x4 = center[0] + arm
        y4 = center[1] - arm
        line_color = (245, 80, 80, 230)
        pygame.draw.line(ghost, line_color, (x1, y1), (x2, y2), width=4)
        pygame.draw.line(ghost, line_color, (x3, y3), (x4, y4), width=4)
        self.screen.blit(ghost, (cx - d // 2, cy - d // 2))

    def _draw_cursor_preview(self, player_id: int) -> None:
        """Draw a ghost marble at the mouse cursor to indicate the next piece color."""
        mx, my = pygame.mouse.get_pos()
        color = self.cfg.player1_color if player_id == 1 else self.cfg.player2_color
        r = self.pick_radius
        d = r * 2 + 8
        ghost = pygame.Surface((d, d), pygame.SRCALPHA)
        center = (d // 2, d // 2)
        fill = (*color, 110)
        rim = (20, 20, 26, 160)
        pygame.draw.circle(ghost, fill, center, r)
        pygame.draw.circle(ghost, rim, center, r, width=2)
        self.screen.blit(ghost, (mx - d // 2, my - d // 2))

    def _draw_status(self, state: GameState, info: str = "") -> None:
        """Draw bottom HUD with dynamic phase hint and help keys."""
        # Compose full HUD string
        base_text = state.status_text()
        # Phase hint
        phase_hint = ""
        if getattr(state, "phase", None) is not None and getattr(state.phase, "name", "") == "PLAYING":
            sub = getattr(state, "subphase", None)
            if sub is not None and sub.name == "REMOVAL":
                phase_hint = "  |  Fase: Retirada — Retira 1–2 y pulsa [Space] o botón End"
            else:
                phase_hint = "  |  Fase: Acción — Coloca [LMB] o escala arrastrando [RMB]"
        elif getattr(state, "phase", None) is not None and getattr(state.phase, "name", "") == "ENDED":
            phase_hint = "  |  Fin de partida — Clic en el banner para volver al menú"

        help_text = "  |  [LMB] Place  [RMB] Select/Drag  [H] Huecos  [I] Índices  [Space] End Removal  [R] Menú  [1-5] Depth  [Esc] Quit"
        full_text = base_text + phase_hint + help_text
        if info:
            full_text += f"  |  {info}"

        # Dynamic word-wrap to avoid overflow. Compute lines within available width.
        padding_x = 24
        padding_y = 16
        max_width = self.cfg.width - 2 * padding_x
        lines = self._wrap_text(full_text, max_width)
        # Compute required panel height
        line_height = self.font.get_height()
        needed_h = padding_y * 2 + line_height * len(lines)
        y0 = self.cfg.height - needed_h
        panel_rect = pygame.Rect(0, y0, self.cfg.width, needed_h)
        pygame.draw.rect(self.screen, (16, 16, 22), panel_rect)
        # Draw lines
        y = y0 + padding_y
        for ln in lines:
            surf = self.font.render(ln, True, self.cfg.text_color)
            self.screen.blit(surf, (padding_x, y))
            y += line_height

    def _draw_win_banner(self, state: GameState) -> None:
        """If the game ended, draw a centered banner above the board with the result."""
        if getattr(state, "phase", None) is None:
            return
        if getattr(state.phase, "name", "") != "ENDED":
            return
        # Board center and top
        x0, y0 = self._compute_layer_top_left(Board.LAYER_SIZES[0])
        board_w = Board.LAYER_SIZES[0] * self.cfg.cell_size + (Board.LAYER_SIZES[0] - 1) * self.cfg.gap
        cx = x0 + board_w // 2
        # Compose message with reason
        if state.winner is None:
            msg = "Empate"
        else:
            loser = 2 if state.winner == 1 else 1
            reason = ""
            if state.board.is_apex_filled():
                reason = "por cima"
            else:
                # If loser has zero reserve, show that cause; else assume bloqueo
                if state.reserve_remaining(loser) == 0:
                    reason = "por reserva vacía del rival"
                else:
                    reason = "por bloqueo"
            msg = f"¡Gana Jugador {state.winner}! — {reason}"
        text = self.font_big.render(msg, True, (250, 250, 255))
        # Hover label to restart
        hover_label = "Comenzar de nuevo"
        hover_surf = self.font.render(hover_label, True, (255, 235, 59))
        pad_x, pad_y = 18, 10
        gap_y = 6
        w = max(text.get_width(), hover_surf.get_width()) + 2 * pad_x
        h = text.get_height() + 2 * pad_y
        bx = cx - w // 2
        by = max(8, y0 - h - 14)
        rect = pygame.Rect(bx, by, w, h)
        # Background with slight transparency
        banner = pygame.Surface((w, h), pygame.SRCALPHA)
        banner.fill((16, 16, 22, 200))
        self.screen.blit(banner, (bx, by))
        pygame.draw.rect(self.screen, (20, 20, 26), rect, width=2, border_radius=8)
        self.screen.blit(text, (rect.centerx - text.get_width() // 2, rect.centery - text.get_height() // 2))
        # If mouse hovers banner, show restart hint and expand highlight area
        mouse_pos = pygame.mouse.get_pos()
        final_rect = rect
        if rect.collidepoint(mouse_pos):
            # Extend banner to fit hover text
            extra_h = gap_y + hover_surf.get_height()
            final_rect = pygame.Rect(bx, by, w, h + extra_h)
            ext = pygame.Surface((w, h + extra_h), pygame.SRCALPHA)
            ext.fill((16, 16, 22, 200))
            self.screen.blit(ext, (bx, by))
            pygame.draw.rect(self.screen, (20, 20, 26), final_rect, width=2, border_radius=8)
            # Re-blit title centered in the top area
            self.screen.blit(text, (final_rect.centerx - text.get_width() // 2, by + pad_y))
            # Draw hover label near bottom
            hy = by + pad_y + text.get_height() + gap_y
            self.screen.blit(hover_surf, (final_rect.centerx - hover_surf.get_width() // 2, hy))
        # Store for controller click handling
        self._last_win_banner_rect = final_rect

    def get_win_banner_rect(self) -> Optional[pygame.Rect]:
        return self._last_win_banner_rect

    def _draw_side_counters(self, state: GameState, is_ai_p2: bool) -> None:
        counts = state.board.count_player_marbles()
        p1_used = counts.get(1, 0)
        p2_used = counts.get(2, 0)
        p1_res = max(0, 15 - p1_used)
        p2_res = max(0, 15 - p2_used)

        # Render text first to know sizes
        left_text = f"P1 used: {p1_used}  res: {p1_res}"
        r_label = "IA" if is_ai_p2 else "P2"
        right_text = f"{r_label} used: {p2_used}  res: {p2_res}"
        left_surf = self.font.render(left_text, True, self.cfg.text_color)
        right_surf = self.font.render(right_text, True, self.cfg.text_color)

        # Padding and positions
        pad_x, pad_y = 12, 10
        margin = 8
        # Icon (player color circle) dimensions
        icon_r = max(6, min(10, self.cfg.cell_size // 8))
        icon_d = icon_r * 2
        icon_gap = 8
        icon_area = icon_d + icon_gap

        left_w = icon_area + left_surf.get_width() + 2 * pad_x
        left_h = max(icon_d, left_surf.get_height()) + 2 * pad_y
        right_w = icon_area + right_surf.get_width() + 2 * pad_x
        right_h = max(icon_d, right_surf.get_height()) + 2 * pad_y

        # If in REMOVAL, add space for End button(s)
        removal_active = getattr(state, "subphase", None) is not None and state.subphase.name == "REMOVAL"
        btn_h = 26
        btn_margin_top = 6
        if removal_active:
            left_h += btn_margin_top + btn_h
            right_h += btn_margin_top + btn_h

        # Clamp widths if too large (rare). If clamped, fall back to right-aligned text within rect.
        max_panel_w = self.cfg.width // 2 - 2 * margin
        left_w = min(left_w, max_panel_w)
        right_w = min(right_w, max_panel_w)

        left_rect = pygame.Rect(margin, margin, left_w, left_h)
        right_rect = pygame.Rect(self.cfg.width - margin - right_w, margin, right_w, right_h)

        # Blink logic for current player's panel
        ticks = pygame.time.get_ticks()
        blink_on = ((ticks // 400) % 2) == 0  # ~1.25 Hz full cycle (toggle every 400ms)
        active_left = state.current_player == 1
        active_right = state.current_player == 2
        # Colors
        base_bg = (16, 16, 22)
        active_bg = (32, 32, 44)
        outline_normal = self.cfg.layer_outline
        outline_blink = self.cfg.highlight_color
        left_bg = active_bg if (active_left and blink_on) else base_bg
        right_bg = active_bg if (active_right and blink_on) else base_bg
        left_outline = outline_blink if (active_left and blink_on) else outline_normal
        right_outline = outline_blink if (active_right and blink_on) else outline_normal

        # Draw panel backgrounds and outlines (with blink)
        pygame.draw.rect(self.screen, left_bg, left_rect, border_radius=8)
        pygame.draw.rect(self.screen, right_bg, right_rect, border_radius=8)
        pygame.draw.rect(self.screen, left_outline, left_rect, width=2, border_radius=8)
        pygame.draw.rect(self.screen, right_outline, right_rect, width=2, border_radius=8)

        # Draw player color icons inside panels (left-aligned inside each panel)
        left_cx = left_rect.x + pad_x + icon_r
        left_cy = left_rect.y + left_rect.height // 2
        right_cx = right_rect.x + pad_x + icon_r
        right_cy = right_rect.y + right_rect.height // 2
        pygame.draw.circle(self.screen, self.cfg.player1_color, (left_cx, left_cy), icon_r)
        pygame.draw.circle(self.screen, (20, 20, 26), (left_cx, left_cy), icon_r, width=2)
        pygame.draw.circle(self.screen, self.cfg.player2_color, (right_cx, right_cy), icon_r)
        pygame.draw.circle(self.screen, (20, 20, 26), (right_cx, right_cy), icon_r, width=2)

        # Blit text with padding; if clamped, we ensure it stays inside by cutting from the left (for right panel) or right (for left panel)
        # Left text
        lx = left_rect.x + pad_x + icon_area
        ly = left_rect.y + pad_y
        if left_surf.get_width() > left_rect.width - 2 * pad_x - icon_area:
            # Clip the left end to fit; show the tail which contains numbers
            clip_w = left_rect.width - 2 * pad_x - icon_area
            clip_rect = pygame.Rect(left_surf.get_width() - clip_w, 0, clip_w, left_surf.get_height())
            self.screen.blit(left_surf, (lx, ly), area=clip_rect)
        else:
            self.screen.blit(left_surf, (lx, ly))

        # Right text (prefer right alignment inside)
        ry = right_rect.y + pad_y
        available_text_w = right_rect.width - 2 * pad_x - icon_area
        if right_surf.get_width() > available_text_w:
            clip_w = available_text_w
            clip_rect = pygame.Rect(right_surf.get_width() - clip_w, 0, clip_w, right_surf.get_height())
            rx = right_rect.x + right_rect.width - pad_x - clip_w
            self.screen.blit(right_surf, (rx, ry), area=clip_rect)
        else:
            rx = right_rect.x + right_rect.width - pad_x - right_surf.get_width()
            self.screen.blit(right_surf, (rx, ry))

        # End Removal buttons (draw on both sides during REMOVAL; availability per rules)
        self._last_end_btn_left = None
        self._last_end_btn_right = None
        if removal_active:
            # Button sizes
            btn_w_left = max(80, left_rect.width - 2 * pad_x)
            btn_w_right = max(80, right_rect.width - 2 * pad_x)
            bl_x = left_rect.x + (left_rect.width - btn_w_left) // 2
            bl_y = left_rect.bottom - pad_y - btn_h
            br_x = right_rect.x + (right_rect.width - btn_w_right) // 2
            br_y = right_rect.bottom - pad_y - btn_h
            btn_left = pygame.Rect(bl_x, bl_y, btn_w_left, btn_h)
            btn_right = pygame.Rect(br_x, br_y, btn_w_right, btn_h)
            self._last_end_btn_left = btn_left
            self._last_end_btn_right = btn_right

            active_left = state.current_player == 1
            active_right = state.current_player == 2
            # Colors
            bg_active = (58, 143, 66)  # green
            bg_disabled = (90, 90, 100)  # gray as requested
            outline = (20, 20, 26)
            txt_enabled = (250, 250, 255)
            txt_disabled = (180, 180, 190)

            # Availability per rules (only current player's button can be enabled)
            can_finish = getattr(state, "can_finish_removal", lambda: False)
            allow_left = active_left and can_finish()
            allow_right = active_right and can_finish()

            mouse_pos = pygame.mouse.get_pos()
            hover_left = allow_left and btn_left.collidepoint(mouse_pos)
            hover_right = allow_right and btn_right.collidepoint(mouse_pos)

            # Left button
            pygame.draw.rect(self.screen, bg_active if allow_left else bg_disabled, btn_left, border_radius=6)
            pygame.draw.rect(self.screen, outline, btn_left, width=2, border_radius=6)
            if hover_left:
                hl = pygame.Surface((btn_w_left, btn_h), pygame.SRCALPHA)
                hl.fill((255, 235, 59, 120))  # semi-transparent yellow
                self.screen.blit(hl, (bl_x, bl_y))
            lbl_left = self.font_small.render("End", True, txt_enabled if allow_left else txt_disabled)
            self.screen.blit(lbl_left, (btn_left.centerx - lbl_left.get_width() // 2, btn_left.centery - lbl_left.get_height() // 2))

            # Right button
            pygame.draw.rect(self.screen, bg_active if allow_right else bg_disabled, btn_right, border_radius=6)
            pygame.draw.rect(self.screen, outline, btn_right, width=2, border_radius=6)
            if hover_right:
                hr = pygame.Surface((btn_w_right, btn_h), pygame.SRCALPHA)
                hr.fill((255, 235, 59, 120))
                self.screen.blit(hr, (br_x, br_y))
            lbl_right = self.font_small.render("End", True, txt_enabled if allow_right else txt_disabled)
            self.screen.blit(lbl_right, (btn_right.centerx - lbl_right.get_width() // 2, btn_right.centery - lbl_right.get_height() // 2))

    def get_end_removal_button_rect(self, player_id: int) -> Optional[pygame.Rect]:
        """Return the last computed End Removal button rect for player (1 left, 2 right)."""
        if player_id == 1:
            return self._last_end_btn_left
        if player_id == 2:
            return self._last_end_btn_right
        return None

    def _wrap_text(self, text: str, max_width: int) -> List[str]:
        """Greedy word-wrap for a single paragraph to fit within max_width.

        Returns list of lines; does not hyphenate long words (rare in HUD text).
        """
        words = text.split(" ")
        lines: List[str] = []
        cur: List[str] = []
        for w in words:
            trial = (" ".join(cur + [w])).strip()
            tw = self.font.size(trial)[0]
            if tw <= max_width or not cur:
                cur.append(w)
            else:
                lines.append(" ".join(cur).strip())
                cur = [w]
        if cur:
            lines.append(" ".join(cur).strip())
        return lines
