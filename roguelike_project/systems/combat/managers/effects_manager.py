import pygame
from pygame.math import Vector2

# Attack Effects
from roguelike_project.systems.combat.effects.laser_beam import LaserBeam
from roguelike_project.systems.combat.effects.lightning import Lightning

# Auras
from roguelike_project.systems.combat.effects.healing_aura import HealingAura
from roguelike_project.utils.benchmark import benchmark

# Other Effects
from roguelike_project.systems.combat.effects.smoke_emitter import SmokeEmitter
from roguelike_project.systems.combat.effects.sphere_magic_shield import SphereMagicShield

class EffectsManager:
    def __init__(self, state):
        self.state = state
        self.lasers = []        
        self.smoke_emitters = []
        self.lightnings = []
        self.healing_auras = []
        self.magic_shields = []

        self.shooting_laser = False
        self.last_laser_time = 0

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

    @benchmark(lambda self: self.state.perf_log, "----3.6.2 effects_render")
    def render(self, screen, camera):
        dirty_rects = []

        for group in [self.lasers,self.magic_shields, self.healing_auras, self.smoke_emitters, self.lightnings]:
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
