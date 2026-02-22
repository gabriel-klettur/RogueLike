import math
import pygame

class ChatProximityRenderSystem:
    """
    Dibuja un halo/círculo amarillo alrededor de los NPCs con ChatComponent
    cuando el jugador está dentro de su chat_range. Esto sirve como feedback
    visual de que un clic izquierdo abrirá el chat.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Pequeña animación de pulso para dar visibilidad
        self._pulse_t = 0.0

    def update(self, world, screen, camera):
        comps = world.components
        if 'ChatComponent' not in comps or 'Position' not in comps:
            return
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        player_pos = comps['Position'].get(player_eid)
        if player_pos is None:
            return

        # Avanzar pulso (suave)
        try:
            self._pulse_t = (self._pulse_t + 0.12) % (2 * math.pi)
        except Exception:
            self._pulse_t = 0.0

        zoom = getattr(camera, 'zoom', 1.0) or 1.0

        for eid in world.get_entities_with('ChatComponent', 'Position'):
            npc_pos = comps['Position'].get(eid)
            chat = comps['ChatComponent'].get(eid)
            if not npc_pos or not chat:
                continue
            # Distancia jugador-npc
            dx = float(getattr(npc_pos, 'x', 0.0)) - float(getattr(player_pos, 'x', 0.0))
            dy = float(getattr(npc_pos, 'y', 0.0)) - float(getattr(player_pos, 'y', 0.0))
            dist = math.hypot(dx, dy)
            rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
            if dist <= rng:
                # Centro en el sprite (con Scale) si es posible; fallback: collider 'feet' o posición
                wx = float(getattr(npc_pos, 'x', 0.0))
                wy = float(getattr(npc_pos, 'y', 0.0))
                spr_cx = spr_cy = None
                base_size = None
                try:
                    sprite_map = comps.get('Sprite', {}) or {}
                    scale_map = comps.get('Scale', {}) or {}
                    sprite = sprite_map.get(eid)
                    scale_comp = scale_map.get(eid)
                    scl = float(getattr(scale_comp, 'scale', 1.0) or 1.0)
                    if sprite and hasattr(sprite, 'image') and sprite.image:
                        sw, sh = sprite.image.get_size()
                        spr_cx = wx + (sw * scl) / 2.0
                        spr_cy = wy + (sh * scl) / 2.0
                        base_size = min(sw, sh) * scl
                except Exception:
                    spr_cx = spr_cy = None
                    base_size = None
                # Fallback a collider de 'feet'
                feet_cx = feet_cy = None
                feet_r = None
                multi = None
                try:
                    multi_map = comps.get('MultiCollider', {})
                    multi = multi_map.get(eid) if isinstance(multi_map, dict) else None
                    if multi and hasattr(multi, 'colliders'):
                        feet = multi.colliders.get('feet')
                        if feet is not None:
                            if hasattr(feet, 'offset_x') and hasattr(feet, 'offset_y'):
                                feet_cx = wx + float(feet.offset_x)
                                feet_cy = wy + float(feet.offset_y)
                            if hasattr(feet, 'radius'):
                                feet_r = float(getattr(feet, 'radius', 0.0) or 0.0)
                except Exception:
                    feet_cx = feet_cy = None
                    feet_r = None
                # Elegir centro mundial
                world_cx = spr_cx if spr_cx is not None else (feet_cx if feet_cx is not None else wx)
                world_cy = spr_cy if spr_cy is not None else (feet_cy if feet_cy is not None else wy)
                # Posición de pantalla del centro elegido
                cx, cy = camera.apply((world_cx, world_cy))
                # 1) Círculo de rango real (radio = chat_range)
                r_px = max(1, int(rng * zoom))
                size = r_px * 2
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                # Relleno muy suave + borde marcado
                pygame.draw.circle(overlay, (255, 220, 0, 35), (r_px, r_px), r_px, width=0)
                pygame.draw.circle(overlay, (255, 220, 0, 160), (r_px, r_px), r_px, width=2)
                screen.blit(overlay, (cx - r_px, cy - r_px))

                # 2) Halo de foco alrededor del NPC (pequeño, efecto pulso)
                # Base del halo: usar tamaño del sprite si se conoce; si no, radio de pies; fallback fijo
                halo_r_world = None
                try:
                    if base_size is not None:
                        # Un cuarto del menor lado del sprite
                        halo_r_world = max(12.0, float(base_size) * 0.25)
                except Exception:
                    halo_r_world = None
                if halo_r_world is None:
                    try:
                        if feet_r is not None:
                            halo_r_world = float(feet_r)
                    except Exception:
                        halo_r_world = None
                if halo_r_world is None:
                    halo_r_world = 18.0
                # Ligeramente más grande para facilitar clic (10% extra)
                r = max(6, int(halo_r_world * 1.1 * zoom))
                pulse = (math.sin(self._pulse_t) + 1.0) * 0.5  # [0..1]
                width = max(2, int(2 + pulse * 2))
                alpha = int(140 + pulse * 80)  # 140..220
                halo = pygame.Surface((r * 2, r * 2), pygame.SRCALPHA)
                pygame.draw.circle(halo, (255, 220, 0, alpha), (r, r), r, width=width)
                screen.blit(halo, (cx - r, cy - r))
