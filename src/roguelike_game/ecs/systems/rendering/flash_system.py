import time
import pygame
from roguelike_engine.utils.benchmark import benchmark

class FlashSystem:
    """
    Sistema que aplica un flash de color a entidades con FlashComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        flash_map = world.components.get('FlashComponent', {})
        sprite_map = world.components.get('Sprite', {})
        flashed_ids = set()
        for eid, flash in list(flash_map.items()):
            elapsed = time.time() - flash.start_time
            if elapsed >= flash.duration:
                del flash_map[eid]
            else:
                blink_interval = flash.duration / 4
                if blink_interval <= 0 or int(elapsed / blink_interval) % 2 == 0:
                    sprite = sprite_map.get(eid)
                    if sprite:
                        img = sprite.image.copy()
                        img.fill(flash.color, special_flags=pygame.BLEND_RGB_ADD)
                        sprite.image = img
                        flashed_ids.add(eid)
        # Red blink while burning (skip if already flashed above)
        burns = world.components.get('BurnComponent', {})
        now = time.time()
        for eid, burn in list(burns.items()):
            if eid in flashed_ids:
                continue
            sprite = sprite_map.get(eid)
            if not sprite:
                continue
            try:
                elapsed = max(0.0, now - float(getattr(burn, 'start_time', 0.0)))
                tick = float(getattr(burn, 'tick_period', 1.0)) or 1.0
                blink_interval = max(0.1, min(0.25, tick / 2.0))
                if int(elapsed / blink_interval) % 2 == 0:
                    img = sprite.image.copy()
                    img.fill((255, 64, 64), special_flags=pygame.BLEND_RGB_ADD)
                    sprite.image = img
            except Exception:
                pass