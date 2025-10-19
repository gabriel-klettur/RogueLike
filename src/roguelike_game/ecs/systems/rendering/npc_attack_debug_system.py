import math
import time
import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.position_utils import compute_entity_center


class NpcAttackDebugSystem:
    """
    Visualiza la traza de daño de NPCs hacia el Player.
    Consume world.components['DebugAttackEvents']["_queue"].
    Dibuja:
      - NPC_MELEE: línea roja desde origen a Player + puntos en extremos
      - NPC_HITBOX_HIT: arco (huella de hitbox) en verde + línea amarilla al impacto
    Mantiene marcadores con desvanecimiento temporal.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._markers = []  # dicts: {kind, data, color, label, t_end}
        self._font = None

    def _ensure_font(self):
        if self._font is None:
            try:
                self._font = pygame.font.SysFont(None, 14)
            except Exception:
                self._font = None

    def _add_marker(self, kind, data, color, label=None, duration=6.0):
        self._markers.append({
            'kind': kind,
            'data': data,
            'color': color,
            'label': label,
            't_end': time.time() + duration,
        })

    def _render_markers(self, screen, camera):
        now = time.time()
        self._markers = [m for m in self._markers if m['t_end'] > now]
        if not self._markers:
            return
        self._ensure_font()
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        for m in self._markers:
            t_left = max(0.0, m['t_end'] - now)
            frac = min(1.0, t_left / 6.0)
            r, g, b = m['color']
            alpha = int(40 + 180 * frac)
            kind = m['kind']
            if kind == 'line':
                (x1, y1), (x2, y2) = m['data']
                p1 = camera.apply((x1, y1))
                p2 = camera.apply((x2, y2))
                pygame.draw.line(overlay, (r, g, b, alpha), p1, p2, 2)
                if self._font and m.get('label'):
                    lx = (p1[0] + p2[0]) / 2
                    ly = (p1[1] + p2[1]) / 2
                    txt = self._font.render(m['label'], True, (r, g, b))
                    overlay.blit(txt, (int(lx) + 4, int(ly) - 10))
            elif kind == 'point':
                x, y, rr = m['data']
                sx, sy = camera.apply((x, y))
                rad = max(2, int(rr * camera.zoom))
                pygame.draw.circle(overlay, (r, g, b, alpha), (int(sx), int(sy)), rad)
            elif kind == 'arc':
                # data: (cx, cy, radius, start_ang, end_ang)
                cx, cy, radius, start_ang, end_ang = m['data']
                left = cx - radius
                top = cy - radius
                sx, sy = camera.apply((left, top))
                rr = int(radius * camera.zoom)
                if rr > 0:
                    rect = pygame.Rect(int(sx), int(sy), rr * 2, rr * 2)
                    pygame.draw.arc(overlay, (r, g, b, alpha), rect, start_ang, end_ang, 2)
                if self._font and m.get('label'):
                    txt = self._font.render(m['label'], True, (r, g, b))
                    overlay.blit(txt, (int(sx) + 2, int(sy) - 12))
        screen.blit(overlay, (0, 0))

    def update(self, world, screen, camera):
        if not getattr(config, 'DEBUG', False):
            return
        dbg = world.components.get('DebugAttackEvents', {})
        queue = dbg.get('_queue', []) if isinstance(dbg, dict) else []
        if queue:
            for ev in queue:
                et = ev.get('type')
                if et == 'NPC_MELEE':
                    posA = ev.get('posA')
                    posB = ev.get('posB')
                    dmg = ev.get('damage', 0)
                    if posA and posB:
                        # Línea roja origen->impacto
                        self._add_marker('line', (tuple(posA), tuple(posB)), (255, 60, 60), label=f"DMG {int(dmg)}")
                        # Puntos en extremos
                        self._add_marker('point', (posA[0], posA[1], 2.0), (255, 100, 100))
                        self._add_marker('point', (posB[0], posB[1], 2.0), (255, 100, 100))
                elif et == 'NPC_HITBOX_HIT':
                    posA = ev.get('posA')  # atacante o centro del hb owner
                    posB = ev.get('posB')  # player
                    hb_center = ev.get('hb_center')
                    r = float(ev.get('hb_radius', 0.0))
                    arc = float(ev.get('arc_angle', 0.0))
                    direction = ev.get('direction', (1.0, 0.0))
                    dmg = ev.get('damage', 0)
                    if posA and posB:
                        # Línea amarilla del hitbox/origen al impacto
                        self._add_marker('line', (tuple(posA), tuple(posB)), (255, 220, 0), label=f"HB {int(dmg)}")
                        self._add_marker('point', (posB[0], posB[1], 2.5), (255, 220, 0))
                    # Arco de la huella del hitbox si tenemos centro y radio
                    if hb_center and r > 0 and arc > 0:
                        dx, dy = float(direction[0]), float(direction[1])
                        ang_center = math.atan2(dy, dx)
                        start_ang = ang_center - arc / 2
                        end_ang = ang_center + arc / 2
                        self._add_marker('arc', (float(hb_center[0]), float(hb_center[1]), r, start_ang, end_ang), (0, 255, 0), label='HB')
            # limpiar después de consumir
            queue.clear()
        # Renderizar marcadores persistentes
        self._render_markers(screen, camera)
        # Dibujar círculo de rango melee para NPCs con MeleeRange (ayuda a validar separación)
        try:
            comps = world.components
            pos_map = comps.get('Position', {})
            spr_map = comps.get('Sprite', {})
            scl_map = comps.get('Scale', {})
            melee_map = comps.get('MeleeRange', {})
            if melee_map:
                overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                for eid, mr in melee_map.items():
                    pos = pos_map.get(eid)
                    if not pos:
                        continue
                    spr = spr_map.get(eid)
                    scl = scl_map.get(eid)
                    try:
                        if spr:
                            c = compute_entity_center(pos, spr, scl)
                            cx, cy = float(c.x), float(c.y)
                        else:
                            cx, cy = float(pos.x), float(pos.y)
                        r = float(getattr(mr, 'range', 0.0)) * float(getattr(__import__('roguelike_engine.config.config_tiles', fromlist=['TILE_SIZE']), 'TILE_SIZE', 32))
                        if r <= 0:
                            continue
                        sx, sy = camera.apply((cx, cy))
                        rr = int(max(1, r * camera.zoom))
                        pygame.draw.circle(overlay, (0, 180, 255, 100), (int(sx), int(sy)), rr, 1)
                    except Exception:
                        continue
                screen.blit(overlay, (0, 0))
        except Exception:
            pass
