import pytest
from roguelike_game.factories.player import config


def test_config_constants():
    # ORIGINAL_SPRITE_SIZE and RENDERED_SPRITE_SIZE deben ser tuplas de ints
    assert isinstance(config.ORIGINAL_SPRITE_SIZE, tuple)
    assert all(isinstance(i, int) for i in config.ORIGINAL_SPRITE_SIZE)
    assert isinstance(config.RENDERED_SPRITE_SIZE, tuple)
    assert all(isinstance(i, int) for i in config.RENDERED_SPRITE_SIZE)

    # DEFAULT_CLASS debe existir en PLAYER_STATS
    assert config.DEFAULT_CLASS in config.PLAYER_STATS

    # PLAYER_STATS para DEFAULT_CLASS debe tener keys esperadas
    stats = config.PLAYER_STATS[config.DEFAULT_CLASS]
    for key in ("max_health", "speed", "attack", "defense", "scale", "trail"):
        assert key in stats

    # MELEE_WEAPON_CFG debe tener damage y cooldown
    assert "damage" in config.MELEE_WEAPON_CFG
    assert "cooldown" in config.MELEE_WEAPON_CFG

    # FEET divisores deben ser int positivos
    assert isinstance(config.FEET_WIDTH_DIVISOR, int) and config.FEET_WIDTH_DIVISOR > 0
    assert isinstance(config.FEET_HEIGHT_DIVISOR, int) and config.FEET_HEIGHT_DIVISOR > 0
