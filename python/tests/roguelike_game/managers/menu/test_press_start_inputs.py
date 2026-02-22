"""Tests for press-to-start input handling in MenuManager.

These tests ensure the overlay can be dismissed by keyboard, mouse, and controller inputs.
"""
from __future__ import annotations

import os
import pygame
import pytest

# Use dummy drivers to avoid opening real windows/audio devices in CI
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

from roguelike_game.managers.menu.manager import MenuManager
from roguelike_game.config.input_config import InputConfig
from roguelike_game.managers.core.events.handlers.menu import handle_menu as _handle_menu


@pytest.fixture(scope="module")
def pygame_display():
    pygame.init()
    pygame.display.init()
    screen = pygame.display.set_mode((320, 240))
    yield screen
    try:
        pygame.display.quit()
        pygame.quit()
    except Exception:
        pass


def _new_menu(screen: pygame.Surface) -> MenuManager:
    class _Game:
        pass

    g = _Game()
    g.state = type("State", (), {"mode": "local", "running": True})()
    g.screen = screen
    g.audio_config = None
    g.audio_manager = None
    g.audio_bus = None

    menu = MenuManager(
        g,
        g.state,
        screen,
        InputConfig(),
        audio_config=None,
        audio_manager=None,
        audio_bus=None,
        font_size=14,
    )
    # Configure as start screen with overlay active
    menu.set_mode("start")
    menu.show_menu = True
    menu.enable_press_to_start()
    return menu


def _new_game(screen: pygame.Surface):
    class _Game:
        pass
    g = _Game()
    g.state = type("State", (), {"mode": "local", "running": True})()
    g.screen = screen
    g.audio_config = None
    g.audio_manager = None
    g.audio_bus = None
    g.menu = MenuManager(
        g,
        g.state,
        screen,
        InputConfig(),
        audio_config=None,
        audio_manager=None,
        audio_bus=None,
        font_size=14,
    )
    g.menu.set_mode("start")
    g.menu.show_menu = True
    g.menu.enable_press_to_start()
    return g


def test_keyboard_key_dismisses_overlay(pygame_display):
    menu = _new_menu(pygame_display)
    assert menu.press.active is True
    ev = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_SPACE)
    menu.handle_input(ev)
    assert menu.press.active is False


def test_mouse_click_dismisses_overlay(pygame_display):
    menu = _new_menu(pygame_display)
    assert menu.press.active is True
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(10, 10))
    menu.handle_input(ev)
    assert menu.press.active is False


def test_controller_button_dismisses_overlay(pygame_display):
    menu = _new_menu(pygame_display)
    assert menu.press.active is True
    # Synthesize a joystick button press event
    ev = pygame.event.Event(getattr(pygame, "JOYBUTTONDOWN"), button=0, joy=0)
    menu.handle_input(ev)
    assert menu.press.active is False


def test_controller_hat_dismisses_overlay(pygame_display):
    menu = _new_menu(pygame_display)
    assert menu.press.active is True
    # Only dismiss on non-neutral hat value
    neutral = pygame.event.Event(getattr(pygame, "JOYHATMOTION"), value=(0, 0), hat=0, joy=0)
    menu.handle_input(neutral)
    assert menu.press.active is True
    # Now simulate a directional press
    ev = pygame.event.Event(getattr(pygame, "JOYHATMOTION"), value=(1, 0), hat=0, joy=0)
    menu.handle_input(ev)
    assert menu.press.active is False


def test_controller_axis_dismisses_overlay(pygame_display):
    menu = _new_menu(pygame_display)
    assert menu.press.active is True
    # Below threshold should not dismiss
    small = pygame.event.Event(getattr(pygame, "JOYAXISMOTION"), value=0.2, axis=0, joy=0)
    menu.handle_input(small)
    assert menu.press.active is True
    # Above threshold should dismiss
    big = pygame.event.Event(getattr(pygame, "JOYAXISMOTION"), value=0.7, axis=0, joy=0)
    menu.handle_input(big)
    assert menu.press.active is False


def test_dispatcher_forwards_controller_axis_to_dismiss(pygame_display):
    g = _new_game(pygame_display)
    assert g.menu.press.active is True
    ev = pygame.event.Event(getattr(pygame, "JOYAXISMOTION"), value=0.8, axis=0, joy=0)
    _handle_menu(g, [ev])
    assert g.menu.press.active is False
