# src.roguelike_project/systems/combat/spells/teleport/model.py
import time

class TeleportModel:
    def __init__(self, start_pos, end_pos, lifespan=0.5):
        self.start_pos = start_pos
        self.end_pos   = end_pos
        self.lifespan  = lifespan
        self.start_time = time.time()
        self.phase = "out"  # "out" hasta la mitad, luego "in"

    def elapsed(self):
        return time.time() - self.start_time

    def is_finished(self):
        return self.elapsed() > self.lifespan

    def should_switch_phase(self):
        return self.phase == "out" and self.elapsed() >= self.lifespan / 2
