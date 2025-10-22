import pygame
from roguelike_editors.buildings.tools.default_tool.default_tool_view import DefaultToolView
from roguelike_editors.buildings.buildings_title_panel.buildings_title_view import BuildingsTitleView
from roguelike_ui.ui_blocker import is_blocked

try:
    from roguelike_engine.config.config_tiles import TILE_SIZE
except Exception:
    TILE_SIZE = 32

from roguelike_editors.buildings.tools.split_z_tool.split_tool_view   import SplitToolView
from roguelike_editors.buildings.tools.z_tool.z_tool_view       import ZToolView
from roguelike_editors.buildings.tools.collider_scope_tool.collider_scope_tool_view import ColliderScopeToolView

from roguelike_editors.buildings.buildings_picker.building_picker_view      import PickerView

class BuildingEditorView:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state
        self.default_view  = DefaultToolView(state, editor_state)

        self.split_view    = SplitToolView(state, editor_state)
        self.z_bottom_view = ZToolView(state, editor_state, target="bottom")
        self.z_top_view    = ZToolView(state, editor_state, target="top")
        self.collider_scope_view = ColliderScopeToolView(state, editor_state)
                
        self.picker_view = PickerView(editor_state)
        # Professional title bar (top-left)
        self.title_view = BuildingsTitleView(None, editor_state)
        # Small font to render building ID labels
        try:
            self._id_font = pygame.font.Font(None, 16)
        except Exception:
            self._id_font = None
        # Caches to avoid per-frame allocations
        self._hover_fill_cache = {}
        self._id_label_cache = {}
        self._dim_cache = {}
        self._modal_text_cache = {}
        self._button_label_cache = {}


    def _render_building_collision_overlay(self, screen, camera, building):
        if not getattr(building, 'collision_map', None):
            return
        cell_w, cell_h = camera.scale((TILE_SIZE, TILE_SIZE))
        for ry, row in enumerate(building.collision_map):
            for cx, val in enumerate(row):
                if val == "#":
                    wx = building.x + cx * TILE_SIZE
                    wy = building.y + ry * TILE_SIZE
                    sx, sy = camera.apply((wx, wy))
                    overlay = pygame.Surface((cell_w, cell_h), pygame.SRCALPHA)
                    overlay.fill((255, 0, 0, 100))
                    screen.blit(overlay, (sx, sy))

    def render(self, screen, camera, buildings):
        if not self.editor.active:
            return

        # Title bar always visible when editor is active
        title_rect = self.title_view.render(screen)
        # Expose last title rect for external layout (e.g., toolbars)
        try:
            self._last_title_rect = title_rect
        except Exception:
            pass
        # Anchor picker: if user dragged the panel, use manual position.
        # Else align next to Add/Remove panel if present; fallback to under title
        try:
            if getattr(self.editor, 'picker_manual_pos', None) is None:
                add_remove_rect = getattr(self.editor, 'add_remove_panel_rect', None)
                if add_remove_rect is not None:
                    # Align to the right of the add/remove panel
                    self.picker_view._left_anchor_x = add_remove_rect.right + 8
                    self.picker_view._top_anchor_y = add_remove_rect.top
                else:
                    # Default: under the title bar
                    self.picker_view._top_anchor_y = title_rect.bottom + 8
                    self.picker_view._left_anchor_x = title_rect.left
            else:
                px, py = self.editor.picker_manual_pos
                self.picker_view._left_anchor_x = int(px)
                self.picker_view._top_anchor_y = int(py)
        except Exception:
            pass

        # Collision overlays/picker are handled by the colliders panel now.

        # (Modo normal: renderizado completo con bordes y z-layer)
        if self.editor.picker_active:
            self.picker_view.render(screen, camera)

        # Cachear el rect del edificio hovered para superposiciones externas (p.ej., tutorial)
        try:
            hb = getattr(self.editor, 'hovered_building', None)
            if hb:
                x, y = camera.apply((hb.x, hb.y))
                w, h = camera.scale(hb.image.get_size())
                self._last_hovered_building_rect = pygame.Rect(x, y, w, h)
            else:
                self._last_hovered_building_rect = None
        except Exception:
            pass

        # Suppress building hover visuals (outline, handles) when UI is blocking
        try:
            mx, my = pygame.mouse.get_pos()
            ui_blocked = bool(is_blocked(mx, my))
        except Exception:
            ui_blocked = False

        # Reset per-frame tool UI rect caches for overlays
        try:
            self._last_split_handle_rect = None
            self._last_z_bottom_minus_rect = None
            self._last_z_bottom_plus_rect = None
            self._last_z_top_minus_rect = None
            self._last_z_top_plus_rect = None
        except Exception:
            pass

        # Draw hover outline only when UI is NOT blocking. If remove mode is active, use red border
        # and a semi-transparent red fill; otherwise, use the standard cyan outline.
        try:
            if not ui_blocked:
                hb = getattr(self.editor, 'hovered_building', None)
                ab = getattr(self.editor, 'active_building', None)
                if hb is not None and hb is not ab:
                    x, y = camera.apply((hb.x, hb.y))
                    w, h = camera.scale(hb.image.get_size())
                    hover_rect = pygame.Rect(x, y, w, h)
                    if getattr(self.editor, 'remove_mode_active', False):
                        size = (max(0, hover_rect.width), max(0, hover_rect.height))
                        if size[0] > 0 and size[1] > 0:
                            key = (size[0], size[1])
                            fill = self._hover_fill_cache.get(key)
                            if fill is None:
                                s = pygame.Surface(size, pygame.SRCALPHA)
                                s.fill((255, 0, 0, 60))
                                self._hover_fill_cache[key] = s
                                fill = s
                            screen.blit(fill, (hover_rect.left, hover_rect.top))
                        # Red border
                        pygame.draw.rect(screen, (255, 0, 0), hover_rect, 3)
                    else:
                        pygame.draw.rect(screen, (0, 255, 255), hover_rect, 2)  # cyan, thin
        except Exception:
            pass

        for b in buildings:
            # Solo mostrar opciones en el edificio activo (persistente)
            if b != getattr(self.editor, 'active_building', None):
                continue
            x, y = camera.apply((b.x, b.y))
            w, h = camera.scale(b.image.get_size())
            rect = pygame.Rect(x, y, w, h)
            try:
                self._last_active_building_rect = rect
            except Exception:
                pass
            try:
                if not getattr(self.editor, 'colliders_mode', False) and getattr(b, 'collision_map', None):
                    self._render_building_collision_overlay(screen, camera, b)
            except Exception:
                pass
            # Active selection outline: yellow, thicker
            pygame.draw.rect(screen, (255, 215, 0), rect, 5)
            # Render small ID label near the top-left of the building rect
            try:
                if self._id_font is not None:
                    bid = getattr(b, 'id', None)
                    if bid is not None:
                        label = f"ID {bid}"
                        ts, ss = self._get_id_label_surfaces(label)
                        lx = rect.left
                        ly = rect.top - ts.get_height() - 2
                        if ly < 0:
                            ly = rect.top + 2
                        # simple shadow for readability
                        screen.blit(ss, (lx + 1, ly + 1))
                        screen.blit(ts, (lx, ly))
            except Exception:
                pass
            # Ocultar handles de herramientas en modo colisiones o cuando la UI bloquea
            if (not getattr(self.editor, 'colliders_mode', False)) and (not ui_blocked):
                self.default_view.render_reset_handle(screen, b, camera)
                # Split handle
                try:
                    split_bounds = self.split_view.render(screen, b, camera)
                    if isinstance(split_bounds, dict):
                        hr = split_bounds.get('handle_rect')
                        if hr is not None:
                            self._last_split_handle_rect = hr.copy()
                except Exception:
                    pass
                # Z bottom
                try:
                    zb = self.z_bottom_view.render(screen, b, camera)
                    if isinstance(zb, dict):
                        px, py = zb.get('panel_pos', (0, 0))
                        m = zb.get('minus_rect')
                        p = zb.get('plus_rect')
                        if m is not None:
                            self._last_z_bottom_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                        if p is not None:
                            self._last_z_bottom_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                except Exception:
                    pass
                # Z top
                try:
                    zt = self.z_top_view.render(screen, b, camera)
                    if isinstance(zt, dict):
                        px, py = zt.get('panel_pos', (0, 0))
                        m = zt.get('minus_rect')
                        p = zt.get('plus_rect')
                        if m is not None:
                            self._last_z_top_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                        if p is not None:
                            self._last_z_top_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                except Exception:
                    pass
            # Render toggle CG/CU bottom-right when UI is not blocked (also visible in colliders_mode)
            try:
                if not ui_blocked:
                    self.collider_scope_view.render(screen, b, camera)
            except Exception:
                pass

        # --- Confirmation modal overlay (render last, on top) ---
        try:
            if getattr(self.editor, 'confirm_delete_visible', False):
                self._render_confirm_delete_modal(screen)
        except Exception:
            pass

    def _render_confirm_delete_modal(self, screen) -> None:
        # Backdrop dim
        try:
            w, h = screen.get_size()
        except Exception:
            return
        try:
            key = (w, h)
            dim = self._dim_cache.get(key)
            if dim is None:
                d = pygame.Surface((w, h), pygame.SRCALPHA)
                d.fill((0, 0, 0, 140))
                self._dim_cache[key] = d
                dim = d
            screen.blit(dim, (0, 0))
        except Exception:
            pass
        # Panel
        panel_w = min(520, int(w * 0.8))
        panel_h = 200
        px = (w - panel_w) // 2
        py = (h - panel_h) // 2
        panel_rect = pygame.Rect(px, py, panel_w, panel_h)
        try:
            pygame.draw.rect(screen, (30, 30, 30), panel_rect, border_radius=8)
            pygame.draw.rect(screen, (220, 220, 220), panel_rect, 2, border_radius=8)
        except Exception:
            pass
        # Text (multi-line)
        text = getattr(self.editor, 'confirm_delete_text', "¿Eliminar?") or "¿Eliminar?"
        try:
            font = pygame.font.Font(None, 24)
        except Exception:
            font = None
        lines = []
        if font is not None:
            cache_key = (text, font.size(" ")[1])
            cached = self._modal_text_cache.get(cache_key)
            if cached is None:
                acc = []
                for raw in str(text).split("\n"):
                    try:
                        s = font.render(raw, True, (240, 240, 240))
                        acc.append(s)
                    except Exception:
                        continue
                self._modal_text_cache[cache_key] = acc
                lines = acc
            else:
                lines = cached
        y = panel_rect.top + 16
        for s in lines:
            try:
                screen.blit(s, (panel_rect.left + 16, y))
                y += s.get_height() + 6
            except Exception:
                continue
        # Buttons
        btn_w = 140
        btn_h = 36
        gap = 20
        bx = panel_rect.centerx - btn_w - (gap // 2)
        by = panel_rect.bottom - btn_h - 16
        yes_rect = pygame.Rect(bx, by, btn_w, btn_h)
        no_rect = pygame.Rect(panel_rect.centerx + (gap // 2), by, btn_w, btn_h)
        try:
            # Yes button (Eliminar)
            pygame.draw.rect(screen, (180, 40, 40), yes_rect, border_radius=6)
            pygame.draw.rect(screen, (255, 255, 255), yes_rect, 2, border_radius=6)
            yfont = pygame.font.Font(None, 28)
            ys = self._get_button_label(yfont, "Eliminar")
            screen.blit(ys, (yes_rect.centerx - ys.get_width() // 2, yes_rect.centery - ys.get_height() // 2))
            # No button (Cancelar)
            pygame.draw.rect(screen, (60, 60, 60), no_rect, border_radius=6)
            pygame.draw.rect(screen, (255, 255, 255), no_rect, 2, border_radius=6)
            ns = self._get_button_label(yfont, "Cancelar")
            screen.blit(ns, (no_rect.centerx - ns.get_width() // 2, no_rect.centery - ns.get_height() // 2))
        except Exception:
            pass
        # Expose rects for event handler
        try:
            self.editor.confirm_yes_rect = yes_rect
            self.editor.confirm_no_rect = no_rect
        except Exception:
            pass

    def _get_id_label_surfaces(self, label: str):
        surf = self._id_label_cache.get(label)
        if surf is None:
            try:
                ts = self._id_font.render(label, True, (255, 255, 255)) if self._id_font else pygame.Surface((0, 0), pygame.SRCALPHA)
                ss = self._id_font.render(label, True, (0, 0, 0)) if self._id_font else pygame.Surface((0, 0), pygame.SRCALPHA)
            except Exception:
                ts = pygame.Surface((0, 0), pygame.SRCALPHA)
                ss = pygame.Surface((0, 0), pygame.SRCALPHA)
            self._id_label_cache[label] = (ts, ss)
            return ts, ss
        return surf

    def _get_button_label(self, font: pygame.font.Font, text: str) -> pygame.Surface:
        key = (text, font.size(" ")[1])
        surf = self._button_label_cache.get(key)
        if surf is None:
            try:
                s = font.render(text, True, (255, 255, 255))
            except Exception:
                s = pygame.Surface((0, 0), pygame.SRCALPHA)
            self._button_label_cache[key] = s
            return s
        return surf