from __future__ import annotations

from typing import Optional, Tuple

import pygame

from roguelike_ui.widgets.menu_renderer.menu_renderer import MenuRenderer
from roguelike_ui.services.formatting import format_key_label  # noqa: F401 (kept for external usage)
from .row_specs import build_row_specs
from .layout_fixed import compute_fixed_layout
from .modals import prompt_key, prompt_mouse, flash_message


class MenuConfigurator:
    """UI to rebind key/mouse inputs reusing MenuRenderer aesthetics.

    Navigation: Up/Down (W/S), Left/Right (A/D), Enter to edit, ESC to exit.
    Tabs: General, Movements, Spells, Editors.
    """

    def __init__(
        self,
        input_config,
        screen: pygame.Surface,
        font: pygame.font.Font,
        underlay_provider=None,
        base_font_size: Optional[int] | None = None,
    ) -> None:
        self.config = input_config
        self.screen = screen
        self.font = font
        self.underlay_provider = underlay_provider

        # Standardize font size through MenuRenderer
        if isinstance(base_font_size, int) and base_font_size > 6:
            self.renderer = MenuRenderer(font_size=base_font_size)
        else:
            try:
                font_size = int(self.font.get_height()) if font else 18
            except Exception:
                font_size = 18
            self.renderer = MenuRenderer(font_size=font_size)

        # Tabs (visible label, internal key)
        self.tabs: list[tuple[str, str]] = [
            ("General", "general"),
            ("Movimientos", "movements"),
            ("Hechizos", "spells"),
            ("Editores", "editors"),
        ]
        self.active_tab_index: int = 0

        # Fixed layout cached values
        self._fixed_col_widths: Optional[list[int]] = None
        self._fixed_panel_size: Optional[Tuple[int, int]] = None
        self._fixed_screen_size: Optional[Tuple[int, int]] = None

    # -------- Public API --------
    def configure(self) -> None:
        """Load config if supported and enter the UI loop until ESC."""
        if hasattr(self.config, "load"):
            self.config.load()
        elif hasattr(self.config, "_load"):
            self.config._load()
        self._show_menu()

    # -------- Internal helpers --------
    def _show_menu(self) -> None:
        selected_row = 0
        selected_col = 1  # default to Keyboard A
        row_scroll_offset = 0
        hovered_row: Optional[int] = None
        hovered_col: Optional[int] = None
        running = True
        clock = pygame.time.Clock()

        # Professional key repeat: initial delay + repeat interval (ms)
        repeat_cfg = {"initial": 260, "interval": 70}
        hold = {
            "up": {"keys": (pygame.K_UP, pygame.K_w), "held": False, "next": 0},
            "down": {"keys": (pygame.K_DOWN, pygame.K_s), "held": False, "next": 0},
            "left": {"keys": (pygame.K_LEFT, pygame.K_a), "held": False, "next": 0},
            "right": {"keys": (pygame.K_RIGHT, pygame.K_d), "held": False, "next": 0},
        }

        # Static headers
        headers = ["Acción", "Keyboard A", "Keyboard B", "Mouse"]

        # Precompute fixed layout across all tabs
        self._recompute_fixed_layout(headers)

        while running:
            # Recompute on screen resize
            if self._fixed_screen_size != self.screen.get_size():
                self._recompute_fixed_layout(headers)

            # Build current tab rows/specs
            _, tab_key = self.tabs[self.active_tab_index]
            row_specs, rows = build_row_specs(self.config.bindings, category=tab_key)
            total_rows = len(rows)

            # Clamp selected row if necessary
            if total_rows and selected_row >= total_rows:
                selected_row = max(0, total_rows - 1)

            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    running = False
                    break
                if event.type == pygame.KEYDOWN:
                    if event.key in (pygame.K_UP, pygame.K_w):
                        selected_row = (selected_row - 1) % max(1, total_rows)
                        hold["up"]["held"], hold["up"]["next"] = True, repeat_cfg["initial"]
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        selected_row = (selected_row + 1) % max(1, total_rows)
                        hold["down"]["held"], hold["down"]["next"] = True, repeat_cfg["initial"]
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        selected_col = max(0, selected_col - 1)
                        hold["left"]["held"], hold["left"]["next"] = True, repeat_cfg["initial"]
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        selected_col = min(3, selected_col + 1)
                        hold["right"]["held"], hold["right"]["next"] = True, repeat_cfg["initial"]
                    elif event.key in (pygame.K_q, pygame.K_PAGEUP):
                        self.active_tab_index = (self.active_tab_index - 1) % len(self.tabs)
                        selected_row, selected_col, row_scroll_offset = 0, 1, 0
                    elif event.key in (pygame.K_e, pygame.K_PAGEDOWN):
                        self.active_tab_index = (self.active_tab_index + 1) % len(self.tabs)
                        selected_row, selected_col, row_scroll_offset = 0, 1, 0
                    elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                        if not row_specs:
                            continue
                        spec = row_specs[selected_row]
                        if spec["kind"] == "tri":
                            if selected_col == 1:
                                prompt_key(self.renderer, self.screen, self.config, spec["kb_a_key"], slot="keyboard_a")
                            elif selected_col == 2:
                                prompt_key(self.renderer, self.screen, self.config, spec["kb_b_key"], slot="keyboard_b")
                            elif selected_col == 3:
                                prompt_mouse(self.renderer, self.screen, self.config, spec["mouse_key"]) 
                        else:
                            if selected_col == 1:
                                prompt_key(self.renderer, self.screen, self.config, spec["action_key"], slot="keyboard_a")
                            elif selected_col == 3 and isinstance(self.config.bindings.get(spec["action_key"]), str) and self.config.bindings.get(spec["action_key"], "").startswith("M_"):
                                prompt_mouse(self.renderer, self.screen, self.config, spec["action_key"]) 
                            else:
                                flash_message(self.renderer, self.screen, [
                                    "Esa celda no es editable",
                                    "Usa Keyboard A o Mouse donde aplique",
                                ], ms=750)
                    elif event.key == pygame.K_ESCAPE:
                        running = False
                        break
                elif event.type == pygame.KEYUP:
                    if event.key in (pygame.K_UP, pygame.K_w):
                        hold["up"]["held"], hold["up"]["next"] = False, 0
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        hold["down"]["held"], hold["down"]["next"] = False, 0
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        hold["left"]["held"], hold["left"]["next"] = False, 0
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        hold["right"]["held"], hold["right"]["next"] = False, 0
                elif event.type == pygame.MOUSEMOTION:
                    hovered_row, hovered_col = self._hit_test_cell(event.pos)
                elif event.type == pygame.MOUSEWHEEL:
                    row_scroll_offset = max(0, row_scroll_offset - event.y)
                elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    # tabs click first
                    layout = getattr(self.renderer, "last_table_layout", None)
                    if layout:
                        tab_rects = layout.get("tab_rects", [])
                        for i, rect in enumerate(tab_rects):
                            if rect.collidepoint(event.pos):
                                if i != self.active_tab_index:
                                    self.active_tab_index = i
                                    selected_row, selected_col, row_scroll_offset = 0, 1, 0
                                break
                        else:
                            pass
                        if any(rect.collidepoint(event.pos) for rect in tab_rects):
                            continue
                    # select cell
                    hr, hc = self._hit_test_cell(event.pos)
                    if hr is not None and hc is not None:
                        selected_row, selected_col = hr, hc
                        if row_specs:
                            spec = row_specs[selected_row]
                            if spec["kind"] == "tri":
                                if selected_col == 1:
                                    prompt_key(self.renderer, self.screen, self.config, spec["kb_a_key"], slot="keyboard_a")
                                elif selected_col == 2:
                                    prompt_key(self.renderer, self.screen, self.config, spec["kb_b_key"], slot="keyboard_b")
                                elif selected_col == 3:
                                    prompt_mouse(self.renderer, self.screen, self.config, spec["mouse_key"]) 
                            else:
                                if selected_col == 1:
                                    prompt_key(self.renderer, self.screen, self.config, spec["action_key"], slot="keyboard_a")
                                elif selected_col == 3 and isinstance(self.config.bindings.get(spec["action_key"]), str) and self.config.bindings.get(spec["action_key"], "").startswith("M_"):
                                    prompt_mouse(self.renderer, self.screen, self.config, spec["action_key"]) 

            # Key repeat update
            dt = clock.get_time()  # ms
            pressed = pygame.key.get_pressed()
            for st in hold.values():
                st["held"] = any(pressed[k] for k in st["keys"])

            def _repeat_step(name: str) -> None:
                nonlocal selected_row, selected_col
                if name == "up":
                    selected_row = (selected_row - 1) % max(1, total_rows)
                elif name == "down":
                    selected_row = (selected_row + 1) % max(1, total_rows)
                elif name == "left":
                    selected_col = max(0, selected_col - 1)
                elif name == "right":
                    selected_col = min(3, selected_col + 1)

            for name, st in hold.items():
                if not st["held"]:
                    st["next"] = 0
                    continue
                if st["next"] <= 0:
                    _repeat_step(name)
                    st["next"] = repeat_cfg["interval"]
                else:
                    st["next"] -= dt

            # Visible window clamp (fixed layout)
            if total_rows:
                header_h = self.renderer.line_height
                tabs_h = self.renderer.line_height
                panel_h = (
                    self._fixed_panel_size[1]
                    if self._fixed_panel_size
                    else int(self.screen.get_size()[1] * 0.85)
                )
                inner_height = panel_h - (
                    self.renderer.padding_y * 2
                    + tabs_h
                    + (self.renderer.item_gap // 2)
                    + header_h
                    + self.renderer.item_gap
                )
                block_h = self.renderer.line_height + self.renderer.item_gap
                max_visible = max(1, (inner_height + self.renderer.item_gap) // block_h)
                if selected_row < row_scroll_offset:
                    row_scroll_offset = selected_row
                elif selected_row >= row_scroll_offset + max_visible:
                    row_scroll_offset = selected_row - max_visible + 1
                max_offset = max(0, total_rows - max_visible)
                row_scroll_offset = max(0, min(row_scroll_offset, max_offset))

            # Underlay: keep background/logo if provided
            panel_top_min = self._compute_panel_top_min()

            # Draw
            self.renderer.draw_table_with_tabs(
                self.screen,
                tabs=[lbl for (lbl, _key) in self.tabs],
                active_tab_index=self.active_tab_index,
                headers=headers,
                rows=rows,
                selected_row=selected_row,
                selected_col=selected_col,
                row_scroll_offset=row_scroll_offset,
                hovered_row=hovered_row,
                hovered_col=hovered_col,
                fixed_size=self._fixed_panel_size,
                fixed_col_widths=self._fixed_col_widths,
                panel_top_min=panel_top_min,
            )
            pygame.display.flip()
            clock.tick(60)

    def _recompute_fixed_layout(self, headers: list[str]) -> None:
        def _rows_for_tab(key: str) -> list[list[str]]:
            _specs, _rows = build_row_specs(self.config.bindings, category=key)
            return _rows

        fixed_screen_size, fixed_col_widths, fixed_panel_size = compute_fixed_layout(
            renderer=self.renderer,
            screen=self.screen,
            tabs=self.tabs,
            headers=headers,
            build_rows_for_tab=_rows_for_tab,
        )
        self._fixed_screen_size = fixed_screen_size
        self._fixed_col_widths = fixed_col_widths
        self._fixed_panel_size = fixed_panel_size

    def _hit_test_cell(self, pos: tuple[int, int]) -> tuple[Optional[int], Optional[int]]:
        layout = getattr(self.renderer, "last_table_layout", None)
        if not layout:
            return (None, None)
        cell_rects = layout.get("cell_rects", {})
        for (r, c), rect in cell_rects.items():
            if rect.collidepoint(pos):
                return (r, c)
        return (None, None)

    def _compute_panel_top_min(self) -> Optional[int]:
        panel_top_min = None
        if callable(self.underlay_provider):
            try:
                panel_top_min = self.underlay_provider(self.screen)
            except Exception:
                panel_top_min = None
        try:
            sh = self.screen.get_size()[1]
        except Exception:
            sh = 720
        if isinstance(panel_top_min, int):
            extra = max(24, int(self.renderer.line_height))
            return panel_top_min + extra
        return None
