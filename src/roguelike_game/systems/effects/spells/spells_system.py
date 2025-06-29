# Path: src/roguelike_game/systems/effects/spells/spells_system.py
import pygame
from pygame.math import Vector2

# MVC: Smoke
from roguelike_game.systems.effects.spells.smoke.model      import SmokeModel
from roguelike_game.systems.effects.spells.smoke.controller import SmokeController
from roguelike_game.systems.effects.spells.smoke.view       import SmokeView

# MVC: SmokeEmitter
from roguelike_game.systems.effects.spells.smoke_emitter.model      import SmokeEmitterModel
from roguelike_game.systems.effects.spells.smoke_emitter.controller import SmokeEmitterController
from roguelike_game.systems.effects.spells.smoke_emitter.view       import SmokeEmitterView

# MVC: FireworkLaunch
from roguelike_game.systems.effects.spells.firework_launch.model      import FireworkLaunchModel
from roguelike_game.systems.effects.spells.firework_launch.controller import FireworkLaunchController
from roguelike_game.systems.effects.spells.firework_launch.view       import FireworkLaunchView


# MVC: Lightning
from roguelike_game.systems.effects.spells.lightning.model      import LightningModel
from roguelike_game.systems.effects.spells.lightning.controller import LightningController
from roguelike_game.systems.effects.spells.lightning.view       import LightningView

# MVC: ArcaneFlame
from roguelike_game.systems.effects.spells.arcane_flame.model      import ArcaneFlameModel
from roguelike_game.systems.effects.spells.arcane_flame.controller import ArcaneFlameController
from roguelike_game.systems.effects.spells.arcane_flame.view       import ArcaneFlameView

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

        # MVC lists
        self.smoke_controllers:         list[SmokeController]          = []
        self.smoke_views:               list[SmokeView]                = []

        self.smoke_emitter_controllers: list[SmokeEmitterController]   = []
        self.smoke_emitter_views:       list[SmokeEmitterView]         = []

        self.firework_controllers:      list[FireworkLaunchController] = []
        self.firework_views:            list[FireworkLaunchView]       = []


        self.lightning_controllers:     list[LightningController]      = []
        self.lightning_views:           list[LightningView]            = []

        self.arcane_controllers:        list[ArcaneFlameController]    = []
        self.arcane_views:              list[ArcaneFlameView]          = []

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



    def spawn_smoke(self, camera, entities):
        mx, my  = pygame.mouse.get_pos()
        wx       = mx / camera.zoom + camera.offset_x
        wy       = my / camera.zoom + camera.offset_y
        px, py   = self._player_center(entities.player)
        dir_vec  = Vector2(wx - px, wy - py)
        if dir_vec.length(): dir_vec.normalize_ip()
        model  = SmokeModel(px, py, dir_vec)
        ctrl   = SmokeController(model)
        view   = SmokeView(model)
        self.smoke_controllers.append(ctrl)
        self.smoke_views.append(view)

    def spawn_smoke_emitter(self, entities):
        px, py = self._player_center(entities.player)
        model   = SmokeEmitterModel(px, py)
        ctrl    = SmokeEmitterController(model)
        view    = SmokeEmitterView(model)
        self.smoke_emitter_controllers.append(ctrl)
        self.smoke_emitter_views.append(view)

    def spawn_firework(self, camera, entities):
        px, py = self._player_center(entities.player)
        mx, my = pygame.mouse.get_pos()
        wx      = mx / camera.zoom + camera.offset_x
        wy      = my / camera.zoom + camera.offset_y
        model  = FireworkLaunchModel(px, py, wx, wy)
        ctrl   = FireworkLaunchController(model)
        view   = FireworkLaunchView(model)
        self.firework_controllers.append(ctrl)
        self.firework_views.append(view)

    def spawn_lightning(self, target_pos, entities):
        px, py   = self._player_center(entities.player)
        model    = LightningModel((px, py), target_pos)
        
        ctrl     = LightningController(model)
        view     = LightningView(model)
        self.lightning_controllers.append(ctrl)
        self.lightning_views.append(view)

    def spawn_arcane_flame(self, x, y):
        model  = ArcaneFlameModel(x, y)
        ctrl   = ArcaneFlameController(model)
        view   = ArcaneFlameView(model)
        self.arcane_controllers.append(ctrl)
        self.arcane_views.append(view)

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
        # Smoke
        for c in self.smoke_controllers: c.update()
        self.smoke_controllers = [c for c in self.smoke_controllers if not c.model.is_finished()]
        self.smoke_views       = [v for v in self.smoke_views       if not v.model.is_finished()]

        # SmokeEmitter
        for c in self.smoke_emitter_controllers:
            wind = (pygame.mouse.get_pos()[0] - screen.get_width()//2)/1000
            c.apply_force(Vector2(wind,0)); c.update()
        self.smoke_emitter_controllers = [c for c in self.smoke_emitter_controllers if not c.model.is_empty()]
        self.smoke_emitter_views       = [v for v in self.smoke_emitter_views       if not v.model.is_empty()]

        # FireworkLaunch
        for c in self.firework_controllers: c.update()
        self.firework_controllers = [c for c in self.firework_controllers if not c.model.finished]
        self.firework_views       = [v for v in self.firework_views       if not v.model.finished]

        # Lightning
        for c in self.lightning_controllers: c.update()
        self.lightning_controllers = [c for c in self.lightning_controllers if not c.model.is_finished()]
        self.lightning_views       = [v for v in self.lightning_views       if not v.model.is_finished()]

        # ArcaneFlame
        for c in self.arcane_controllers: c.update()
        self.arcane_controllers = [c for c in self.arcane_controllers if not c.model.is_finished()]
        self.arcane_views       = [v for v in self.arcane_views       if not v.model.is_finished()]

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
        for v in self.smoke_views:         v.render(screen, camera)
        for v in self.smoke_emitter_views: v.render(screen, camera)
        for v in self.firework_views:      v.render(screen, camera)        
        for v in self.lightning_views:
            if (d := v.render(screen, camera)): dirty_rects.append(d)
        for v in self.arcane_views:         v.render(screen, camera)
        for v in self.shield_views:
            if (d := v.render(screen, camera)): dirty_rects.append(d)
        for v in self.teleport_views:       v.render(screen, camera)
                        

        return dirty_rects

    def _player_center(self, player):        
        return (player.x + player.sprite_size[0]//2, player.y + player.sprite_size[1]//2)