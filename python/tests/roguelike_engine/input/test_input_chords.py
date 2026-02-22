from types import SimpleNamespace

import pygame

from roguelike_engine.input.events import handle_events


def test_mmb_pan_chord_down_move_up_updates_state_and_defers_follow(monkeypatch):
    state = SimpleNamespace(running=True)
    # Minimal camera with offsets and zoom
    camera = SimpleNamespace(offset_x=0.0, offset_y=0.0, zoom=1.0, screen_width=800, screen_height=600)

    class _Ed:
        editor_state = SimpleNamespace(active=False)
        handler = SimpleNamespace(handle=lambda *a, **k: None)

    tiles_editor = _Ed()
    buildings_editor = _Ed()
    map_editor = _Ed()

    # Enable MMB panning via a visible particles editor (other editors would work as well)
    particles_editor = SimpleNamespace(model=SimpleNamespace(visible=True))

    # Create sequence: MMB down -> motion -> up
    down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2, "pos": (100, 100)})
    move = pygame.event.Event(pygame.MOUSEMOTION, {"pos": (120, 110)})
    up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 2, "pos": (120, 110)})

    handle_events(
        state, camera, None, None, None, None,
        tiles_editor, buildings_editor, map_editor,
        events=[down, move, up],
        particles_editor=particles_editor,
    )

    # After the chord, panning must be ended and defer_follow_frames set
    assert getattr(state, "mmb_panning", False) is False
    assert int(getattr(state, "defer_follow_frames", 0)) >= 12
