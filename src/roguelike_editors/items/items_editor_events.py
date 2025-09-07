import pygame
from typing import Any
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
import logging
logger = logging.getLogger(__name__)


class ItemsEditorEvents:
    """Enrutador de eventos para el Editor de Ítems."""

    def handle_event(self, controller: Any, event: pygame.event.Event) -> bool:
        model = controller.model

        # Atajos globales (funcionan incluso si no es visible para abrir/cerrar)
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE and model.visible:
                # If tutorial panel is active, close it first
                try:
                    tut = getattr(controller, 'tutorial_controller', None)
                    if tut is not None and getattr(tut, 'is_active', lambda: False)():
                        tut.deactivate()
                        return True
                except Exception:
                    pass
                controller.hide()
                return True

        if not model.visible:
            return False

        # Si el Assets Picker (de propiedades) está visible, priorizar su manejo
        try:
            props = controller.properties_controller
            ap = getattr(props, 'assets_picker', None)
            ap_model = getattr(ap, 'model', None)
            if ap and ap_model and getattr(ap_model, 'visible', False):
                # 1) Eventos con posición: si caen dentro del rect del picker, manejar y salir
                panel_rect = getattr(ap_model, 'panel_rect', None)
                if hasattr(event, 'pos') and isinstance(getattr(event, 'pos'), (tuple, list)) and panel_rect:
                    if panel_rect.collidepoint(*event.pos):
                        if ap.handle_event(event):
                            return True
                # 2) Rueda: si el cursor está sobre el panel del picker, manejar
                if event.type == pygame.MOUSEWHEEL and panel_rect:
                    mx, my = pygame.mouse.get_pos()
                    if panel_rect.collidepoint(mx, my):
                        if ap.handle_event(event):
                            return True
                # 3) Otros eventos globales (p.ej. ESC) delegarlos también
                if event.type in (pygame.KEYDOWN, pygame.KEYUP):
                    if ap.handle_event(event):
                        return True
        except Exception:
            logger.exception("[ItemsEditorEvents] routing to assets picker failed")

        # Toolbars primero (permitir clicks/drag sobre ellos antes que otros paneles)
        try:
            itb = getattr(controller, 'items_toolbar_controller', None)
            if itb and itb.handle_event(event):
                return True
            arm_visible = getattr(getattr(controller, 'items_add_remove_model', None), 'visible', False)
            if arm_visible:
                iar = getattr(controller, 'items_add_remove_controller', None)
                if iar and iar.handle_event(event):
                    return True
        except Exception:
            logger.exception("[ItemsEditorEvents] toolbar routing failed")

        # Si estamos en modo press-and-hold, ocultamos paneles y sólo atendemos el mouseup para restaurar
        if getattr(model, 'holding_pos_focus', False):
            if event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 1:
                try:
                    cb = getattr(controller.instances_controller, 'on_end_hold_focus', None)
                    if cb:
                        cb()
                except Exception:
                    logger.exception("[ItemsEditorEvents] on_end_hold_focus failed")
                finally:
                    model.holding_pos_focus = False
                return True
            # Consumir el resto de eventos mientras se mantiene presionado
            return True

        # Si el panel de propiedades está editando texto, priorizarlo
        props = controller.properties_controller
        if getattr(props, 'text_input', None) and props.text_input.active:
            props.handle_event(event)
            return True

        # Enrutado de rueda del ratón: si el ratón está sobre propiedades o instancias, scroll allí
        if event.type == pygame.MOUSEWHEEL:
            props_rect = getattr(props.model, 'panel_rect', None)
            mx, my = pygame.mouse.get_pos()
            if props_rect:
                over_props = props_rect.collidepoint(mx, my)
                logger.debug(f"[ItemsEditorEvents] MOUSEWHEEL pos=({mx},{my}) over_props={over_props} props_rect={props_rect}")
                if over_props:
                    props.handle_event(event)
                    return True
            # Instances panel wheel routing
            try:
                list_rect, params_rect = controller.instances_controller.get_layout_rects()
            except Exception:
                list_rect = params_rect = None
            if list_rect and list_rect.collidepoint(mx, my) or (params_rect and params_rect.collidepoint(mx, my)):
                handled = controller.instances_controller.handle_event(event)
                if handled:
                    return True
            logger.debug("[ItemsEditorEvents] MOUSEWHEEL routing to picker by default")
            controller.picker_controller.handle_event(event)
            logger.debug("[ItemsEditorEvents] MOUSEWHEEL routed to picker")
            return True

        # Hit-test por orden z: propiedades encima del picker
        props_rect = getattr(props.model, 'panel_rect', None)
        picker_rect = getattr(controller.picker_controller.picker_state, 'rect', None)
        try:
            inst_list_rect, inst_params_rect = controller.instances_controller.get_layout_rects()
        except Exception:
            inst_list_rect = inst_params_rect = None

        # --- Modo borrar/spawn: manejar clics específicos antes del enrutado estándar ---
        if hasattr(event, 'type') and event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = getattr(event, 'pos', (0, 0))
            over_picker = bool(picker_rect and picker_rect.collidepoint(mx, my))
            over_props = bool(props_rect and props_rect.collidepoint(mx, my))
            over_instances = bool((inst_list_rect and inst_list_rect.collidepoint(mx, my)) or (inst_params_rect and inst_params_rect.collidepoint(mx, my)))
            # Delete mode: 
            # - si clic sobre el picker, delegar al picker para que dispare on_select/on_open
            #   (el ItemsEditorController intercepta y elimina del sistema cuando 'remove_item' está activo)
            # - si clic sobre el mapa fuera de picker/props/instancias, eliminar drop del mapa
            if getattr(model, 'delete_mode_active', False):
                if over_picker:
                    controller.picker_controller.handle_event(event)
                    return True
                if not over_props and not over_instances:
                    if controller.delete_drop_at_screen_pos(mx, my):
                        # Salir de modo borrar y limpiar tool activa
                        controller.exit_delete_mode()
                        arm = getattr(controller, 'items_add_remove_model', None)
                        if arm is not None:
                            arm.active_tool = None
                    return True
            # Spawn mode
            if getattr(model, 'spawn_mode_active', False):
                # Si aún no hay ítem seleccionado para spawn, permitir selección desde el picker
                if model.spawn_item_id is None:
                    if over_picker:
                        # Delega al picker para actualizar selección
                        controller.picker_controller.handle_event(event)
                        sel = getattr(controller.picker_controller.model, 'selected_item_id', None)
                        if sel:
                            model.spawn_item_id = sel
                            try:
                                pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
                            except Exception:
                                pass
                        return True
                    # Clic fuera del picker sin selección previa: no hacer nada aún
                    return True
                else:
                    # Ya hay ítem elegido: primero, si el inventario UI está visible y el clic cae adentro, añadir al inventario
                    try:
                        world = getattr(getattr(controller, 'game', None), 'ecs', None)
                        world = getattr(world, 'ecs_world', None)
                        ui_sys = next((s for s in getattr(world, 'render_systems', []) if isinstance(s, InventoryUISystem)), None) if world else None
                    except Exception:
                        world = None
                        ui_sys = None
                    if world and ui_sys and ui_sys.visible and ui_sys.panel_rect and ui_sys.panel_rect.collidepoint(mx, my):
                        # Añadir al inventario del jugador y persistir
                        try:
                            comps = world.components
                            player = getattr(world, 'player_entity', None)
                            inv_comp = comps.get('InventoryComponent', {}).get(player) if player else None
                            if inv_comp:
                                inv_comp.add(model.spawn_item_id, 1)
                                pickup_sys = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, InventoryPickupSystem)), None)
                                if pickup_sys:
                                    pickup_sys._persist_inventory(player, inv_comp)
                                # Salir de modo spawn y limpiar tool
                                controller.exit_spawn_mode()
                                arm = getattr(controller, 'items_add_remove_model', None)
                                if arm is not None:
                                    arm.active_tool = None
                                return True
                        except Exception:
                            logger.exception("[ItemsEditorEvents] add to inventory failed")
                        return True
                    # Si no fue inventario, y clic fuera de picker/props/instancias, spawnear en mapa
                    if not over_picker and not over_props and not over_instances:
                        if controller.spawn_item_at_screen_pos(mx, my):
                            controller.exit_spawn_mode()
                            arm = getattr(controller, 'items_add_remove_model', None)
                            if arm is not None:
                                arm.active_tool = None
                        return True
                    # Consumir clics dentro del picker/otros paneles durante spawn
                    return True

        if hasattr(event, 'pos') and isinstance(getattr(event, 'pos'), (tuple, list)):
            mx, my = event.pos
            # 1) Propiedades primero
            if props_rect and props_rect.collidepoint(mx, my):
                props.handle_event(event)
                return True
            # 2) Panel de instancias (lista/params)
            if (inst_list_rect and inst_list_rect.collidepoint(mx, my)) or (inst_params_rect and inst_params_rect.collidepoint(mx, my)):
                if controller.instances_controller.handle_event(event):
                    return True
            # 3) Siempre delegar al picker (aunque aún no haya rect válido)
            controller.picker_controller.handle_event(event)
            # 4) Click fuera limpia solo si estamos fuera de picker, props e instancias
            outside_picker = picker_rect and not picker_rect.collidepoint(mx, my)
            outside_props = props_rect and not props_rect.collidepoint(mx, my)
            outside_instances = (
                (inst_list_rect is None or not inst_list_rect.collidepoint(mx, my)) and
                (inst_params_rect is None or not inst_params_rect.collidepoint(mx, my))
            )
            if outside_picker and (outside_props or props_rect is None) and outside_instances:
                if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    model.selected_item_id = None
            return True

        # Resto de eventos (teclado para navegar picker, rueda, etc.)
        # Permitir que el panel de instancias procese teclas/otros eventos (por ejemplo, ediciones de params)
        controller.instances_controller.handle_event(event)
        controller.picker_controller.handle_event(event)
        # Notar que props.handle_event se auto-limita por hit-test y text_input
        return True

