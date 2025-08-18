import pygame
import importlib
import roguelike_game.config.players_config as players_config
from typing import Dict, Optional, List, Any


class ClassSelectorManager:
    """
    Displays a class selection menu toggled by F2.
    UI: grid with 5 columns; each cell shows class name and key stats.
    """
    def __init__(self, state, input_config, screen, font_size=36):
        self.state = state
        self.input_config = input_config
        self.screen = screen
        # Options from configuration keys (refreshed from JSON)
        self.options = []
        self.stats_map: Dict[str, Dict[str, Any]] = {}
        self.refresh_options()
        self.selected = 0
        self.show = False
        self.font = pygame.font.SysFont("Arial", font_size)
        self.small_font = pygame.font.SysFont("Arial", max(14, font_size - 16))
        self.padding = 10
        # Grid layout config
        self.columns = 5
        self.cell_h_margin = 16
        self.cell_v_margin = 16
        self.panel_margin = 40
        # Header image config
        self.header_height = 180  # fallback when image not available
        self.header_margin = 20
        self._header_cache: Dict[str, pygame.Surface] = {}
        # Desired left-to-right order for classes
        self.desired_order: List[str] = [
            "barbarian", "elven", "mague", "valkyrie", "dwarf"
        ]
        # Specific header image overrides per class (absolute paths provided by user)
        self.header_override: Dict[str, str] = {
            "elven": r"C:\\Project\\RogueLike\\assets\\ui\\character_selection\\character_selection_elve.png",
            "valkyrie": r"C:\\Project\\RogueLike\\assets\\ui\\character_selection\\character_selection_valkyrie.png",
            "mague": r"C:\\Project\\RogueLike\\assets\\ui\\character_selection\\character_selection_mague.png",
            "dwarf": r"C:\\Project\\RogueLike\\assets\\ui\\character_selection\\character_selection_drwaft.png",
            "barbarian": r"C:\\Project\\RogueLike\\assets\\ui\\character_selection\\character_selection_barbrian.png",
        }

    def refresh_options(self):
        """Reload players_config and refresh available class options."""
        try:
            importlib.reload(players_config)
            opts = list(players_config.PLAYER_ASSETS.keys())
            # Apply desired order: include only those present, then append any extras
            ordered = [c for c in self.desired_order if c in opts]
            extras = [c for c in opts if c not in self.desired_order]
            self.options = ordered + extras
            self.stats_map = dict(players_config.PLAYER_STATS)
            # Clamp selected index
            if self.options:
                self.selected %= len(self.options)
            else:
                self.selected = 0
        except Exception:
            # In case of transient JSON edits, keep previous options
            pass

    def handle_input(self, event):
        # Handle mouse click on class options
        if event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = event.pos
            # Use same header reservation as in draw()
            grid_rect, cell_w, cell_h = self._calc_grid_geometry(reserve_top=self._reserved_top())
            if grid_rect and grid_rect.collidepoint(mx, my) and self.options:
                rel_x = mx - grid_rect.x
                rel_y = my - grid_rect.y
                col = int(rel_x // (cell_w + self.cell_h_margin))
                row = int(rel_y // (cell_h + self.cell_v_margin))
                # Compute exact cell rect and ensure click is inside cell (not margin)
                cx = col * (cell_w + self.cell_h_margin)
                cy = row * (cell_h + self.cell_v_margin)
                cell_rect = pygame.Rect(grid_rect.x + cx, grid_rect.y + cy, cell_w, cell_h)
                idx = row * self.columns + col
                if 0 <= idx < len(self.options) and cell_rect.collidepoint(mx, my):
                    chosen = self.options[idx]
                    self.state.current_player_class = chosen
                    self.show = False
                    return chosen

        if event.type == pygame.KEYDOWN:
            key = event.key
            up_key = self.input_config.get_key("move_up")
            down_key = self.input_config.get_key("move_down")
            left_key = self.input_config.get_key("move_left")
            right_key = self.input_config.get_key("move_right")
            select_key = self.input_config.get_key("select_class")
            if key == pygame.K_ESCAPE:
                self.show = False
                return None
            # Allow toggling/closing with F2 (select_class)
            if key == select_key:
                self.show = False
                return None
            if key == up_key or key == pygame.K_UP:
                if self.options:
                    self.selected = (self.selected - self.columns) % len(self.options)
                return None
            elif key == down_key or key == pygame.K_DOWN:
                if self.options:
                    self.selected = (self.selected + self.columns) % len(self.options)
                return None
            elif key == left_key or key == pygame.K_LEFT:
                if self.options:
                    self.selected = (self.selected - 1) % len(self.options)
                return None
            elif key == right_key or key == pygame.K_RIGHT:
                if self.options:
                    self.selected = (self.selected + 1) % len(self.options)
                return None
            elif key == pygame.K_RETURN:
                if self.options:
                    chosen = self.options[self.selected]
                    self.state.current_player_class = chosen
                    self.show = False
                    return chosen
        return None

    def draw(self):
        # Ensure options reflect latest JSON when the selector is visible
        self.refresh_options()
        # Semi-transparent full-screen overlay
        overlay = pygame.Surface(self.screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 128))

        # Draw header image for the currently selected class
        header_rect = self._calc_header_rect()
        if self.options and header_rect.height > 0:
            cls_name = self.options[self.selected]
            img = self._get_header_image(cls_name)
            if img is not None:
                # Show full image (contain): scale to fit within header rect without cropping
                iw, ih = img.get_size()
                if iw > 0 and ih > 0:
                    scale = min(header_rect.width / iw, header_rect.height / ih)
                    tw = max(1, int(iw * scale))
                    th = max(1, int(ih * scale))
                    scaled = pygame.transform.smoothscale(img, (tw, th))
                    # Shadow + background panel
                    shadow = pygame.Surface((header_rect.width, header_rect.height), pygame.SRCALPHA)
                    shadow.fill((0, 0, 0, 90))
                    overlay.blit(shadow, (header_rect.x, header_rect.y + 6))
                    bg = pygame.Surface((header_rect.width, header_rect.height), pygame.SRCALPHA)
                    bg.fill((28, 28, 28, 220))
                    overlay.blit(bg, header_rect.topleft)
                    # Center the scaled image (letterbox if necessary)
                    hx = header_rect.x + (header_rect.width - tw) // 2
                    hy = header_rect.y + (header_rect.height - th) // 2
                    overlay.blit(scaled, (hx, hy))
            else:
                # Fallback text when no image available
                fallback = self.font.render(cls_name, True, (255, 255, 255))
                overlay.blit(fallback, (header_rect.x, header_rect.y))

        grid_rect, cell_w, cell_h = self._calc_grid_geometry(reserve_top=self._reserved_top())
        if not grid_rect:
            self.screen.blit(overlay, (0, 0))
            return

        # Panel background with drop shadow
        panel_shadow = pygame.Surface(grid_rect.size, pygame.SRCALPHA)
        panel_shadow.fill((0, 0, 0, 100))
        overlay.blit(panel_shadow, (grid_rect.x, grid_rect.y + 6))
        panel = pygame.Surface(grid_rect.size, pygame.SRCALPHA)
        panel.fill((44, 44, 44, 235))

        # Draw grid cells with names and stats
        for idx, cls_name in enumerate(self.options):
            row = idx // self.columns
            col = idx % self.columns
            cx = col * (cell_w + self.cell_h_margin)
            cy = row * (cell_h + self.cell_v_margin)
            cell_rect = pygame.Rect(cx, cy, cell_w, cell_h)

            # Cell background
            pygame.draw.rect(panel, (62, 62, 62), cell_rect, border_radius=10)
            # Border highlight for selection
            if idx == self.selected:
                pygame.draw.rect(panel, (20, 20, 20), cell_rect.inflate(6, 6), width=2, border_radius=12)
                pygame.draw.rect(panel, (255, 220, 90), cell_rect, width=4, border_radius=10)
            else:
                pygame.draw.rect(panel, (95, 95, 95), cell_rect, width=2, border_radius=10)

            # Title (class name)
            title_surf = self.font.render(cls_name, True, (240, 240, 240))
            ttx = cell_rect.x + self.padding
            tty = cell_rect.y + self.padding
            panel.blit(title_surf, (ttx, tty))

            # Stats block
            stats = self.stats_map.get(cls_name, {}) or {}
            lines = self._format_stats_lines(stats)
            sy = tty + title_surf.get_height() + 6
            for line in lines:
                stat_surf = self.small_font.render(line, True, (200, 200, 200))
                panel.blit(stat_surf, (ttx, sy))
                sy += stat_surf.get_height() + 2

        # Blit the populated panel (shadow already drawn above)
        overlay.blit(panel, grid_rect.topleft)
        self.screen.blit(overlay, (0, 0))

    def _calc_grid_geometry(self, reserve_top: int = 0):
        """Compute grid rect and cell size; returns (rect, cell_w, cell_h) or (None,0,0).
        reserve_top: vertical space in pixels to leave at the top (for header).
        """
        sw, sh = self.screen.get_size()
        if not self.options:
            # Just draw dimmed overlay
            return pygame.Rect(self.panel_margin, self.panel_margin + reserve_top, sw - 2 * self.panel_margin, sh - 2 * self.panel_margin - reserve_top), 0, 0
        cols = max(1, self.columns)
        rows = (len(self.options) + cols - 1) // cols
        # Target panel width nearly full width, with margins
        avail_w = sw - 2 * self.panel_margin
        avail_h = sh - 2 * self.panel_margin - reserve_top
        # Cell sizes with spacing
        total_hgap = self.cell_h_margin * (cols - 1)
        cell_w = max(120, (avail_w - total_hgap) // cols)
        # Estimate cell height: title + ~6 stat lines
        est_title = self.font.get_height()
        est_line = self.small_font.get_height()
        est_stats_h = est_line * 6 + 10
        cell_h = max(est_title + est_stats_h + 2 * self.padding, 120)
        total_vgap = self.cell_v_margin * (rows - 1)
        panel_w = cols * cell_w + total_hgap
        panel_h = rows * cell_h + total_vgap
        # Position panel: centered horizontally, bottom-aligned within available area
        x = (sw - panel_w) // 2
        y_space_top = self.panel_margin + reserve_top
        y_space_bottom = sh - self.panel_margin
        y = min(max(y_space_top, y_space_bottom - panel_h), y_space_bottom - panel_h)
        return pygame.Rect(x, y, panel_w, panel_h), cell_w, cell_h

    def _format_stats_lines(self, stats: dict) -> List[str]:
        """Return compact stat lines for rendering under the class name."""
        if not stats:
            return ["(no stats)"]
        lines: List[str] = []
        # Common keys expected in new_players.json
        hp = stats.get("max_strength")
        atk = stats.get("basic_attack")
        arm = stats.get("basic_armor")
        spd = stats.get("basic_speed")
        mana = stats.get("max_intelligence")
        energy = stats.get("max_dexterity")
        # Optional extras
        hunger = stats.get("max_hunger")
        dmg_dur = stats.get("damage_duration")
        atk_dur = stats.get("attack_duration")
        if hp is not None: lines.append(f"HP: {hp}")
        if atk is not None: lines.append(f"ATK: {atk}")
        if arm is not None: lines.append(f"ARM: {arm}")
        if spd is not None: lines.append(f"SPD: {spd}")
        if mana is not None: lines.append(f"MANA: {mana}")
        if energy is not None: lines.append(f"ENG: {energy}")
        if hunger is not None: lines.append(f"HUN: {hunger}")
        if dmg_dur is not None: lines.append(f"DMG t: {dmg_dur}")
        if atk_dur is not None: lines.append(f"ATK t: {atk_dur}")
        return lines[:8]

    # ---------- Header helpers ----------
    def _reserved_top(self) -> int:
        """Total vertical space reserved for header + margins (dynamic based on image)."""
        if not self.options:
            return 0
        rect = self._calc_header_rect()
        return rect.height + self.header_margin

    def _calc_header_rect(self) -> pygame.Rect:
        sw, sh = self.screen.get_size()
        if not self.options:
            return pygame.Rect(0, 0, 0, 0)
        # Full screen width header with top margin
        x = 0
        y = self.panel_margin
        width = sw
        # Compute desired image height from aspect ratio and ensure the grid fits below
        height = self.header_height
        try:
            cls_name = self.options[self.selected]
            img = self._get_header_image(cls_name)
            iw, ih = img.get_size() if img is not None else (0, 0)
            desired_h = int(width * (ih / iw)) if iw > 0 and ih > 0 else self.header_height
            # Estimate grid height to preserve space below
            cols = max(1, self.columns)
            rows = (len(self.options) + cols - 1) // cols if self.options else 1
            est_title = self.font.get_height()
            est_line = self.small_font.get_height()
            est_stats_h = est_line * 6 + 10
            cell_h = max(est_title + est_stats_h + 2 * self.padding, 120)
            total_vgap = self.cell_v_margin * (rows - 1)
            panel_h = rows * cell_h + total_vgap
            reserved_bottom = self.panel_margin
            avail_h_for_header = max(100, sh - (y + self.header_margin + panel_h + reserved_bottom))
            # Use as much as possible while ensuring grid fits; if room allows, use full-width AR height
            height = max(100, min(desired_h, avail_h_for_header))
        except Exception:
            pass
        return pygame.Rect(x, y, width, height)

    def _get_header_image(self, cls_name: str) -> Optional[pygame.Surface]:
        # Cache
        if cls_name in self._header_cache:
            return self._header_cache.get(cls_name)
        # Try to obtain an idle sprite path from PLAYER_ASSETS
        try:
            # 1) Try user-provided override mapping first
            override_path = self.header_override.get(cls_name)
            if override_path:
                img = pygame.image.load(override_path).convert_alpha()
                self._header_cache[cls_name] = img
                return img

            # 2) Fallback: use idle sprite from assets config
            assets = players_config.PLAYER_ASSETS.get(cls_name, {}) or {}
            img_path: Optional[str] = None
            # Prefer sets -> sprites_set -> idle
            sets = assets.get("sets") or assets.get("Sets")
            if isinstance(sets, dict):
                sprites_set = sets.get("sprites_set") or {}
                idle_list = sprites_set.get("idle") or []
                if isinstance(idle_list, list) and idle_list:
                    img_path = idle_list[0]
            # Fallback: assets may place idle under other forms; not handled extensively
            if img_path is None:
                return None
            # Resolve to absolute path
            full_path = self._resolve_asset_path(img_path)
            if full_path is None:
                return None
            img = pygame.image.load(str(full_path)).convert_alpha()
            self._header_cache[cls_name] = img
            return img
        except Exception:
            return None

    def _resolve_asset_path(self, p: str):
        from pathlib import Path
        root = players_config.top
        pp = p.replace("\\", "/")
        cand: Path
        # If already absolute
        try:
            cand = Path(pp)
            if cand.is_absolute() and cand.exists():
                return cand
        except Exception:
            pass
        # Paths starting with assets/
        cand = root / pp
        if cand.exists():
            return cand
        # Paths like characters/... should live under assets/characters
        cand = root / "assets" / pp
        if cand.exists():
            return cand
        return None

