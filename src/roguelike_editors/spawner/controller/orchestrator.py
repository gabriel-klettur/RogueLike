from __future__ import annotations

"""Orquestación de eventos y render del Spawner Editor.

Contiene funciones puras que actúan sobre el `controller` (fachada) para:
- Manejar el enrutado de eventos entre toolbars/paneles/overlays.
- Sincronizar y aplicar el estado de UI de forma consistente por frame.
- Renderizar la vista y overlays auxiliares.
"""

from typing import Any
import pygame

from .ui_state import (
    UIState,
    compute_ui_state,
    apply_ui_state,
    update_tutorial_pulses,
    maybe_refresh_instances_on_first_show,
)


def handle_event(controller: Any, event: pygame.event.Event) -> bool:
    """Orquesta el enrutado de eventos hacia toolbars, paneles y lógica del editor."""
    try:
        # Overlay Visuals Picker tiene prioridad (modal)
        try:
            ip = getattr(controller, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
                # Obtener cámara si existe
                try:
                    cam = getattr(controller, 'game', None)
                    cam = getattr(cam, 'camera', None)
                except Exception:
                    cam = None
                handled = False
                try:
                    handled = bool(ip.handle_visuals_picker_event(event, cam))
                except Exception:
                    handled = False
                # Consumir inputs comunes mientras el overlay esté abierto para evitar reacciones debajo
                if handled:
                    return True
                if event.type in (
                    pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                    pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP,
                ):
                    return True
        except Exception:
            pass

        # Estado de UI (cálculo/aplicación/pulsos/refresh inicial)
        state: UIState = compute_ui_state(controller)
        apply_ui_state(controller, state)
        update_tutorial_pulses(controller, state)
        maybe_refresh_instances_on_first_show(controller, state)
        controller._instances_visible_last = bool(controller.model.visible and (state.active_tool == 'spawner_instances'))
        controller._manager_visible_last = bool(state.manager_visible)

        # Tutorial overlay primero para consumir ESC/clicks dentro
        try:
            if hasattr(controller, 'tutorial') and controller.tutorial.handle_event(event):
                return True
        except Exception:
            pass

        # Toolbar principal
        if hasattr(controller, 'spawner_toolbar') and controller.spawner_toolbar.handle_event(event):
            return True

        # Toolbar de instancias (puede estar visible durante Add Mode para cancelar)
        try:
            if getattr(getattr(controller.instance_toolbar, 'model', None), 'visible', False):
                if controller.instance_toolbar.handle_event(event):
                    return True
        except Exception:
            pass

        # Manager (Templates)
        if getattr(controller.spawner_manager.model, 'visible', False):
            if controller.spawner_manager.handle_event(event):
                return True

        # Lista de Instancias (solo si visible y no hay Add/Remove/Placement)
        placing_active = bool(state.placing_active)
        if (
            controller.model.visible
            and (state.active_tool == 'spawner_instances')
            and not getattr(controller.model, 'add_mode_active', False)
            and not getattr(controller.model, 'remove_mode_active', False)
            and not placing_active
        ):
            if controller.spawner_instances.handle_event(event):
                return True
            if hasattr(controller, 'instance_properties') and getattr(getattr(controller.instance_properties, 'model', None), 'visible', False):
                if controller.instance_properties.handle_event(event):
                    return True

        # Título
        if hasattr(controller, 'title_controller') and controller.title_controller.handle_event(event):
            return True

        # Event handler modular (drag de ancla, resize, split, confirmaciones, etc.)
        try:
            if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                print(f"[SpawnerEditor] Orchestrator routing LMB to core: pos={getattr(event,'pos',None)}")
        except Exception:
            pass
        _ret = controller.events.handle_event(event)
        try:
            if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                print(f"[SpawnerEditor] Core handler returned: {_ret}")
        except Exception:
            pass
        return _ret
    except Exception:
        return False


def render(controller: Any, screen: pygame.Surface) -> None:
    """Renderiza overlays del editor y sincroniza estado de UI por frame."""
    try:
        # Sincronización de estado antes de render (por si hay toggles externos)
        state: UIState = compute_ui_state(controller)
        apply_ui_state(controller, state)
        if (controller.model.visible and (state.active_tool == 'spawner_instances')) and not controller._instances_visible_last:
            try:
                controller.spawner_instances.refresh_from_disk()
            except Exception:
                pass
        controller._instances_visible_last = bool(controller.model.visible and (state.active_tool == 'spawner_instances'))

        # Mientras hold, mantener cámara centrada en target
        if state.hold and getattr(controller.model, 'hold_focus_target_px', None) is not None:
            try:
                cam = getattr(controller, 'game', None)
                cam = getattr(cam, 'camera', None)
                if cam is not None:
                    tx, ty = controller.model.hold_focus_target_px
                    zoom = getattr(cam, 'zoom', 1.0) or 1.0
                    cam.offset_x = float(tx) - (cam.screen_width / (2 * zoom))
                    cam.offset_y = float(ty) - (cam.screen_height / (2 * zoom))
            except Exception:
                pass

        # Render principal
        controller.view.render(screen)
        # Overlay tutorial encima
        try:
            if hasattr(controller, 'tutorial'):
                controller.tutorial.render(screen)
        except Exception:
            pass
        try:
            if state.placing_active:
                mx, my = pygame.mouse.get_pos()
                pygame.draw.circle(screen, (0, 255, 255), (int(mx), int(my)), 10, 2)
        except Exception:
            pass
    except Exception:
        pass
