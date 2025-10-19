from __future__ import annotations

from typing import Optional, Tuple


class SpawnerToolbarView:
    def __init__(self) -> None:
        self.last_rect = None  # layout rect returned after render
        self.toolbar = None    # internal ToolbarView instance
        self.icons = None      # cached icons dict per tool
        self._last_model = None
        self.anchor = (20, 60)
        self.size = 64
        self.padding = 8

    def _build_icons(self, model) -> dict:
        try:
            import pygame  # type: ignore
            from roguelike_ui.widgets.icon_cache import IconCache
        except Exception:
            return {}
        icon_size = (max(8, self.size - 8), max(8, self.size - 8))
        # Base placeholder
        base = IconCache.get_icon('assets/ui/generic_icon.png', icon_size)
        if base is None:
            base = pygame.Surface(icon_size, pygame.SRCALPHA)
            base.fill((180, 180, 180, 255))
        mapping = {
            'undo': 'assets/ui/undo.png',
            'spawner_instances': 'assets/ui/spawner_editor/spawner_list.png',
            'spawner_templates': 'assets/ui/spawner_editor/spawner_manager.png',
            'tutorial_spawner': 'assets/ui/tutorials_button.png',
            'redo': 'assets/ui/redo.png',
        }
        icons = {}
        for tool in getattr(model, 'buttons', []) or []:
            surf = mapping.get(tool)
            icon = None
            if surf:
                icon = IconCache.get_icon(surf, icon_size)
            icons[tool] = icon or base.copy()
        return icons

    def ensure_ready(self, model, *, anchor: Optional[Tuple[int, int]] = None) -> None:
        try:
            from roguelike_ui.widgets.toolbar_panel import ToolbarView as _ToolbarView
        except Exception:
            return
        if anchor is None:
            anchor = self.anchor
        x, y = anchor
        # Build icons/toolbar once or when button list changes
        needs_rebuild = (
            self.toolbar is None or self.icons is None or getattr(self.toolbar, 'items', None) != getattr(model, 'buttons', None)
        )
        if needs_rebuild:
            self.icons = self._build_icons(model)
            self.toolbar = _ToolbarView(
                controller=self,
                items=model.buttons,
                icons=self.icons,
                x=x,
                y=y,
                size=self.size,
                padding=self.padding,
                name='SpawnerToolbar',
            )
        else:
            # If not dragged yet and no pos, set initial anchor
            if not getattr(self.toolbar.panel, 'dragging', False) and getattr(self.toolbar.panel, 'pos', None) is None:
                self.toolbar.panel.pos = (x, y)

    def render(self, model, screen, *, anchor: Optional[Tuple[int, int]] = None):
        if not getattr(model, 'visible', True):
            return None
        self._last_model = model
        self.ensure_ready(model, anchor=anchor)
        try:
            import pygame  # type: ignore
        except Exception:
            return None
        self.toolbar.render(screen)
        panel_pos = self.toolbar.panel.pos or (self.anchor if anchor is None else anchor)
        panel_size = self.toolbar.panel.surface.get_size()
        self.last_rect = pygame.Rect(panel_pos, panel_size)
        # Register blocker
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.last_rect)
        except Exception:
            pass
        return self.last_rect

    # ToolbarView expects controller to expose this
    def is_active(self, tool: str) -> bool:
        model = self._last_model
        if model is None:
            return False
        return getattr(model, 'active_tool', None) == tool


__all__ = ['SpawnerToolbarView']
