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
        """Anchor the panel to the top-right corner and sync picker selection info.

        - Position: top-right of the screen with UI margin.
        - Picker data: copy selected id/def from the picker model when available.
        """
        # 1) Top-right anchor using the game's screen size
        try:
            game = getattr(editor_controller, "game", None)
            screen = getattr(game, "screen", None)
            if screen is not None:
                w = int(getattr(self.model, "width", 260))
                self.model.x = int(screen.get_width() - w - UI_MARGIN)
                self.model.y = int(UI_MARGIN)
        except Exception:
            # Fallback: keep previous position or defaults
            pass

        # 2) Feed picker selection info into the model
        try:
            picker = getattr(editor_controller, "particles_picker_controller", None)
            p_model = getattr(picker, "model", None)
            pid = getattr(p_model, "selected_id", None)
            items = getattr(p_model, "items", {}) if p_model else {}
            self.model.picker_selected_id = pid if isinstance(pid, str) else None
            self.model.picker_selected_def = dict(items.get(pid)) if isinstance(items.get(pid), dict) else None
        except Exception:
            self.model.picker_selected_id = None
            self.model.picker_selected_def = None

    # ---- Wiring to editor ----
    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen, self.model)

    def handle_event(self, event: pygame.event.Event) -> bool:
        # No interactive fields for now
        return False
