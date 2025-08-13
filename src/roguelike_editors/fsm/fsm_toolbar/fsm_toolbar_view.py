from __future__ import annotations


class FsmToolbarView:
    def __init__(self) -> None:
        self.last_rect = None  # layout rect returned after render
        self.toolbar = None    # internal ToolbarView instance
        self.icons = None      # cached icons dict per tool
        self._last_model = None
        self.anchor = (20, 60)
        self.size = 32
        self.padding = 8
        self.icon_path = "assets/ui/generic_icon.png"

    def ensure_ready(self, model, *, anchor=None):
        """Ensure internal ToolbarView is constructed for event handling/rendering."""
        try:
            import pygame  # type: ignore
            from roguelike_ui.widgets.toolbar_panel import ToolbarView as _ToolbarView
            from roguelike_ui.widgets.icon_cache import IconCache
        except Exception:
            return
        if anchor is None:
            anchor = self.anchor
        x, y = anchor
        # Build icons dict once or if buttons changed
        if self.toolbar is None or self.icons is None or getattr(self.toolbar, 'items', None) != model.buttons:
            icon_size = (max(8, self.size - 8), max(8, self.size - 8))
            surf = IconCache.get_icon(self.icon_path, icon_size)
            if surf is None:
                surf = pygame.Surface(icon_size, pygame.SRCALPHA)
                surf.fill((180, 180, 180, 255))
            self.icons = {tool: surf for tool in model.buttons}
            self.toolbar = _ToolbarView(
                controller=self,
                items=model.buttons,
                icons=self.icons,
                x=x,
                y=y,
                size=self.size,
                padding=self.padding,
                name="FSMToolbar",
            )
        else:
            # Keep position if user dragged; otherwise set to anchor on first creation
            if not getattr(self.toolbar.panel, 'dragging', False) and getattr(self.toolbar.panel, 'pos', None) is None:
                self.toolbar.panel.pos = (x, y)

    def render(self, model, screen, *, anchor=None):
        if not getattr(model, "visible", True):
            return None
        # Keep reference for is_active queries from ToolbarView
        self._last_model = model
        # Ensure toolbar exists for rendering
        self.ensure_ready(model, anchor=anchor)
        try:
            import pygame  # type: ignore
        except Exception:
            return None

        # Render and compute panel rect
        self.toolbar.render(screen)
        panel_pos = self.toolbar.panel.pos or (self.anchor if anchor is None else anchor)
        panel_size = self.toolbar.panel.surface.get_size()
        self.last_rect = pygame.Rect(panel_pos, panel_size)
        return self.last_rect

    # ToolbarView expects its controller to provide is_active(tool)
    def is_active(self, tool: str) -> bool:
        model = self._last_model
        if model is None:
            return False
        return getattr(model, "active_tool", None) == tool


__all__ = ["FsmToolbarView"]
