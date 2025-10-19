import types

import roguelike_game.ecs.systems.audio.audio_system as aus


class FakeBus:
    def __init__(self):
        self.calls = []

    def play_sfx(self, **kw):
        self.calls.append(("play_sfx", kw))

    def play_music(self, **kw):
        self.calls.append(("play_music", kw))

    def stop_music(self, **kw):
        self.calls.append(("stop_music", kw))

    def crossfade(self, **kw):
        self.calls.append(("crossfade", kw))

    def set_music_volume(self, *a, **kw):
        self.calls.append(("set_music_vol", (a, kw)))

    def set_sfx_volume(self, *a, **kw):
        self.calls.append(("set_sfx_vol", (a, kw)))

    def set_ambient_volume(self, *a, **kw):
        self.calls.append(("set_ambient_vol", (a, kw)))


def test_audio_system_process_play_sfx(monkeypatch):
    # get_bus() -> fake
    bus = FakeBus()
    monkeypatch.setattr(aus, 'get_bus', lambda: bus, raising=True)

    sys = aus.AudioSystem()
    world = types.SimpleNamespace(components={'AudioEventQueue': [
        {'type': 'play_sfx', 'sfx_id': 'click_ui', 'volume': 0.8}
    ]})

    sys.update(world)

    # Verificar que el bus recibió la orden
    assert ('play_sfx', {'sfx_id': 'click_ui', 'volume': 0.8, 'pan': None, 'group': 'sfx'}) in bus.calls
