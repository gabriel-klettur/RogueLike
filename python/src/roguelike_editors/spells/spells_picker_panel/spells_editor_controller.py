import pygame
pygame.font.init()
import os
import logging
from roguelike_editors.spells.spells_picker_panel.spells_editor_model import SpellEditorModel
from roguelike_editors.spells.spells_picker_panel.spells_editor_view import SpellEditorView
from roguelike_ui.widgets.text_input.text_input import TextInput
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
from roguelike_game.config.spells_config import reload_spells
from roguelike_editors.spells.spells_tutorial_panel.spells_tutorial_panel_controller import (
    SpellsTutorialPanelController,
)
from roguelike_editors.spells.spells_picker_panel.services.particle_preview_manager import (
    ParticlePreviewManager,
)
from roguelike_editors.spells.spells_picker_panel.services.persistence import (
    commit_edit as _svc_commit_edit,
    reload_sprites_from_spells as _svc_reload_sprites,
)
from roguelike_editors.spells.spells_picker_panel.services.ui_layout import (
    get_assets_anchor_rect,
    picker_left_anchor_x,
    set_properties_anchor,
)

logger = logging.getLogger(__name__)
# Env-gated spelling editor preview debug
LOG_SPELLS_PREVIEW_DEBUG = (
    os.getenv("RL_SPELLS_PREVIEW_DEBUG") == "1"
    or os.getenv("RL_SPELLS_EDITOR_DEBUG") == "1"
)

class SpellEditorController:
    """Controller for Spell Editor UI."""
    def __init__(self, spells: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = SpellEditorModel(spells=spells.copy(), assets=assets)
        self.view = SpellEditorView(assets, font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        self.event_handler = SpellEditorEventHandler(self)
        # Throttle timestamp for frame-id debug logs (ms)
        self._last_frameid_log_ts: int = 0
        # Particle preview manager (centralized)
        self.preview_manager = ParticlePreviewManager(
            view=self.view,
            get_frame_id=lambda: getattr(self, "_render_frame_id", 0),
            enable_debug=(LOG_SPELLS_PREVIEW_DEBUG and logger.isEnabledFor(logging.DEBUG)),
        )
        # Backward-compat alias if other modules peek into this cache
        self._particle_previews = self.preview_manager.previews_cache
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
        # Link back so properties panel can query preview providers and selection from this controller
        try:
            self.spells_properties_controller.editor_controller = self
        except Exception:
            pass
        # Tutorial panel controller (overlay)
        try:
            self.spells_tutorial = SpellsTutorialPanelController(self)
        except Exception:
            self.spells_tutorial = None

        # Provide callbacks

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
                # Rebuild preview providers in case this change toggles preview behavior
                try:
                    self.preview_manager.rebuild(self.model.spells)
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
            # Rebuild previews so picker reflects particle setting or params
            try:
                self.preview_manager.rebuild(self.model.spells)
            except Exception:
                pass

        self.spells_properties_controller.get_assets_anchor_rect = lambda: get_assets_anchor_rect(self)
        self.spells_properties_controller.on_asset_changed = _on_asset_changed
        self.spells_properties_controller.on_after_commit_edit = _on_after_commit_edit

        # Frame id used to ensure previews update only once per frame across all views
        self._render_frame_id: int = 0

        # Provide a left-anchor provider so the picker grid sits to the right of Add/Remove panel
        try:
            self.view.get_picker_left_anchor_x = lambda: picker_left_anchor_x(self)
        except Exception:
            pass

        # Initialize particle previews for spells with vfx.preview == 'particles'
        try:
            self.preview_manager.rebuild(self.model.spells)
        except Exception:
            pass

    def handle_event(self, event: pygame.event.Event) -> None:
        # Route to toolbar first, then add/remove, only if editor visible
        if self.model.visible:
            # Tutorial overlay consumes clicks inside its panel and ESC while active
            try:
                if getattr(self, 'spells_tutorial', None) is not None and self.spells_tutorial.is_active():
                    if self.spells_tutorial.handle_event(event):
                        return
            except Exception:
                pass
            if self.spells_toolbar_controller.handle_event(event):
                return
            if self.spells_add_remove_controller.handle_event(event):
                return
            # Then properties panel
            if self.spells_properties_controller.handle_event(event):
                return
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        # Advance frame id once per controller draw call
        self._render_frame_id += 1
        if LOG_SPELLS_PREVIEW_DEBUG and logger.isEnabledFor(logging.DEBUG):
            now_ms = pygame.time.get_ticks()
            if now_ms - getattr(self, "_last_frameid_log_ts", 0) >= 1000:
                try:
                    logger.debug("[SpellsEditor] frame_id=%d", self._render_frame_id)
                except Exception:
                    pass
                try:
                    self._last_frameid_log_ts = now_ms
                except Exception:
                    pass
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
                # Anchor properties panel via utility
                try:
                    set_properties_anchor(self)
                except Exception:
                    pass

                self.spells_properties_controller.draw(screen, title_rect=title_rect)

        # Blink yellow border around the Spells Picker panel while in add or remove modes
        # to guide user action (duplicate-on-selection or delete-on-click).
        try:
            active_tool = getattr(self.spells_add_remove_model, 'active_tool', None)
            if active_tool in ('add_spell', 'remove_spell') and getattr(self.model, 'picker_visible', False):
                panel_rect = getattr(self.model, 'panel_rect', None)
                if panel_rect:
                    now = pygame.time.get_ticks()
                    if (now // 500) % 2 == 0:
                        pygame.draw.rect(screen, (255, 255, 0), panel_rect.inflate(6, 6), 3)
        except Exception:
            pass

        # Render tutorial overlay on top
        try:
            if getattr(self, 'spells_tutorial', None) is not None:
                self.spells_tutorial.render(screen)
        except Exception:
            pass

    def reload_sprites_from_spells(self) -> None:
        """Reload sprite surfaces for spells and sync model/view assets via service."""
        _svc_reload_sprites(self)

    def _commit_edit(self) -> None:
        """Commit current edit via persistence service."""
        _svc_commit_edit(self)

