from __future__ import annotations
import logging

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.connect.view")


class ConnectView:
    def render_overlay(self, *, model, screen, canvas_rect, view):
        try:
            import pygame  # type: ignore
        except Exception:
            return
        # Need a pending source to preview
        src_id = getattr(model, 'connect_source_node_id', None)
        if not src_id:
            return
        # Fetch src node world rect
        nodes = getattr(model, 'nodes', []) or []
        src = None
        for n in nodes:
            if n.get('id') == src_id:
                src = n
                break
        if not src:
            return
        # Transform helpers (world -> local)
        try:
            zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))
        except Exception:
            zoom = 1.0
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
        def WL(wx: float, wy: float):
            return (int(wx * zoom + pan_x), int(wy * zoom + pan_y))
        # Source center in local coords
        sx = float(src.get('x', 0)) + float(src.get('w', 120)) / 2.0
        sy = float(src.get('y', 0)) + float(src.get('h', 60)) / 2.0
        slx, sly = WL(sx, sy)
        # Mouse in local coords (canvas space)
        try:
            mx, my = pygame.mouse.get_pos()
        except Exception:
            return
        if not canvas_rect.collidepoint((mx, my)):
            return
        mlx = mx - int(canvas_rect.left)
        mly = my - int(canvas_rect.top)
        # Draw on the screen at absolute positions
        color = getattr(self, 'preview_color', (255, 230, 120))
        abs_start = (int(canvas_rect.left) + int(slx), int(canvas_rect.top) + int(sly))
        abs_end = (int(canvas_rect.left) + int(mlx), int(canvas_rect.top) + int(mly))
        pygame.draw.line(screen, color, abs_start, abs_end, 2)
        # Arrowhead at mouse
        vx, vy = (abs_end[0] - abs_start[0]), (abs_end[1] - abs_start[1])
        mag = (vx * vx + vy * vy) ** 0.5 or 1.0
        ux, uy = vx / mag, vy / mag
        head_len = int(getattr(self, 'arrow_head_len', 14))
        head_width = int(getattr(self, 'arrow_head_width', 10))
        bx = abs_end[0] - ux * head_len
        by = abs_end[1] - uy * head_len
        px, py = -uy, ux
        hw = head_width / 2.0
        left = (bx + px * hw, by + py * hw)
        right = (bx - px * hw, by - py * hw)
        pygame.draw.polygon(screen, color, [left, right, abs_end])


__all__ = ["ConnectView"]
