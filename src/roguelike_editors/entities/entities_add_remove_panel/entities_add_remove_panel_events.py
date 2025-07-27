import pygame

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
        # Click izquierdo para añadir/quitar entidades
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            # Debug click en panel add/remove
            print(f"[DEBUG][AddRemovePanel] Click izquierdo en {pos}")
            # Obtener widget de panel
            panel_widget = None
            try:
                panel_widget = self.controller.add_remove_controller.view.widget
            except Exception:
                pass
            if panel_widget:
                for tool in self.model.tools:
                    rect = panel_widget.icon_rects.get(tool)
                    print(f"[DEBUG][AddRemovePanel] Tool '{tool}' rect: {rect}")
                    if rect and rect.collidepoint(pos):
                        print(f"[DEBUG][AddRemovePanel] '{tool}' presionado")
                        self.model.active_tool = tool
                        if tool == 'add_entitie' and self.controller.model.toolbar_model.active_tool == 'entities_on_map':
                            print("[DEBUG][AddRemovePanel] Iniciando spawn mode")
                            self.controller.enter_spawn_mode()
                        return True
        return False
