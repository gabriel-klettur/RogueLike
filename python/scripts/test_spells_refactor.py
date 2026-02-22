import sys
from types import SimpleNamespace

from roguelike_game.config.spells_config import SpellConfig, FLATTEN_PARTICLES_MAPPING
from roguelike_game.ecs.systems.combat.spells.spells_apply import apply_aura_cfg


def test_apply_aura_cfg():
    # Dummy aura component
    aura = SimpleNamespace(
        radius=0,
        duration=0,
        buff={},
        offset_x=0,
        particles_per_frame=0,
        particle_speed=0.0,
        particle_min_size=0,
        particle_max_size=0,
        particle_colors=[],
        particle_lifespan=0,
    )
    cfg = SpellConfig(
        key="unit_test",
        radius=120,
        duration=7.5,
        buff={"heal_per_second": 4},
        particle_speed=1.2,
        particle_colors=[(10, 20, 30)],
        particle_lifespan=90,
        size_range=[6, 8],
        emit_rate=3,
    )
    apply_aura_cfg(aura, cfg)

    assert aura.radius == 120
    assert aura.duration == 7.5
    assert isinstance(aura.buff, dict) and aura.buff.get("heal_per_second") == 4
    assert aura.particle_speed == 1.2
    assert aura.particle_colors == [(10, 20, 30)]
    assert aura.particle_lifespan == 90
    assert aura.particle_min_size == 6 and aura.particle_max_size == 8
    assert aura.particles_per_frame == 3


def test_flatten_mapping_exported():
    # Ensure mapping contains expected keys
    for k in [
        "count",
        "dispersion",
        "colors",
        "lifespan",
        "speed",
        "size_range",
        "emit_rate",
    ]:
        assert k in FLATTEN_PARTICLES_MAPPING


if __name__ == "__main__":
    try:
        test_apply_aura_cfg()
        test_flatten_mapping_exported()
        print("OK: tests passed")
        sys.exit(0)
    except AssertionError as e:
        print("FAIL:", e)
        sys.exit(1)
