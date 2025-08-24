import pygame
from typing import Any, Dict

from .spells_editor_models import SpellsEditorModel
from .spells_editor_events import SpellsEditorEvents
from .spells_editor_view import SpellsEditorView

# Temporary composition: reuse the existing inner controller that already
# orchestrates toolbar, add/remove, picker grid, and properties panel until
# the picker panel is split into its own MVC.
from .spells_picker_panel.spells_editor_controller import SpellEditorController as _InnerSpellEditorController


class SpellsEditorController:
    """Top-level orchestrator for the Spells Editor (MVC).

    - Owns a global model mirroring Items Editor structure.
    - Composes the existing inner SpellEditorController (picker-driven) to keep
      behavior while we complete the refactor. State is synchronized both ways.
    """

    def __init__(self, spells: Dict[str, Any], assets: Dict[str, pygame.Surface], font: pygame.font.Font):
        # Top-level MVC
        self.model = SpellsEditorModel(spells=spells, assets=assets)
        self.events = SpellsEditorEvents()
        self.view = SpellsEditorView()

        # Inner controller currently holds the working implementation
        self.inner = _InnerSpellEditorController(spells, assets, font)
        # Force shared references for spells/assets to avoid divergence
        try:
            self.inner.model.spells = self.model.spells
            self.inner.model.assets = self.model.assets
        except Exception:
            pass

        # Initial visibility mirrors prior behavior (closed by default)
        self.model.visible = getattr(self.inner.model, 'visible', False)
        self.model.picker_visible = getattr(self.inner.model, 'picker_visible', False)

    # --- Sync helpers ---
    def _push_state_to_inner(self) -> None:
        im = self.inner.model
        m = self.model
        im.visible = m.visible
        im.picker_visible = getattr(m, 'picker_visible', getattr(im, 'picker_visible', False))
        im.delete_mode_active = getattr(m, 'delete_mode_active', getattr(im, 'delete_mode_active', False))
        im.selected_id = m.selected_id
        im.hovered_id = m.hovered_id

    def _pull_state_from_inner(self) -> None:
        im = self.inner.model
        m = self.model
        m.visible = getattr(im, 'visible', m.visible)
        m.picker_visible = getattr(im, 'picker_visible', m.picker_visible)
        m.delete_mode_active = getattr(im, 'delete_mode_active', m.delete_mode_active)
        m.selected_id = getattr(im, 'selected_id', m.selected_id)
        m.hovered_id = getattr(im, 'hovered_id', m.hovered_id)

    # --- Main loop ---
    def handle_event(self, event: pygame.event.Event) -> None:
        # Global shortcuts first (e.g., F4 visibility)
        handled = self.events.handle_event(self, event)
        # Push any changed state (e.g., visibility) before delegating
        self._push_state_to_inner()
        if handled:
            # If we toggled visibility here, don't let the inner toggle again
            return
        # Delegate to inner controller which routes to subcontrollers
        self.inner.handle_event(event)
        # Pull back selection/hover/modes
        self._pull_state_from_inner()

    def draw(self, screen: pygame.Surface) -> None:
        # Ensure inner visibility matches top-level
        self._push_state_to_inner()
        # Delegate drawing to inner (title, picker grid, properties, toolbars)
        self.inner.draw(screen)
        # Sync interesting rects
        title_rect = getattr(self.inner.view, 'title_rect', None)
        self.view.title_rect = title_rect
        self.model.title_rect = title_rect
        # Keep selection/hover in sync
        self._pull_state_from_inner()

