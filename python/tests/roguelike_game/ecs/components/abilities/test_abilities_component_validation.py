from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.config.spells_defaults import (
    DEFAULT_AURA_OFFSET_X,
    DEFAULT_AURA_PARTICLES_PER_FRAME,
    DEFAULT_AURA_PARTICLE_SPEED,
    DEFAULT_AURA_PARTICLE_MIN_SIZE,
    DEFAULT_AURA_PARTICLE_MAX_SIZE,
    DEFAULT_AURA_PARTICLE_COLORS,
    DEFAULT_AURA_PARTICLE_LIFESPAN,
)


def test_aura_component_overrides_from_buff_dict():
    buff = {
        "offset_x": DEFAULT_AURA_OFFSET_X + 3,
        "particles_per_frame": DEFAULT_AURA_PARTICLES_PER_FRAME + 1,
        "particle_speed": DEFAULT_AURA_PARTICLE_SPEED + 0.5,
        "particle_min_size": DEFAULT_AURA_PARTICLE_MIN_SIZE + 1,
        "particle_max_size": DEFAULT_AURA_PARTICLE_MAX_SIZE + 1,
        "particle_colors": [(1, 2, 3)],
        "particle_lifespan": DEFAULT_AURA_PARTICLE_LIFESPAN + 5,
    }
    aura = AuraComponent(radius=24, buff=buff, duration=2.0)
    assert aura.offset_x == buff["offset_x"]
    assert aura.particles_per_frame == buff["particles_per_frame"]
    assert aura.particle_speed == buff["particle_speed"]
    assert aura.particle_min_size == buff["particle_min_size"]
    assert aura.particle_max_size == buff["particle_max_size"]
    assert aura.particle_colors == buff["particle_colors"]
    assert aura.particle_lifespan == buff["particle_lifespan"]
