import random
import types
import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_fuzz_inputs_no_crash(monkeypatch):
    rng = random.Random(4242)

    # Deterministic random side selection but still varied
    def choice(seq):
        # rotate through items deterministically
        idx = getattr(choice, 'i', 0) % len(seq)
        choice.i = getattr(choice, 'i', 0) + 1
        return seq[idx]
    monkeypatch.setattr(svc, 'random', types.SimpleNamespace(choice=choice))

    for _ in range(15):
        zw = rng.randint(3, 12)
        zh = rng.randint(3, 12)
        lob_x = rng.randint(0, 20)
        lob_y = rng.randint(0, 20)
        dun_x = lob_x + rng.choice([-zw, 0, zw])
        dun_y = lob_y + rng.choice([-zh, 0, zh])

        class Settings:
            zone_width = zw
            zone_height = zh
            additional_zones = {}
            zone_offsets = {
                'lobby': (lob_x, lob_y),
                'dungeon': (dun_x, dun_y),
            }
            @staticmethod
            def calculate_offset(parent_off, side):
                x, y = parent_off
                return {
                    'right': (x + Settings.zone_width, y),
                    'left': (x - Settings.zone_width, y),
                    'top': (x, y - Settings.zone_height),
                    'bottom': (x, y + Settings.zone_height),
                }[side]
        monkeypatch.setattr(svc, 'global_map_settings', Settings, raising=True)

        # World stub with player state
        world = types.SimpleNamespace()
        world.player_entity = 1
        world.player_position = types.SimpleNamespace(x=float(lob_x * 16), y=float(lob_y * 16))
        world.components = {'Position': {1: types.SimpleNamespace(x=0.0, y=0.0)}}
        class MapManager:
            lobby_offset = Settings.zone_offsets['lobby']
            def expand_zone(self, side, new_key, parent_key):
                assert side in ('bottom', 'top', 'left', 'right')
        world.map_manager = MapManager()

        # Should not raise
        new_key, parent_key = svc.expand_dungeon(world)
        assert isinstance(new_key, str) and isinstance(parent_key, str)
        assert new_key in Settings.additional_zones
