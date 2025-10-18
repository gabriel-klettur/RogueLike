from __future__ import annotations

"""Orquestación del render de la vista del Spawner Editor.

Extraído de `SpawnerEditorView.render` para mantener la clase de vista ligera.
Se apoya en los atributos del objeto `view` (fonts, z-tools, split view, cachés de rects).
"""
import logging
import pygame
from .rect_cache import reset_last_rects
from . import overlays
from . import buildings_overlay
from . import theme

logger = logging.getLogger(__name__)


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

    # 1) Title bar (always renders with its own font)
    try:
        title_rect = c.title_controller.render(screen)
    except (AttributeError, pygame.error):
        title_rect = None
    try:
        view._last_title_rect = title_rect
    except AttributeError:
        logger.debug("orchestrate_render: failed to store last_title_rect", exc_info=True)
    # 2) Spawner toolbar just below title
    tb_rect = None
    try:
        if hasattr(c, 'spawner_toolbar') and c.spawner_toolbar:
            if title_rect is not None:
                anchor = (title_rect.left, title_rect.bottom + 8)
            else:
                anchor = (20, 60)
            c.spawner_toolbar.render(screen, anchor=anchor)
            tb_rect = getattr(getattr(c.spawner_toolbar, 'view', None), 'last_rect', None)
    except (AttributeError, TypeError, pygame.error):
        logger.debug("orchestrate_render: spawner_toolbar render failed", exc_info=True)
    try:
        view._last_toolbar_rect = tb_rect
    except AttributeError:
        logger.debug("orchestrate_render: failed to store last_toolbar_rect", exc_info=True)
    # 2b) Instance Toolbar to the RIGHT of main toolbar when Instances tool is active
    inst_tb_rect = None
    try:
        # Visible is synced in controller based on active tool
        if hasattr(c, 'instance_toolbar') and getattr(getattr(c.instance_toolbar, 'model', None), 'visible', False):
            if tb_rect is not None:
                anchor = (tb_rect.right + 8, tb_rect.top)
            else:
                base_x = title_rect.left if title_rect else 20
                anchor = (base_x, (title_rect.bottom + 8) if title_rect else 90)
            c.instance_toolbar.render(screen, anchor=anchor)
            inst_tb_rect = getattr(getattr(c.instance_toolbar, 'view', None), 'last_rect', None)
    except (AttributeError, TypeError, pygame.error):
        logger.debug("orchestrate_render: instance_toolbar render failed", exc_info=True)
    try:
        view._last_instance_toolbar_rect = inst_tb_rect
    except AttributeError:
        logger.debug("orchestrate_render: failed to store last_instance_toolbar_rect", exc_info=True)
    # 3) Spawner Manager (Templates list) to the RIGHT of toolbar/instance toolbar when active
    mgr_rect = None
    try:
        if hasattr(c, 'spawner_manager') and getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
            width = 720
            try:
                width = int(getattr(getattr(getattr(c.spawner_manager, 'list_controller', None), 'model', None), 'panel_width', 720) or 720)
            except Exception:
                width = 720
            if inst_tb_rect is not None:
                ax, ay = inst_tb_rect.right + 8, inst_tb_rect.top
            elif tb_rect is not None:
                ax, ay = tb_rect.right + 8, tb_rect.top
            else:
                base_x = title_rect.left if title_rect else 20
                ax, ay = base_x, (title_rect.bottom + 8) if title_rect else 90
            try:
                sw = screen.get_width()
                if ax + width > sw - 4:
                    base = inst_tb_rect or tb_rect
                    if base is not None:
                        ax = max(20, base.left - width - 8)
                try:
                    logger.debug("[Spawner.View] Manager anchor=(%s,%s) width=%s sw=%s", ax, ay, width, sw)
                except Exception:
                    pass
            except Exception:
                pass
            anchor = (ax, ay)
            mgr_rect = c.spawner_manager.render(screen, anchor=anchor)
            try:
                logger.debug("[Spawner.View] Manager rendered rect=%s", getattr(mgr_rect, 'size', None) and (mgr_rect.left, mgr_rect.top, mgr_rect.width, mgr_rect.height))
            except Exception:
                pass
    except (AttributeError, TypeError, pygame.error):
        logger.debug("orchestrate_render: spawner_manager render failed", exc_info=True)
    try:
        view._last_manager_rect = mgr_rect
    except AttributeError:
        logger.debug("orchestrate_render: failed to store last_manager_rect", exc_info=True)
    # 3b) Spawner Instances list (spawners_instances.json) when active, same placement
    inst_rect = None
    try:
        if hasattr(c, 'spawner_instances') and getattr(getattr(c.spawner_instances, 'model', None), 'visible', True):
            # Only render when not showing manager to avoid overlap
            if not getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
                width = 720
                try:
                    width = int(getattr(getattr(c.spawner_instances, 'model', None), 'panel_width', 720) or 720)
                except Exception:
                    width = 720
                if inst_tb_rect is not None:
                    ax, ay = inst_tb_rect.right + 8, inst_tb_rect.top
                elif tb_rect is not None:
                    ax, ay = tb_rect.right + 8, tb_rect.top
                else:
                    base_x = title_rect.left if title_rect else 20
                    ax, ay = base_x, (title_rect.bottom + 8) if title_rect else 90
                try:
                    sw = screen.get_width()
                    if ax + width > sw - 4:
                        base = inst_tb_rect or tb_rect
                        if base is not None:
                            ax = max(20, base.left - width - 8)
                except Exception:
                    pass
                anchor = (ax, ay)
                inst_rect = c.spawner_instances.render(screen, anchor=anchor)
    except (AttributeError, TypeError, pygame.error):
        logger.debug("orchestrate_render: spawner_instances render failed", exc_info=True)
    try:
        view._last_instances_rect = inst_rect
    except AttributeError:
        logger.debug("orchestrate_render: failed to store last_instances_rect", exc_info=True)
    # 3c) Instance Properties panel to the RIGHT of Instances list when a selection exists
    try:
        ip = getattr(c, 'instance_properties', None)
        if ip is not None and getattr(getattr(ip, 'model', None), 'visible', False):
            if inst_rect is not None:
                anchor = (inst_rect.right + 8, inst_rect.top)
            elif inst_tb_rect is not None:
                anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
            elif tb_rect is not None:
                anchor = (tb_rect.right + 8, tb_rect.top)
            else:
                base_x = title_rect.left if title_rect else 20
                anchor = (base_x + 420, (title_rect.bottom + 8) if title_rect else 90)
            props_rect = ip.render(screen, anchor=anchor)
            try:
                view._last_properties_rect = props_rect
            except AttributeError:
                logger.debug("orchestrate_render: failed to store last_properties_rect", exc_info=True)
    except (AttributeError, TypeError, pygame.error):
        logger.debug("orchestrate_render: instance_properties render failed", exc_info=True)
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
    # 4) Hint overlay
    overlays.render_hint_overlay(view, screen, title_rect, tb_rect, mgr_rect, inst_rect)

    # 5) Zone change confirmation overlay
    overlays.render_zone_change_confirmation(view, screen)

    # 7) Visuals Picker overlay (uses Buildings Picker UI)
    overlays.render_visuals_picker(view, screen)

    # 7b) Draw hover (cyan) and selection (yellow) outlines for spawner-linked buildings
    # Buildings overlays (hover/selección, z-tools, split bar)
    buildings_overlay.render_buildings_overlays(view, screen)

    # 6) Delete instance confirmation overlay
    overlays.render_delete_instance_confirmation(view, screen)
