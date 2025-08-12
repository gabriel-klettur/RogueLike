import pygame
pygame.font.init()
import os
from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_picker_panel.spells_editor_view import SpellEditorView
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.spells.spells_picker_panel.spells_editor_events import SpellEditorEventHandler
from roguelike_editors.spells.spells_properties_panel.spells_properties_panel_controller import (
    SpellsPropertiesPanelController,
)
from roguelike_engine.utils.loader import load_image
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_model import (
    SpellsToolBarPanelModel,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_view import (
    SpellsToolBarPanelView,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_events import (
    SpellsToolBarPanelEventHandler,
)
from roguelike_editors.spells.spells_tool_bar_panel.spells_tool_bar_panel_controller import (
    SpellsToolBarPanelController,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_model import (
    SpellsAddRemovePanelModel,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_view import (
    SpellsAddRemovePanelView,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_events import (
    SpellsAddRemovePanelEventHandler,
)
from roguelike_editors.spells.spells_add_remove_panel.spells_add_remove_panel_controller import (
    SpellsAddRemovePanelController,
)
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_game.config.spells_config import reload_spells

class SpellEditorController:
    """Controller for Spell Editor UI."""
    def __init__(self, spells: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = SpellEditorModel(spells=spells.copy(), assets=assets)
        self.view = SpellEditorView(assets, font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        self.event_handler = SpellEditorEventHandler(self)
        # Toolbar MVC
        self.spells_toolbar_model = SpellsToolBarPanelModel()
        self.spells_toolbar_view = SpellsToolBarPanelView(controller=self, model=self.spells_toolbar_model)
        self.spells_toolbar_events = SpellsToolBarPanelEventHandler(controller=self, model=self.spells_toolbar_model)
        self.spells_toolbar_controller = SpellsToolBarPanelController(self, self.spells_toolbar_model, self.spells_toolbar_view, self.spells_toolbar_events)
        # Ensure ToolbarView uses the panel controller for active-state checks
        if hasattr(self.spells_toolbar_view, 'widget'):
            self.spells_toolbar_view.widget.controller = self.spells_toolbar_controller
        # Add/Remove MVC
        self.spells_add_remove_model = SpellsAddRemovePanelModel()
        self.spells_add_remove_view = SpellsAddRemovePanelView(controller=self, model=self.spells_add_remove_model)
        self.spells_add_remove_events = SpellsAddRemovePanelEventHandler(controller=self, model=self.spells_add_remove_model)
        self.spells_add_remove_controller = SpellsAddRemovePanelController(self, self.spells_add_remove_model, self.spells_add_remove_view, self.spells_add_remove_events)
        if hasattr(self.spells_add_remove_view, 'widget'):
            self.spells_add_remove_view.widget.controller = self.spells_add_remove_controller

        # Properties panel MVC
        self.spells_properties_controller = SpellsPropertiesPanelController(self.model.spells, font)
        # Provide callbacks
        def _get_assets_anchor_rect():
            """Return an anchor rect so the Assets picker appears BELOW and ALIGNED to the Spells Picker panel.
            Primary anchor: the picker's grid_rect (left x, bottom y + margin, same width).
            Fallbacks: asset cell rect, then properties panel rect.
            """
            # Prefer aligning to the Spells Picker grid
            try:
                grid_rect = getattr(self.view, 'grid_rect', None)
                if grid_rect is not None:
                    # Build a rect whose top-left is just below the grid, aligned on the left, and with same width
                    return pygame.Rect(grid_rect.x, grid_rect.bottom + UI_MARGIN, grid_rect.w, 0)
            except Exception:
                pass
            # Fallback to the properties panel cell rect if present
            try:
                cell = getattr(self.spells_properties_controller.model, 'asset_cell_rect', None)
                if cell:
                    return cell
            except Exception:
                pass
            # Final fallback to the properties panel rect
            try:
                return getattr(self.spells_properties_controller.model, 'panel_rect', None)
            except Exception:
                return None

        def _on_asset_changed(spell_id: str, new_asset_path: str) -> None:
            try:
                img = load_image(new_asset_path)
                self.model.assets[spell_id] = img
                # Keep view dict in sync
                try:
                    self.view.assets[spell_id] = img
                except Exception:
                    pass
                # Hot-reload game spells so new casts use updated sprite/scale
                try:
                    reload_spells()
                except Exception:
                    pass
            except Exception:
                # Leave asset unchanged on failure
                pass

        def _on_after_commit_edit(key: str, old_id: str, new_id: str | None, value):
            # Handle id rename to keep model and assets in sync
            if key == 'id' and new_id and new_id != old_id:
                try:
                    if old_id in self.model.spells:
                        self.model.spells[new_id] = self.model.spells.pop(old_id)
                    if old_id in self.model.assets:
                        self.model.assets[new_id] = self.model.assets.pop(old_id)
                    if self.model.selected_id == old_id:
                        self.model.selected_id = new_id
                    if self.model.hovered_id == old_id:
                        self.model.hovered_id = new_id
                except Exception:
                    pass
            # Hot-reload game spells so runtime immediately reflects edits
            try:
                reload_spells()
            except Exception:
                pass

        self.spells_properties_controller.get_assets_anchor_rect = _get_assets_anchor_rect
        self.spells_properties_controller.on_asset_changed = _on_asset_changed
        self.spells_properties_controller.on_after_commit_edit = _on_after_commit_edit

        # Provide a left-anchor provider so the picker grid sits to the right of Add/Remove panel
        def _picker_left_anchor_x() -> int | None:
            try:
                # Only when picker is visible (tied to 'spells_on_map')
                if not getattr(self.model, 'picker_visible', False):
                    return None
                # Need toolbar widget to compute base position
                tb_widget = getattr(self.spells_toolbar_view, 'widget', None)
                if tb_widget is None:
                    return None
                tb_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
                tb_w, _ = tb_widget.panel.surface.get_size()
                # Add/Remove width (even if not yet rendered this frame)
                arm_widget = getattr(self.spells_add_remove_view, 'widget', None)
                if arm_widget is None:
                    return tb_pos[0] + tb_w + UI_MARGIN
                arm_w, _ = arm_widget.panel.surface.get_size()
                # Picker left is to the right of Add/Remove panel
                return tb_pos[0] + tb_w + UI_MARGIN + arm_w + UI_MARGIN
            except Exception:
                return None

        try:
            self.view.get_picker_left_anchor_x = _picker_left_anchor_x
        except Exception:
            pass

    def handle_event(self, event: pygame.event.Event) -> None:
        # Route to toolbar first, then add/remove, only if editor visible
        if self.model.visible:
            if self.spells_toolbar_controller.handle_event(event):
                return
            if self.spells_add_remove_controller.handle_event(event):
                return
            # Then properties panel
            if self.spells_properties_controller.handle_event(event):
                return
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)
        # Draw toolbar and add/remove on top only when visible
        if self.model.visible:
            self.spells_toolbar_controller.render(screen)
            self.spells_add_remove_controller.render(screen)
            # Update context and draw properties panel only when picker is visible and not in delete mode
            if getattr(self.model, 'picker_visible', False) and not getattr(self.model, 'delete_mode_active', False):
                self.spells_properties_controller.update_context(
                    self.model.spells, self.model.selected_id, self.model.hovered_id
                )
                title_rect = getattr(self.view, 'title_rect', None)
                # Anchor properties to the right of the picker grid like Entities editor
                grid_rect = getattr(self.view, 'grid_rect', None)
                if grid_rect is not None:
                    left_x = grid_rect.right + UI_MARGIN
                    top_y = grid_rect.y
                    try:
                        self.spells_properties_controller.view.set_anchor(left_x, top_y)
                    except Exception:
                        pass
                else:
                    try:
                        self.spells_properties_controller.view.set_anchor(None, None)
                    except Exception:
                        pass
                self.spells_properties_controller.draw(screen, title_rect=title_rect)

    def _commit_edit(self) -> None:
        if not self.model.editing_property:
            return
        sid = self.model.selected_id or self.model.hovered_id
        if not sid:
            return
        key = self.model.editing_property
        new_text = self.model.editing_text
        # JSON path
        path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
        root = load_from_json(path)
        entry = root.get(sid, {})
        old_val = entry.get(key)
        # Convert type
        try:
            if isinstance(old_val, bool):
                converted = new_text.lower() in ("true", "1", "yes")
            elif isinstance(old_val, int):
                converted = int(new_text)
            elif isinstance(old_val, float):
                converted = float(new_text)
            else:
                converted = new_text
        except ValueError:
            converted = new_text
        entry[key] = converted
        # Persist changes
        save_to_json(path, sid, entry)
        # Hot-reload runtime spells config so new casts reflect changes
        try:
            reload_spells()
        except Exception:
            pass
        # Update model
        self.model.spells[sid] = entry
        # Reset editing
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
