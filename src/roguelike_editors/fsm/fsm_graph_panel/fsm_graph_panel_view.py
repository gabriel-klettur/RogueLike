from __future__ import annotations
from roguelike_ui.widgets.text_input import TextInput
from .view import (
    draw_grid,
    draw_nodes,
    draw_edges,
    redraw_hovered_edge,
    draw_edge_handles_and_preview,
    draw_legend,
)


class FsmGraphPanelView:
    def __init__(self) -> None:
        self.canvas_rect = None
        # Last rendered label rects (in local canvas coordinates)
        self.node_label_rects = {}
        self.edge_label_rects = {}
        # Last rendered edge paths (list of local points) for hover proximity checks
        self.edge_paths = {}
        # Last rendered edge endpoints in local (canvas) coordinates: {edge_idx: {"from": (x,y), "to": (x,y)}}
        self.edge_endpoints_local = {}
        # Legend overlay rects (screen-space)
        self.legend_rect = None
        self.legend_button_rect = None
        # Inline text input widget and absolute rect for outside-click checks
        self.text_input: TextInput | None = None
        self.text_input_abs_rect = None
        self._pending_text_edit: tuple[str, bool] | None = None

    # Called by controller when user starts an edit (double-click on label)
    def begin_text_edit(self, initial_text: str, select_all: bool = False) -> None:
        # Defer actual creation/activation to render; store initial text
        self._pending_text_edit = (str(initial_text or ''), bool(select_all))

    def render(self, model, screen, *, anchor=(360, 120), toolbar=None):
        if not getattr(model, "visible", True):
            return None
        try:
            import pygame  # type: ignore
        except Exception:
            return None
        # Canvas placement and size (temporary fixed size)
        x, y = anchor
        w, h = 800, 520
        self.canvas_rect = pygame.Rect(x, y, w, h)

        # Background panel
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        # Use fully opaque background to completely hide any underlying game elements
        # when the FSM Graph Panel is visible.
        surf.fill((15, 15, 18, 255))
        # Reset last label rects for new frame
        self.node_label_rects = {}
        self.edge_label_rects = {}
        self.edge_paths = {}
        self.edge_endpoints_local = {}
        # Reset legend rects
        self.legend_rect = None
        self.legend_button_rect = None

        # Draw top graph toolbar (horizontal) via toolbar submodule
        tb_h = 0
        if toolbar is not None:
            try:
                active_tool = getattr(model, 'active_graph_tool', None)
                # Expect toolbar to be a controller with .view and .model
                tb_h = int(toolbar.view.render_into(surf, toolbar.model, screen_origin=(x, y), width=w, active_tool=active_tool) or 0)
            except Exception:
                tb_h = 0
        # Pan/zoom parameters
        pan_x = float(getattr(model, 'pan_x', 0.0))
        pan_y = float(getattr(model, 'pan_y', 0.0))
        zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))

        # Helper to transform world->local in-canvas
        def W(p):
            return (int(p[0] * zoom + pan_x), int(p[1] * zoom + pan_y))

        # Grid that respects pan/zoom (extracted to view/grid_view.py)
        try:
            draw_grid(surf, w, h, pan_x, pan_y, zoom, top_offset=int(tb_h))
        except Exception:
            pass

        # Edges (beneath nodes)
        try:
            draw_edges(model, surf, W, self)
            # Emphasize hovered edge above others while still beneath nodes
            redraw_hovered_edge(model, surf, self)
        except Exception:
            pass

        # Nodes
        try:
            draw_nodes(model, surf, W, zoom, self)
        except Exception:
            pass
        
        # Overlay: edge handles and drag preview (above nodes, on canvas)
        try:
            draw_edge_handles_and_preview(model, surf, W, self)
        except Exception:
            pass

        # Border of canvas
        pygame.draw.rect(surf, (95, 95, 105), surf.get_rect(), 2)
        # Inline TextInput overlay (draw on top of canvas contents)
        try:
            # Determine if an edit is active
            edit_node = getattr(model, 'editing_node_id', None)
            edit_edge_idx = getattr(model, 'editing_edge_index', None)
            edit_edge_id = getattr(model, 'editing_edge_id', None)
            target_rect_local = None
            if edit_node is not None:
                target_rect_local = self.node_label_rects.get(edit_node)
            else:
                # Edge editing: resolve by ID first, then index fallback
                if isinstance(edit_edge_id, str):
                    target_rect_local = self.edge_label_rects.get(edit_edge_id)
                if target_rect_local is None and edit_edge_idx is not None:
                    try:
                        target_rect_local = self.edge_label_rects.get(int(edit_edge_idx))
                    except Exception:
                        target_rect_local = None
            if target_rect_local is not None:
                # Ensure widget exists and activated
                if self.text_input is None:
                    # Create default font
                    font = pygame.font.SysFont(None, 18)
                    self.text_input = TextInput(font)
                # Update font size to match label size roughly
                # For node labels we used base_font_size (20), for edge labels ~18
                try:
                    base_font_size = 20 if edit_node is not None else 18
                    self.text_input.font = pygame.font.SysFont(None, base_font_size)
                except Exception:
                    pass
                # Activate with pending/init text if needed
                if self._pending_text_edit is not None:
                    init_text, select_all = self._pending_text_edit
                    try:
                        self.text_input.activate(init_text, select_all=select_all)
                    except Exception:
                        pass
                    self._pending_text_edit = None
                # Draw input at local position
                tx = int(target_rect_local.left)
                ty = int(target_rect_local.top)
                # Slightly inset to center within the rect vertically
                self.text_input.draw(surf, tx, ty, color=(255, 255, 255))
                # Compute absolute screen-space rect for controller click-outside checks
                lr = getattr(self.text_input, 'last_rect', None)
                if isinstance(lr, pygame.Rect):
                    self.text_input_abs_rect = pygame.Rect(x + lr.left, y + lr.top, lr.width, lr.height)
        except Exception:
            self.text_input_abs_rect = None
        # Blit to screen
        screen.blit(surf, (x, y))

        # Legend overlay (AFTER blitting canvas so it's on top)
        try:
            draw_legend(model, screen, x, y, w, h, self)
        except Exception:
            # Non-fatal if we can't render legend
            self.legend_rect = None
            self.legend_button_rect = None

        # Register blocker so gameplay input under canvas is suppressed
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.canvas_rect)
        except Exception:
            pass

        return self.canvas_rect

    # Optional overlay for active tool-specific visuals. The controller calls this
    # after the main render. If a tool-specific View is provided and it has a
    # render_overlay() method, delegate to it with canvas_rect and this view.
    def render_active_tool_overlay(self, model, screen, tool_view=None):
        if self.canvas_rect is None:
            return None
        try:
            if tool_view and hasattr(tool_view, 'render_overlay'):
                tool_view.render_overlay(model=model, screen=screen, canvas_rect=self.canvas_rect, view=self)
        except Exception:
            # Non-fatal if tool overlay fails
            pass
        return None


__all__ = ["FsmGraphPanelView"]
