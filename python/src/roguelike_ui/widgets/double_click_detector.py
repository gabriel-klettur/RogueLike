import pygame
import time

class DoubleClickDetector:
    """
    Generic double-click detector for arbitrary keys or regions.
    """
    def __init__(self, interval_ms: int = 500):
        self.interval = interval_ms
        self.last_time = 0
        self.last_key = None

    def is_double_click(self, key) -> bool:
        now = int(time.time() * 1000)
        if self.last_key == key and (now - self.last_time) <= self.interval:
            # reset
            self.last_time = 0
            self.last_key = None
            return True
        self.last_time = now
        self.last_key = key
        return False
