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

        # Suppress building hover visuals (outline, handles) when UI is blocking
        try:
            mx, my = pygame.mouse.get_pos()
            if is_blocked(mx, my):
                return
        except Exception:
            pass

        for b in buildings:
            # Solo mostrar opciones en el edificio activo (persistente)
            if b != getattr(self.editor, 'active_building', None):
                continue
            x, y = camera.apply((b.x, b.y))
            w, h = camera.scale(b.image.get_size())
            rect = pygame.Rect(x, y, w, h)
            pygame.draw.rect(screen, (0, 255, 255), rect, 4)
            pygame.draw.rect(screen, (255, 255, 255), rect, 1)
            # Ocultar handles de herramientas en modo colisiones (colliders_mode)
            if not getattr(self.editor, 'colliders_mode', False):
                self.default_view.render_reset_handle(screen, b, camera)
                self.split_view.render(screen, b, camera)
                self.z_bottom_view.render(screen, b, camera)
                self.z_top_view.render(screen, b, camera)
            # Render toggle CG/CU bottom-right
            try:
                self.collider_scope_view.render(screen, b, camera)
            except Exception:
                pass