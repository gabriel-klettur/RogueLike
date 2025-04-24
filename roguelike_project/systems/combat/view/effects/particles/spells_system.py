# File: roguelike_project/systems/combat/view/effects/particles/spells_system.py
import pygame
from pygame.math import Vector2

# MVC: LaserBeam
from roguelike_project.systems.combat.spells.laser_beam.model      import LaserBeamModel
from roguelike_project.systems.combat.spells.laser_beam.controller import LaserBeamController
from roguelike_project.systems.combat.spells.laser_beam.view       import LaserBeamView

# MVC: HealingAura
from roguelike_project.systems.combat.spells.healing_aura.model      import HealingAuraModel
from roguelike_project.systems.combat.spells.healing_aura.controller import HealingAuraController
from roguelike_project.systems.combat.spells.healing_aura.view       import HealingAuraView

# MVC: Smoke (formerly FartAttack)
from roguelike_project.systems.combat.spells.smoke.model      import SmokeModel
from roguelike_project.systems.combat.spells.smoke.controller import SmokeController
from roguelike_project.systems.combat.spells.smoke.view       import SmokeView

# MVC: SmokeEmitter
from roguelike_project.systems.combat.spells.smoke_emitter.model      import SmokeEmitterModel
from roguelike_project.systems.combat.spells.smoke_emitter.controller import SmokeEmitterController
from roguelike_project.systems.combat.spells.smoke_emitter.view       import SmokeEmitterView

# MVC: FireworkLaunch
from roguelike_project.systems.combat.spells.firework_launch.model      import FireworkLaunchModel
from roguelike_project.systems.combat.spells.firework_launch.controller import FireworkLaunchController
from roguelike_project.systems.combat.spells.firework_launch.view       import FireworkLaunchView

# MVC: Fireball
from roguelike_project.systems.combat.spells.fireball.model      import FireballModel
from roguelike_project.systems.combat.spells.fireball.controller import FireballController
from roguelike_project.systems.combat.spells.fireball.view       import FireballView

# MVC: Lightning 
from roguelike_project.systems.combat.spells.lightning.model      import LightningModel
from roguelike_project.systems.combat.spells.lightning.controller import LightningController
from roguelike_project.systems.combat.spells.lightning.view       import LightningView

# MVC: ArcaneFlame
from roguelike_project.systems.combat.spells.arcane_flame.model      import ArcaneFlameModel
from roguelike_project.systems.combat.spells.arcane_flame.controller import ArcaneFlameController
from roguelike_project.systems.combat.spells.arcane_flame.view       import ArcaneFlameView

# MVC: SphereMagicShield
from roguelike_project.systems.combat.spells.sphere_magic_shield.model      import SphereMagicShieldModel
from roguelike_project.systems.combat.spells.sphere_magic_shield.controller import SphereMagicShieldController
from roguelike_project.systems.combat.spells.sphere_magic_shield.view       import SphereMagicShieldView

# MVC: Teleport
from roguelike_project.systems.combat.spells.teleport.model      import TeleportModel
from roguelike_project.systems.combat.spells.teleport.controller import TeleportController
from roguelike_project.systems.combat.spells.teleport.view       import TeleportView

# Legacy effects
from roguelike_project.systems.combat.view.effects.particles.spells.dash_trail      import DashTrail
from roguelike_project.systems.combat.view.effects.particles.spells.dash_bounce     import DashBounce
from roguelike_project.systems.combat.view.effects.particles.spells.slash_effect    import SlashEffect

from roguelike_project.utils.benchmark import benchmark


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

        # Legacy lists                        
        self.dash_trails   = []
        self.dash_bounces  = []
        self.slash_effects = []

        # Laser continuous
        self.shooting_laser = False
        self.last_laser_time = 0

    # ------------------------------------------------ #
    #                   Spawn methods                  #
    # ------------------------------------------------ #
    def spawn_slash_effect(self, player, direction):
        self.slash_effects.append(SlashEffect(player, direction))

    def spawn_laser(self, x, y, enemies):
        px, py = self._player_center()
        model = LaserBeamModel(px, py, x, y, enemies=enemies)
        ctrl  = LaserBeamController(model)
        view  = LaserBeamView(model)
        self.laser_controllers.append(ctrl)
        self.laser_views.append(view)
        if len(self.laser_controllers) > 3:
            self.laser_controllers.pop(0)
            self.laser_views.pop(0)

    def spawn_smoke(self):
        mx, my    = pygame.mouse.get_pos()
        world_x   = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y   = my / self.state.camera.zoom + self.state.camera.offset_y
        px, py    = self._player_center()
        dir_vec   = Vector2(world_x - px, world_y - py)
        if dir_vec.length():
            dir_vec.normalize_ip()
        model = SmokeModel(px, py, dir_vec)
        ctrl  = SmokeController(model)
        view  = SmokeView(model)
        self.smoke_controllers.append(ctrl)
        self.smoke_views.append(view)

    def spawn_smoke_emitter(self):
        px, py = self._player_center()
        model  = SmokeEmitterModel(px, py)
        ctrl   = SmokeEmitterController(model)
        view   = SmokeEmitterView(model)
        self.smoke_emitter_controllers.append(ctrl)
        self.smoke_emitter_views.append(view)

    def spawn_firework(self):
        px, py = self._player_center()
        mx, my = pygame.mouse.get_pos()
        wx     = mx / self.state.camera.zoom + self.state.camera.offset_x
        wy     = my / self.state.camera.zoom + self.state.camera.offset_y
        model  = FireworkLaunchModel(px, py, wx, wy)
        ctrl   = FireworkLaunchController(model)
        view   = FireworkLaunchView(model)
        self.firework_controllers.append(ctrl)
        self.firework_views.append(view)

    def spawn_fireball(self, angle):
        px, py  = self._player_center()
        tiles    = [t for t in self.state.tiles if t.solid]
        enemies  = self.state.enemies + list(self.state.remote_entities.values())
        model    = FireballModel(px, py, angle)
        ctrl     = FireballController(model, tiles, enemies, self.state.systems.explosions)
        view     = FireballView(model)
        self.fireball_controllers.append(ctrl)
        self.fireball_views.append(view)

    def spawn_lightning(self, target_pos):
        px, py = self._player_center()
        model   = LightningModel((px, py), target_pos)
        enemies = self.state.enemies + list(self.state.remote_entities.values())
        ctrl    = LightningController(model, enemies)
        view    = LightningView(model)
        self.lightning_controllers.append(ctrl)
        self.lightning_views.append(view)

    def spawn_healing_aura(self):
        model = HealingAuraModel(self.state.player)
        ctrl  = HealingAuraController(model, self.state.clock)
        view  = HealingAuraView(model)
        self.healing_controllers.append(ctrl)
        self.healing_views.append(view)

    def spawn_arcane_flame(self, x, y):
        model = ArcaneFlameModel(x, y)
        ctrl  = ArcaneFlameController(model)
        view  = ArcaneFlameView(model)
        self.arcane_controllers.append(ctrl)
        self.arcane_views.append(view)

    def spawn_magic_shield(self):
        model = SphereMagicShieldModel(self.state.player)
        ctrl  = SphereMagicShieldController(model)
        view  = SphereMagicShieldView(model)
        self.shield_controllers.append(ctrl)
        self.shield_views.append(view)

    def spawn_teleport(self, x, y):
        px, py = self._player_center()
        model = TeleportModel((px,py), (x,y))
        ctrl  = TeleportController(model)
        view  = TeleportView(model)
        self.teleport_controllers.append(ctrl)
        self.teleport_views.append(view)

    def spawn_dash_trail(self, player, direction):
        self.dash_trails.append(DashTrail(player, direction))

    def stop_dash_trails(self):
        for t in self.dash_trails:
            t.stop()

    def spawn_dash_bounce(self, x, y):
        self.dash_bounces.append(DashBounce(x, y))

    # ------------------------------------------------ #
    #                     Update                       #
    # ------------------------------------------------ #
    def update(self):
        # HealingAura MVC
        for c in self.healing_controllers:
            c.update()
        self.healing_controllers = [
            c for c in self.healing_controllers if not c.model.is_empty()
        ]
        self.healing_views = [
            v for v in self.healing_views if not v.model.is_empty()
        ]

        # LaserBeam MVC
        for c in self.laser_controllers:
            c.update()
        self.laser_controllers = [
            c for c in self.laser_controllers if not c.model.finished
        ]
        self.laser_views = [
            v for v in self.laser_views if not v.model.finished
        ]

        # Smoke MVC
        for c in self.smoke_controllers:
            c.update()
        self.smoke_controllers = [
            c for c in self.smoke_controllers if not c.model.is_finished()
        ]
        self.smoke_views = [
            v for v in self.smoke_views if not v.model.is_finished()
        ]

        # SmokeEmitter MVC
        for c in self.smoke_emitter_controllers:
            wind_x = (pygame.mouse.get_pos()[0] - self.state.screen.get_width()//2) / 1000
            c.apply_force(Vector2(wind_x, 0))
            c.update()
        self.smoke_emitter_controllers = [
            c for c in self.smoke_emitter_controllers if not c.model.is_empty()
        ]
        self.smoke_emitter_views = [
            v for v in self.smoke_emitter_views if not v.model.is_empty()
        ]

        # FireworkLaunch MVC
        for c in self.firework_controllers:
            c.update()
        self.firework_controllers = [
            c for c in self.firework_controllers if not c.model.finished
        ]
        self.firework_views = [
            v for v in self.firework_views if not v.model.finished
        ]

        # Fireball MVC
        for c in self.fireball_controllers:
            c.update()
        self.fireball_controllers = [
            c for c in self.fireball_controllers if c.m.alive
        ]
        self.fireball_views = [
            v for v in self.fireball_views if v.m.alive
        ]

        # Lightning MVC
        for c in self.lightning_controllers:
            c.update()
        # Filtrar terminados
        self.lightning_controllers = [c for c in self.lightning_controllers if not c.finished]
        self.lightning_views       = [v for v in self.lightning_views       if not v.m.is_finished]

        # ArcaneFlame MVC
        for c in self.arcane_controllers:
            c.update()
        self.arcane_controllers = [c for c in self.arcane_controllers if not c.is_finished()]
        self.arcane_views       = [v for v in self.arcane_views       if not v.m.is_finished()]

        # SphereMagicShield MVC
        for c in self.shield_controllers:
            c.update()
        self.shield_controllers = [c for c in self.shield_controllers if not c.is_finished()]
        self.shield_views       = [v for v in self.shield_views       if not v.m.is_finished()]

        # Teleport MVC
        for c in self.teleport_controllers:
            c.update()
        self.teleport_controllers = [
            c for c in self.teleport_controllers if not c.is_finished()
        ]
        self.teleport_views = [
            v for v in self.teleport_views if not v.m.is_finished()
        ]

        # Legacy: Dash Trails
        for t in self.dash_trails:
            t.update()
        self.dash_trails = [t for t in self.dash_trails if not t.is_finished()]

        # Legacy: Dash Bounces
        for b in self.dash_bounces:
            b.update()
        self.dash_bounces = [b for b in self.dash_bounces if not b.is_finished()]

        # Legacy: Slash Effects
        for s in self.slash_effects:
            s.update()
        self.slash_effects = [s for s in self.slash_effects if not s.is_finished()]

    # ------------------------------------------------ #
    #                     Render                       #
    # ------------------------------------------------ #
    @benchmark(lambda self: self.state.perf_log, "----3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        # MVC: LaserBeam
        for v in self.laser_views:
            v.render(screen, camera)
        # MVC: Smoke
        for v in self.smoke_views:
            v.render(screen, camera)
        # MVC: SmokeEmitter
        for v in self.smoke_emitter_views:
            v.render(screen, camera)
        # MVC: FireworkLaunch
        for v in self.firework_views:
            v.render(screen, camera)
        # MVC: Fireball
        for v in self.fireball_views:
            v.render(screen, camera)
        # MVC: HealingAura
        for v in self.healing_views:
            v.render(screen, camera)
        # MVC: Lightning
        for v in self.lightning_views:
            if (d := v.render(screen, camera)):
                dirty_rects.append(d)
        # MVC: ArcaneFlame
        for v in self.arcane_views:
            v.render(screen, camera)
        # MVC: SphereMagicShield
        for v in self.shield_views:
            if d := v.render(screen, camera):
                dirty_rects.append(d)    
        # MVC: Teleport
        for v in self.teleport_views:
            v.render(screen, camera)      

        # Legacy effects
        for e in self.slash_effects:
            if (d := e.render(screen, camera)):
                dirty_rects.append(d)
        for t in self.dash_trails:
            if (d := t.render(screen, camera)):
                dirty_rects.append(d)
        for b in self.dash_bounces:
            if (d := b.render(screen, camera)):
                dirty_rects.append(d)

        return dirty_rects

    def _player_center(self):
        p = self.state.player
        return (p.x + p.sprite_size[0] // 2, p.y + p.sprite_size[1] // 2)
