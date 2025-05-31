import time
import pygame
from roguelike_engine.utils.benchmark import benchmark

class FlashSystem:
    """
    Sistema que aplica un flash de color a entidades con FlashComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.FlashSystem.update")
    def update(self, world, camera=None):
        flash_map = world.components.get('FlashComponent', {})
        sprite_map = world.components.get('Sprite', {})
        for eid, flash in list(flash_map.items()):
            elapsed = time.time() - flash.start_time
            if elapsed >= flash.duration:
                del flash_map[eid]
            else:
                sprite = sprite_map.get(eid)
                if sprite:
                    img = sprite.image.copy()
                    img.fill(flash.color, special_flags=pygame.BLEND_RGB_ADD)
                    sprite.image = img
