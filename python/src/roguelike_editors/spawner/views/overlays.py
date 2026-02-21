from __future__ import annotations

"""Dibujo de overlays auxiliares para la vista del Spawner Editor."""

import logging
import pygame
from typing import Optional
from . import theme
import roguelike_engine.config.config as config
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_game.ecs.systems.spawner.placement.loaders import load_waves
from roguelike_game.ecs.systems.spawner.placement.config_resolver import resolve_config
from roguelike_editors.spawner.services.persistence import load_spawners_json

logger = logging.getLogger(__name__)


def render_hint_overlay(view, screen: pygame.Surface, title_rect: Optional[pygame.Rect], tb_rect: Optional[pygame.Rect], mgr_rect: Optional[pygame.Rect], inst_rect: Optional[pygame.Rect]) -> None:
    """Dibuja el hint inferior con una breve ayuda de uso."""
    try:
        c = view.controller
        if c.font:
            base_y = (title_rect.bottom + 6) if title_rect else 10
            if tb_rect is not None:
                base_y = max(base_y, tb_rect.bottom + 6)
            if mgr_rect is not None:
                base_y = max(base_y, mgr_rect.bottom + 6)
            if inst_rect is not None:
                base_y = max(base_y, inst_rect.bottom + 6)
            text = c.font.render(theme.HINT_TEXT, True, theme.COLOR_HINT)
            screen.blit(text, (10, base_y))
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_hint_overlay: error while drawing hint", exc_info=True)


def render_zone_change_confirmation(view, screen: pygame.Surface) -> None:
    """Dibuja el overlay de confirmación de cambio de zona."""
    try:
        c = view.controller
        pending = getattr(c.model, 'pending_zone_confirm', None)
        if not pending:
            return
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((*theme.COLOR_BLACK, theme.MODAL_BACKDROP_ALPHA))
        screen.blit(overlay, (0, 0))
        orig_zone = str(pending.get('orig_zone'))
        prop_zone = str(pending.get('proposed_zone'))
        lines = [
            theme.ZONE_CONFIRM_LINE_1.format(prop_zone=prop_zone),
            theme.ZONE_CONFIRM_LINE_2.format(orig_zone=orig_zone),
            theme.ZONE_CONFIRM_LINE_3,
        ]
        font = getattr(c, 'font', None)
        if not font:
            try:
                font = pygame.font.Font(None, 14)
            except Exception:
                return
        max_w = 0
        rendered = []
        for ln in lines:
            surf = font.render(ln, True, theme.COLOR_WHITE)
            rendered.append(surf)
            max_w = max(max_w, surf.get_width())
        pad = 14
        line_h = rendered[0].get_height()
        total_h = line_h * len(rendered) + pad * 2
        total_w = max_w + pad * 2
        vw, vh = screen.get_size()
        rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
        pygame.draw.rect(screen, theme.ZONE_PANEL_BG, rect)
        pygame.draw.rect(screen, theme.ZONE_PANEL_BORDER, rect, 2)
        y = rect.top + pad
        for surf in rendered:
            x = rect.left + (rect.width - surf.get_width()) // 2
            screen.blit(surf, (x, y))
            y += line_h
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_zone_change_confirmation: error while drawing zone confirm", exc_info=True)


def render_delete_instance_confirmation(view, screen: pygame.Surface) -> None:
    """Dibuja el overlay de confirmación de eliminación de instancia."""
    try:
        c = view.controller
        pending_del = getattr(c.model, 'pending_delete_confirm', None)
        if not pending_del:
            return
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((*theme.COLOR_BLACK, theme.MODAL_BACKDROP_ALPHA))
        screen.blit(overlay, (0, 0))
        tpl = str(pending_del.get('template_id'))
        zone = str(pending_del.get('zone'))
        lt = pending_del.get('local_tile') or (0, 0)
        lines = [
            theme.DELETE_CONFIRM_LINE_1,
            f"Template: '{tpl}' | Zone: '{zone}' | Tile: ({lt[0]}, {lt[1]})",
            theme.DELETE_CONFIRM_LINE_3,
        ]
        font = getattr(c, 'font', None)
        if not font:
            return
        max_w = 0
        rendered = []
        for ln in lines:
            surf = font.render(ln, True, theme.DELETE_TEXT)
            rendered.append(surf)
            max_w = max(max_w, surf.get_width())
        pad = 14
        line_h = rendered[0].get_height()
        total_h = line_h * len(rendered) + pad * 2
        total_w = max_w + pad * 2
        vw, vh = screen.get_size()
        rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
        pygame.draw.rect(screen, theme.DELETE_PANEL_BG, rect)
        pygame.draw.rect(screen, theme.DELETE_PANEL_BORDER, rect, 2)
        y = rect.top + pad
        for surf in rendered:
            x = rect.left + (rect.width - surf.get_width()) // 2
            screen.blit(surf, (x, y))
            y += line_h
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_delete_instance_confirmation: error while drawing delete confirm", exc_info=True)


def render_visuals_picker(view, screen: pygame.Surface) -> None:
    """Dibuja/actualiza el picker de visuales cuando está abierto."""
    try:
        c = view.controller
        ip = getattr(c, 'instance_properties', None)
        if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
            cam = getattr(c, 'game', None)
            cam = getattr(cam, 'camera', None)
            if cam is not None:
                try:
                    inst_rect_anchor = getattr(view, '_last_instances_rect', None)
                    picker = ip.get_visuals_picker()
                    if picker is not None and inst_rect_anchor is not None:
                        picker.set_anchors(left_x=int(inst_rect_anchor.left), top_y=int(inst_rect_anchor.bottom + 6), reserved_bottom_h=0)
                except (AttributeError, TypeError, ValueError):
                    logger.debug("render_visuals_picker: failed to set anchors", exc_info=True)
                ip.render_visuals_picker(screen, cam)
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_visuals_picker: error while drawing picker", exc_info=True)


def render_spawner_info_panel(view, screen: pygame.Surface) -> None:
    try:
        c = view.controller
        ip = getattr(c, 'instance_properties', None)
        if ip is None or not getattr(getattr(ip, 'model', None), 'visible', False):
            return
        props_rect = getattr(view, '_last_properties_rect', None)
        if props_rect is None:
            return
        font = getattr(c, 'font', None)
        if not font:
            return
        game = getattr(c, 'game', None)
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        comps = getattr(world, 'components', {}) if world is not None else {}
        has_runtime = ('SpawnerConfig' in comps and 'SpawnerState' in comps)
        eid = getattr(getattr(c, 'model', None), 'selected_eid', None)
        if (not has_runtime) or (eid is None) or (eid not in comps.get('SpawnerConfig', {})) or (eid not in comps.get('SpawnerState', {})):
            # Fallback: resolve eid from selected instance in the hub
            try:
                sel_inst = None
                try:
                    sel_inst = getattr(getattr(getattr(c, 'instance_properties', None), 'model', None), 'selected_instance', None)
                except Exception:
                    sel_inst = None
                if sel_inst is None and hasattr(c, 'spawner_instances'):
                    try:
                        sel_inst = c.spawner_instances.get_selected_instance()
                    except Exception:
                        sel_inst = None
                if isinstance(sel_inst, dict) and world is not None and has_runtime:
                    # 1) Try via mapped building carrying _spawner_eid
                    inst_id = None
                    try:
                        if sel_inst.get('id') is not None:
                            inst_id = str(sel_inst.get('id'))
                    except Exception:
                        inst_id = None
                    if inst_id:
                        try:
                            for ob in getattr(world, 'buildings', []) or []:
                                try:
                                    sid = getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', None))
                                    link = getattr(ob, '_spawner_eid', None)
                                    if sid is not None and str(sid) == inst_id and link is not None:
                                        eid = int(link)
                                        break
                                except Exception:
                                    continue
                        except Exception:
                            pass
                    # 2) Fallback via zone + local tile -> global tile match
                    if (eid is None) and ('zone' in sel_inst) and ('tile' in sel_inst) and world is not None and has_runtime:
                        try:
                            zone = str(sel_inst.get('zone'))
                            lt = sel_inst.get('tile') or [0, 0]
                            lx = int(lt[0]); ly = int(lt[1])
                            off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                            gx, gy = int(lx + int(off_x)), int(ly + int(off_y))
                            for cand in world.get_entities_with('SpawnerConfig'):
                                try:
                                    cc = comps['SpawnerConfig'][cand]
                                    if str(getattr(cc, 'zone', '')) == zone:
                                        ax, ay = getattr(cc, 'anchor_tile', (None, None))
                                        if int(ax) == gx and int(ay) == gy:
                                            eid = int(cand)
                                            break
                                except Exception:
                                    continue
                        except Exception:
                            pass
            except Exception:
                pass
        cfg = None
        st = None
        if has_runtime and (eid is not None) and (eid in comps['SpawnerConfig']) and (eid in comps['SpawnerState']):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
        else:
            # Build cfg/state from selected instance (offline) to still show info in the hub
            try:
                if isinstance(sel_inst, dict):
                    tpls = load_spawners_json() or []
                    tpl = None
                    for t in tpls:
                        try:
                            if str(t.get('id')) == str(sel_inst.get('template_id')):
                                tpl = t
                                break
                        except Exception:
                            continue
                    if tpl is not None:
                        waves = load_waves()
                        cfg = resolve_config(tpl, sel_inst, waves)
                        st = SpawnerState()
            except Exception:
                cfg = None
                st = None
        if cfg is None or st is None:
            return
        fps = getattr(config, 'FPS', 60) or 60
        total_waves = max(1, len(getattr(cfg, 'waves', []) or []))
        wave_num = min(getattr(st, 'current_wave_idx', 0) + 1, total_waves)
        try:
            live = len(getattr(st, 'current_wave_entities', set()) or [])
        except Exception:
            live = 0
        exp = int(getattr(st, 'expected_this_wave', 0) or 0)
        cd_frames = int(getattr(st, 'cooldown_remaining', 0) or 0)
        rc_frames = int(getattr(st, 'restart_cooldown_remaining', 0) or 0)
        cd_s = cd_frames / float(fps)
        rc_s = rc_frames / float(fps)
        loop_policy = bool((getattr(cfg, 'policy', {}) or {}).get('loop') or (getattr(cfg, 'policy', {}) or {}).get('repeat') or (getattr(cfg, 'policy', {}) or {}).get('restart_on_done'))
        mode = str((getattr(cfg, 'policy', {}) or {}).get('mode', '') or '')
        status = 'ON' if getattr(st, 'started', False) else 'OFF'
        if getattr(st, 'finished', False):
            status = 'DONE'
        bw_frames = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
        if getattr(st, 'finished', False) and rc_frames > 0:
            cd_line = f"rc {rc_s:.2f}s"
        elif cd_frames > 0 and bw_frames > 0 and not getattr(st, 'spawned_this_wave', False) and not getattr(st, 'finished', False):
            cd_line = f"bwc {cd_s:.2f}s"
        else:
            cd_line = f"cd {cd_s:.2f}s"
        shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
        bld = getattr(cfg, 'building_id', None)
        fsm = str(getattr(st, 'fsm_state', '') or '')
        fsm_set = getattr(st, 'fsm_set_id', None)
        hp_line = ""
        try:
            token = None
            tok = getattr(st, 'visual_override_token', None)
            token = str(tok).strip().lower() if tok else None
            if not token:
                token = str(getattr(st, 'fsm_state', '') or '').strip().lower()
            eff = {}
            base = getattr(cfg, 'life_defaults', None) or {}
            if isinstance(base, dict):
                eff.update(base)
            vm = getattr(cfg, 'visuals_life', None) or {}
            if token and isinstance(vm, dict) and token in vm and isinstance(vm[token], dict):
                eff.update(vm[token])
            if bool(eff.get('damageable', False)):
                hp_map = world.components.setdefault('SpawnerHealth', {})
                entry = hp_map.get(eid, {})
                scope = str(entry.get('scope', getattr(cfg, 'hp_scope', 'per_state'))).lower()
                cur = max_hp = None
                if scope == 'shared':
                    pool = entry.get('shared') or {}
                    cur = pool.get('current_hp')
                    max_hp = pool.get('max_hp')
                else:
                    pool = (entry.get('by_state') or {}).get(token) or {}
                    cur = pool.get('current_hp')
                    max_hp = pool.get('max_hp')
                if max_hp is None:
                    max_hp = eff.get('max_hp')
                    cur = eff.get('max_hp')
                if max_hp is not None and cur is not None:
                    hp_line = f"hp {int(cur)}/{int(max_hp)}"
        except Exception:
            hp_line = ""
        lines = [
            f"{cfg.template_id}" + (f"  (bld:{bld})" if bld is not None else ""),
            f"{status} | wave {wave_num}/{total_waves}",
            f"live {live}/{exp} | {cd_line}",
            hp_line,
            (f"fsm: {fsm}" if fsm else ""),
            (f"set: {fsm_set}" if fsm_set else ""),
            f"{mode} | loop:{'on' if loop_policy else 'off'} | shape:{shape}",
        ]
        lines = [t for t in lines if t]
        padding = 4
        line_gap = 1
        rendered = [font.render(t, True, theme.COLOR_HINT) for t in lines]
        max_w = max((s.get_width() for s in rendered), default=0)
        total_h = sum((s.get_height() for s in rendered)) + line_gap * (len(rendered) - 1 if rendered else 0)
        # Keep at least the text width, but prefer properties panel width
        pref_w = int(getattr(props_rect, 'width', 0) or 0)
        box_w = max(pref_w, max_w + padding * 2)
        box_h = total_h + padding * 2
        box = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
        box.fill((0, 0, 0, 150))
        pygame.draw.rect(box, theme.COLOR_HINT, box.get_rect(), width=1)
        y = padding
        for srf in rendered:
            x = (box_w - srf.get_width()) // 2
            box.blit(srf, (x, y))
            y += srf.get_height() + line_gap
        left = int(getattr(props_rect, 'left', 10) or 10)
        top = int(getattr(props_rect, 'bottom', 10) or 10) + 6
        # Clamp inside screen
        try:
            sw, sh = screen.get_width(), screen.get_height()
            if left + box_w > sw - 4:
                left = max(4, sw - box_w - 4)
            if top + box_h > sh - 4:
                # If doesn't fit below, try above the properties panel
                alt_top = int(getattr(props_rect, 'top', 10) or 10) - 6 - box_h
                top = max(4, alt_top)
        except Exception:
            pass
        screen.blit(box, (left, top))
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_spawner_info_panel: error while drawing panel", exc_info=True)
