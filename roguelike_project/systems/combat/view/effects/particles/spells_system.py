# File: roguelike_project/systems/combat/view/effects/particles/spells_system.py
import pygame
from pygame.math import Vector2

# MVC imports for LaserBeam
from roguelike_project.systems.combat.spells.laser_beam.model      import LaserBeamModel
from roguelike_project.systems.combat.spells.laser_beam.controller import LaserBeamController
from roguelike_project.systems.combat.spells.laser_beam.view       import LaserBeamView

# MVC imports for HealingAura
from roguelike_project.systems.combat.spells.healing_aura.model      import HealingAuraModel
from roguelike_project.systems.combat.spells.healing_aura.controller import HealingAuraController
from roguelike_project.systems.combat.spells.healing_aura.view       import HealingAuraView

# Other effect imports (views remain)
from roguelike_project.systems.combat.view.effects.particles.spells.lightning      import Lightning
from roguelike_project.systems.combat.view.effects.particles.spells.smoke_emitter import SmokeEmitter
from roguelike_project.systems.combat.view.effects.particles.spells.sphere_magic_shield import SphereMagicShield
from roguelike_project.systems.combat.view.effects.particles.spells.pixel_fire     import PixelFireEffect
from roguelike_project.systems.combat.view.effects.particles.spells.teleport_beam  import TeleportBeamEffect
from roguelike_project.systems.combat.view.effects.particles.spells.dash_trail     import DashTrail
from roguelike_project.systems.combat.view.effects.particles.spells.dash_bounce   import DashBounce
from roguelike_project.systems.combat.view.effects.particles.spells.slash_effect  import SlashEffect

from roguelike_project.utils.benchmark import benchmark

class SpellsSystem:
    def __init__(self, state):
        self.state = state
        # Laser MVC
        self.laser_controllers: list[LaserBeamController] = []
        self.laser_views:       list[LaserBeamView]       = []
        # Legacy lists for other effects
        self.smoke_emitters = []
        self.lightnings    = []
        # Healing MVC
        self.healing_controllers: list[HealingAuraController] = []
        self.healing_views:       list[HealingAuraView]       = []
        self.magic_shields = []
        self.pixel_fires   = []
        self.teleport_beams = []
        self.dash_trails   = []
        self.dash_bounces  = []
        self.slash_effects = []
        # Continuous laser state
        self.shooting_laser = False
        self.last_laser_time = 0

    # ------------------------------------------- #
    #  Métodos para instanciar nuevos efectos    #
    # ------------------------------------------- #
    def spawn_slash_effect(self, player, direction):
        self.slash_effects.append(SlashEffect(player, direction))

    def spawn_laser(self, x, y, enemies):
        px, py = self._player_center()
        model = LaserBeamModel(px, py, x, y, enemies=enemies)
        ctrl  = LaserBeamController(model)
        view  = LaserBeamView(model)
        self.laser_controllers.append(ctrl)
        self.laser_views.append(view)
        # Limitar a últimos 3 rayos
        if len(self.laser_controllers) > 3:
            self.laser_controllers.pop(0)
            self.laser_views.pop(0)

    def spawn_smoke_emitter(self):
        px, py = self._player_center()
        self.smoke_emitters.append(SmokeEmitter(px, py))

    def spawn_lightning(self, target_pos):
        px, py = self._player_center()
        self.lightnings.append(Lightning((px, py), target_pos))

    def spawn_healing_aura(self):
        model = HealingAuraModel(self.state.player)
        ctrl  = HealingAuraController(model, self.state.clock)
        view  = HealingAuraView(model)
        self.healing_controllers.append(ctrl)
        self.healing_views.append(view)

    def spawn_magic_shield(self):
        self.magic_shields.append(SphereMagicShield(self.state.player))

    def spawn_pixel_fire(self, x, y):
        self.pixel_fires.append(PixelFireEffect(x, y))

    def spawn_teleport_beam(self, x, y):
        self.teleport_beams.append(TeleportBeamEffect(x, y))

    def spawn_dash_trail(self, player, direction):
        self.dash_trails.append(DashTrail(player, direction))

    def stop_dash_trails(self):
        for trail in self.dash_trails:
            trail.stop()

    def spawn_dash_bounce(self, x, y):
        self.dash_bounces.append(DashBounce(x, y))

    # ------------------------------------------- #
    #               Actualizar todos             #
    # ------------------------------------------- #
    def update(self):
        # MVC: LaserBeam
        for ctrl in self.laser_controllers:
            ctrl.update()
        # Limpiar finalizados
        self.laser_controllers = [c for c in self.laser_controllers if not c.model.is_finished()]
        self.laser_views       = [v for v in self.laser_views       if not v.model.is_finished()]

        # Smoke Emitters
        for emitter in self.smoke_emitters:
            wind_x = (pygame.mouse.get_pos()[0] - self.state.screen.get_width() // 2) / 1000
            emitter.apply_force(Vector2(wind_x, 0))
            emitter.update()
        self.smoke_emitters = [e for e in self.smoke_emitters if len(e.particles) > 0]

        # Lightnings
        for lightning in self.lightnings:
            lightning.update()
        self.lightnings = [l for l in self.lightnings if l.lifetime > 0]

        # MVC: HealingAura
        for ctrl in self.healing_controllers:
            ctrl.update()
        self.healing_controllers = [c for c in self.healing_controllers if not c.model.is_empty()]
        self.healing_views       = [v for v in self.healing_views       if not v.model.is_empty()]

        # Magic Shields
        for shield in self.magic_shields:
            shield.update()
        self.magic_shields = [s for s in self.magic_shields if not s.is_finished()]

        # Pixel Fires
        for fire in self.pixel_fires:
            fire.update()
        self.pixel_fires = [f for f in self.pixel_fires if not f.is_empty()]

        # Teleport Beams
        for beam in self.teleport_beams:
            beam.update()
        self.teleport_beams = [b for b in self.teleport_beams if not b.is_finished()]

        # Dash Trails
        for trail in self.dash_trails:
            trail.update()
        self.dash_trails = [t for t in self.dash_trails if not t.is_finished()]

        # Dash Bounces
        for bounce in self.dash_bounces:
            bounce.update()
        self.dash_bounces = [b for b in self.dash_bounces if not b.is_finished()]

        # Slash Effects
        for slash in self.slash_effects:
            slash.update()
        self.slash_effects = [s for s in self.slash_effects if not s.is_finished()]

    # ------------------------------------------- #
    #               Renderizar todos             #
    # ------------------------------------------- #
    @benchmark(lambda self: self.state.perf_log, "----3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        # MVC: LaserBeam (no dirty rects)
        for vw in self.laser_views:
            vw.render(screen, camera)

        # Slash Effects
        for effect in self.slash_effects:
            if (d := effect.render(screen, camera)):
                dirty_rects.append(d)

        # Dash Trails
        for trail in self.dash_trails:
            if (d := trail.render(screen, camera)):
                dirty_rects.append(d)

        # Dash Bounces
        for bounce in self.dash_bounces:
            if (d := bounce.render(screen, camera)):
                dirty_rects.append(d)

        # Teleport Beams
        for beam in self.teleport_beams:
            if (d := beam.render(screen, camera)):
                dirty_rects.append(d)

        # Pixel Fires
        for fire in self.pixel_fires:
            if (d := fire.render(screen, camera)):
                dirty_rects.append(d)

        # Magic Shields
        for shield in self.magic_shields:
            if (d := shield.render(screen, camera)):
                dirty_rects.append(d)

        # MVC: HealingAura (no dirty rects)
        for vw in self.healing_views:
            vw.render(screen, camera)

        # Smoke Emitters
        for emitter in self.smoke_emitters:
            if (d := emitter.render(screen, camera)):
                dirty_rects.append(d)

        # Lightnings
        for lightning in self.lightnings:
            if (d := lightning.render(screen, camera)):
                dirty_rects.append(d)

        return dirty_rects

    def _player_center(self):
        p = self.state.player
        return (p.x + p.sprite_size[0] // 2, p.y + p.sprite_size[1] // 2)
