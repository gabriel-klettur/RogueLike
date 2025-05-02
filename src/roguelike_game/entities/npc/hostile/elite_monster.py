# Path: src/roguelike_game/entities/npc/hostile/elite_monster.py
import pygame
from src.roguelike_engine.utils.loader import load_image
from src.roguelike_game.entities.npc.hostile.monster import Monster

class Elite(Monster):
    def __init__(self, x, y, name="Elite"):
        super().__init__(x, y, name)

        self.health = 100
        self.max_health = 100
        self.speed = 6
        self.name = name

        # Sprites por dirección
        self.sprites = {
            "up": load_image("assets/npc/monsters/barbol_elite/elite_barbol_1_top.png", self.sprite_size),
            "down": load_image("assets/npc/monsters/barbol_elite/elite_barbol_1_down.png", self.sprite_size),
            "left": load_image("assets/npc/monsters/barbol_elite/elite_barbol_1_left.png", self.sprite_size),
            "right": load_image("assets/npc/monsters/barbol_elite/elite_barbol_1_right.png", self.sprite_size),
        }