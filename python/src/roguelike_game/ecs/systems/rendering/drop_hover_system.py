import os
import pygame
import math
import roguelike_game.config.players_config as players_config
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_ui.ui_helpers import draw_highlight_rect, draw_tooltip
from roguelike_ui.ui_blocker import is_blocked


class DropHoverRenderSystem:
    """
    Sistema de renderizado para mostrar información y resaltar
    ítems al hacer hover con el ratón sobre drops en el mapa.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        # Ignorar items_path, cargar desde SQLite
        self.items, _assets = ItemsLoader().load()
    
    def update(self, world, screen, camera):
        # Bloqueo genérico para cualquier panel UI
        mouse_x, mouse_y = pygame.mouse.get_pos()
        if is_blocked(mouse_x, mouse_y):
            return
        # Si estamos arrastrando (drop del mapa o ítem del inventario), resaltar jugador; círculo si el origen estaba fuera de rango
        try:
            drag_sys = None
            for s in getattr(world, 'update_systems', []):
                if (hasattr(s, 'dragging_eid') and getattr(s, 'dragging_eid', None) is not None) or \
                   (hasattr(s, 'dragging_idx') and getattr(s, 'dragging_idx', None) is not None):
                    drag_sys = s
                    break
            if drag_sys is not None:
                comps0 = world.components
                player = getattr(world, 'player_entity', None)
                if player is not None:
                    ppos = comps0.get('Position', {}).get(player)
                    pspr = comps0.get('Sprite', {}).get(player)
                    if ppos and pspr:
                        pscale_comp = comps0.get('Scale', {}).get(player)
                        pscale = pscale_comp.scale if pscale_comp else 1.0
                        pw, ph = pspr.image.get_size()
                        pw = int(pw * pscale * camera.zoom)
                        ph = int(ph * pscale * camera.zoom)
                        psx, psy = camera.apply((ppos.x, ppos.y))
                        prect = pygame.Rect(psx, psy, pw, ph).inflate(12, 12)
                        if prect.collidepoint(mouse_x, mouse_y):
                            # Calcular si el origen del drag estaba fuera de rango (solo aplica si el drag tiene drag_origin)
                            out_of_range = False
                            try:
                                cls = getattr(getattr(world, 'state', None), 'current_player_class', None) or players_config.PLAYER_CFG.get("DEFAULT_CLASS")
                                stats = players_config.PLAYER_STATS.get(cls, {}) or {}
                                rng = float(stats.get('drag_drop_range', 128))
                                if hasattr(drag_sys, 'drag_origin') and drag_sys.drag_origin is not None:
                                    # Centro jugador (coords mundo)
                                    jw, jh = pspr.image.get_size()
                                    jw = jw * (pscale_comp.scale if pscale_comp else 1.0)
                                    jh = jh * (pscale_comp.scale if pscale_comp else 1.0)
                                    jcx = ppos.x + jw * 0.5
                                    jcy = ppos.y + jh * 0.5
                                    dx = drag_sys.drag_origin[0] - jcx
                                    dy = drag_sys.drag_origin[1] - jcy
                                    out_of_range = math.hypot(dx, dy) > rng
                            except Exception:
                                out_of_range = False
                            # Determinar si el drag YA está activo (post-umbral)
                            is_active_drag = (
                                (hasattr(drag_sys, 'dragging_eid') and getattr(drag_sys, 'dragging_eid', None) is not None) or
                                (hasattr(drag_sys, 'dragging_idx') and getattr(drag_sys, 'dragging_idx', None) is not None)
                            )
                            # Siempre dibujar el borde del jugador mientras el cursor está encima
                            now_ts = pygame.time.get_ticks()
                            t = now_ts / 1000.0
                            s = (math.sin(2.0 * math.pi * 2.0 * t) + 1.0) * 0.5  # 2 Hz
                            base_alpha, max_alpha = 120, 230
                            base_th, max_th = 2, 6
                            alpha = int(base_alpha + (max_alpha - base_alpha) * s)
                            thickness = int(base_th + (max_th - base_th) * s)
                            border_overlay = pygame.Surface((pw, ph), pygame.SRCALPHA)
                            border_color = (80, 220, 120, alpha) if is_active_drag else (255, 215, 0, alpha)
                            pygame.draw.rect(border_overlay, border_color, border_overlay.get_rect(), max(1, thickness))
                            screen.blit(border_overlay, (psx, psy))
                            # Círculo de rango solo si está fuera de rango
                            if out_of_range:
                                scx, scy = camera.apply((jcx, jcy))
                                radius_screen = max(1, int(rng * camera.zoom))
                                circle_overlay = pygame.Surface((radius_screen * 2 + 2, radius_screen * 2 + 2), pygame.SRCALPHA)
                                circle_color = (80, 220, 120, alpha) if is_active_drag else (255, 215, 0, alpha)
                                pygame.draw.circle(circle_overlay, circle_color, (radius_screen + 1, radius_screen + 1), radius_screen, max(1, thickness))
                                screen.blit(circle_overlay, (int(scx - radius_screen - 1), int(scy - radius_screen - 1)))
        except Exception:
            pass
        
        # Si estamos arrastrando (inventario o drop) y el mouse está FUERA de rango para soltar, dibujar círculo de rango
        try:
            drag_active = False
            for s in getattr(world, 'update_systems', []):
                if (hasattr(s, 'dragging_eid') and getattr(s, 'dragging_eid', None) is not None) or \
                   (hasattr(s, 'dragging_idx') and getattr(s, 'dragging_idx', None) is not None):
                    drag_active = True
                    break
            if drag_active:
                player = getattr(world, 'player_entity', None)
                comps0 = world.components
                if player is not None:
                    ppos = comps0.get('Position', {}).get(player)
                    pspr = comps0.get('Sprite', {}).get(player)
                    pscale_comp = comps0.get('Scale', {}).get(player)
                    if ppos and pspr:
                        jscale = pscale_comp.scale if pscale_comp else 1.0
                        jw, jh = pspr.image.get_size()
                        jcx = ppos.x + jw * jscale * 0.5
                        jcy = ppos.y + jh * jscale * 0.5
                        cls = getattr(getattr(world, 'state', None), 'current_player_class', None) or players_config.PLAYER_CFG.get("DEFAULT_CLASS")
                        stats = players_config.PLAYER_STATS.get(cls, {}) or {}
                        rng = float(stats.get('drag_drop_range', 128))
                        # Mouse -> mundo
                        mx, my = pygame.mouse.get_pos()
                        world_x = mx / camera.zoom + camera.offset_x
                        world_y = my / camera.zoom + camera.offset_y
                        if math.hypot(world_x - jcx, world_y - jcy) > rng:
                            now_ts = pygame.time.get_ticks()
                            t = now_ts / 1000.0
                            s = (math.sin(2.0 * math.pi * 2.0 * t) + 1.0) * 0.5  # 2 Hz
                            base_alpha, max_alpha = 120, 230
                            base_th, max_th = 2, 6
                            alpha = int(base_alpha + (max_alpha - base_alpha) * s)
                            thickness = int(base_th + (max_th - base_th) * s)
                            scx, scy = camera.apply((jcx, jcy))
                            radius_screen = max(1, int(rng * camera.zoom))
                            circle_overlay = pygame.Surface((radius_screen * 2 + 2, radius_screen * 2 + 2), pygame.SRCALPHA)
                            pygame.draw.circle(circle_overlay, (255, 215, 0, alpha), (radius_screen + 1, radius_screen + 1), radius_screen, max(1, thickness))
                            screen.blit(circle_overlay, (int(scx - radius_screen - 1), int(scy - radius_screen - 1)))
        except Exception:
            pass
        
        # Destacar todos los drops según flag show_all_drops en InputComponent
        show_all = False
        for inp in world.components.get('InputComponent', {}).values():
            if getattr(inp, 'show_all_drops', False):
                show_all = True
                break
        if show_all:
            comps = world.components
            for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
                pos = comps['Position'][eid]
                sprite = comps['Sprite'][eid]
                scale_comp = comps.get('Scale', {}).get(eid)
                scale = scale_comp.scale if scale_comp else 1.0
                w, h = sprite.image.get_size()
                w = int(w * scale * camera.zoom)
                h = int(h * scale * camera.zoom)
                sx, sy = camera.apply((pos.x, pos.y))
                rect = pygame.Rect(sx, sy, w, h)
                draw_highlight_rect(screen, rect)
            return
        # Obtener posición actual del ratón
        mouse_x, mouse_y = pygame.mouse.get_pos()
        comps = world.components
        hovered = None
        max_layer = None
        # Detectar entidad drop bajo el cursor, priorizando la capa Z más alta
        for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
            pos = comps['Position'][eid]
            sprite = comps['Sprite'][eid]
            scale_comp = comps.get('Scale', {}).get(eid)
            scale = scale_comp.scale if scale_comp else 1.0
            zoom = camera.zoom
            w, h = sprite.image.get_size()
            w = int(w * scale * zoom)
            h = int(h * scale * zoom)
            sx, sy = camera.apply((pos.x, pos.y))
            rect = pygame.Rect(sx, sy, w, h)
            if rect.collidepoint(mouse_x, mouse_y):
                layer = comps['ZLayer'][eid].layer
                if hovered is None or layer >= max_layer:
                    hovered = eid
                    max_layer = layer
        if hovered is None:
            return
        # Resaltar el drop
        pos = comps['Position'][hovered]
        sprite = comps['Sprite'][hovered]
        scale_comp = comps.get('Scale', {}).get(hovered)
        scale = scale_comp.scale if scale_comp else 1.0
        w, h = sprite.image.get_size()
        w = int(w * scale * camera.zoom)
        h = int(h * scale * camera.zoom)
        sx, sy = camera.apply((pos.x, pos.y))
        border_rect = pygame.Rect(sx, sy, w, h)
        # Color del borde: amarillo por defecto, verde si ya alcanzó el umbral de hold o si está en drag activo
        border_color_rgb = (255, 255, 0)
        try:
            ready_green = False
            # Caso 1: aún en hold pre-drag sobre este drop y progreso >= umbral
            drag_sys_hold = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'potential_drag_eid')), None)
            if drag_sys_hold is not None:
                pot_eid = getattr(drag_sys_hold, 'potential_drag_eid', None)
                press_time = getattr(drag_sys_hold, 'drag_press_time', None)
                threshold = getattr(drag_sys_hold, 'drag_hold_threshold', 300)
                if pot_eid == hovered and press_time is not None:
                    now_ts = pygame.time.get_ticks()
                    p = max(0.0, min(1.0, (now_ts - press_time) / max(1, threshold)))
                    if p >= 0.999:
                        ready_green = True
            # Caso 2: drag activo de este mismo drop
            if not ready_green:
                drag_sys_active = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'dragging_eid')), None)
                if drag_sys_active and getattr(drag_sys_active, 'dragging_eid', None) == hovered:
                    ready_green = True
            if ready_green:
                border_color_rgb = (80, 220, 120)
        except Exception:
            pass
        draw_highlight_rect(screen, border_rect, color=border_color_rgb) # highlight using UI helper
        # Efecto de rellenado progresivo (hold-to-grab) y borde pulsante
        try:
            drag_sys = next((s for s in getattr(world, 'update_systems', []) if hasattr(s, 'potential_drag_eid')), None)
            if drag_sys and getattr(drag_sys, 'dragging_eid', None) is None:
                pot_eid = getattr(drag_sys, 'potential_drag_eid', None)
                press_time = getattr(drag_sys, 'drag_press_time', None)
                threshold = getattr(drag_sys, 'drag_hold_threshold', 300)
                if pot_eid == hovered and press_time is not None:
                    now_ts = pygame.time.get_ticks()
                    p = max(0.0, min(1.0, (now_ts - press_time) / max(1, threshold)))
                    # easing (ease-out cubic)
                    pe = 1 - pow(1 - p, 3)
                    # Relleno (amarillo -> verde al completar) desde abajo hacia arriba
                    overlay = pygame.Surface((w, h), pygame.SRCALPHA)
                    fill_h = int(h * pe)
                    fill_rect = pygame.Rect(0, h - fill_h, w, fill_h)
                    done = p >= 0.999
                    base_color = (80, 220, 120) if done else (255, 255, 0)  # verde si completo
                    pygame.draw.rect(overlay, (*base_color, 220), fill_rect)
                    screen.blit(overlay, (sx, sy))
                    # Borde pulsante (doradito) sincronizado con el progreso
                    t = now_ts / 1000.0
                    s = (math.sin(2.0 * math.pi * 2.0 * t) + 1.0) * 0.5  # 2 Hz
                    pulse_factor = s * pe
                    base_alpha, max_alpha = 90, 200
                    base_th, max_th = 2, 5
                    alpha = int(base_alpha + (max_alpha - base_alpha) * pulse_factor)
                    thickness = int(base_th + (max_th - base_th) * pulse_factor)
                    border_overlay = pygame.Surface((w, h), pygame.SRCALPHA)
                    pulse_color = (80, 220, 120) if done else (255, 215, 0)
                    pygame.draw.rect(border_overlay, (*pulse_color, alpha), border_overlay.get_rect(), max(1, thickness))
                    screen.blit(border_overlay, (sx, sy))
                    # Si el drop está fuera de rango respecto al jugador, resaltar jugador con borde dorado
                    try:
                        comps0 = world.components
                        player = getattr(world, 'player_entity', None)
                        if player is not None:
                            ppos = comps0.get('Position', {}).get(player)
                            pspr = comps0.get('Sprite', {}).get(player)
                            pscale_comp = comps0.get('Scale', {}).get(player)
                            if ppos and pspr:
                                # Centro jugador (coords mundo)
                                jw, jh = pspr.image.get_size()
                                jscale = pscale_comp.scale if pscale_comp else 1.0
                                jcx = ppos.x + jw * jscale * 0.5
                                jcy = ppos.y + jh * jscale * 0.5
                                # Centro del drop hovered (coords mundo)
                                dpos = comps['Position'][hovered]
                                dscale_comp = comps.get('Scale', {}).get(hovered)
                                dscale = dscale_comp.scale if dscale_comp else 1.0
                                dw, dh = sprite.image.get_size()
                                dcx = dpos.x + dw * dscale * 0.5
                                dcy = dpos.y + dh * dscale * 0.5
                                # Rango por clase
                                cls = getattr(getattr(world, 'state', None), 'current_player_class', None) or players_config.PLAYER_CFG.get("DEFAULT_CLASS")
                                stats = players_config.PLAYER_STATS.get(cls, {}) or {}
                                rng = float(stats.get('drag_drop_range', 128))
                                if math.hypot(dcx - jcx, dcy - jcy) > rng:
                                    # Dibujar highlight en el jugador
                                    pw, ph = pspr.image.get_size()
                                    pw = int(pw * jscale * camera.zoom)
                                    ph = int(ph * jscale * camera.zoom)
                                    psx, psy = camera.apply((ppos.x, ppos.y))
                                    prect = pygame.Rect(psx, psy, pw, ph)
                                    prect = prect.inflate(12, 12)
                                    # Borde pulsante igual que antes
                                    base_alpha2, max_alpha2 = 120, 230
                                    base_th2, max_th2 = 2, 6
                                    alpha2 = int(base_alpha2 + (max_alpha2 - base_alpha2) * s)
                                    thickness2 = int(base_th2 + (max_th2 - base_th2) * s)
                                    border_overlay2 = pygame.Surface((pw, ph), pygame.SRCALPHA)
                                    pygame.draw.rect(border_overlay2, (255, 215, 0, alpha2), border_overlay2.get_rect(), max(1, thickness2))
                                    screen.blit(border_overlay2, (psx, psy))
                                    # Círculo de rango
                                    scx, scy = camera.apply((jcx, jcy))
                                    radius_screen = max(1, int(rng * camera.zoom))
                                    circle_overlay = pygame.Surface((radius_screen * 2 + 2, radius_screen * 2 + 2), pygame.SRCALPHA)
                                    pygame.draw.circle(circle_overlay, (255, 215, 0, alpha2), (radius_screen + 1, radius_screen + 1), radius_screen, max(1, thickness2))
                                    screen.blit(circle_overlay, (int(scx - radius_screen - 1), int(scy - radius_screen - 1)))
                    except Exception:
                        pass
        except Exception:
            pass
        # Mostrar tooltip con nombre y descripción usando UI helper
        phys = comps['PhysicalItemComponent'][hovered]
        model = self.items.get(phys.item_id)
        name = getattr(model, 'name', phys.item_id)
        desc = getattr(model, 'description', '')
        lines = [name, desc] if desc else [name]
        draw_tooltip(screen, mouse_x, mouse_y, lines)
        return

