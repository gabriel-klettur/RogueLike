import types

import roguelike_game.ecs.systems.audio.audio_system as aus


class FakeCatalogA:
    def __init__(self):
        self.tracks = {'trackA': {'title': 'Song A', 'duration_s': 15}}
    def resolve_music_for(self, level=None, zone=None, biome=None):
        return 'trackA'
    def resolve_ambient_for(self, level=None, zone=None, biome=None):
        return {'choices': ['wind'], 'min_interval': 5.0, 'max_interval': 10.0, 'group': 'ambient', 'volume': 0.5}
    def get_default_music(self):
        return {'ingame_track_id': 'trackA', 'crossfade_ms': 600, 'playlist_interval_s': 15, 'ingame_playlist': ['trackA', 'trackB']}
    def track_path(self, track_id):
        return f"/music/{track_id}.ogg"


class FakeCatalogB(FakeCatalogA):
    def __init__(self):
        super().__init__()
        self.tracks['trackB'] = {'title': 'Song B', 'duration_s': 20}
    def resolve_music_for(self, level=None, zone=None, biome=None):
        return 'trackB'


class FakeBus:
    def __init__(self):
        self.calls = []
    def crossfade(self, **kw):
        self.calls.append(("crossfade", kw))
    def play_sfx(self, **kw):
        self.calls.append(("play_sfx", kw))


def test_audio_reload_catalog_applies_new_music_and_ambient(monkeypatch):
    # get_bus y load_audio_catalog apuntan a fakes
    bus = FakeBus()
    monkeypatch.setattr(aus, 'get_bus', lambda: bus, raising=True)

    # Primera carga será A, y en el reload se usará B
    loaders = [FakeCatalogA(), FakeCatalogB()]
    monkeypatch.setattr(aus, 'load_audio_catalog', lambda: loaders.pop(0), raising=True)

    sys = aus.AudioSystem()
    world = types.SimpleNamespace(
        player_entity=1,
        map=types.SimpleNamespace(name='level1', current_zone=None, biome='forest'),
        components={'AudioEventQueue': [{'type': 'reload_audio_catalog'}]},
    )

    sys.update(world)

    # Debe haberse llamado crossfade con el trackB (nuevo)
    assert any(c[0] == 'crossfade' and c[1].get('to_track_id') == 'trackB' for c in bus.calls)
    # Debió encolar enable_ambient tras recargar
    aq = world.components.get('AudioEventQueue', [])
    assert any(ev.get('type') == 'enable_ambient' for ev in aq)
