from types import SimpleNamespace

import roguelike_game.ecs.systems.audio.audio_system as audio_mod
from roguelike_game.ecs.systems.audio.audio_system import AudioSystem


class FakeCatalog:
    def __init__(self):
        # minimal track registry
        self._default = {
            "crossfade_ms": 600,
            "ingame_track_id": "track_a",
            "ingame_playlist": ["track_a", "track_b"],
            "playlist_mode": "loop",
            "playlist_interval_s": 10,
        }
        self.tracks = {"track_a": {"title": "Song A", "duration_s": 5}}

    def track_path(self, track_id):
        return f"/fake/{track_id}.ogg"

    def get_default_music(self):
        return self._default

    def resolve_music_for(self, level=None, zone=None, biome=None):
        # return default track id
        return self._default["ingame_track_id"]

    def resolve_ambient_for(self, level=None, zone=None, biome=None):
        return {
            "choices": ["wind_1", "wind_2"],
            "min_interval": 1.0,
            "max_interval": 2.0,
            "group": "ambient",
            "volume": 0.5,
        }


class FakeBus:
    def __init__(self):
        self.calls = []

    def play_sfx(self, *a, **k):
        self.calls.append(("play_sfx", a, k))

    def play_music(self, *a, **k):
        self.calls.append(("play_music", a, k))

    def stop_music(self, *a, **k):
        self.calls.append(("stop_music", a, k))

    def crossfade(self, *a, **k):
        self.calls.append(("crossfade", a, k))

    def set_music_volume(self, *a, **k):
        self.calls.append(("set_music_volume", a, k))

    def set_sfx_volume(self, *a, **k):
        self.calls.append(("set_sfx_volume", a, k))

    def set_ambient_volume(self, *a, **k):
        self.calls.append(("set_ambient_volume", a, k))


def test_audio_system_enables_ambient_and_crossfades_default(monkeypatch):
    # Patch bus and catalog providers
    fake_bus = FakeBus()
    monkeypatch.setattr(audio_mod, "get_bus", lambda: fake_bus)
    monkeypatch.setattr(audio_mod, "load_audio_catalog", lambda: FakeCatalog())

    sys_under_test = AudioSystem()

    # Minimal world with player position for zone lookup path (safe if not used)
    world = SimpleNamespace(
        components={
            "Position": {1: SimpleNamespace(x=0, y=0)},
        },
        player_entity=1,
        map=SimpleNamespace(current_zone=None, biome="forest"),
    )

    # First update should load catalog, resolve music/ambient and enqueue ambient
    sys_under_test.update(world)

    # Verify AudioEventQueue got enable_ambient event
    aq = world.components.get("AudioEventQueue", [])
    assert any(ev.get("type") == "enable_ambient" for ev in aq)

    # Verify a crossfade call was issued to bus
    assert any(call[0] == "crossfade" for call in fake_bus.calls)
