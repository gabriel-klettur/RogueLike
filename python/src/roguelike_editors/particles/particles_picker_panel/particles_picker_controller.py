import pygame
import logging
from typing import Dict

from .particles_picker_model import ParticlesPickerModel
from .particles_picker_view import ParticlesPickerView
from .particles_picker_events import ParticlesPickerEventHandler
from roguelike_game.config.particles_config import PARTICLES
from roguelike_editors.particles.services.preview import (
    build_preview_for_definition,
    resolve_particles_dict_from_definition,
)

logger = logging.getLogger(__name__)


class ParticlesPickerController:
    """Controller for the Particles Picker grid.

    Loads presets from the centralized PARTICLES catalog and builds animated previews.
    """

    def __init__(self, font: pygame.font.Font | None):
        self.model = ParticlesPickerModel()
        self.view = ParticlesPickerView(self.model, font)
        # Event handler receives a reference to this controller for rebuild() and back-refs
        self.events = ParticlesPickerEventHandler(self.model, controller=self)
        self._last_ticks = 0
        self._built = False
        # Back-reference to the parent editor controller (set by ParticlesEditorController)
        self.editor_controller = None

    def set_anchor(self, x: int | None, y: int | None) -> None:
        if x is not None and y is not None:
            self.model.grid_origin = (int(x), int(y))

    def _build_from_catalog(self) -> None:
        # Build items and previews from PARTICLES
        items: Dict[str, dict] = {}
        providers: Dict[str, object] = {}
        for pid, p in PARTICLES.items():
            try:
                # Minimal dict definition expected by preview builder
                defn = {
                    "id": getattr(p, "id", pid),
                    "name": getattr(p, "name", pid),
                    "type": getattr(p, "type", ""),
                    "vfx": getattr(p, "vfx", {}),
                }
                # Resolve kind robustly for grouping
                try:
                    parts, _meta = resolve_particles_dict_from_definition(defn)
                    k = parts.get("kind")
                    if isinstance(k, str) and k:
                        defn["kind"] = k
                except Exception:
                    pass
                items[pid] = defn
                preview_obj = build_preview_for_definition(defn)
                if preview_obj is None:
                    continue

                # Provider expects (size, dt_ms) -> Surface
                def make_provider(obj):
                    def provider(size, dt_ms):
                        return obj.render(size, dt_ms)
                    return provider
                providers[pid] = make_provider(preview_obj)
            except Exception:
                logger.exception("[ParticlesPicker] Failed to build preview for %s", pid)
        self.model.items = items
        self.model.preview_providers = providers
        self._built = True

    def rebuild(self) -> None:
        """Force rebuilding items and previews from the PARTICLES catalog."""
        try:
            # Clear current state
            self.model.items.clear()
            self.model.preview_providers.clear()
        except Exception:
            pass
        self._built = False
        self._build_from_catalog()

    def handle_event(self, event: pygame.event.Event) -> bool:
        return self.events.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self._built:
            self._build_from_catalog()
        now = pygame.time.get_ticks()
        if self._last_ticks == 0:
            dt = 16
        else:
            dt = max(0, min(48, now - self._last_ticks))
        self._last_ticks = now
        self.view.draw(screen, dt_ms=dt)
