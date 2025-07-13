import time
import pygame
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent

class MagicSpellBarRenderSystem:
    """
    Sistema para renderizar la barra de progreso de hechizo en HUD,
    justo encima de la barra de experiencia.
    """
    def __init__(self, perf_log=None):
        pygame.font.init()
        self.perf_log = perf_log

    def update(self, world, screen, camera):
        # Render en espacio de mundo: barra sobre el jugador encima de la barra de salud
        bars = world.components.get('MagicSpellBarComponent', {})
        positions = world.components.get('Position', {})
        sprites = world.components.get('Sprite', {})
        scales = world.components.get('Scale', {})
        tags = world.components.get('PlayerTagComponent', {})
        current_time = time.time()
        for eid, bar in bars.items():
            if eid not in tags or not bar.active or bar.duration <= 0:
                continue
            pos = positions.get(eid)
            sprite = sprites.get(eid)
            if not pos or not sprite:
                continue
            # calcular escala y tamaño
            scale = scales.get(eid).scale if eid in scales else 1.0
            orig_w, orig_h = sprite.image.get_size()
            scaled_w = int(orig_w * scale)
            # coordenadas en pantalla
            screen_x, screen_y = camera.apply((pos.x, pos.y))
            # offset sobre la barra de salud (margin=2, hp_height=5)
            hp_margin = 2
            hp_bar_h = 5
            bar_h = 3
            spacing = 1
            bar_x = screen_x + scaled_w/2 - scaled_w/2
            bar_y = screen_y - hp_margin - hp_bar_h - spacing - bar_h
            # fondo
            pygame.draw.rect(screen, (50, 50, 50), (bar_x, bar_y, scaled_w, bar_h))
            # progreso
            ratio = min(max((current_time - bar.start_time) / bar.duration, 0.0), 1.0)
            if bar.state == 'prepare':
                color = (255, 255, 0)
            elif bar.state == 'channel':
                color = (255, 165, 0)
            else:
                color = (128, 128, 128)
            fill_w = int(scaled_w * ratio)
            pygame.draw.rect(screen, color, (bar_x, bar_y, fill_w, bar_h))
            # borde
            pygame.draw.rect(screen, (0, 0, 0), (bar_x, bar_y, scaled_w, bar_h), 1)
            break  # solo una barra por jugador
