from __future__ import annotations

import logging
logger = logging.getLogger(__name__)

from .spawner_toolbar_model import (
    TOOL_SPAWNER_INSTANCES,
    TOOL_SPAWNER_TEMPLATES,
    TOOL_TUTORIAL_SPAWNER,
    TOOL_UNDO,
    TOOL_REDO,
)
from roguelike_editors.spawner.spawner_toolbar.buttons.spawner_instances_button.spawner_instances_button_events import (
    SpawnerInstancesButtonEvents,
)
from roguelike_editors.spawner.spawner_toolbar.buttons.spawner_templates_button.spawner_templates_button_events import (
    SpawnerTemplatesButtonEvents,
)

def _apply_ui_state(controller, ensure_manager: bool = False) -> None:
    try:
        from roguelike_editors.spawner.spawner_toolbar.service.ui_state import (
            apply_ui_state_basic,
            apply_ui_state_ensure_manager,
        )
        if ensure_manager:
            apply_ui_state_ensure_manager(controller)
        else:
            apply_ui_state_basic(controller)
    except Exception:
        pass


class SpawnerToolbarEventHandler:
    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False

        # Clear active tool with ESC
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_ESCAPE:
            if getattr(controller.model, 'active_tool', None) is not None:
                controller.set_active(None)
                return True
            return False

        # Toggle 'spawner_list' with the 'M' key (kept for parity with previous manager toggle)
        if getattr(event, 'type', None) == pygame.KEYDOWN and getattr(event, 'key', None) == pygame.K_m:
            ok = SpawnerInstancesButtonEvents.on_key_toggle(controller)
            logger.debug("[SpawnerToolbar][KEY M] toggled 'spawner_instances'")
            return bool(ok)

        toolbar = getattr(controller.view, 'toolbar', None)
        if toolbar is None:
            return False

        # Panel rect for hit-testing
        try:
            panel_pos = toolbar.panel.pos or (toolbar.x, toolbar.y)
            panel_rect = pygame.Rect(panel_pos, toolbar.panel.surface.get_size())
        except Exception:
            return False

        # Block mouse wheel over toolbar (avoid zoom/scroll elsewhere)
        if getattr(event, 'type', None) == pygame.MOUSEWHEEL:
            mouse_pos = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mouse_pos):
                logger.debug("[SpawnerToolbar][WHEEL] consumed over toolbar: x=%s y=%s", mouse_pos[0], mouse_pos[1])
                return True

        # Handle left click inside toolbar
        if getattr(event, 'type', None) == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = getattr(event, 'pos', None)
            if not pos or not panel_rect.collidepoint(pos):
                return False
            icon_rects = getattr(toolbar, 'icon_rects', {})
            # Compute icon rects for hit-testing if not available yet (pre-render)
            try:
                if not icon_rects or len(icon_rects) != len(getattr(toolbar, 'items', [])):
                    icon_rects = {}
                    items = list(getattr(toolbar, 'items', []) or [])
                    size = int(getattr(toolbar, 'size', 48) or 48)
                    padding = int(getattr(toolbar, 'padding', 8) or 8)
                    edge_padding = int(getattr(toolbar, 'edge_padding', 8) or 8)
                    for idx, tool in enumerate(items):
                        lx = edge_padding
                        ly = edge_padding + idx * (size + padding)
                        rect_local = pygame.Rect(lx, ly, size, size)
                        icon_rects[tool] = rect_local.move(panel_pos)
            except Exception:
                pass
            # Tutorial (toggle Spawner Tutorial panel)
            rect = icon_rects.get(TOOL_TUTORIAL_SPAWNER)
            if rect and rect.collidepoint(pos):
                try:
                    editor = getattr(controller, 'editor_controller', None)
                    tut = getattr(editor, 'tutorial', None)
                    if tut is not None:
                        # Toggle toolbar active state
                        new_state = None if controller.is_active(TOOL_TUTORIAL_SPAWNER) else TOOL_TUTORIAL_SPAWNER
                        controller.set_active(new_state)
                        if new_state is None:
                            tut.deactivate()
                        else:
                            tut.activate()
                except Exception:
                    pass
                return True
            # Undo
            rect = icon_rects.get(TOOL_UNDO)
            if rect and rect.collidepoint(pos):
                controller.on_undo()
                return True
            # Redo
            rect = icon_rects.get(TOOL_REDO)
            if rect and rect.collidepoint(pos):
                controller.on_redo()
                return True
            # Spawner list activate (idempotent)
            rect = icon_rects.get(TOOL_SPAWNER_INSTANCES)
            if rect and rect.collidepoint(pos):
                logger.debug("[SpawnerToolbar][CLICK ICON] 'spawner_instances'")
                return bool(SpawnerInstancesButtonEvents.on_click(controller))
            # Spawner templates (manager) activate (idempotent)
            rect = icon_rects.get(TOOL_SPAWNER_TEMPLATES)
            if rect and rect.collidepoint(pos):
                logger.debug("[SpawnerToolbar][CLICK ICON] 'spawner_templates'")
                return bool(SpawnerTemplatesButtonEvents.on_click(controller))
            # Clicked toolbar background: block
            logger.debug("[SpawnerToolbar][CLICK BG] blocked (no action)")
            return True

        # Consume other clicks inside panel (except RMB for drag handled by DraggablePanel)
        if getattr(event, 'type', None) in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            pos = getattr(event, 'pos', None)
            if pos and panel_rect.collidepoint(pos):
                if getattr(event, 'button', None) == 3:
                    # allow drag handling by ToolbarView's panel
                    return False
                logger.debug("[SpawnerToolbar][CLICK OTHER] consumed event=%s button=%s", event.type, getattr(event, 'button', None))
                return True

        return False


__all__ = ["SpawnerToolbarEventHandler"]
