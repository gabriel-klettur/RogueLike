from __future__ import annotations

from typing import Optional, Tuple


class SpawnerInstanceToolbarView:
    def __init__(self) -> None:
        self.last_rect = None
        self.toolbar = None  # underlying ToolbarView instance
        self.icons = None
        self.anchor = (20, 60)
        self.size = 64
        self.padding = 8
        # Keep last model for active/blink state queries from ToolbarView
        self._model_ref = None
        # Dropdown state (rendered by this view)
        self.dropdown_rect = None
        self.dropdown_item_rects = []  # list[tuple[str, pygame.Rect]]

    def _build_icons(self, model) -> dict:
        try:
            import pygame  # type: ignore
            from roguelike_ui.widgets.icon_cache import IconCache
        except ImportError:
            return {}
        icon_size = (max(8, self.size - 8), max(8, self.size - 8))
        # Base placeholder
        base = IconCache.get_icon('assets/ui/generic_icon.png', icon_size)
        if base is None:
            base = pygame.Surface(icon_size, pygame.SRCALPHA)
            base.fill((180, 180, 180, 255))
        mapping = {
            'add_spawner': 'assets/ui/spawner_editor/spawner_add.png',
            'remove_spawner': 'assets/ui/spawner_editor/spawner_remove.png',
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
        except ImportError:
            return
        if anchor is None:
            anchor = self.anchor
        x, y = anchor
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
                name='SpawnerInstanceToolbar',
            )
        else:
            if not getattr(self.toolbar.panel, 'dragging', False) and getattr(self.toolbar.panel, 'pos', None) is None:
                self.toolbar.panel.pos = (x, y)

    def render(self, model, screen, *, anchor: Optional[Tuple[int, int]] = None):
        if not getattr(model, 'visible', True):
            return None
        # Update model ref for is_active/blink queries
        self._model_ref = model
        self.ensure_ready(model, anchor=anchor)
        try:
            import pygame  # type: ignore
        except ImportError:
            return None
        self.toolbar.render(screen)
        panel_pos = self.toolbar.panel.pos or (self.anchor if anchor is None else anchor)
        panel_size = self.toolbar.panel.surface.get_size()
        self.last_rect = pygame.Rect(panel_pos, panel_size)
        # Register blocker so game input is blocked beneath the panel
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.last_rect)
        except (ImportError, AttributeError):
            pass
        # Optional dropdown for Add mode
        self.dropdown_rect = None
        self.dropdown_item_rects = []
        if getattr(model, 'add_mode_active', False) and getattr(model, 'add_templates', None):
            add_rect = None
            try:
                add_rect = self.toolbar.icon_rects.get('add_spawner')
            except AttributeError:
                add_rect = None
            if add_rect is None:
                add_rect = self.last_rect
            # Layout
            item_h = 22
            max_w = 220
            pad = 6
            x = add_rect.right + 8
            y = add_rect.top
            items = list(getattr(model, 'add_templates', []) or [])
            height = len(items) * item_h + 2 * pad
            width = max_w
            self.dropdown_rect = pygame.Rect(x, y, width, height)
            # Draw background
            bg = pygame.Surface((width, height), pygame.SRCALPHA)
            bg.fill((0, 0, 0, 200))
            screen.blit(bg, (x, y))
            # Draw items
            try:
                font = pygame.font.Font(None, 18)
            except pygame.error:
                font = None
            for idx, tpl in enumerate(items):
                item_rect = pygame.Rect(x + pad, y + pad + idx * item_h, width - 2 * pad, item_h)
                # hover highlight
                if item_rect.collidepoint(pygame.mouse.get_pos()):
                    hover_surf = pygame.Surface(item_rect.size, pygame.SRCALPHA)
                    hover_surf.fill((255, 255, 0, 40))
                    screen.blit(hover_surf, item_rect.topleft)
                if font:
                    txt = font.render(str(tpl), True, (230, 230, 230))
                    screen.blit(txt, (item_rect.x + 6, item_rect.y + 2))
                self.dropdown_item_rects.append((str(tpl), item_rect))
            # Border
            pygame.draw.rect(screen, (255, 255, 255), self.dropdown_rect, 1)
            # Block interactions under dropdown too
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.dropdown_rect)
            except (ImportError, AttributeError):
                pass
        return self.last_rect

    # ToolbarView expects controller to expose this
    def is_active(self, tool: str) -> bool:
        # Active for remove/add modes
        if tool == 'remove_spawner':
            # The actual flag lives in the editor controller model; it is forwarded into this model
            return bool(getattr(self._model_ref, 'remove_mode_active', False))
        if tool == 'add_spawner':
            return bool(getattr(self._model_ref, 'add_mode_active', False))
        return False

    # Optional: blinking support for active tools
    def blink_active(self, tool: str) -> bool:
        if tool == 'remove_spawner':
            return bool(getattr(self._model_ref, 'remove_mode_active', False))
        if tool == 'add_spawner':
            return bool(getattr(self._model_ref, 'add_mode_active', False))
        return False


__all__ = ['SpawnerInstanceToolbarView']
