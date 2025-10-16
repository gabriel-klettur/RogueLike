from __future__ import annotations

import logging
import time
from pathlib import Path
from typing import Optional

import pygame

from roguelike_engine.audio.config import load_audio_catalog  # kept for compat if needed
from roguelike_engine.world.models import WorldSnapshot  # kept for compat if needed
from roguelike_game.managers.map import MapManager  # kept for compat if needed
from roguelike_game.ecs.components.experience_component import ExperienceComponent  # compat

from roguelike_game.managers.menu.menu_handler import MenuHandler
from roguelike_ui.widgets.menu_renderer.menu_renderer import MenuRenderer
from roguelike_ui.widgets.menu_configurator.controller import MenuConfigurator
from roguelike_ui.widgets.options_configurator import OptionsConfigurator

from .subsystems.background import BackgroundManager
from .subsystems.music import MusicManager
from .subsystems.logo import LogoManager
from .subsystems.press_start import PressStartManager
from .subsystems.saves import SaveListManager
from .subsystems.actions import MenuActions

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)


class MenuManager:
    """Orquesta la lógica, entrada y renderizado del menú (refactorizada).

    - Delegación en subsistemas: fondo/carrusel, música, logo, press-to-start, lista de partidas, acciones.
    - Mantiene la API pública esperada por el resto del juego.
    - Limita este archivo a coordinación y enrutamiento (legible y escalable).
    """

    # ---- construcción ----
    def __init__(
        self,
        game,
        state,
        screen: pygame.Surface,
        input_config,
        *,
        audio_config=None,
        audio_manager=None,
        audio_bus=None,
        font_size: int = 36,
        background_path: Optional[str] = None,
    ) -> None:
        # referencias
        self.game = game
        self.state = state
        self.screen = screen
        self.input_config = input_config
        self.audio_config = audio_config
        self.audio_manager = audio_manager
        self.audio_bus = audio_bus

        # flags de estado
        self.show_menu: bool = False
        self.mode: str = "pause"  # start | pause | load_list
        self.prev_mode: str = "start"

        # renderer y configuradores
        self.renderer = MenuRenderer(font_size)
        self.configurator = MenuConfigurator(
            input_config,
            screen,
            self.renderer.font,
            underlay_provider=self._underlay_for_options,
            base_font_size=self.renderer.font_size,
        )
        try:
            self.options_configurator = OptionsConfigurator(
                screen=screen,
                font=self.renderer.font,
                input_configurator=self.configurator,
                audio_config=self.audio_config,
                on_audio_change=self._on_audio_change,
                underlay_provider=self._underlay_for_options,
                base_font_size=self.renderer.font_size,
            )
        except Exception:
            self.options_configurator = None
        self.handler = MenuHandler(state, input_config, self.configurator, options_configurator=self.options_configurator)

        # subsistemas
        self.background = BackgroundManager()
        self.logo = LogoManager()
        self.music = MusicManager(audio_manager=audio_manager, audio_bus=audio_bus)
        self.press = PressStartManager()
        self.saves = SaveListManager(game, self.renderer, screen)
        self.actions = MenuActions(game)

        # compat: exponer music manager a SaveListManager vía game para su propio stop
        try:
            setattr(self.game, "_menu_music_mgr", self.music)
        except Exception:
            pass

        # compat: inicializaciones previas
        if background_path:
            self.background.set_background(background_path)

        # valores por defecto desde audio_config
        try:
            if self.audio_config is not None:
                self.music._music_volume = float(self.audio_config.get("music"))
        except Exception:
            pass

        # startup FX (atributos públicos compatibles con stages.py que el usuario puede setear)
        self._startup_flash_enabled: bool = True
        self._startup_flash_trigger: str = "time"
        self._startup_flash_at_s: float = 6.0
        self._startup_enable_cycle_after_flash: bool = True
        self._startup_flash_duration_s: float = 0.25
        self._startup_flash_ease: str = "linear"
        self._startup_flash_color_rgba = (255, 255, 255, 255)
        self._startup_fade_in_ms: int = 300
        self._music_already_playing_externally: bool = False

    # ---- helpers de forwarding de configuración ----
    def _apply_startup_fx_to_subsystems(self) -> None:
        # background
        self.background._startup_flash_enabled = bool(self._startup_flash_enabled)
        self.background._startup_flash_trigger = str(self._startup_flash_trigger)
        self.background._startup_flash_at_s = float(self._startup_flash_at_s)
        self.background._startup_enable_cycle_after_flash = bool(self._startup_enable_cycle_after_flash)
        self.background._startup_flash_duration_s = float(self._startup_flash_duration_s)
        self.background._startup_flash_ease = str(self._startup_flash_ease)
        self.background._startup_flash_color_rgba = tuple(self._startup_flash_color_rgba)
        # music
        self.music.startup_fade_in_ms = int(self._startup_fade_in_ms)
        self.music.external_already_playing = bool(self._music_already_playing_externally)

    # ---- API públicas equivalentes ----
    def set_mode(self, mode: str) -> None:
        if mode not in ("start", "pause", "load_list"):
            logger.warning("Modo de menú desconocido: %s", mode)
            return
        self.mode = mode
        self.handler.selected = 0

    # backgrounds
    def set_background(self, path: Optional[str], *, scale_mode: Optional[str] = None) -> None:
        self.background.set_background(path, scale_mode=scale_mode)

    def set_backgrounds(
        self,
        paths: list[str],
        interval_s: float = 2.0,
        transition_s: float = 0.6,
        slide_px: int = 24,
        scale_mode: str = "cover",
    ) -> None:
        self.background.set_backgrounds(paths, interval_s, transition_s, slide_px, scale_mode)

    # music
    def set_music(self, path: Optional[str], *, loop: bool = True, volume: float = 0.6) -> None:
        self.music.set_music(path, loop=loop, volume=volume)

    def stop_music(self, fade_ms: Optional[int] = None) -> None:
        self.music.stop_music(fade_ms)

    def _on_audio_change(self, kind: str, value: float) -> None:
        self.music.on_audio_change(kind, value)

    # logo
    def set_logo(
        self,
        path: Optional[str],
        *,
        max_width_ratio: float = 0.6,
        max_height_ratio: float = 0.22,
        gap_px: int = 16,
        initial_scale: float = 0.5,
        top_ratio: float = 0.08,
    ) -> None:
        self.logo.set_logo(
            path,
            max_width_ratio=max_width_ratio,
            max_height_ratio=max_height_ratio,
            gap_px=gap_px,
            initial_scale=initial_scale,
            top_ratio=top_ratio,
        )

    # press to start
    def enable_press_to_start(self, text: Optional[str] = None, blink_interval_s: float = 0.85) -> None:
        self.press.enable(text, blink_interval_s)

    def disable_press_to_start(self) -> None:
        self.press.disable()

    # ---- entrada ----
    def handle_input(self, event):
        # gate de press-to-start
        if self.show_menu and self.mode == "start" and self.press.active:
            joy_types = []
            try:
                if hasattr(pygame, "JOYBUTTONDOWN"):
                    joy_types.append(pygame.JOYBUTTONDOWN)
                if hasattr(pygame, "JOYHATMOTION"):
                    joy_types.append(pygame.JOYHATMOTION)
                if hasattr(pygame, "CONTROLLERBUTTONDOWN"):
                    joy_types.append(pygame.CONTROLLERBUTTONDOWN)
            except Exception:
                pass
            base_types = (pygame.KEYDOWN, pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL)
            if event.type in (*base_types, *tuple(joy_types)):
                if hasattr(pygame, "JOYHATMOTION") and event.type == getattr(pygame, "JOYHATMOTION", None):
                    if getattr(event, "value", (0, 0)) in ((0, 0), 0):
                        return None
                self.disable_press_to_start()
                return None
            
            if hasattr(pygame, "JOYAXISMOTION") and event.type == getattr(pygame, "JOYAXISMOTION", None):
                try:
                    if abs(float(getattr(event, "value", 0.0))) >= 0.5:
                        self.disable_press_to_start()
                        return None
                except Exception:
                    pass
            return None
        # lista de partidas
        if self.mode == "load_list":
            return self.saves.handle_input(event)
        # delegar al handler
        self.handler.mode = self.mode
        return self.handler.handle_input(event)

    # ---- render ----
    def draw(self, screen: pygame.Surface):
        # aplicar sincronización de configuración de FX/música
        self._apply_startup_fx_to_subsystems()
        # fondo/flash para modos de inicio/lista
        if self.mode in ("start", "load_list"):
            self.background.draw(screen, self.mode, self.game)
        # música acorde al modo
        try:
            self.music.ensure_for_menu(show_menu=self.show_menu, mode=self.mode, game=self.game)
        except Exception:
            pass
        # layout de logo
        logo_layout = self.logo.compute_layout(screen) if self.logo.logo_path else None
        panel_top_min = None
        if logo_layout is not None:
            _, (_, y), bottom = logo_layout
            panel_top_min = bottom + self.logo.gap_px
        # overlay press-to-start si aplica
        if self.mode == "start" and self.press.active:
            return self.press.draw(screen, self.renderer.font, logo_layout)
        # vista de lista de partidas
        if self.mode == "load_list":
            return self.saves.draw(screen, panel_top_min=panel_top_min, logo_layout=logo_layout)
        # menú normal
        self.handler.mode = self.mode
        options = self.handler.get_options()
        selected = self.handler.selected
        overlay_rect = self.renderer.draw(
            screen,
            selected,
            options,
            panel_top_min=panel_top_min if panel_top_min is not None else None,
        )
        # logo por encima del panel
        if logo_layout is not None:
            surf, pos, _ = logo_layout
            screen.blit(surf, pos)
        return overlay_rect

    # ---- ejecución de opción ----
    def execute_menu_option(self, selected, state) -> None:
        if selected == "Continuar":
            self.stop_music(fade_ms=None)
            self.show_menu = False
            return
        if selected == "Guardar partida":
            self.actions.save_game()
        elif selected in ("Nuevo juego", "Nueva Partida"):
            self.show_menu = False
            self.mode = "pause"
            self.actions.open_class_selector()
        elif selected == "Cargar juego":
            self._enter_load_list()
        elif selected == "Opciones":
            try:
                pygame.event.clear([pygame.KEYDOWN, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP])
            except Exception:
                pygame.event.clear()
            if getattr(self, "options_configurator", None) is not None:
                self.options_configurator.configure()
            else:
                self.configurator.configure()
        else:
            self.handler.execute_option(selected)

    # ---- flujo de load list ----
    def _enter_load_list(self) -> None:
        self.saves.enter()
        self.prev_mode = self.mode
        self.set_mode("load_list")

    # ---- underlay para configuradores ----
    def _underlay_for_options(self, screen: pygame.Surface) -> Optional[int]:
        try:
            if self.mode in ("start", "load_list"):
                # fondo y flashes
                self.background.draw(screen, self.mode, self.game)
                # flash/press y cálculo de panel
                logo_layout = self.logo.compute_layout(screen) if self.logo.logo_path else None
                if logo_layout is not None:
                    _, (_, y), bottom = logo_layout
                    panel_top_min = bottom + self.logo.gap_px
                else:
                    panel_top_min = None
                # dibujar logo encima
                if logo_layout is not None:
                    surf, pos, _ = logo_layout
                    screen.blit(surf, pos)
                return panel_top_min
        except Exception:
            pass
        return None

    # ---- compat: finalize new game after class selection ----
    def finalize_new_game_with_class(self, class_key: str) -> None:
        self.actions.finalize_new_game_with_class(class_key)
