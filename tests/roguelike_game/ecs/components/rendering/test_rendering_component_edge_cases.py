import time
import pygame
from dataclasses import asdict

from roguelike_game.ecs.components.rendering.trail_component import (
    TrailComponent,
    TrailConfig,
)
from roguelike_game.ecs.components.rendering.sprite import Sprite


def test_trail_component_defaults_factories():
    cfg = TrailConfig(interval=0.05, life_time=0.2, max_trails=8)
    t0 = time.time()
    tc = TrailComponent(config=cfg)
    assert tc.config is cfg
    assert t0 <= tc.last_gen <= time.time()
    assert isinstance(tc.snapshots, list) and tc.snapshots == []


def test_sprite_accepts_surface_without_io():
    surf = pygame.Surface((4, 4))
    s = Sprite(surf)
    assert s.image is surf
