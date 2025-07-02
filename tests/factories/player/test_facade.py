import pytest
from roguelike_game.factories.registry import get_factory
from roguelike_game.factories.player import loader, calibrator, config as player_config
from roguelike_game.factories.player.facade import PlayerFactory

# A fake assets class to avoid real file I/O
class DummyAssets:
    def __init__(self, class_player, size): pass
    def get_sprites(self):
        # Return dict with down idle frame only
        class Frame:
            def __init__(self): self._size = (10, 20)
            def get_size(self): return self._size
        sprites = {'down': {'idle': [Frame()], 'walk': []}, 'up': {'idle': [], 'walk': []}}
        return sprites, None

@pytest.fixture(autouse=True)
def patch_player(monkeypatch):
    # Patch PlayerAssets and pygame.transform.scale
    import roguelike_game.ecs.components.rendering.sprite as sprite_mod
    class DummySprite:
        def __init__(self, image): self.image = image
    monkeypatch.setattr(sprite_mod, 'Sprite', DummySprite)
    # Bypass load_image for non-surface inputs
    monkeypatch.setattr(sprite_mod, 'load_image', lambda path, scale=None: path)
    import roguelike_game.factories.player.builder as builder_mod
    monkeypatch.setattr(builder_mod, 'Sprite', DummySprite)
    monkeypatch.setattr(loader, 'PlayerAssets', DummyAssets)
    import pygame
    monkeypatch.setattr(pygame.transform, 'scale', lambda frame, size: frame)
    # Patch config stats for 'test'
    stats = {'scale': player_config.DEFAULT_SCALE,
             'speed': 5, 'max_health': 100, 'attack': 10, 'defense': 5,
             'trail': player_config.DEFAULT_TRAIL}
    player_config.PLAYER_STATS['test'] = stats
    # Ensure melee cfg
    player_config.MELEE_WEAPON_CFG['damage'] = 3
    player_config.MELEE_WEAPON_CFG['cooldown'] = 1
    yield


def test_factory_instance():
    factory = get_factory("player")
    assert isinstance(factory, PlayerFactory)


def test_create_pixel(world):
    factory = get_factory("player")
    eid = factory.create(world, x=7, y=9, class_player='test')
    pos = world.components['Position'][eid]
    assert pos.x == 7 and pos.y == 9
    tag = world.components['PlayerTagComponent'][eid]
    assert tag.class_name == 'test'


def test_create_tile(world):
    factory = get_factory("player")
    # Provide tile coords
    tx, ty = 3, 4
    # Calculate expected via calibrator
    sprites, _ = DummyAssets('test', None).get_sprites()
    frame = sprites['down']['idle'][0]
    expected_x, expected_y = calibrator.calibrate_tile_position(tx, ty, frame)
    eid = factory.create(world, tile_x=tx, tile_y=ty, class_player='test')
    pos = world.components['Position'][eid]
    assert (pos.x, pos.y) == (expected_x, expected_y)
