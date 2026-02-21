try:
    from roguelike_game.managers.core.render.pipeline_helpers import should_render_hud_widget, render_game_clock
except Exception:
    def should_render_hud_widget(widget_id, manager, state, menu):  # type: ignore
        return False
    def render_game_clock(manager, screen):  # type: ignore
        return None
"""Minimal HUD Orchestrator skeleton.

Provides safe update/render/event hooks so the render pipeline can call it
without breaking when the Action Grid and other widgets are still WIP.
"""
from __future__ import annotations

from typing import Any, Optional
import pygame
try:
    from roguelike_game.config.input_config import InputConfig
except Exception:
    InputConfig = None  # type: ignore
try:
    from roguelike_ui.hud.input_profiles import InputProfileProvider
except Exception:
    InputProfileProvider = None  # type: ignore

try:
    from roguelike_ui.hud.action_grid.action_grid_model import ActionGridModel
    from roguelike_ui.hud.action_grid.action_grid_view import ActionGridView
    from roguelike_ui.hud.action_grid.action_grid_controller import ActionGridController
    from roguelike_ui.hud.action_grid.action_grid_events import ActionGridEvents
except Exception:
    ActionGridModel = None  # type: ignore
    ActionGridView = None  # type: ignore
    ActionGridController = None  # type: ignore
    ActionGridEvents = None  # type: ignore


class HudOrchestrator:
    """Coordinates HUD widgets (Action Grid, bars, minimap helpers, etc.).

    This initial version is a safe no-op: methods do not fail if dependencies
    are missing. It enables incremental integration into the pipeline.
    """

    def __init__(
        self,
        *,
        input_config: Optional[Any] = None,
        profiles: Optional[Any] = None,
        minimap: Optional[Any] = None,
        systems: Optional[Any] = None,
    ) -> None:
        # Dependencies (lazy fallbacks)
        self.input_config = input_config or (InputConfig() if InputConfig is not None else None)
        self.profiles = profiles or (InputProfileProvider() if InputProfileProvider is not None else None)
        self.minimap = minimap
        self.systems = systems or {}
        # Placeholders for future widgets
        self._action_grid = None
        # Action Grid MVC wiring (if available)
        try:
            if ActionGridModel and ActionGridView and ActionGridController and ActionGridEvents:
                self._ag_model = ActionGridModel()
                self._ag_model.rows = getattr(self._ag_model, 'rows', 3)
                self._ag_model.cols = getattr(self._ag_model, 'cols', 10)
                self._ag_view = ActionGridView()
                self._ag_ctrl = ActionGridController(input_config=self.input_config)
                self._ag_events = ActionGridEvents()
                self._action_grid = True  # sentinel: grid is wired
        except Exception:
            self._action_grid = None

    def update(self, world: Optional[Any] = None, screen: Optional[pygame.Surface] = None, camera: Optional[Any] = None) -> None:
        """Update HUD state: sync Action Grid items and bindings."""
        try:
            if self._action_grid:
                # Populate actions from current mode if model is empty
                if getattr(self, '_ag_model', None) is not None:
                    if not self._ag_model.items and self.profiles is not None:
                        mode = "gameplay"
                        try:
                            mode = self.profiles.get_mode(world, getattr(world, 'state', None))
                        except Exception:
                            pass
                        try:
                            self._ag_model.items = self.profiles.get_actions_for_mode(mode)
                        except Exception:
                            self._ag_model.items = []
                    # Sync bindings/pressed states (placeholder for now)
                    if getattr(self, '_ag_ctrl', None) is not None:
                        self._ag_ctrl.sync_bindings(self._ag_model)
        except Exception:
            # Never let HUD update break the game loop
            pass

    def render(self, screen: pygame.Surface) -> None:
        """Render HUD widgets on the UI layer. Safe to call at any time."""
        try:
            if self._action_grid and getattr(self, '_ag_view', None) is not None:
                self._ag_view.render(
                    screen,
                    self._ag_model,
                    get_binding_label=(self._ag_ctrl.get_binding_label if getattr(self, '_ag_ctrl', None) else (lambda _a: "")),
                )
        except Exception:
            # HUD is optional; keep render robust
            pass

    # --- Centralized helpers ---
    def render_minimap(self, manager, screen, state=None, menu=None) -> None:
        try:
            if should_render_hud_widget('minimap', manager, state, menu):
                manager._render_minimap(screen)
        except Exception:
            pass

    def render_clock(self, manager, screen, state=None, menu=None) -> None:
        try:
            if should_render_hud_widget('clock', manager, state, menu):
                render_game_clock(manager, screen)
        except Exception:
            pass

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Route UI events to widgets. Returns True if the event was consumed."""
        try:
            if self._action_grid and getattr(self, '_ag_events', None) is not None:
                return bool(self._ag_events.handle_event(event, self._ag_model))
        except Exception:
            pass
        return False
