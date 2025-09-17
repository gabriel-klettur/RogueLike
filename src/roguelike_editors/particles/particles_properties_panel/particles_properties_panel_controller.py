import pygame
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_editors.particles.services.instances_service import (
    load_particles_instances,
)
from .particles_properties_panel_model import ParticlesPropertiesPanelModel
from .particles_properties_panel_view import ParticlesPropertiesPanelView


class ParticlesPropertiesPanelController:
    """Controller for the particles properties panel.

    Minimal read-only panel that shows information of the selected persisted
    particle instance (id, preset_id, zone, rel_x, rel_y).
    """

    def __init__(self, font: pygame.font.Font | None):
        self.model = ParticlesPropertiesPanelModel()
        self.view = ParticlesPropertiesPanelView(font)

    # ---- API ----
    def hide(self) -> None:
        self.model.visible = False

    def show_for_id(self, entry_id: int | None) -> None:
        if entry_id is None:
            self.hide()
            return
        entry = None
        try:
            for e in load_particles_instances() or []:
                try:
                    if int(e.get("id")) == int(entry_id):
                        entry = e
                        break
                except Exception:
                    continue
        except Exception:
            entry = None
        if not isinstance(entry, dict):
            # Still show with just the id if not found (should be rare)
            self.model.selected_id = int(entry_id)
            self.model.entry = None
            self.model.visible = True
            return
        self.model.selected_id = int(entry_id)
        self.model.entry = dict(entry)
        self.model.visible = True

    def set_anchor_from_editor(self, editor_controller) -> None:
        """Place the panel near the picker if visible, otherwise near the toolbar.
        Fallback below the editor title.
        """
        x = 16
        y = 80
        # Prefer below the picker grid
        try:
            picker = getattr(editor_controller, "particles_picker_controller", None)
            grid_rect = getattr(getattr(picker, "view", None), "model", None)
            grid_rect = getattr(grid_rect, "grid_rect", None)
            if grid_rect is not None:
                x = int(grid_rect.left)
                y = int(grid_rect.bottom + UI_MARGIN)
        except Exception:
            pass
        # Else, place to the right of the Add/Remove panel
        if x == 16 and y == 80:
            try:
                ar_view = getattr(editor_controller, "particles_add_remove_view", None)
                if ar_view is not None:
                    tb_view = getattr(editor_controller, "particles_toolbar_view", None)
                    tb_widget = getattr(tb_view, "widget", None)
                    if tb_widget is not None:
                        panel_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
                        panel_w, _ = tb_widget.panel.surface.get_size()
                        x = int(panel_pos[0] + panel_w + UI_MARGIN)
                        y = int(panel_pos[1] + 64 + UI_MARGIN)
            except Exception:
                pass
        # Fallback: below editor title
        if x == 16 and y == 80:
            try:
                title_rect = getattr(getattr(editor_controller, "view", None), "title_rect", None)
                if title_rect is not None:
                    x = int(title_rect.left)
                    y = int(title_rect.bottom + UI_MARGIN)
            except Exception:
                pass
        self.model.x = x
        self.model.y = y

    # ---- Wiring to editor ----
    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)

    def handle_event(self, event: pygame.event.Event) -> bool:
        # No interactive fields for now
        return False
