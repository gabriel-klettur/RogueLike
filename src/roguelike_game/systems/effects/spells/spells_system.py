# Path: src/roguelike_game/systems/effects/spells/spells_system.py
import pygame
from pygame.math import Vector2

# MVC: SphereMagicShield
from roguelike_game.systems.effects.spells.sphere_magic_shield.model      import SphereMagicShieldModel
from roguelike_game.systems.effects.spells.sphere_magic_shield.controller import SphereMagicShieldController
from roguelike_game.systems.effects.spells.sphere_magic_shield.view       import SphereMagicShieldView

# MVC: Teleport
from roguelike_game.systems.effects.spells.teleport.model      import TeleportModel
from roguelike_game.systems.effects.spells.teleport.controller import TeleportController
from roguelike_game.systems.effects.spells.teleport.view       import TeleportView

# Benchmarking
from roguelike_engine.utils.benchmark import benchmark


class SpellsSystem:
    def __init__(self, state, perf_log, ecs_world):
        self.state = state
        self.perf_log = perf_log
        self.ecs_world = ecs_world  # Mundo ECS para colisionar con NPCs

        self.shield_controllers:        list[SphereMagicShieldController] = []
        self.shield_views:              list[SphereMagicShieldView]       = []

        self.teleport_controllers:      list[TeleportController]        = []
        self.teleport_views:            list[TeleportView]              = []

        # Laser continuous
        self.shooting_laser = False
        self.last_laser_time = 0

    # ------------------------------------------------ #
    #                   Spawn methods                  #
    # ------------------------------------------------ #


    def spawn_magic_shield(self, entities):
        model  = SphereMagicShieldModel(entities.player)
        ctrl   = SphereMagicShieldController(model)
        view   = SphereMagicShieldView(model)
        self.shield_controllers.append(ctrl)
        self.shield_views.append(view)

    def spawn_teleport(self, x, y, entities):
        px, py  = self._player_center(entities.player)
        model   = TeleportModel((px, py), (x, y))
        ctrl    = TeleportController(model)
        view    = TeleportView(model)
        self.teleport_controllers.append(ctrl)
        self.teleport_views.append(view)

    # ------------------------------------------------ #
    #                     Update                       #
    # ------------------------------------------------ #
    def update(self, clock, screen):

        # SphereMagicShield
        for c in self.shield_controllers: c.update()
        self.shield_controllers = [c for c in self.shield_controllers if not c.model.is_finished()]
        self.shield_views       = [v for v in self.shield_views       if not v.model.is_finished()]

        # Teleport
        for c in self.teleport_controllers: c.update()
        self.teleport_controllers = [c for c in self.teleport_controllers if not c.model.is_finished()]
        self.teleport_views       = [v for v in self.teleport_views       if not v.model.is_finished()]
        

    # ------------------------------------------------ #
    #                     Render                       #
    # ------------------------------------------------ #
    @benchmark(lambda self: self.perf_log, "3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        # MVC renders                             
        for v in self.shield_views:
            if (d := v.render(screen, camera)): dirty_rects.append(d)
        for v in self.teleport_views:       v.render(screen, camera)
                        

        return dirty_rects

    def _player_center(self, player):        
        return (player.x + player.sprite_size[0]//2, player.y + player.sprite_size[1]//2)