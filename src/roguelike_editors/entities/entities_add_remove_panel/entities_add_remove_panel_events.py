import pygame
import logging
logger = logging.getLogger(__name__)
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP

class EntitiesAddRemovePanelEventHandler:
    """
    Manejador de eventos para el panel de añadir/eliminar entidades.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        Procesa eventos de click y atajos.
        """
        # Accionar en mouse up para no bloquear interacción posterior
        if event.type == pygame.MOUSEBUTTONUP and getattr(event, 'button', None) == 1:
            pos = event.pos
            # Debug click en panel add/remove
            logger.debug(f" Click izquierdo en {pos}")
            # Obtener widget de panel
            panel_widget = None
            try:
                panel_widget = self.controller.add_remove_controller.view.widget
            except Exception:
                pass
            if panel_widget:
                for tool in self.model.tools:
                    rect = panel_widget.icon_rects.get(tool)
                    logger.debug(f" Tool '{tool}' rect: {rect}")
                    if rect and rect.collidepoint(pos):
                        logger.debug(f" '{tool}' presionado")
                        # Alternar modo de añadir/borrar entidades
                        if tool == 'add_entitie' and self.controller.model.toolbar_model.active_tool == ENTITIES_TOOL_ON_MAP:
                            # Si veníamos de 'add_entities_on_system', salir de ese modo
                            if self.model.active_tool == 'add_entities_on_system':
                                self.model.active_tool = None
                                try:
                                    pp_model = self.controller.properties_controller.model
                                    pp_model.show_add_system_selector = False
                                    pp_model.entity_type_rect = None
                                    # Restaurar layout UI
                                    self.controller.exit_add_entities_on_system_mode()
                                except Exception:
                                    pass
                            if self.controller.model.spawn_mode_active:
                                logger.debug(" Cancelando spawn mode")
                                self.model.active_tool = None
                                self.controller.exit_spawn_mode()
                            else:
                                logger.debug(" Iniciando spawn mode")
                                self.model.active_tool = tool
                                self.controller.enter_spawn_mode()
                        elif tool == 'remove_entitie' and self.controller.model.toolbar_model.active_tool == ENTITIES_TOOL_ON_MAP:
                            # Si veníamos de 'add_entities_on_system', salir de ese modo
                            if self.model.active_tool == 'add_entities_on_system':
                                self.model.active_tool = None
                                try:
                                    pp_model = self.controller.properties_controller.model
                                    pp_model.show_add_system_selector = False
                                    pp_model.entity_type_rect = None
                                    # Restaurar layout UI
                                    self.controller.exit_add_entities_on_system_mode()
                                except Exception:
                                    pass
                            if self.controller.model.delete_mode_active:
                                logger.debug(" Cancelando delete mode")
                                self.model.active_tool = None
                                self.controller.exit_delete_mode()
                            else:
                                logger.debug(" Iniciando delete mode")
                                self.model.active_tool = tool
                                self.controller.enter_delete_mode()
                        elif tool == 'add_entities_on_system':
                            # Toggle modo de añadir entidad al sistema
                            if self.model.active_tool == 'add_entities_on_system':
                                logger.debug(" Cerrando modo 'Add Entity on System'")
                                self.model.active_tool = None
                                # Ocultar selector en Properties Panel
                                try:
                                    pp_model = self.controller.properties_controller.model
                                    pp_model.show_add_system_selector = False
                                    pp_model.entity_type_rect = None
                                    # Restaurar layout UI
                                    self.controller.exit_add_entities_on_system_mode()
                                except Exception:
                                    pass
                            else:
                                logger.debug(" Opening Properties Panel to add new hostile class")
                                self.model.active_tool = tool
                                try:
                                    # Asegurar que no estamos en modos que ocultan Properties
                                    if self.controller.model.spawn_mode_active:
                                        self.controller.exit_spawn_mode()
                                    if self.controller.model.delete_mode_active:
                                        self.controller.exit_delete_mode()
                                    # Abrir propiedades para nuevo Hostile (alias de Monster)
                                    try:
                                        self.controller.open_new_hostile_properties()
                                    except Exception:
                                        # Compatibilidad: fallback al método original si no existiera
                                        self.controller.open_new_monster_properties()
                                    # Mostrar selector en Properties Panel, por defecto 'Hostile'
                                    pp_model = self.controller.properties_controller.model
                                    pp_model.show_add_system_selector = True
                                    pp_model.add_system_entity_type = 'Hostile'
                                    # Activar layout expandido y ocultar picker
                                    self.controller.enter_add_entities_on_system_mode()
                                except Exception as e:
                                    logger.error(f" Error opening new monster properties: {e}")
                        return True
        return False
