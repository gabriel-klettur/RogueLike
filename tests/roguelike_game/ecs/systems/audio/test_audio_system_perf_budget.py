from types import SimpleNamespace

import roguelike_game.ecs.systems.audio.audio_system as audio_mod
from roguelike_game.ecs.systems.audio.audio_system import AudioSystem


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


def test_audio_update_many_iterations_does_not_block(monkeypatch):
    # Minimal fake bus
    monkeypatch.setattr(audio_mod, "get_bus", lambda: FakeBus())
    # Catalog loader may fail silently; we just want update loop to be safe
    monkeypatch.setattr(audio_mod, "load_audio_catalog", lambda: None)

    sys_under_test = AudioSystem()
    world = SimpleNamespace(components={})

    # Run many iterations; should not raise or block
    for _ in range(200):
        sys_under_test.update(world)
