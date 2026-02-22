from types import SimpleNamespace

import pytest

import roguelike_game.ecs.systems.audio.audio_system as audio_mod
from roguelike_game.ecs.systems.audio.audio_system import AudioSystem


def test_audio_update_no_bus_graceful(monkeypatch):
    # Force get_bus to return None
    monkeypatch.setattr(audio_mod, "get_bus", lambda: None)
    sys_under_test = AudioSystem()
    world = SimpleNamespace(components={})
    # Should not raise
    sys_under_test.update(world)


def test_audio_update_catalog_load_failure(monkeypatch):
    # Make get_bus return a minimal fake bus
    class FakeBus:
        def play_sfx(self, *a, **k):
            pass
        def play_music(self, *a, **k):
            pass
        def stop_music(self, *a, **k):
            pass
        def crossfade(self, *a, **k):
            pass
        def set_music_volume(self, *a, **k):
            pass
        def set_sfx_volume(self, *a, **k):
            pass
        def set_ambient_volume(self, *a, **k):
            pass

    monkeypatch.setattr(audio_mod, "get_bus", lambda: FakeBus())
    # Force load_audio_catalog to fail
    monkeypatch.setattr(audio_mod, "load_audio_catalog", lambda: (_ for _ in ()).throw(RuntimeError("fail")))

    sys_under_test = AudioSystem()
    world = SimpleNamespace(components={})
    # Should not raise
    sys_under_test.update(world)
