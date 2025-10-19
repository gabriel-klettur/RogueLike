import time

from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent
from roguelike_game.config.spells_defaults import (
    DEFAULT_AURA_OFFSET_X,
    DEFAULT_AURA_PARTICLES_PER_FRAME,
    DEFAULT_AURA_PARTICLE_SPEED,
    DEFAULT_AURA_PARTICLE_MIN_SIZE,
    DEFAULT_AURA_PARTICLE_MAX_SIZE,
    DEFAULT_AURA_PARTICLE_COLORS,
    DEFAULT_AURA_PARTICLE_LIFESPAN,
)
from roguelike_game.ecs.systems.rendering.combat.spells.teleport.model import TeleportModel


def test_aura_component_defaults_from_buff_and_time():
    t0 = time.time()
    comp = AuraComponent(radius=32, buff={}, duration=1.5)
    assert comp.radius == 32
    assert comp.buff == {}
    assert comp.duration == 1.5
    assert t0 <= comp.start_time <= time.time()
    assert comp.offset_x == DEFAULT_AURA_OFFSET_X
    assert comp.particles_per_frame == DEFAULT_AURA_PARTICLES_PER_FRAME
    assert comp.particle_speed == DEFAULT_AURA_PARTICLE_SPEED
    assert comp.particle_min_size == DEFAULT_AURA_PARTICLE_MIN_SIZE
    assert comp.particle_max_size == DEFAULT_AURA_PARTICLE_MAX_SIZE
    assert comp.particle_colors == DEFAULT_AURA_PARTICLE_COLORS
    assert comp.particle_lifespan == DEFAULT_AURA_PARTICLE_LIFESPAN


def test_lightning_component_updates_model_lifetime():
    comp = LightningComponent(start_pos=(0.0, 0.0), end_pos=(10.0, 0.0), segments=4, offset=2, lifetime=3)
    assert comp.model.lifetime == 3
    comp.update()
    assert comp.model.lifetime == 2
    assert comp.is_finished() is False
    comp.update()
    comp.update()
    assert comp.is_finished() is True


def test_teleport_component_wraps_model():
    model = TeleportModel(start_pos=(0, 0), end_pos=(5, 5), lifespan=0.1)
    comp = TeleportComponent(model=model)
    assert comp.model is model
    assert comp.model.should_switch_phase() in (True, False)
