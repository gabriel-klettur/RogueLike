import types
import time as _time

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


def test_ambient_scheduler_triggers_and_reschedules(monkeypatch):
    # Determinismo de tiempo y aleatoriedad
    t0 = 5000.0
    monkeypatch.setattr("time.time", lambda: t0)
    monkeypatch.setattr("random.choice", lambda seq: seq[0])
    monkeypatch.setattr("random.uniform", lambda a, b: 7.5)

    # Bus simulado
    bus = FakeBus()
    monkeypatch.setattr(aus, 'get_bus', lambda: bus, raising=True)

    # Estado con ambient habilitado y next_at vencido
    world = types.SimpleNamespace(components={
        'AudioEventQueue': [],
        'AudioAmbientState': {
            'enabled': True,
            'choices': ['bird_1', 'bird_2'],
            'min_interval': 5.0,
            'max_interval': 10.0,
            'next_at': t0 - 1.0,
            'group': 'ambient',
            'volume': 0.6,
        },
    })

    sys = aus.AudioSystem()
    sys.update(world)

    # Debe reproducir un SFX de ambient y reprogramar next_at
    assert any(call[0] == 'play_sfx' and call[1]['sfx_id'] == 'bird_1' for call in bus.calls)
    st = world.components['AudioAmbientState']
    assert st['next_at'] == t0 + 7.5
