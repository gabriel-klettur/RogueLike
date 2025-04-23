import pygame
from pygame.math import Vector2

# Visual-only effects
from roguelike_project.systems.effects.particles.spells.laser_beam import LaserBeam
from roguelike_project.systems.effects.particles.spells.lightning import Lightning
from roguelike_project.systems.effects.particles.spells.healing_aura import HealingAura
from roguelike_project.systems.effects.particles.spells.smoke_emitter import SmokeEmitter
from roguelike_project.systems.effects.particles.spells.sphere_magic_shield import SphereMagicShield
from roguelike_project.systems.effects.particles.spells.pixel_fire import PixelFireEffect
from roguelike_project.systems.effects.particles.spells.teleport_beam import TeleportBeamEffect
from roguelike_project.systems.effects.particles.spells.dash_trail import DashTrail
from roguelike_project.systems.effects.particles.spells.dash_bounce import DashBounce
from roguelike_project.systems.effects.particles.spells.slash_effect import SlashEffect

from roguelike_project.utils.benchmark import benchmark

class SpellsSystem:
    def __init__(self, state):
        self.state = state
        self.lasers = []        
        self.smoke_emitters = []
        self.lightnings = []
        self.healing_auras = []
        self.magic_shields = []
        self.pixel_fires = []
        self.teleport_beams = []
        self.dash_trails = []
        self.dash_bounces = []
        self.slash_effects = []

        self.shooting_laser = False
        self.last_laser_time = 0

    def spawn_slash_effect(self, player, direction):
        self.slash_effects.append(SlashEffect(player, direction))

    def spawn_dash_trail(self, player, direction):
        self.dash_trails.append(DashTrail(player, direction))

    def stop_dash_trails(self):
        for trail in self.dash_trails:
            trail.stop()

    def spawn_dash_bounce(self, x, y):
        self.dash_bounces.append(DashBounce(x, y))

    def spawn_teleport_beam(self, x, y):
        self.teleport_beams.append(TeleportBeamEffect(x, y))

    def spawn_pixel_fire(self, x, y):
        self.pixel_fires.append(PixelFireEffect(x, y))

    def spawn_magic_shield(self):
        self.magic_shields.append(SphereMagicShield(self.state.player))

    def spawn_laser(self, x, y, enemies):
        px = self._player_center()
        self.lasers.append(LaserBeam(px[0], px[1], x, y, enemies=enemies))
        if len(self.lasers) > 3:
            self.lasers.pop(0)

    def spawn_smoke_emitter(self):
        px = self._player_center()
        self.smoke_emitters.append(SmokeEmitter(*px))

    def spawn_lightning(self, target_pos):
        px = self._player_center()
        self.lightnings.append(Lightning(px, target_pos))

    def spawn_healing_aura(self):
        px = self._player_center()
        self.healing_auras.append(HealingAura(self.state.player))

    def update(self):
        # 🔁 Lasers
        for laser in self.lasers[:]:
            if not laser.update():
                self.lasers.remove(laser)

        # 🌫️ Smoke Emitters
        for emitter in self.smoke_emitters:
            wind_x = (pygame.mouse.get_pos()[0] - self.state.screen.get_width() // 2) / 1000
            emitter.apply_force(Vector2(wind_x, 0))
            emitter.update()
        self.smoke_emitters = [e for e in self.smoke_emitters if len(e.particles) > 0]

        # ⚡ Lightnings
        self.lightnings[:] = [l for l in self.lightnings if l.lifetime > 0]
        for lightning in self.lightnings:
            lightning.update()

        # 💖 Healing Auras                
        for aura in self.healing_auras:
            aura.update()
        self.healing_auras = [a for a in self.healing_auras if not a.is_empty()]

        for shield in self.magic_shields:
            shield.update()
        self.magic_shields = [s for s in self.magic_shields if not s.is_finished()]

        self.pixel_fires = [f for f in self.pixel_fires if not f.is_empty()]
        for fire in self.pixel_fires:
            fire.update()
        
        for beam in self.teleport_beams:
            beam.update()
        self.teleport_beams = [b for b in self.teleport_beams if not b.is_finished()]

        # 💨 Dash Trail
        for trail in self.dash_trails:
            trail.update()
        self.dash_trails = [t for t in self.dash_trails if not t.is_finished()]

        for b in self.dash_bounces:
            b.update()
        self.dash_bounces = [b for b in self.dash_bounces if not b.is_finished()]

        for effect in self.slash_effects:
            effect.update()
        self.slash_effects = [e for e in self.slash_effects if not e.is_finished()]

    @benchmark(lambda self: self.state.perf_log, "----3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        for group in [
            self.lasers,
            self.slash_effects,
            self.dash_trails,
            self.dash_bounces,
            self.teleport_beams,
            self.pixel_fires,
            self.magic_shields,
            self.healing_auras,
            self.smoke_emitters,
            self.lightnings
        ]:
            for effect in group:
                if (d := effect.render(screen, camera)):
                    dirty_rects.append(d)

        return dirty_rects

    def _player_center(self):
        p = self.state.player
        return (
            p.x + p.sprite_size[0] // 2,
            p.y + p.sprite_size[1] // 2
        )

    def _mouse_world(self):
        mx, my = pygame.mouse.get_pos()
        return (
            mx / self.state.camera.zoom + self.state.camera.offset_x,
            my / self.state.camera.zoom + self.state.camera.offset_y
        )
