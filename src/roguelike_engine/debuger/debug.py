from roguelike_engine.utils.benchmark import benchmark
import roguelike_engine.config.config as config
from roguelike_engine.debuger.overlay.model import DebugOverlayModel
from roguelike_engine.debuger.overlay.view import DebugOverlayView
from roguelike_engine.debuger.overlay.controller import DebugOverlayController
from roguelike_engine.debuger.overlay.events import handle_event as handle_overlay_event

# Re-export helper draw functions for backward-compatible imports
from roguelike_engine.debuger.helpers import (
    draw_debug_rect,
    draw_debug_mask_outline,
    draw_zone_border,
)


class DebugOverlay:
    def __init__(
        self,
        perf_log: dict[str, list[float]],
        font_name: str = "Consolas",
        font_size: int = 12,
        bg_color: tuple[int, int, int, int] = (0, 0, 0, 180),
        text_color: tuple[int, int, int] = (255, 255, 255),
        value_color: tuple[int, int, int] = (200, 255, 200),
        padding_x: int = 10,
        padding_y: int = 4,
        spacing: int = 4,
        border_colors: dict[str, tuple[int, int, int]] | None = None,
        border_width: int = 5,
        update_interval: float = 0.2,
        scroll_speed: int = 20,
    ):
        self.model = DebugOverlayModel(
            perf_log=perf_log,
            font_name=font_name,
            font_size=font_size,
            bg_color=bg_color,
            text_color=text_color,
            value_color=value_color,
            padding_x=padding_x,
            padding_y=padding_y,
            spacing=spacing,
            border_colors=border_colors or {
                "lobby": (255, 255, 255),
                "dungeon": (0, 255, 0),
                "global": (128, 0, 128),
            },
            border_width=border_width,
            update_interval=update_interval,
            scroll_speed=scroll_speed,
        )
        self.view = DebugOverlayView()
        self.controller = DebugOverlayController(self.model, self.view)

    @property
    def perf_log(self):
        return self.model.perf_log

    # Public accessor for hit-testing in input routing
    @property
    def panel_rect(self):
        return self.model.panel_rect

    def hit_test(self, pos) -> bool:
        return bool(self.model.panel_rect and self.model.panel_rect.collidepoint(pos))

    def handle_event(self, event) -> bool:
        return handle_overlay_event(self.model, self.view, event)

    @benchmark(lambda self: self.perf_log, "3.12. debug.render")
    def render(
        self,
        screen,
        state=None,
        camera=None,
        map_manager=None,
        entities=None,
        extra_lines=None,
        position=(8, 8),
        show_borders=False,
    ):
        self.controller.render(
            screen,
            state=state,
            camera=camera,
            map_manager=map_manager,
            entities=entities,
            extra_lines=extra_lines,
            position=position,
            show_borders=show_borders,
        )


def render_debug_overlay(debug_overlay, screen, state, camera, map_manager, entities, show_borders=False):
    if not config.DEBUG or debug_overlay.perf_log is None:
        return
    debug_overlay.render(
        screen,
        state=state,
        camera=camera,
        map_manager=map_manager,
        entities=entities,
        show_borders=show_borders
    )