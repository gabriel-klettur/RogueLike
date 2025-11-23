from roguelike_ui.widgets.toolbar_panel import ToolbarView

# Ordered list of tools for the Map toolbar
TOOLS = [
    "view_layers",
    "add_zone",
    "delete_zone",
    "paint_tiles",
    "clear_colliders",
    "paint_colliders",
    "debug_coords",   # Toggle overlay de depuración de coords
    "map_tutorial",
]


class MapToolBarPanelView:
    """
    Wrapper view for the Map toolbar that reuses the shared ToolbarView widget.
    Lives under map_tool_bar_panel to centralize toolbar responsibilities.
    """
    def __init__(self, controller, model):
        self.toolbar = controller
        self.model = model
        # Enforce explicit model injection to avoid implicit controller coupling
        src = self.model
        self.widget = ToolbarView(
            controller=self.toolbar,
            items=TOOLS,
            icons=getattr(src, "icons", {}),
            x=getattr(src, "x", 10),
            y=getattr(src, "y", 100),
            size=getattr(src, "size", 64),
            padding=getattr(src, "padding", 8),
            name="MapToolBar",
        )

    def render(self, screen):
        # Delegate drawing to the shared widget and expose icon rects for handlers/tests
        self.widget.render(screen)
        # Mirror icon rects back to controller for legacy handlers
        try:
            self.toolbar.icon_rects = dict(self.widget.icon_rects)
        except Exception:
            pass
        # And to model if present
        if getattr(self.toolbar, "model", None) is not None:
            self.toolbar.model.icon_rects = dict(self.widget.icon_rects)

    def handle_event(self, event):
        # Delegate drag (right click) to the shared widget
        return self.widget.handle_event(event)

