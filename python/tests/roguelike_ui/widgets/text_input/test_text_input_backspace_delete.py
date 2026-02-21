import pygame

from roguelike_ui.widgets.text_input.text_input import TextInput


def make_key_event(key, unicode="", mod=0):
    return pygame.event.Event(pygame.KEYDOWN, key=key, unicode=unicode, mod=mod)


def test_backspace_deletes_before_cursor_no_selection():
    ti = TextInput(font=None)
    ti.activate(initial_text="abc", select_all=False)
    # place cursor after 'b'
    ti.cursor = 2
    ti.selection_start = ti.cursor
    ti.selection_end = ti.cursor

    # Backspace should remove 'b'
    ev = make_key_event(pygame.K_BACKSPACE)
    assert ti.handle_event(ev) is True
    assert ti.text == "ac"
    assert ti.cursor == 1
    assert ti.selection_start == ti.cursor == ti.selection_end


def test_backspace_removes_selection_range():
    ti = TextInput(font=None)
    ti.activate(initial_text="hello", select_all=False)
    # select 'ell'
    ti.selection_start = 1
    ti.selection_end = 4
    ti.cursor = 4

    ev = make_key_event(pygame.K_BACKSPACE)
    ti.handle_event(ev)

    assert ti.text == "ho"
    # cursor collapses to start of removed region
    assert ti.cursor == 1
    assert ti.selection_start == 1 and ti.selection_end == 1
