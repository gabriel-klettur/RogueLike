from __future__ import annotations


class FsmGraphToolbarView:
    def render_into(self, surface, model, *, screen_origin, width, active_tool=None):
        try:
            import pygame  # type: ignore
            from roguelike_ui.widgets.icon_cache import IconCache
        except Exception:
            return 0
        x0, y0 = screen_origin
        w = int(width)
        tb_h = int(getattr(model, 'height', 48))
        pad = int(getattr(model, 'padding', 8))
        size = int(getattr(model, 'button_size', 40))
        icon_size = (max(8, size - 6), max(8, size - 6))

        # Background bar
        pygame.draw.rect(surface, (22, 22, 26), pygame.Rect(0, 0, w, tb_h))
        pygame.draw.line(surface, (60, 60, 68), (0, tb_h - 1), (w, tb_h - 1), 1)

        # Icons
        base = IconCache.get_icon("assets/ui/generic_icon.png", icon_size)
        if base is None:
            base = pygame.Surface(icon_size, pygame.SRCALPHA)
            base.fill((180, 180, 180, 255))
        gp_icon_map = {
            'select': 'assets/ui/fsm_editor/graph_panel/select_node.png',
            'add_node': 'assets/ui/fsm_editor/graph_panel/add_node.png',
            'clone_node': 'assets/ui/fsm_editor/graph_panel/clone_node.png',
            'connect': 'assets/ui/fsm_editor/graph_panel/connect_node.png',
            'disconnect': 'assets/ui/fsm_editor/graph_panel/disconnect_node.png',
            'delete': 'assets/ui/fsm_editor/graph_panel/delete_node.png',
            'mark_ini': 'assets/ui/fsm_editor/graph_panel/start_node.png',
            'mark_end': 'assets/ui/fsm_editor/graph_panel/end_node.png',
            'zoom_in': 'assets/ui/fsm_editor/graph_panel/zoom_in.png',
            'zoom_out': 'assets/ui/fsm_editor/graph_panel/zoom_out.png',
        }

        y_cursor = (tb_h - size) // 2
        x_cursor = pad
        model.rects_abs = {}
        for tool in list(getattr(model, 'buttons', [])):
            rect = pygame.Rect(x_cursor, y_cursor, size, size)
            # Button background
            pygame.draw.rect(surface, (32, 34, 40), rect, border_radius=4)
            # Border highlight
            if tool == active_tool:
                pygame.draw.rect(surface, (90, 170, 255), rect, 2, border_radius=4)
            else:
                pygame.draw.rect(surface, (75, 75, 85), rect, 1, border_radius=4)
            # Icon
            icon = None
            sp = gp_icon_map.get(tool)
            if sp:
                icon = IconCache.get_icon(sp, icon_size)
            if icon is None:
                icon = base
            ir = icon.get_rect(center=rect.center)
            try:
                font = pygame.font.SysFont(None, 16)
                labels = {'zoom_in': '+', 'zoom_out': '-'}
                if sp is None and tool in labels:
                    lbl = font.render(labels[tool], True, (40, 40, 40))
                    pygame.draw.rect(surface, (230, 230, 230), rect.inflate(-6, -6), 1, border_radius=3)
                    lrr = lbl.get_rect(center=rect.center)
                    surface.blit(lbl, lrr)
                else:
                    surface.blit(icon, ir)
            except Exception:
                surface.blit(icon, ir)

            # Save absolute rect
            model.rects_abs[tool] = pygame.Rect(x0 + rect.x, y0 + rect.y, rect.w, rect.h)
            x_cursor += size + pad

        return tb_h


__all__ = ["FsmGraphToolbarView"]