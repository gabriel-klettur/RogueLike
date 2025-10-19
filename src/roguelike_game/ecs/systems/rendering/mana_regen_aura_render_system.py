import pygame
import math
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.mana import Mana
from roguelike_game.ecs.components.fsm.npc_state import NPCState
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent


class ManaRegenAuraRenderSystem:
    """
    Dibuja un aura azul alrededor del jugador SOLO cuando:
    - Tiene componente Mana
    - current_mana < max_mana
    - Su FSM (NPCState) está en IdleState (regenerando)

    El aura es un pulso suave en tonos azules, centrado en el sprite del jugador.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Estilo del aura
        self.base_color = (90, 150, 255)
        self.max_alpha = 140
        self.min_alpha = 60
        self.thickness = 2
        # Cache de superficies de contorno por (frame_id, scale_key)
        self._outline_cache: dict[tuple[int, int], pygame.Surface] = {}

    def update(self, world, screen, camera):
        comps = world.components
        players = comps.get('PlayerTagComponent', {})
        if not players:
            return
        pos_map = comps.get('Position', {})
        spr_map = comps.get('Sprite', {})
        scale_map = comps.get('Scale', {})
        mana_map = comps.get('Mana', {})
        npc_map = comps.get('NPCState', {})

        # Solo evaluamos el jugador (si hay múltiples, iteramos todos los PlayerTagComponent)
        for eid in list(players.keys()):
            pos: Position = pos_map.get(eid)
            spr: Sprite = spr_map.get(eid)
            mana: Mana = mana_map.get(eid)
            npc: NPCState = npc_map.get(eid)
            if not (pos and spr and mana and npc):
                continue
            # Condiciones de regeneración: Idle/Cooldown o quieto sin castear/atacar, y mana incompleto
            fsm = getattr(npc, 'fsm', None)
            cur_name = fsm.current_state.__class__.__name__ if fsm and getattr(fsm, 'current_state', None) else None
            allow = False
            if cur_name and (('Idle' in cur_name) or ('Cooldown' in cur_name)):
                allow = True
            else:
                vel: Velocity = world.components.get('Velocity', {}).get(eid)
                still = (not vel) or (getattr(vel, 'vx', 0) == 0 and getattr(vel, 'vy', 0) == 0)
                disallow_states = ('Prepare', 'Channel', 'Cast', 'Attack')
                in_disallowed = any(p in (cur_name or '') for p in disallow_states)
                allow = still and not in_disallowed
            if not allow:
                continue
            if getattr(mana, 'current_mana', 0) >= getattr(mana, 'max_mana', 0):
                continue

            try:
                # Pulsación de alpha
                t = pygame.time.get_ticks() / 1000.0
                pulse = 0.5 + 0.5 * math.sin(t * 4.0)
                alpha = int(self.min_alpha + (self.max_alpha - self.min_alpha) * pulse)

                # Posición de dibujo (top-left del sprite)
                draw_x, draw_y = camera.apply((pos.x, pos.y))
                # Escala de entidad
                entity_scale = getattr(scale_map.get(eid), 'scale', 1.0) if isinstance(scale_map.get(eid), Scale) else 1.0
                # Cache key por frame y escala
                frame_id = id(spr.image)
                scale_key = max(1, int(entity_scale * 100))
                cache_key = (frame_id, scale_key)
                aura = self._outline_cache.get(cache_key)
                if aura is None:
                    # Generar contorno desde la máscara del sprite
                    base_img: pygame.Surface = spr.image
                    mw, mh = base_img.get_size()
                    if mw <= 0 or mh <= 0:
                        continue
                    mask = pygame.mask.from_surface(base_img)
                    outline = mask.outline()
                    if not outline:
                        # Si no hay contorno, fallback a elipse ligera
                        aura = pygame.Surface((mw, mh), pygame.SRCALPHA)
                        pygame.draw.ellipse(aura, (*self.base_color, 180), (0, mh*0.5, mw, mh*0.5), self.thickness)
                    else:
                        # Dibujar el contorno en una surface base
                        base = pygame.Surface((mw, mh), pygame.SRCALPHA)
                        # Línea cerrada
                        pygame.draw.polygon(base, (*self.base_color, 255), outline, self.thickness)
                        # Suavizado simple: un segundo contorno más suave interior
                        if self.thickness > 1:
                            pygame.draw.polygon(base, (*self.base_color, 120), outline, max(1, self.thickness - 1))
                        aura = base
                    # Escalar según entity_scale si no es 1.0
                    if abs(entity_scale - 1.0) > 1e-3:
                        tw = max(1, int(mw * entity_scale))
                        th = max(1, int(mh * entity_scale))
                        aura = pygame.transform.smoothscale(aura, (tw, th))
                    self._outline_cache[cache_key] = aura
                # Aplicar alpha por pulso (superficie entera)
                aura.set_alpha(alpha)
                screen.blit(aura, (int(draw_x), int(draw_y)))
            except Exception:
                # Fallback rápido: círculo suave como antes
                sx, sy = camera.apply((pos.x, pos.y))
                entity_scale = getattr(scale_map.get(eid), 'scale', 1.0) if isinstance(scale_map.get(eid), Scale) else 1.0
                sw, sh = spr.image.get_size()
                sw = int(sw * entity_scale)
                radius = max(10, int(sw * 0.65))
                t = pygame.time.get_ticks() / 1000.0
                pulse = 0.5 + 0.5 * math.sin(t * 4.0)
                alpha = int(self.min_alpha + (self.max_alpha - self.min_alpha) * pulse)
                size = radius * 2 + self.thickness * 2
                surf = pygame.Surface((size, size), pygame.SRCALPHA)
                center = (size // 2, size // 2)
                pygame.draw.circle(surf, (*self.base_color, alpha), center, radius, self.thickness)
                screen.blit(surf, (int(sx + (sw - size) * 0.5), int(sy + (sh * entity_scale - size) * 0.5)))
