from __future__ import annotations

"""Orquestación del render de la vista del Spawner Editor.

Extraído de `SpawnerEditorView.render` para mantener la clase de vista ligera.
Se apoya en los atributos del objeto `view` (fonts, z-tools, split view, cachés de rects).
"""
import logging
import os
import json
import pygame
from .rect_cache import reset_last_rects
from . import overlays
from . import buildings_overlay
from . import theme

logger = logging.getLogger(__name__)

# Cache of last debug values per key to avoid repetitive logs each frame
_last_debug_values: dict[str, object] = {}

# Z-order config (loaded once)
_Z_CONFIG_PATH = os.path.join(os.path.dirname(__file__), '..', 'z-order.json')
_Z_CONFIG: dict[str, dict] | None = None

def _load_z_config() -> dict[str, dict]:
    global _Z_CONFIG
    if _Z_CONFIG is not None:
        return _Z_CONFIG
    # Defaults are conservative and mirror current intent
    defaults: dict[str, dict] = {
        "buildings_overlays": {"z": 10},
        "hints_overlay": {"z": 300},
        "zone_change_confirmation": {"z": 350},
        "visuals_picker": {"z": 360},
        "delete_instance_confirmation": {"z": 370},
    }
    try:
        path = os.path.normpath(os.path.join(os.path.dirname(__file__), '..', 'z-order.json'))
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
            if isinstance(data, dict):
                # Merge with defaults (defaults provide missing keys)
                merged = dict(defaults)
                merged.update({k: (v if isinstance(v, dict) else {}) for k, v in data.items()})
                _Z_CONFIG = merged
                return merged
    except Exception:
        logger.debug("orchestrate_render: using default z-order (z-order.json not found or invalid)", exc_info=True)
    _Z_CONFIG = defaults
    return defaults

def _z_of(name: str) -> int:
    cfg = _load_z_config()
    try:
        entry = cfg.get(name, {})
        z = int(entry.get('z', 0))
        return z
    except Exception:
        return 0

def _topo_sort_items(items: list[tuple[str, int, callable]]) -> list[tuple[str, int, callable]]:
    """Order items by z and optional before/after constraints from z-order.json.

    Stable fallback: sort by (z asc, original index).
    """
    cfg = _load_z_config()
    # Build graph
    name_to_idx = {name: i for i, (name, _z, _fn) in enumerate(items)}
    names = [name for name, _z, _fn in items]
    zmap = {name: z for name, z, _fn in items}
    edges: dict[str, set[str]] = {name: set() for name in names}
    indeg: dict[str, int] = {name: 0 for name in names}
    # Edges from z: lower z -> higher z (soft constraint)
    for i, (na, za, _fa) in enumerate(items):
        for j in range(i + 1, len(items)):
            nb, zb, _fb = items[j]
            if za < zb:
                if nb not in edges[na]:
                    edges[na].add(nb)
                    indeg[nb] += 1
            elif zb < za:
                if na not in edges[nb]:
                    edges[nb].add(na)
                    indeg[na] += 1
            # If equal z, keep original order; no edge needed
    # Edges from before/after
    for name in names:
        entry = cfg.get(name, {}) if isinstance(cfg, dict) else {}
        # before: name -> b
        for b in (entry.get('before') or []):
            if b not in edges:
                # If reference exists in config but not in this group, it's a cross-group rule: ignore silently
                if isinstance(cfg, dict) and b in cfg and b not in names:
                    continue
                # Log once per (name,b) pair
                try:
                    _debug_if_changed(
                        f"z_unknown_before.{name}.{b}", True,
                        "z-order.json: 'before' references unknown item '%s' from '%s'",
                        b, name,
                    )
                except Exception:
                    pass
                continue
            if b not in edges[name]:
                edges[name].add(b)
                indeg[b] += 1
        # after: a -> name
        for a in (entry.get('after') or []):
            if a not in edges:
                # If reference exists in config but not in this group, it's a cross-group rule: ignore silently
                if isinstance(cfg, dict) and a in cfg and a not in names:
                    continue
                # Log once per (name,a) pair
                try:
                    _debug_if_changed(
                        f"z_unknown_after.{name}.{a}", True,
                        "z-order.json: 'after' references unknown item '%s' for '%s'",
                        a, name,
                    )
                except Exception:
                    pass
                continue
            if name not in edges[a]:
                edges[a].add(name)
                indeg[name] += 1
    # Kahn's algorithm
    from collections import deque
    q = deque([n for n in names if indeg[n] == 0])
    result: list[str] = []
    while q:
        # To preserve stability among zero-indegree items, pick by original index
        if len(q) > 1:
            q = deque(sorted(list(q), key=lambda n: (zmap.get(n, 0), name_to_idx.get(n, 0))))
        n = q.popleft()
        result.append(n)
        for m in list(edges[n]):
            indeg[m] -= 1
            if indeg[m] == 0:
                q.append(m)
    if len(result) != len(names):
        # Cycle detected or error: fallback to stable z sort (log once)
        try:
            _debug_if_changed(
                f"z_cycle_detected.{','.join(names)}", True,
                "orchestrate_render: cycle in z-order constraints; using stable z sort",
            )
        except Exception:
            pass
        return sorted(items, key=lambda t: (t[1], name_to_idx[t[0]]))
    # Map back to tuples
    order = {name: i for i, name in enumerate(result)}
    return sorted(items, key=lambda t: order.get(t[0], 0))

def _debug_if_changed(key: str, value: object, msg: str, *args: object) -> None:
    """Log a DEBUG message only when the associated value changes.

    This prevents per-frame spam in the render loop by emitting logs
    only the first time and on subsequent changes.

    Args:
        key: Identifier for the message/value being tracked.
        value: Hashable or comparable value to detect changes.
        msg: Log format string passed to logger.debug.
        *args: Arguments for the log format string.
    """
    try:
        prev = _last_debug_values.get(key, object())
        if value != prev:
            _last_debug_values[key] = value
            logger.debug(msg, *args)
    except Exception:
        # Never let logging disrupt the render loop
        pass


def orchestrate_render(view, screen: pygame.Surface) -> None:
    """Dibuja los overlays/paneles del editor usando el estado del `controller`.

    Args:
        view: Instancia de `SpawnerEditorView` (fachada de la vista).
        screen: Superficie de destino donde se dibuja la UI.
    """
    c = view.controller
    if not c.model.visible:
        return
    # While hold-to-focus is active, hide all editor panels/overlays
    try:
        if getattr(c.model, 'hold_focus_active', False):
            return
    except (AttributeError, TypeError):
        logger.debug("orchestrate_render: hold_focus_active check failed", exc_info=True)

    # Reset last rects each frame
    reset_last_rects(view)

    # 7b) Draw hover (cyan) and selection (yellow) outlines for spawner-linked buildings
    # Buildings overlays (hover/selección, z-tools, split bar)
    buildings_overlay.render_buildings_overlays(view, screen)

    # Panels: schedule drawing by z-order while preserving anchor geometry via last_* rects
    panel_calls: list[tuple[str, int, callable]] = []

    # Title bar
    def _draw_title():
        try:
            rect = c.title_controller.render(screen)
        except (AttributeError, pygame.error):
            rect = None
        try:
            view._last_title_rect = rect
        except AttributeError:
            logger.debug("orchestrate_render: failed to store last_title_rect", exc_info=True)

    panel_calls.append(("spawner_title", _z_of("spawner_title"), _draw_title))

    # Spawner toolbar (below title)
    def _draw_toolbar():
        tb_rect_local = None
        try:
            if hasattr(c, 'spawner_toolbar') and c.spawner_toolbar:
                last_title = getattr(view, '_last_title_rect', None)
                if last_title is not None:
                    anchor = (last_title.left, last_title.bottom + 8)
                else:
                    anchor = (20, 60)
                c.spawner_toolbar.render(screen, anchor=anchor)
                tb_rect_local = getattr(getattr(c.spawner_toolbar, 'view', None), 'last_rect', None)
        except (AttributeError, TypeError, pygame.error):
            logger.debug("orchestrate_render: spawner_toolbar render failed", exc_info=True)
        try:
            view._last_toolbar_rect = tb_rect_local
        except AttributeError:
            logger.debug("orchestrate_render: failed to store last_toolbar_rect", exc_info=True)

    panel_calls.append(("spawner_toolbar", _z_of("spawner_toolbar"), _draw_toolbar))


    # Spawner Manager (Templates list)
    def _draw_manager():
        mgr_rect_local = None
        try:
            if hasattr(c, 'spawner_manager') and getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
                width = 720
                try:
                    width = int(getattr(getattr(getattr(c.spawner_manager, 'list_controller', None), 'model', None), 'panel_width', 720) or 720)
                except Exception:
                    width = 720
                last_inst_tb = getattr(view, '_last_instance_toolbar_rect', None)
                last_tb = getattr(view, '_last_toolbar_rect', None)
                last_title = getattr(view, '_last_title_rect', None)
                if last_inst_tb is not None:
                    ax, ay = last_inst_tb.right + 8, last_inst_tb.top
                elif last_tb is not None:
                    ax, ay = last_tb.right + 8, last_tb.top
                else:
                    base_x = last_title.left if last_title else 20
                    ax, ay = base_x, (last_title.bottom + 8) if last_title else 90
                try:
                    sw = screen.get_width()
                    if ax + width > sw - 4:
                        base = last_inst_tb or last_tb
                        if base is not None:
                            ax = max(20, base.left - width - 8)
                    try:
                        _debug_if_changed(
                            "manager_anchor",
                            (ax, ay, width, sw),
                            "[Spawner.View] Manager anchor=(%s,%s) width=%s sw=%s",
                            ax,
                            ay,
                            width,
                            sw,
                        )
                    except Exception:
                        pass
                except Exception:
                    pass
                anchor = (ax, ay)
                mgr_rect_local = c.spawner_manager.render(screen, anchor=anchor)
                try:
                    _debug_if_changed(
                        "manager_rect",
                        (getattr(mgr_rect_local, 'size', None) and (mgr_rect_local.left, mgr_rect_local.top, mgr_rect_local.width, mgr_rect_local.height)),
                        "[Spawner.View] Manager rendered rect=%s",
                        getattr(mgr_rect_local, 'size', None) and (mgr_rect_local.left, mgr_rect_local.top, mgr_rect_local.width, mgr_rect_local.height),
                    )
                except Exception:
                    pass
        except (AttributeError, TypeError, pygame.error):
            logger.debug("orchestrate_render: spawner_templates_panel render failed", exc_info=True)
        try:
            view._last_manager_rect = mgr_rect_local
        except AttributeError:
            logger.debug("orchestrate_render: failed to store last_manager_rect", exc_info=True)

    panel_calls.append(("spawner_templates_panel", _z_of("spawner_templates_panel"), _draw_manager))

    # Spawner Instances list
    def _draw_instances():
        inst_rect_local = None
        try:
            if hasattr(c, 'spawner_instances') and getattr(getattr(c.spawner_instances, 'model', None), 'visible', True):
                if not getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
                    width = 720
                last_tb = getattr(view, '_last_toolbar_rect', None)
                last_title = getattr(view, '_last_title_rect', None)
                if last_tb is not None:
                    ax, ay = last_tb.right + 8, last_tb.top
                else:
                    base_x = last_title.left if last_title else 20
                    ax, ay = base_x, (last_title.bottom + 8) if last_title else 90
                try:
                    sw = screen.get_width()
                    if ax + width > sw - 4:
                        base = last_tb
                        if base is not None:
                            ax = max(20, base.left - width - 8)
                except Exception:
                    pass
                anchor = (ax, ay)
                inst_rect_local = c.spawner_instances.render(screen, anchor=anchor)
        except (AttributeError, TypeError, pygame.error):
            logger.debug("orchestrate_render: spawner_instances render failed", exc_info=True)
        try:
            view._last_instances_rect = inst_rect_local
        except AttributeError:
            logger.debug("orchestrate_render: failed to store last_instances_rect", exc_info=True)

    panel_calls.append(("spawner_instances_panel", _z_of("spawner_instances_panel"), _draw_instances))

    # Instance Properties panel
    def _draw_instance_properties():
        try:
            ip = getattr(c, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visible', False):
                last_inst = getattr(view, '_last_instances_rect', None)
                last_tb = getattr(view, '_last_toolbar_rect', None)
                last_title = getattr(view, '_last_title_rect', None)
                if last_inst is not None:
                    anchor = (last_inst.right + 8, last_inst.top)
                elif last_tb is not None:
                    anchor = (last_tb.right + 8, last_tb.top)
                else:
                    base_x = last_title.left if last_title else 20
                    anchor = (base_x + 420, (last_title.bottom + 8) if last_title else 90)
                props_rect = ip.render(screen, anchor=anchor)
                try:
                    view._last_properties_rect = props_rect
                except AttributeError:
                    logger.debug("orchestrate_render: failed to store last_properties_rect", exc_info=True)
        except (AttributeError, TypeError, pygame.error):
            logger.debug("orchestrate_render: instance_properties render failed", exc_info=True)

    panel_calls.append(("spawner_instance_properties_panel", _z_of("spawner_instance_properties_panel"), _draw_instance_properties))

    # Execute panels by z and before/after constraints
    try:
        for name, z, fn in _topo_sort_items(panel_calls):
            try:
                fn()
                try:
                    _debug_if_changed(f"panel_fail.{name}", False, "orchestrate_render: panel '%s' recovered", name)
                except Exception:
                    pass
            except Exception:
                try:
                    _debug_if_changed(f"panel_fail.{name}", True, "orchestrate_render: panel '%s' failed", name)
                except Exception:
                    pass
    except Exception:
        logger.debug("orchestrate_render: panels z-order pipeline failed; falling back to sequential order", exc_info=True)
    # 3d) Visual focus overlay when editing a Visuals Template cell: dim the world and re-render properties
    try:
        ip = getattr(c, 'instance_properties', None)
        if ip is not None and getattr(getattr(ip, 'model', None), 'visible', False):
            if getattr(getattr(ip, 'model', None), 'visuals_editing_state', None) is not None:
                overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                overlay.fill((*theme.COLOR_BLACK, theme.FOCUS_DIM_ALPHA))
                screen.blit(overlay, (0, 0))
                # Re-render properties panel on top for clarity
                # Use last known rect as anchor to avoid layout shift
                last_rect = getattr(view, '_last_properties_rect', None)
                if last_rect is not None:
                    ip.render(screen, anchor=(last_rect.left, last_rect.top))
                else:
                    # Fallback to anchor calculation above
                    if inst_rect is not None:
                        anchor = (inst_rect.right + 8, inst_rect.top)
                    elif inst_tb_rect is not None:
                        anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
                    elif tb_rect is not None:
                        anchor = (tb_rect.right + 8, tb_rect.top)
                    else:
                        base_x = title_rect.left if title_rect else 20
                        anchor = (base_x + 420, (title_rect.bottom + 8) if title_rect else 90)
                    ip.render(screen, anchor=anchor)
    except (AttributeError, TypeError, pygame.error):
        pass
    # Overlays (editor-side): draw using configured z-order
    try:
        overlay_calls: list[tuple[str, int, callable]] = []
        # Prepare callables with their z values
        overlay_calls.append((
            'hints_overlay', _z_of('hints_overlay'),
            lambda: overlays.render_hint_overlay(view, screen, title_rect, tb_rect, mgr_rect, inst_rect)
        ))
        overlay_calls.append((
            'spawner_info_panel', _z_of('spawner_info_panel'),
            lambda: overlays.render_spawner_info_panel(view, screen)
        ))
        overlay_calls.append((
            'zone_change_confirmation', _z_of('zone_change_confirmation'),
            lambda: overlays.render_zone_change_confirmation(view, screen)
        ))
        overlay_calls.append((
            'visuals_picker', _z_of('visuals_picker'),
            lambda: overlays.render_visuals_picker(view, screen)
        ))
        overlay_calls.append((
            'delete_instance_confirmation', _z_of('delete_instance_confirmation'),
            lambda: overlays.render_delete_instance_confirmation(view, screen)
        ))
        # Order by z and before/after constraints using topological sort
        overlay_calls_sorted = _topo_sort_items(overlay_calls)
        for name, z, fn in overlay_calls_sorted:
            try:
                fn()
                # Log recovery once if it was failing before
                try:
                    _debug_if_changed(f"overlay_fail.{name}", False, "orchestrate_render: overlay '%s' recovered", name)
                except Exception:
                    pass
            except Exception:
                try:
                    _debug_if_changed(f"overlay_fail.{name}", True, "orchestrate_render: overlay '%s' failed", name)
                except Exception:
                    pass
    except Exception:
        logger.debug("orchestrate_render: overlays z-order pipeline failed; falling back to no-op", exc_info=True)
