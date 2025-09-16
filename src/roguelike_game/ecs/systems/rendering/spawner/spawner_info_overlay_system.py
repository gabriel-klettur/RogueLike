from __future__ import annotations

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.debug_draw import ensure_font, draw_translucent_box, CYAN, AMBER

# Optional FSM editor/runtime bridge
try:
    from roguelike_editors.fsm.services.fsm_runtime_bridge import (
        set_editor_highlight_context as _fsm_set_highlight_ctx,
        lint_set_params as _fsm_lint,
    )
except Exception:  # pragma: no cover - editor/runtime bridge may be missing
    _fsm_set_highlight_ctx = None
    _fsm_lint = None


class SpawnerInfoOverlaySystem:
    """Draws the info box for hovered/selected spawners with wave/cooldown data and FSM hints.
    Exposes world.state.spawner_info_rect for dragging. Gated by Spawner Editor active or DEBUG_SPAWNER.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._font = None

    def _ensure_font(self):
        if self._font is None:
            self._font = ensure_font("Arial", 14)

    def update(self, world, screen, camera):
        # Gate
        editor_active = False
        try:
            editor_active = bool(getattr(getattr(world, 'state', None), 'spawner_editor_active', False))
        except Exception:
            editor_active = False
        if not editor_active and not getattr(config, 'DEBUG_SPAWNER', False):
            return

        comps = world.components
        if 'SpawnerConfig' not in comps:
            return

        self._ensure_font()
        font = self._font
        zoom = getattr(camera, 'zoom', 1.0) or 1.0

        try:
            hovered_eid = getattr(getattr(world, 'state', None), 'spawner_editor_hovered_eid', None)
        except Exception:
            hovered_eid = None
        try:
            selected_eid = getattr(getattr(world, 'state', None), 'spawner_selected_eid', None)
        except Exception:
            selected_eid = None

        highlight_sid = None
        highlight_params = None
        highlight_warnings = []

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]

            # Only one info box; it is rendered relative to current spawner anchor position
            tx, ty = cfg.anchor_tile
            px = tx * TILE_SIZE + TILE_SIZE // 2
            py = ty * TILE_SIZE + TILE_SIZE // 2
            sx, sy = camera.apply((px, py))
            cx, cy = int(sx), int(sy)

            # Build compact multiline info centered on the spawner anchor (inside cyan circle)
            if font:
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

                # Decide which cooldown to display
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

                is_hover = (eid == hovered_eid)
                if is_hover and fsm_set:
                    highlight_sid = fsm_set
                    # Build params view for linter from cfg/state (best-effort)
                    params = {}
                    try:
                        for k in (
                            'cooldown_frames',
                            'between_waves_cooldown_frames',
                            'restart_cooldown_frames',
                            'max_active',
                            'spawn_radius',
                            'spawner_shape',
                            'advance_on',
                        ):
                            v = getattr(cfg, k, None)
                            if v is not None:
                                params[k] = v
                        pol = getattr(cfg, 'policy', {}) or {}
                        for k in ('advance_on',):
                            if k in pol and pol[k] is not None:
                                params.setdefault(k, pol[k])
                    except Exception:
                        pass
                    highlight_params = params or None
                    if _fsm_lint is not None:
                        try:
                            highlight_warnings = list(_fsm_lint(highlight_sid, highlight_params) or [])
                        except Exception:
                            highlight_warnings = []

                lines = [
                    f"{cfg.template_id}" + (f"  (bld:{bld})" if bld is not None else ""),
                    f"{status} | wave {wave_num}/{total_waves}",
                    f"live {live}/{exp} | {cd_line}",
                    (f"fsm: {fsm}" if fsm else ""),
                    (f"set: {fsm_set}" if fsm_set else ""),
                    f"{mode} | loop:{'on' if loop_policy else 'off'} | shape:{shape}",
                ]
                lines = [t for t in lines if t]

                # Render multiline with translucent background
                padding = 4
                line_gap = 1
                line_surfs = [font.render(t, True, CYAN) for t in lines]
                max_w = max((s.get_width() for s in line_surfs), default=0)
                total_h = sum((s.get_height() for s in line_surfs)) + line_gap * (len(line_surfs) - 1 if line_surfs else 0)
                box_w = max_w + padding * 2
                box_h = total_h + padding * 2
                box = draw_translucent_box((box_w, box_h), border_color=CYAN, bg_rgba=(0, 0, 0, 150))
                y = padding
                for srf in line_surfs:
                    x = (box_w - srf.get_width()) // 2
                    box.blit(srf, (x, y))
                    y += srf.get_height() + line_gap

                # Default position: 400px above the anchor (centered on anchor X)
                default_left = int(cx - box_w // 2)
                default_top = int(cy - box_h // 2 - 400)
                pos_left = default_left
                pos_top = default_top
                try:
                    stw = getattr(world, 'state', None)
                    if stw is not None:
                        manual = getattr(stw, 'spawner_info_pos', None)
                        if isinstance(manual, (tuple, list)) and len(manual) == 2:
                            pos_left = int(manual[0])
                            pos_top = int(manual[1])
                except Exception:
                    pass
                screen.blit(box, (pos_left, pos_top))
                # Expose rect for input handling (dragging) in editor events
                try:
                    if getattr(world, 'state', None) is not None:
                        setattr(world.state, 'spawner_info_rect', pygame.Rect(pos_left, pos_top, box_w, box_h))
                except Exception:
                    pass

                # If hovered and we have linter warnings, draw them near the anchor
                if is_hover and highlight_warnings and font:
                    warn_padding = 4
                    warn_gap = 1
                    warn_surfs = [font.render(f"! {w}", True, AMBER) for w in highlight_warnings[:4]]
                    wmax = max((s.get_width() for s in warn_surfs), default=0)
                    wh = sum((s.get_height() for s in warn_surfs)) + warn_gap * (len(warn_surfs) - 1 if warn_surfs else 0)
                    wb = pygame.Surface((wmax + warn_padding * 2, wh + warn_padding * 2), pygame.SRCALPHA)
                    wb.fill((30, 20, 0, 170))
                    pygame.draw.rect(wb, AMBER, wb.get_rect(), width=1)
                    wy = warn_padding
                    for srf in warn_surfs:
                        wx = warn_padding
                        wb.blit(srf, (wx, wy))
                        wy += srf.get_height() + warn_gap
                    screen.blit(wb, (pos_left + box_w + 6, pos_top + (box_h - wb.get_height()) // 2))

        # Update Editor highlight context once per frame
        if _fsm_set_highlight_ctx is not None:
            try:
                _fsm_set_highlight_ctx(highlight_sid, highlight_params)
            except Exception:
                pass
