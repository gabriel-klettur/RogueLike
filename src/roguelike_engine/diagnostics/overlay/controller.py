import time
import re
import pygame
from typing import Dict, List, Optional, Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS

from .model import DiagnosticsOverlayModel
from .view import DiagnosticsOverlayView


class DiagnosticsOverlayController:
    def __init__(self, model: DiagnosticsOverlayModel, view: DiagnosticsOverlayView):
        self.model = model
        self.view = view

    def _parse_numeric_id(self, key: str) -> Tuple[Optional[str], str]:
        """
        Returns (id_str, rest_label).
        id_str is like '1', '1.2', '1.2.3' if a numeric dotted prefix exists at the beginning of key,
        otherwise None. rest_label is the remaining label after stripping the numeric prefix and extra spaces/dot.
        """
        m = re.match(r"^\s*(\d+(?:\.\d+)*)(?:\.)?\s*(.*)$", key)
        if m:
            id_str = m.group(1)
            rest = m.group(2) or ""
            return id_str, rest
        # No numeric prefix; keep full key as label
        return None, key

    def _build_perf_tree(self, perf_log: Dict[str, List[float]]):
        """
        Build a hierarchical tree based on numeric id prefixes.
        Node structure: { 'id': str | None, 'children': dict[str, node], 'items': list[(label, avg_ms)] }
        Root node has id=None.
        """
        root = {"id": None, "children": {}, "items": [], "title": ""}
        for key, samples in perf_log.items():
            recent = samples[-60:]
            if not recent:
                continue
            avg_ms = sum(recent) / len(recent) * 1000
            id_str, rest_label = self._parse_numeric_id(key)
            if id_str:
                parts = id_str.split('.')
                node = root
                for i in range(1, len(parts) + 1):
                    sub_id = '.'.join(parts[:i])
                    if sub_id not in node["children"]:
                        node["children"][sub_id] = {"id": sub_id, "children": {}, "items": [], "title": ""}
                    node = node["children"][sub_id]
                # Attach item to the most specific id node; store (id,label,value)
                label = rest_label if rest_label else key
                node["items"].append((id_str, label, avg_ms))
                # If this item names the node (exact id), record a title (prefer shortest label)
                if rest_label:
                    if not node["title"] or len(rest_label) < len(node["title"]):
                        node["title"] = rest_label
            else:
                # No numeric id: group by first token; fall back to 'Other'
                group = key.split('.')[0].strip() or 'Other'
                node = root["children"].setdefault(group, {"id": group, "children": {}, "items": [], "title": ""})
                # No numeric id; store with id=None
                node["items"].append((None, key, avg_ms))

        # compute totals and counts recursively
        def compute(node):
            total = sum(v for _, _, v in node["items"])
            count = len(node["items"])
            for child in node["children"].values():
                c_total, c_count = compute(child)
                total += c_total
                count += c_count
            node["total"] = total
            node["count"] = count
            return total, count

        compute(root)
        return root

    def _collect_group_ids(self, node) -> List[str]:
        ids = []
        for gid, child in node["children"].items():
            ids.append(gid)
            ids.extend(self._collect_group_ids(child))
        return ids

    def _numeric_sort_key(self, gid: str):
        # If gid is numeric dotted id, sort numerically by components; else sort after numeric groups
        if re.match(r"^(\d+(?:\.\d+)*)$", gid):
            return (0, [int(p) for p in gid.split('.')])
        return (1, [gid])

    def _is_numeric_id(self, gid: Optional[str]) -> bool:
        return bool(gid and re.match(r"^(\d+(?:\.\d+)*)$", gid))

    def _find_sole_item(self, node) -> Optional[Tuple[str, str, float]]:
        """
        If subtree has exactly one item, return (deepest_gid, label, avg_ms).
        Otherwise, None.
        """
        if node.get('count', 0) != 1:
            return None
        # If item is directly here
        if len(node.get('items', [])) == 1 and all(c.get('count', 0) == 0 for c in node.get('children', {}).values()):
            item_id, label, val = node['items'][0]
            gid = item_id if self._is_numeric_id(item_id) else (node.get('id') if self._is_numeric_id(node.get('id')) else '')
            return gid or '', label, val
        # Otherwise the sole item must be in the only child with count==1
        for child in node.get('children', {}).values():
            if child.get('count', 0) == 1:
                res = self._find_sole_item(child)
                if res:
                    return res
        return None

    def get_custom_debug_lines(self, state, camera, map_manager, entities) -> List[str]:
        lines = [
            f"Modo: {state.mode}",
            f"Pos: ({round(entities.player.x)}, {round(entities.player.y)})",
        ]
        mx, my = pygame.mouse.get_pos()
        wx = round(mx / camera.zoom + camera.offset_x)
        wy = round(my / camera.zoom + camera.offset_y)
        lines.append(f"Mouse: ({wx}, {wy})")
        tile_col, tile_row = wx // TILE_SIZE, wy // TILE_SIZE
        tile_text = next((t.tile_type for t in map_manager.tiles_in_region if t.rect.collidepoint(wx, wy)), "?")
        lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")
        # --- Camera diagnostics (numeric with 5 decimals) ---
        try:
            z = float(getattr(camera, 'zoom', 1.0))
        except Exception:
            z = 1.0
        try:
            ox = float(getattr(camera, 'offset_x', 0.0))
        except Exception:
            ox = 0.0
        try:
            oy = float(getattr(camera, 'offset_y', 0.0))
        except Exception:
            oy = 0.0
        try:
            sw = int(getattr(camera, 'screen_width', 0))
        except Exception:
            sw = 0
        try:
            sh = int(getattr(camera, 'screen_height', 0))
        except Exception:
            sh = 0

        # World view rectangle derived from camera/state
        vw = (sw / z) if z else 0.0
        vh = (sh / z) if z else 0.0
        cx = ox + vw / 2.0
        cy = oy + vh / 2.0
        # Screen size of one tile
        try:
            ts_w, ts_h = camera.scale((TILE_SIZE, TILE_SIZE))
        except Exception:
            ts_w, ts_h = int(TILE_SIZE * z), int(TILE_SIZE * z)

        # Optional constraints/step if exposed somewhere (do not assume defaults)
        def _fmt_opt(val):
            try:
                if val is None:
                    return 'n/a'
                f = float(val)
                return f"{f:.5f}"
            except Exception:
                return 'n/a'

        min_zoom = getattr(camera, 'min_zoom', None)
        max_zoom = getattr(camera, 'max_zoom', None)
        zoom_step = getattr(camera, 'zoom_step', None)

        lines.append(
            (
                "Camera: "
                f"zoom={z:.5f} scale={z:.5f} "
                f"offset=({ox:.5f}, {oy:.5f})"
            )
        )
        lines.append(
            (
                "  screen="
                f"{sw}x{sh} world_view=(x0={ox:.5f}, y0={oy:.5f}, w={vw:.5f}, h={vh:.5f})"
            )
        )
        lines.append(
            (
                "  center_world="
                f"({cx:.5f}, {cy:.5f}) tile_screen={ts_w}x{ts_h} px_per_world={z:.5f}"
            )
        )
        lines.append(
            (
                "  limits: "
                f"min_zoom={_fmt_opt(min_zoom)} max_zoom={_fmt_opt(max_zoom)} step={_fmt_opt(zoom_step)}"
            )
        )
        # Allowed zooms list (for quick debugging/reference)
        try:
            allowed_str = ", ".join(f"{v:.5f}" for v in ALLOWED_ZOOMS)
            lines.append(f"  allowed_zooms: [{allowed_str}]")
        except Exception:
            pass
        return lines

    def _build_lines(
        self,
        state=None,
        camera=None,
        map_manager=None,
        entities=None,
        extra_lines: Optional[List[str]] = None,
    ):
        model = self.model
        lines: List[Tuple[str, str]] = []
        line_levels: List[Optional[int]] = []
        label_w = value_w = 0

        tree = self._build_perf_tree(model.perf_log)
        if model.initially_collapsed:
            model.collapsed_groups = set(self._collect_group_ids(tree))
            model.initially_collapsed = False

        def render_node(node, level: int = 0):
            # Render child groups first (with headers), flattening single-item subtrees into item lines
            for gid in sorted(node["children"].keys(), key=self._numeric_sort_key):
                child = node["children"][gid]
                sole = self._find_sole_item(child)
                if sole:
                    full_gid, label, avg_ms = sole
                    # Compose label without duplicating the id
                    if full_gid and (not label or label.startswith(full_gid)):
                        display_label = full_gid
                    elif full_gid:
                        display_label = f"{full_gid} {label}"
                    else:
                        display_label = label
                    # No header -> do not add an extra indent level
                    lbl = f"{'  ' * level}{display_label:<20}"
                    val = f"{avg_ms:>6.2f} ms"
                    lines.append((lbl, val))
                    line_levels.append(level)
                    continue
                # Render header for multi-item groups
                name_part = f" {child.get('title')}" if child.get('title') else ""
                is_collapsed = gid in model.collapsed_groups
                indicator = '▶' if is_collapsed else '▼'
                header_lbl = f"{'  ' * level}{indicator} {gid}{name_part} ({child['count']}):"
                header_val = f"{child['total']:>6.2f} ms"
                lines.append((header_lbl, header_val))
                line_levels.append(level)
                if gid not in model.collapsed_groups:
                    render_node(child, level + 1)
            # Direct items at this level
            if level > 0:  # root has no direct label, so only indent items when inside a group
                for item_id, label, avg_ms in sorted(node["items"], key=lambda x: x[1]):
                    # If we have a numeric id (from item or from this node), show it
                    display_id = item_id if self._is_numeric_id(item_id) else (node.get('id') if self._is_numeric_id(node.get('id')) else None)
                    display_label = f"{display_id} {label}".strip() if display_id else label
                    lbl = f"{'  ' * level}{display_label:<20}"
                    val = f"{avg_ms:>6.2f} ms"
                    lines.append((lbl, val))
                    line_levels.append(level)

        render_node(tree, 0)

        # Build a set of normalized labels from loop-generated lines to avoid duplicates
        def _norm(lbl: str) -> str:
            s = (lbl or "").strip()
            # Remove expand/collapse indicators
            s = re.sub(r'^[▶▼]\s*', '', s)
            # Remove numeric dotted prefixes
            s = re.sub(r'^(\d+(?:\.\d+)*)\s*', '', s)
            # Drop trailing colon
            s = s.rstrip(':')
            # Collapse internal whitespace
            s = re.sub(r'\s+', ' ', s)
            return s

        existing_norms = { _norm(l) for (l, _r) in lines }

        if state and hasattr(state, 'clock'):
            fps = state.clock.get_fps()
            ft = (1000 / fps) if fps > 0 else 0
            if _norm("FrameTime:") not in existing_norms:
                lines.insert(0, ("FrameTime:", f"{ft:0.1f} ms"))
                line_levels.insert(0, None)
            if _norm("FPS:") not in existing_norms:
                lines.insert(0, ("FPS:", f"{fps:0.1f}"))
                line_levels.insert(0, None)

        if extra_lines is None and state and camera and map_manager and entities:
            extra_lines = self.get_custom_debug_lines(state, camera, map_manager, entities)
        if extra_lines:
            # Filter out custom/manual lines that duplicate existing loop-generated labels
            filtered = []
            for text in extra_lines:
                if _norm(text) not in existing_norms:
                    filtered.append(text)
            if filtered:
                lines.append(("", ""))
                line_levels.append(None)
                for text in filtered:
                    lines.append((text, ""))
                    line_levels.append(None)

        # Safety: limit number of lines only when paging is disabled
        truncated_count = 0
        if not getattr(model, 'paging_enabled', False):
            max_lines = getattr(model, 'max_lines', 400)
            if len(lines) > max_lines:
                truncated_count = len(lines) - max_lines
                keep = max_lines - 1 if max_lines >= 1 else 0
                if keep > 0:
                    lines = lines[:keep] + [("...", f"{truncated_count} líneas ocultas")]  # notice line
                    line_levels = line_levels[:keep] + [None]
                else:
                    lines = [("...", f"{truncated_count} líneas ocultas")]  # only notice
                    line_levels = [None]

        # Truncate fields to avoid creating huge text surfaces. Do NOT truncate header labels (ending with ':').
        def _truncate_field(left: str, right: str) -> tuple[str, str]:
            max_chars = getattr(model, 'max_chars_per_field', 256)
            l = left
            # Respect headers (used for group ids and interaction)
            if not left.strip().endswith(':') and len(left) > max_chars:
                l = left[: max_chars - 1] + '…'
            r = right
            if len(right) > max_chars:
                r = right[: max_chars - 1] + '…'
            return l, r

        lines = [ _truncate_field(l, r) for (l, r) in lines ]

        # Final width adjust for all lines (single pass) using possibly truncated values
        font = self.view._get_font(model.font_name, model.font_size)
        for left, right in lines:
            lw, _ = font.size(left)
            vw, _ = font.size(right)
            label_w = max(label_w, lw)
            value_w = max(value_w, vw)

        return lines, label_w, value_w, line_levels

    def draw_borders(self, screen, camera, map_manager):
        # Lobby
        x0, y0 = map_manager.lobby_offset
        tl = camera.apply((x0 * TILE_SIZE, y0 * TILE_SIZE))
        sz = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
        pygame.draw.rect(screen, self.model.border_colors['lobby'], pygame.Rect(tl, sz), self.model.border_width)
        # Dungeon
        dx, dy = calculate_dungeon_offset(map_manager.lobby_offset)
        tl2 = camera.apply((dx * TILE_SIZE, dy * TILE_SIZE))
        sz2 = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
        pygame.draw.rect(screen, self.model.border_colors['dungeon'], pygame.Rect(tl2, sz2), self.model.border_width)
        # Global
        tl3 = camera.apply((0, 0))
        sz3 = camera.scale((global_map_settings.global_width * TILE_SIZE, global_map_settings.global_height * TILE_SIZE))
        pygame.draw.rect(screen, self.model.border_colors['global'], pygame.Rect(tl3, sz3), self.model.border_width)

    def render(
        self,
        screen,
        state=None,
        camera=None,
        map_manager=None,
        entities=None,
        extra_lines: Optional[List[str]] = None,
        position=(8, 8),
        show_borders=False,
    ):
        now = time.perf_counter()
        rebuild = (now - self.model.last_update_time) >= self.model.update_interval
        if rebuild or self.model.panel_surf is None:
            lines, label_w, value_w, line_levels = self._build_lines(state, camera, map_manager, entities, extra_lines)

            # Paging: slice lines according to current page and available height
            if getattr(self.model, 'paging_enabled', False):
                line_h = self.view.line_height(self.model)
                screen_surf = pygame.display.get_surface()
                if screen_surf is not None:
                    screen_h = screen_surf.get_height()
                    # Estimar alto visible coherente con view (usa +200 margen)
                    visible_h = max(line_h, min(getattr(self.model, 'max_surface_height', 8000), screen_h - position[1] + 200))
                else:
                    visible_h = getattr(self.model, 'max_surface_height', 8000)
                lines_per_page = max(1, int(visible_h // line_h))
                total_lines = len(lines)
                total_pages = max(1, (total_lines + lines_per_page - 1) // lines_per_page)
                # Clamp y slice
                pi = max(0, min(self.model.page_index, total_pages - 1))
                i0 = pi * lines_per_page
                i1 = min(total_lines, i0 + lines_per_page)
                page_lines = lines[i0:i1]
                page_levels = line_levels[i0:i1]
                # Persist runtime paging metadata
                self.model.page_index = pi
                self.model.total_lines = total_lines
                self.model.lines_per_page = lines_per_page
                self.model.total_pages = total_pages
                # Rebuild with just the page
                self.view.rebuild_panel(self.model, position, page_lines, label_w, value_w)
                self.model.line_levels = page_levels
            else:
                # No paging: render all (ya limitado por max_lines si aplica)
                self.view.rebuild_panel(self.model, position, lines, label_w, value_w)
                self.model.line_levels = line_levels
            self.model.last_update_time = now

        if self.model.panel_surf and self.model.panel_rect:
            clip = screen.get_clip()
            screen.set_clip(self.model.panel_rect)
            screen.blit(self.model.panel_surf, (self.model.panel_rect.left, self.model.panel_rect.top - self.model.scroll_offset))
            screen.set_clip(clip)
            # Hover highlight group rectangle
            mx, my = pygame.mouse.get_pos()
            if self.model.panel_rect.collidepoint((mx, my)):
                line_h = self.view.line_height(self.model)
                local_y = my - self.model.panel_rect.top + self.model.scroll_offset
                index = local_y // line_h
                keys = self.model.line_keys
                levels = getattr(self.model, 'line_levels', [])
                if 0 <= index < len(keys) and 0 <= index < len(levels):
                    cur_level = levels[index]
                    if cur_level is not None:
                        # Find owning header at level <= current line's level
                        h = index
                        while h >= 0:
                            if keys[h].endswith(':') and levels[h] is not None and levels[h] <= cur_level:
                                break
                            h -= 1
                        if h >= 0 and keys[h].endswith(':'):
                            header_level = levels[h] or 0
                            j = h + 1
                            while j < len(keys):
                                lv = levels[j] if j < len(levels) else None
                                # Stop at separators/others (None) or any line at same or shallower level
                                if lv is None or lv <= header_level:
                                    break
                                j += 1
                            start_idx = h
                            end_idx = j - 1
                            if end_idx >= start_idx:
                                rect_x = self.model.panel_rect.left
                                rect_y = self.model.panel_rect.top - self.model.scroll_offset + start_idx * line_h
                                rect_w = self.model.panel_rect.width
                                rect_h = (end_idx - start_idx + 1) * line_h
                                pygame.draw.rect(screen, (255, 255, 0), pygame.Rect(rect_x, rect_y, rect_w, rect_h), 2)

        if show_borders:
            if not (map_manager and camera):
                raise ValueError("Para dibujar bordes debe proporcionar map_manager y camera")
            self.draw_borders(screen, camera, map_manager)
