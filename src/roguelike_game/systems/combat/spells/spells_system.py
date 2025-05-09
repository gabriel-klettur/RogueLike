
# Path: src/roguelike_game/systems/combat/spells/spells_system.py
import pygame
from pygame.math import Vector2

# MVC: LaserBeam
from roguelike_game.systems.combat.spells.laser_beam.model      import LaserBeamModel
from roguelike_game.systems.combat.spells.laser_beam.controller import LaserBeamController
from roguelike_game.systems.combat.spells.laser_beam.view       import LaserBeamView

# MVC: HealingAura
from roguelike_game.systems.combat.spells.healing_aura.model      import HealingAuraModel
from roguelike_game.systems.combat.spells.healing_aura.controller import HealingAuraController
from roguelike_game.systems.combat.spells.healing_aura.view       import HealingAuraView

# MVC: Smoke
from roguelike_game.systems.combat.spells.smoke.model      import SmokeModel
from roguelike_game.systems.combat.spells.smoke.controller import SmokeController
from roguelike_game.systems.combat.spells.smoke.view       import SmokeView

# MVC: SmokeEmitter
from roguelike_game.systems.combat.spells.smoke_emitter.model      import SmokeEmitterModel
from roguelike_game.systems.combat.spells.smoke_emitter.controller import SmokeEmitterController
from roguelike_game.systems.combat.spells.smoke_emitter.view       import SmokeEmitterView

# MVC: FireworkLaunch
from roguelike_game.systems.combat.spells.firework_launch.model      import FireworkLaunchModel
from roguelike_game.systems.combat.spells.firework_launch.controller import FireworkLaunchController
from roguelike_game.systems.combat.spells.firework_launch.view       import FireworkLaunchView

# MVC: Fireball
from roguelike_game.systems.combat.spells.fireball.model      import FireballModel
from roguelike_game.systems.combat.spells.fireball.controller import FireballController
from roguelike_game.systems.combat.spells.fireball.view       import FireballView

# MVC: Lightning
from roguelike_game.systems.combat.spells.lightning.model      import LightningModel
from roguelike_game.systems.combat.spells.lightning.controller import LightningController
from roguelike_game.systems.combat.spells.lightning.view       import LightningView

# MVC: ArcaneFlame
from roguelike_game.systems.combat.spells.arcane_flame.model      import ArcaneFlameModel
from roguelike_game.systems.combat.spells.arcane_flame.controller import ArcaneFlameController
from roguelike_game.systems.combat.spells.arcane_flame.view       import ArcaneFlameView

# MVC: SphereMagicShield
from roguelike_game.systems.combat.spells.sphere_magic_shield.model      import SphereMagicShieldModel
from roguelike_game.systems.combat.spells.sphere_magic_shield.controller import SphereMagicShieldController
from roguelike_game.systems.combat.spells.sphere_magic_shield.view       import SphereMagicShieldView

# MVC: Teleport
from roguelike_game.systems.combat.spells.teleport.model      import TeleportModel
from roguelike_game.systems.combat.spells.teleport.controller import TeleportController
from roguelike_game.systems.combat.spells.teleport.view       import TeleportView

# MVC: SlashEffect (legacy)
from roguelike_game.systems.combat.spells.slash.model      import SlashModel
from roguelike_game.systems.combat.spells.slash.controller import SlashController
from roguelike_game.systems.combat.spells.slash.view       import SlashView

# Legacy: DashTrail, DashBounce
from roguelike_game.systems.combat.spells.dash.model      import DashModel
from roguelike_game.systems.combat.spells.dash.controller import DashController
from roguelike_game.systems.combat.spells.dash.view       import DashView

# Benchmarking
from roguelike_engine.utils.benchmark import benchmark


class SpellsSystem:
    def __init__(self, state):
        self.state = state        

        # MVC lists
        self.laser_controllers:         list[LaserBeamController]      = []
        self.laser_views:               list[LaserBeamView]            = []

        self.healing_controllers:       list[HealingAuraController]    = []
        self.healing_views:             list[HealingAuraView]          = []

        self.smoke_controllers:         list[SmokeController]          = []
        self.smoke_views:               list[SmokeView]                = []

        self.smoke_emitter_controllers: list[SmokeEmitterController]   = []
        self.smoke_emitter_views:       list[SmokeEmitterView]         = []

        self.firework_controllers:      list[FireworkLaunchController] = []
        self.firework_views:            list[FireworkLaunchView]       = []

        self.fireball_controllers:      list[FireballController]       = []
        self.fireball_views:            list[FireballView]             = []

        self.lightning_controllers:     list[LightningController]      = []
        self.lightning_views:           list[LightningView]            = []

        self.arcane_controllers:        list[ArcaneFlameController]    = []
        self.arcane_views:              list[ArcaneFlameView]          = []

        self.shield_controllers:        list[SphereMagicShieldController] = []
        self.shield_views:              list[SphereMagicShieldView]       = []

        self.teleport_controllers:      list[TeleportController]        = []
        self.teleport_views:            list[TeleportView]              = []

        self.slash_controllers:         list[SlashController]           = []
        self.slash_views:               list[SlashView]                 = []
        
        self.dash_controllers:          list[DashController]            = []
        self.dash_views:                list[DashView]                  = []

        # Laser continuous
        self.shooting_laser = False
        self.last_laser_time = 0

    # ------------------------------------------------ #
    #                   Spawn methods                  #
    # ------------------------------------------------ #

    def spawn_dash(self, player, direction: pygame.Vector2):
        model = DashModel(player, direction)
        ctrl  = DashController(model)
        view  = DashView(model)
        self.dash_controllers.append(ctrl)
        self.dash_views.append(view)

    def spawn_slash(self, direction: Vector2, entities):
        px, py = self._player_center(entities.player)
        #enemies = entities.enemies + list(self.state.remote_entities.values()) 
        model = SlashModel(px, py, direction)
        ctrl = SlashController(model)
        view = SlashView(model)
        self.slash_controllers.append(ctrl)
        self.slash_views.append(view)


    def spawn_laser(self, x, y, enemies, entities):
        px, py = self._player_center(entities.player)
        model  = LaserBeamModel(px, py, x, y, enemies=enemies)
        ctrl   = LaserBeamController(model)
        view   = LaserBeamView(model)
        self.laser_controllers.append(ctrl)
        self.laser_views.append(view)
        # cap history
        if len(self.laser_controllers) > 3:
            self.laser_controllers.pop(0)
            self.laser_views.pop(0)

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

    def spawn_fireball(self, angle, map, entities):
        px, py   = self._player_center(entities.player)
        tiles    = [t for t in map.tiles_in_region if t.solid]
        enemies  = entities.enemies + list(self.state.remote_entities.values())
        model   = FireballModel(px, py, angle)
        ctrl    = FireballController(model, tiles, enemies, self.state.systems.explosions)
        view    = FireballView(model)
        self.fireball_controllers.append(ctrl)
        self.fireball_views.append(view)

    def spawn_lightning(self, target_pos, entities):
        px, py   = self._player_center(entities.player)
        model    = LightningModel((px, py), target_pos)
        enemies  = entities.enemies + list(self.state.remote_entities.values())
        ctrl     = LightningController(model, enemies)
        view     = LightningView(model)
        self.lightning_controllers.append(ctrl)
        self.lightning_views.append(view)

    def spawn_healing_aura(self, clock, entities):
        model  = HealingAuraModel(entities.player)
        ctrl   = HealingAuraController(model, clock)
        view   = HealingAuraView(model)
        self.healing_controllers.append(ctrl)
        self.healing_views.append(view)

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
        # HealingAura
        for c in self.healing_controllers: c.update()
        self.healing_controllers = [c for c in self.healing_controllers if not c.model.is_empty()]
        self.healing_views       = [v for v in self.healing_views       if not v.model.is_empty()]

        # LaserBeam
        for c in self.laser_controllers: c.update()
        self.laser_controllers = [c for c in self.laser_controllers if not c.model.finished]
        self.laser_views       = [v for v in self.laser_views       if not v.model.finished]

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

        # Fireball
        for c in self.fireball_controllers: c.update()
        self.fireball_controllers = [c for c in self.fireball_controllers if c.model.alive]
        self.fireball_views       = [v for v in self.fireball_views       if v.model.alive]

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

        # Slash
        for c in self.slash_controllers: c.update()
        self.slash_controllers = [c for c in self.slash_controllers if not c.model.is_finished()]
        self.slash_views = [v for v in self.slash_views if not v.model.is_finished()]

        #Dash
        for c in self.dash_controllers: c.update(clock)
        self.dash_controllers = [c for c in self.dash_controllers if not c.is_finished()]
        self.dash_views       = [v for v in self.dash_views       if not v.model.is_finished()]
        

    # ------------------------------------------------ #
    #                     Render                       #
    # ------------------------------------------------ #
    @benchmark(lambda self: self.state.perf_log, "----3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        # MVC renders
        for v in self.laser_views:         v.render(screen, camera)
        for v in self.smoke_views:         v.render(screen, camera)
        for v in self.smoke_emitter_views: v.render(screen, camera)
        for v in self.firework_views:      v.render(screen, camera)
        for v in self.fireball_views:      v.render(screen, camera)
        for v in self.healing_views:       v.render(screen, camera)
        for v in self.lightning_views:
            if (d := v.render(screen, camera)): dirty_rects.append(d)
        for v in self.arcane_views:         v.render(screen, camera)
        for v in self.shield_views:
            if (d := v.render(screen, camera)): dirty_rects.append(d)
        for v in self.teleport_views:       v.render(screen, camera)
        for v in self.slash_views:
            if (d := v.render(screen, camera)):
                dirty_rects.append(d)        
        for v in self.dash_views:
            if (d := v.render(screen, camera)):
                dirty_rects.append(d)

        return dirty_rects

    def _player_center(self, player):        
        return (player.x + player.sprite_size[0]//2, player.y + player.sprite_size[1]//2)