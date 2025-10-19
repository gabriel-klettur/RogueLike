import types

import roguelike_game.ecs.systems.audio.audio_system as aus


class FakeCatalog:
    def __init__(self):
        self.tracks = {'track1': {'title': 'Song 1', 'duration_s': 30}}
    def resolve_music_for(self, level=None, zone=None, biome=None):
        return 'track1'
    def resolve_ambient_for(self, level=None, zone=None, biome=None):
        return {'choices': ['wind'], 'min_interval': 5.0, 'max_interval': 10.0, 'group': 'ambient', 'volume': 0.5}
    def get_default_music(self):
        return {'ingame_track_id': 'track1', 'crossfade_ms': 600, 'playlist_interval_s': 30, 'ingame_playlist': ['track1', 'track2']}
    def track_path(self, track_id):
        return f"/music/{track_id}.ogg"


class FakeBus:
    def __init__(self):
        self.calls = []
    def play_sfx(self, **kw):
        self.calls.append(("play_sfx", kw))
    def crossfade(self, **kw):
        self.calls.append(("crossfade", kw))


def test_audio_process_crossfades_and_enqueues_ambient(monkeypatch):
    # Patch bus and catalog loader
    bus = FakeBus()
    monkeypatch.setattr(aus, 'get_bus', lambda: bus, raising=True)
    monkeypatch.setattr(aus, 'load_audio_catalog', lambda: FakeCatalog(), raising=True)

    sys = aus.AudioSystem()
    # world with minimal map info so resolve_* works
    world = types.SimpleNamespace(
        player_entity=1,
        map=types.SimpleNamespace(name='level1', current_zone=None, biome='forest'),
        components={},
    )

    sys.update(world)

    # Crossfade to resolved track and ToastQueue enqueued
    assert any(c[0] == 'crossfade' for c in bus.calls)
    tq = world.components.get('ToastQueue', [])
    assert any('Song:' in item.get('text', '') for item in tq)
    # Ambient enable was queued for processing
    aq = world.components.get('AudioEventQueue', [])
    assert any(ev.get('type') == 'enable_ambient' for ev in aq)
