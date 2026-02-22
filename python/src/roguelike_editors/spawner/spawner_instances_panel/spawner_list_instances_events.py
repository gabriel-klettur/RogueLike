from __future__ import annotations

from roguelike_editors.spawner.common import ListPanelEventHandler
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings


class SpawnerListInstancesEventHandler(ListPanelEventHandler):
    """
    Extiende el handler común para detectar clics sobre el prefijo "@ zona (x,y)"
    y disparar callbacks de foco mientras se mantiene el click.
    """
    def __init__(self) -> None:
        super().__init__()
        self._hold_pressed: bool = False
        self._last_dup_ms: int = 0
        self._last_dup_row: int | None = None

    def handle_event(self, controller, event) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        model = controller.model
        view = controller.view
        if not getattr(model, 'visible', True):
            return False
        rect = getattr(view, 'panel_rect', None)
        if rect is None:
            return False
        et = getattr(event, 'type', None)
        pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        # Per-row buttons: handle before other logic
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                for info in getattr(view, 'row_button_rects', []) or []:
                    gidx = info.get('gidx')
                    if gidx is None or not controller.is_row_instance(gidx):
                        continue
                    if info.get('dup') and info['dup'].collidepoint(pos):
                        model.selected_index = int(gidx)
                        # Debounce: avoid firing twice for the same click
                        try:
                            now = pygame.time.get_ticks()
                            if self._last_dup_row == int(gidx) and (now - int(self._last_dup_ms)) < 300:
                                return True
                            self._last_dup_row = int(gidx)
                            self._last_dup_ms = int(now)
                        except Exception:
                            pass
                        try:
                            now = pygame.time.get_ticks()
                            setattr(model, '_blink_row_index', int(gidx))
                            setattr(model, '_blink_end_ticks', int(now + 450))
                        except Exception:
                            pass
                        try:
                            controller.duplicate_instance_at(int(gidx))
                        except Exception:
                            pass
                        return True
                    if info.get('delete') and info['delete'].collidepoint(pos):
                        model.selected_index = int(gidx)
                        try:
                            now = pygame.time.get_ticks()
                            setattr(model, '_blink_row_index', int(gidx))
                            setattr(model, '_blink_end_ticks', int(now + 450))
                        except Exception:
                            pass
                        try:
                            controller.prepare_delete_at(int(gidx))
                        except Exception:
                            pass
                        return True
        header_h = int(getattr(model, 'header_height', 28) or 28)
        row_h = int(getattr(model, 'row_height', 20) or 20)
        visible_rows = int(getattr(model, 'visible_rows', 11) or 11)
        items = list(getattr(model, 'items', []) or [])

        # Helper para mapear Y a índice global
        def compute_gidx(py):
            local_y = py - rect.top
            if local_y < header_h:
                return None
            i = (local_y - header_h) // row_h
            if i < 0 or i >= visible_rows:
                return None
            start = int(getattr(model, 'scroll_offset', 0) or 0)
            g_idx = start + int(i)
            if 0 <= g_idx < len(items):
                return g_idx
            return None

        # Terminar foco al soltar click en cualquier lugar
        if et == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 1:
            if self._hold_pressed:
                try:
                    cb = getattr(controller, 'on_end_hold_focus', None)
                    if cb:
                        cb()
                except Exception:
                    pass
                self._hold_pressed = False
                return True

        # Toggle agrupado por zona con teclado (G/Z)
        if et == pygame.KEYDOWN:
            try:
                key = getattr(event, 'key', None)
                if key in (getattr(pygame, 'K_g', None), getattr(pygame, 'K_z', None)):
                    controller.toggle_group_by_zone()
                    return True
            except Exception:
                pass

        # Evitar seleccionar encabezados (filas sin instancia) al hacer click
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                gidx = compute_gidx(pos[1])
                try:
                    if gidx is not None and not controller.is_row_instance(gidx):
                        # Consumir el click para no seleccionar encabezados
                        return True
                except Exception:
                    pass

        # Detectar click sobre el segmento de coordenadas
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            if rect.collidepoint(pos):
                gidx = compute_gidx(pos[1])
                hitboxes = getattr(view, 'coords_hitboxes', {}) or {}
                seg = hitboxes.get(gidx)
                if gidx is not None and seg is not None:
                    local_x = pos[0] - rect.left
                    local_y = pos[1] - rect.top
                    if seg.collidepoint(local_x, local_y):
                        # Calcular coords mundiales (px) del centro del tile
                        try:
                            inst_idx = controller.instance_index_for_row(gidx)
                            if inst_idx is None:
                                return True
                            inst = controller._instances[inst_idx]
                            zone = inst.get('zone')
                            tile = inst.get('tile', [0, 0])
                            ox, oy = global_map_settings.zone_offsets.get(zone, (0, 0))
                            gx = int(ox) + int(tile[0])
                            gy = int(oy) + int(tile[1])
                            x_px = (gx + 0.5) * float(TILE_SIZE)
                            y_px = (gy + 0.5) * float(TILE_SIZE)
                            cb = getattr(controller, 'on_start_hold_focus', None)
                            if cb:
                                cb(float(x_px), float(y_px))
                            self._hold_pressed = True
                        except Exception:
                            pass
                        return True
        # Delegar resto de eventos al handler común (hover, selección, scroll)
        return super().handle_event(controller, event)


__all__ = ["SpawnerListInstancesEventHandler"]
