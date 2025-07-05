import os
import pygame
import pytest

from roguelike_ui.widgets.text_input import TextInput

@pytest.fixture(scope="module", autouse=True)
def init_pygame():
    # Use dummy video driver for headless testing
    os.environ['SDL_VIDEODRIVER'] = 'dummy'
    pygame.display.init()
    pygame.display.set_mode((1,1))
    pygame.font.init()
    yield
    pygame.quit()


def test_activate_and_deactivate():
    font = pygame.font.Font(None, 16)
    ti = TextInput(font)
    assert not ti.active
    ti.activate("hello", select_all=True)
    assert ti.active
    assert ti.text == "hello"
    assert ti.cursor == len("hello")
    assert ti.selection_start == 0
    assert ti.selection_end == len("hello")
    ti.deactivate()
    assert not ti.active


def test_insertion_and_deletion():
    font = pygame.font.Font(None, 16)
    ti = TextInput(font)
    ti.activate("", select_all=False)
    # Insert 'a'
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a, unicode="a", mod=0)
    assert ti.handle_event(evt)
    assert ti.text == "a"
    assert ti.cursor == 1
    # Insert 'b'
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_b, unicode="b", mod=0)
    assert ti.handle_event(evt)
    assert ti.text == "ab"
    assert ti.cursor == 2
    # Backspace deletes 'b'
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_BACKSPACE)
    assert ti.handle_event(evt)
    assert ti.text == "a"
    assert ti.cursor == 1


def test_cursor_movement_and_selection():
    font = pygame.font.Font(None, 16)
    ti = TextInput(font)
    ti.activate("xyz", select_all=False)
    # Move left arrow
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_LEFT, mod=0)
    assert ti.handle_event(evt)
    assert ti.cursor == 2
    # Home with shift selects to start
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_HOME, mod=pygame.KMOD_SHIFT)
    assert ti.handle_event(evt)
    assert ti.selection_start == 0
    assert ti.selection_end == 2
    # Ctrl+A selects all
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a, unicode="a", mod=pygame.KMOD_CTRL)
    assert ti.handle_event(evt)
    assert ti.selection_start == 0
    assert ti.selection_end == len(ti.text)


def test_enter_commit_and_numpad_enter():
    font = pygame.font.Font(None, 16)
    ti = TextInput(font)
    ti.activate("val", select_all=False)
    # Main Enter
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_RETURN)
    assert ti.handle_event(evt)
    assert not ti.active
    # Numpad Enter
    ti.activate("val2", select_all=False)
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_KP_ENTER)
    assert ti.handle_event(evt)
    assert not ti.active
