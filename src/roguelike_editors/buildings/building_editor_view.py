import pygame
from roguelike_editors.buildings.tools.default_tool.default_tool_view import DefaultToolView
from roguelike_editors.buildings.buildings_title_panel.buildings_title_view import BuildingsTitleView
from roguelike_ui.ui_blocker import is_blocked

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
                        # Semi-transparent red fill
                        fill = pygame.Surface((hover_rect.width, hover_rect.height), pygame.SRCALPHA)
                        fill.fill((255, 0, 0, 60))  # 60 alpha for subtle overlay
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
            # Exponer el rect del edificio activo para overlays (tutorial)
            try:
                self._last_active_building_rect = rect
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
                        text_surf = self._id_font.render(label, True, (255, 255, 255))
                        shadow_surf = self._id_font.render(label, True, (0, 0, 0))
                        lx = rect.left
                        ly = rect.top - text_surf.get_height() - 2
                        if ly < 0:
                            ly = rect.top + 2
                        # simple shadow for readability
                        screen.blit(shadow_surf, (lx + 1, ly + 1))
                        screen.blit(text_surf, (lx, ly))
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