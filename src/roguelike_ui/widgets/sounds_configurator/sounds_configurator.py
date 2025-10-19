from __future__ import annotations

import pygame
from typing import Any, Callable, Optional

from .model import SoundSettingsModel
from .view import SoundsView

try:
    from roguelike_engine.audio.api import apply_audio_config_now
except Exception:  # pragma: no cover - optional at runtime
    def apply_audio_config_now() -> None:  # type: ignore
        pass


class SoundsConfigurator:
    """Compact orchestrator for the Sound Options UI.

    Delegates state and persistence to `SoundSettingsModel` and draws via
    `SoundsView`. Keeps user interaction (events, selection, hover and scroll).
    """

    def __init__(
        self,
        screen: pygame.Surface,
        audio_config: Any,
        on_change: Optional[Callable[[str, float], None]] = None,
        font: Optional[pygame.font.Font] = None,
        underlay_provider: Optional[Callable[[pygame.Surface], Optional[int]]] = None,
        base_font_size: Optional[int] = None,
    ) -> None:
        self.screen = screen
        self.model = SoundSettingsModel(audio_config=audio_config, on_change=on_change)
        self.view = SoundsView(font=font, base_font_size=base_font_size, underlay_provider=underlay_provider)

        # UI state
        self.selected: int = 0  # 0..13 rows
        self.scroll: float = 0.0
        self.hover_value_idx: Optional[int] = None
        self.hover_mute_idx: Optional[int] = None
        self.hover_reset_idx: Optional[int] = None

    # ------------------- Public API -------------------
    def configure(self) -> None:
        running = True
        clock = pygame.time.Clock()
        while running:
            for event in pygame.event.get():
                if self._handle_event(event):
                    running = False
                    break
            # Render
            self.view.draw(
                screen=self.screen,
                model=self.model,
                selected=self.selected,
                scroll=self.scroll,
                hover_value_idx=self.hover_value_idx,
                hover_mute_idx=self.hover_mute_idx,
                hover_reset_idx=self.hover_reset_idx,
            )
            pygame.display.flip()
            clock.tick(60)

    # ------------------- Events -------------------
    def _handle_event(self, event: pygame.event.Event) -> bool:
        if event.type == pygame.QUIT:
            return True
        if event.type == pygame.KEYDOWN:
            return self._handle_keydown(event)
        if event.type == pygame.MOUSEWHEEL:
            return self._handle_mousewheel(event)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            return self._handle_click(event)
        if event.type == pygame.MOUSEMOTION:
            return self._handle_motion(event)
        return False

    def _handle_keydown(self, event: pygame.event.Event) -> bool:
        key = event.key
        if key in (pygame.K_ESCAPE,):
            return True
        if key == pygame.K_p:
            try:
                apply_audio_config_now()
            except Exception:
                pass
            return False
        if key in (pygame.K_UP, pygame.K_w):
            self.selected = (self.selected - 1) % 14
            self.scroll = self.view.ensure_visible(self.selected, self.scroll)
            return False
        if key in (pygame.K_DOWN, pygame.K_s):
            self.selected = (self.selected + 1) % 14
            self.scroll = self.view.ensure_visible(self.selected, self.scroll)
            return False
        if key == pygame.K_PAGEUP:
            step = max(24, int(self.view.last_viewport_h * 0.9))
            self.scroll = max(0.0, self.scroll - step)
            return False
        if key == pygame.K_PAGEDOWN:
            step = max(24, int(self.view.last_viewport_h * 0.9))
            self.scroll = min(self.view.max_scroll, self.scroll + step)
            return False
        if key == pygame.K_HOME:
            self.scroll = 0.0
            return False
        if key == pygame.K_END:
            self.scroll = self.view.max_scroll
            return False

        if key in (pygame.K_LEFT, pygame.K_a):
            self._nudge_selected(-1)
            return False
        if key in (pygame.K_RIGHT,):
            self._nudge_selected(+1)
            return False

        if key == pygame.K_m and self.selected in (0, 1, 2):
            self.model.toggle_mute(self.selected)
            return False
        if key == pygame.K_d and self.selected in (0, 1, 2):
            self.model.reset_channel(self.selected)
            return False
        if key == pygame.K_r:
            self.model.reset_defaults()
            return False
        if pygame.K_0 <= key <= pygame.K_9 and self.selected in (0, 1, 2):
            self._apply_number_key(key)
            return False
        return False

    def _handle_mousewheel(self, event: pygame.event.Event) -> bool:
        layout = getattr(self.view, "last_layout", {})
        hovered_slider = None
        try:
            mx, my = pygame.mouse.get_pos()
            for i, srect in enumerate(layout.get("slider_rects") or []):
                if srect and srect.collidepoint((mx, my)):
                    hovered_slider = i
                    break
        except Exception:
            hovered_slider = None
        if hovered_slider in (0, 1, 2):
            self.selected = int(hovered_slider)
            self.model.nudge_volume(("music", "ambient", "sfx")[self.selected], event.y * 0.02)
        else:
            step = self.view.renderer.line_height
            self.scroll = max(0.0, min(self.view.max_scroll, self.scroll - event.y * step))
        return False

    def _handle_click(self, event: pygame.event.Event) -> bool:
        layout = getattr(self.view, "last_layout", {})
        sliders = layout.get("slider_rects", [])
        vals_rects = layout.get("value_rects", [])
        mute_rects = layout.get("mute_rects", [])
        reset_rects = layout.get("reset_rects", [])
        mx, my = event.pos
        handled = False
        # 1) Mute buttons
        for i, mrect in enumerate(mute_rects):
            if mrect and mrect.collidepoint((mx, my)):
                if i in (0, 1, 2):
                    self.model.toggle_mute(i)
                handled = True
                break
        # 1b) Reset buttons
        if not handled:
            for i, rrect in enumerate(reset_rects):
                if rrect and rrect.collidepoint((mx, my)):
                    if i in (0, 1, 2):
                        self.model.reset_channel(i)
                    handled = True
                    break
        # 2) Sliders (0..2)
        if not handled:
            for i, srect in enumerate(sliders[:3]):
                if srect and srect.collidepoint((mx, my)):
                    rel = (mx - srect.x) / max(1, srect.w)
                    key = ("music", "ambient", "sfx")[i]
                    self.model.set_volume(key, rel)
                    handled = True
                    break
        # 3) Values (select row)
        if not handled:
            for i, vrect in enumerate(vals_rects):
                if vrect and vrect.collidepoint((mx, my)):
                    self.selected = i
                    handled = True
                    break
        return False

    def _handle_motion(self, event: pygame.event.Event) -> bool:
        layout = getattr(self.view, "last_layout", {})
        self.hover_value_idx = None
        self.hover_mute_idx = None
        self.hover_reset_idx = None
        vals_rects = layout.get("value_rects", [])
        mute_rects = layout.get("mute_rects", [])
        reset_rects = layout.get("reset_rects", [])
        for i, vrect in enumerate(vals_rects):
            if vrect and vrect.collidepoint(event.pos):
                self.hover_value_idx = i
                break
        for i, mrect in enumerate(mute_rects):
            if mrect and mrect.collidepoint(event.pos):
                self.hover_mute_idx = i
                break
        for i, rrect in enumerate(reset_rects):
            if rrect and rrect.collidepoint(event.pos):
                self.hover_reset_idx = i
                break
        return False

    # ------------------- Helpers -------------------
    def _nudge_selected(self, step: int) -> None:
        i = self.selected
        if i in (0, 1, 2):
            key = ("music", "ambient", "sfx")[i]
            self.model.nudge_volume(key, 0.05 * step)
            return
        if i == 3:
            self.model.step_intro_track(step)
            return
        if i == 4:
            self.model.step_ingame_track(step)
            return
        if i == 5:
            self.model.nudge_ambient_min(step)
            return
        if i == 6:
            self.model.nudge_ambient_max(step)
            return
        if i == 7:
            self.model.nudge_duck_db(step)
            return
        if i == 8:
            self.model.nudge_duck_hold(step)
            return
        if i == 9:
            self.model.nudge_duck_release(step)
            return
        if i == 10:
            self.model.step_zone(step)
            return
        if i == 11:
            self.model.step_zone_track(step)
            return
        if i in (12, 13):
            self.model.nudge_zone_ambient("min" if i == 12 else "max", step)
            return

    def _apply_number_key(self, key_code: int) -> None:
        num = key_code - pygame.K_0
        shift = pygame.key.get_mods() & pygame.KMOD_SHIFT
        pct = 1.0 if (num == 0 and shift) else (num / 10.0)
        key = ("music", "ambient", "sfx")[self.selected]
        self.model.set_volume(key, pct)
