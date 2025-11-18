import time
import pygame
from roguelike_engine.utils.benchmark import benchmark

class FlashSystem:
    """
    Sistema que aplica un flash de color a entidades con FlashComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        comps = world.components
        sprite_map = comps.get('Sprite', {})
        if not sprite_map:
            return
        flash_map = comps.get('FlashComponent', {})
        burn_map = comps.get('BurnComponent', {})
        stun_map = comps.get('StunComponent', {})
        poison_map = comps.get('PoisonComponent', {})

        now = time.time()
        # Candidatos: cualquier entidad con flash puntual o con estados
        candidates = set(flash_map.keys()) | set(burn_map.keys()) | set(stun_map.keys()) | set(poison_map.keys())

        for eid in list(candidates):
            sprite = sprite_map.get(eid)
            if not sprite:
                continue
            # Mantener una base limpia por entidad usando un flag de si el frame anterior fue teñido
            try:
                was_tinted = bool(getattr(sprite, '_flash_was_tinted', False))
                orig = getattr(sprite, '_flash_orig', None)
                if not was_tinted:
                    # Confiamos en que sprite.image proviene del animator (frame limpio)
                    if orig is None or orig.get_size() != sprite.image.get_size():
                        setattr(sprite, '_flash_orig', sprite.image.copy())
                        orig = getattr(sprite, '_flash_orig', None)
                # Siempre restaurar a base limpia al inicio del frame
                if orig is not None:
                    sprite.image = orig.copy()
            except Exception:
                was_tinted = False
                orig = None

            color = None
            blink_interval = 0.0

            # Prioridad: Stun > Burn > Poison > FlashComponent (blanco)
            if eid in stun_map:
                # Amarillo mientras dure el stun
                try:
                    st = stun_map[eid]
                    start = float(getattr(st, 'start_time', now))
                    dur = float(getattr(st, 'duration', 0.0))
                    if now >= start + dur:
                        # Expirado: no aplicar
                        pass
                    else:
                        color = (255, 255, 0)
                        blink_interval = 0.125  # ~8 Hz
                        elapsed = max(0.0, now - start)
                        if int(elapsed / blink_interval) % 2 != 0:
                            color = None
                except Exception:
                    # Fallback de parpadeo fijo
                    color = (255, 255, 0)
                    blink_interval = 0.125
                    # Usar fase global sencilla
                    if int(now / blink_interval) % 2 != 0:
                        color = None
            elif eid in burn_map:
                # Rojo mientras haya quemadura
                try:
                    bc = burn_map[eid]
                    start = float(getattr(bc, 'start_time', 0.0))
                    tick = float(getattr(bc, 'tick_period', 1.0)) or 1.0
                    blink_interval = max(0.1, min(0.25, tick / 2.0))
                    elapsed = max(0.0, now - start)
                    if int(elapsed / blink_interval) % 2 == 0:
                        color = (255, 64, 64)
                except Exception:
                    # Fallback suave
                    if int(now / 0.2) % 2 == 0:
                        color = (255, 64, 64)
            elif eid in poison_map:
                # Verde mientras haya veneno (si existe dicho componente en el mundo)
                try:
                    pc = poison_map[eid]
                    start = float(getattr(pc, 'start_time', now))
                    tick = float(getattr(pc, 'tick_period', 1.0)) or 1.0
                    blink_interval = max(0.1, min(0.25, tick / 2.0))
                    elapsed = max(0.0, now - start)
                    if int(elapsed / blink_interval) % 2 == 0:
                        color = (64, 255, 64)
                except Exception:
                    if int(now / 0.2) % 2 == 0:
                        color = (64, 255, 64)
            elif eid in flash_map:
                # Blanco por daño puntual
                try:
                    fc = flash_map[eid]
                    start = float(getattr(fc, 'start_time', now))
                    dur = float(getattr(fc, 'duration', 0.0))
                    elapsed = max(0.0, now - start)
                    if elapsed >= dur:
                        flash_map.pop(eid, None)
                    else:
                        blink_interval = max(1e-6, dur / 4.0)
                        if int(elapsed / blink_interval) % 2 == 0:
                            c = getattr(fc, 'color', (255, 255, 255))
                            color = (int(c[0]), int(c[1]), int(c[2]))
                except Exception:
                    # En caso de error, expirar el flash puntual para evitar quedarse pegado
                    flash_map.pop(eid, None)

            try:
                base_surface = orig if orig is not None else sprite.image
                if color is None:
                    # Sin flash: restaurar base limpia y marcar no-teñido
                    try:
                        sprite.image = base_surface.copy()
                        setattr(sprite, '_flash_was_tinted', False)
                    except Exception:
                        pass
                else:
                    # Con flash: tintar copia de la base y marcar teñido
                    img = base_surface.copy()
                    img.fill(color, special_flags=pygame.BLEND_RGB_ADD)
                    sprite.image = img
                    try:
                        setattr(sprite, '_flash_was_tinted', True)
                    except Exception:
                        pass
            except Exception:
                pass