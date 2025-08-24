from __future__ import annotations


class FsmToolbarView:
    def __init__(self) -> None:
        self.last_rect = None  # layout rect returned after render
        self.toolbar = None    # internal ToolbarView instance
        self.icons = None      # cached icons dict per tool
        self._last_model = None
        self.anchor = (20, 60)
        self.size = 64
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
            base = IconCache.get_icon(self.icon_path, icon_size)
            if base is None:
                base = pygame.Surface(icon_size, pygame.SRCALPHA)
                base.fill((180, 180, 180, 255))
            # Build per-tool icons, assign specific assets when available
            specific_map = {
                'sets_list': 'assets/ui/fsm_editor/tool_panel/sets_list.png',
                'sets_entities_assignment': 'assets/ui/fsm_editor/tool_panel/set_assigment_entities.png',
                'sets_animation_assignment': 'assets/ui/fsm_editor/tool_panel/set_assigment_animations.png',
                'set_properties': 'assets/ui/fsm_editor/tool_panel/set_properties.png',
                'undo': 'assets/ui/undo.png',
                'redo': 'assets/ui/redo.png',
            }
            icons = {}
            for tool in model.buttons:
                surf = base.copy()
                # Try specific icon file first
                specific_path = specific_map.get(tool)
                specific_icon = IconCache.get_icon(specific_path, icon_size) if specific_path else None
                if specific_icon is not None:
                    surf = specific_icon
                elif tool == 'sets_list':
                    # Fallback for sets_list: legacy dedicated options, then overlay 'S'
                    specific = (IconCache.get_icon('assets/ui/fsm_sets.png', icon_size)
                                or IconCache.get_icon('assets/ui/icons/fsm_sets.png', icon_size))
                    if specific is not None:
                        surf = specific
                    else:
                        try:
                            font = pygame.font.SysFont(None, max(12, int(icon_size[1] * 0.8)))
                            label = font.render('S', True, (40, 40, 40))
                            # simple contrast ring
                            pygame.draw.rect(surf, (230, 230, 230), surf.get_rect(), 1)
                            # center the label
                            lr = label.get_rect(center=(icon_size[0] // 2, icon_size[1] // 2))
                            surf.blit(label, lr)
                        except Exception:
                            pass
                icons[tool] = surf
            self.icons = icons
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
        # Visual enhancements for 'sets_list' button: active border + count badge
        try:
            icon_rects = getattr(self.toolbar, 'icon_rects', {}) or {}
            sets_rect = icon_rects.get('sets_list')
            if sets_rect is not None:
                # Active highlight
                if getattr(model, 'active_tool', None) == 'sets_list':
                    pygame.draw.rect(screen, (80, 160, 240), sets_rect.inflate(4, 4), 2)
                # Badge with number of sets loaded
                count = 0
                # Prefer runtime bridge helper when available
                try:
                    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set_ids as _get_set_ids
                except Exception:
                    _get_set_ids = None
                if _get_set_ids is not None:
                    try:
                        count = len(list(_get_set_ids() or []))
                    except Exception:
                        count = 0
                # Fallback to snapshot if helper is missing or returned nothing
                if count == 0:
                    try:
                        from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot
                        snap = get_snapshot()
                        count = len(snap.get('sets', []))
                    except Exception:
                        pass
                if count > 0:
                    badge_r = 8
                    cx = sets_rect.right - 2
                    cy = sets_rect.top + 2
                    pygame.draw.circle(screen, (240, 90, 60), (cx, cy), badge_r)
                    pygame.draw.circle(screen, (255, 255, 255), (cx, cy), badge_r, 1)
                    try:
                        font = pygame.font.SysFont(None, 14)
                        txt = font.render(str(min(99, count)), True, (255, 255, 255))
                        tr = txt.get_rect(center=(cx, cy))
                        screen.blit(txt, tr)
                    except Exception:
                        pass
                # Hover highlight and tooltip
                try:
                    mx, my = pygame.mouse.get_pos()
                    if sets_rect.collidepoint((mx, my)):
                        # subtle hover ring
                        pygame.draw.rect(screen, (200, 200, 200), sets_rect.inflate(2, 2), 1)
                        # tooltip bubble
                        tip = "FSM Sets List"
                        font = pygame.font.SysFont(None, 18)
                        txt = font.render(tip, True, (240, 240, 240))
                        tw, th = txt.get_size()
                        pad = 6
                        bx = sets_rect.right + 8
                        by = max(sets_rect.top - 4, 4)
                        bw = tw + pad * 2
                        bh = th + pad * 2
                        # keep inside screen if possible
                        sw, sh = screen.get_size()
                        if bx + bw + 4 > sw:
                            bx = sets_rect.left - 8 - bw
                        if by + bh + 4 > sh:
                            by = sh - bh - 4
                        bg = pygame.Surface((bw, bh), pygame.SRCALPHA)
                        bg.fill((20, 20, 20, 210))
                        pygame.draw.rect(bg, (100, 100, 100), bg.get_rect(), 1)
                        screen.blit(bg, (bx, by))
                        screen.blit(txt, (bx + pad, by + pad))
                except Exception:
                    pass
        except Exception:
            pass
        # Register blocker so gameplay input under toolbar is suppressed
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.last_rect)
        except Exception:
            pass
        return self.last_rect

    # ToolbarView expects its controller to provide is_active(tool)
    def is_active(self, tool: str) -> bool:
        model = self._last_model
        if model is None:
            return False
        return getattr(model, "active_tool", None) == tool


__all__ = ["FsmToolbarView"]
