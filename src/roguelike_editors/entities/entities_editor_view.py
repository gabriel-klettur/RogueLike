import pygame
from roguelike_editors.entities.services.entity_lookup import find_clickable_entity_rect_at

class EntitiesEditorView:
    """
    Vista encargada de renderizar el editor de entidades.
    Separa la lógica de pintura (view) de la lógica de control (controller).
    """
    def __init__(self, controller):
        self.controller = controller

    def render(self, screen: pygame.Surface) -> None:
        """
        Dibuja título, toolbar y panels de entidades según estado activo.
        """
        c = self.controller
        # Título
        c.title_controller.render(screen)
        # Toolbar
        c.toolbar_controller.render(screen)
        active = c.model.toolbar_model.active_tool
        margin = 8
        # Si se seleccionó alguna herramienta de entidades, mostrar panels
        if active in ('entities_on_map', 'entities_on_system'):
            # Dibujar panels activos
            c.add_remove_controller.render(screen)
            c.picker_controller.draw(screen)
            # Dibujar Properties solo si no estamos en delete/spawn
            if not (c.model.delete_mode_active or c.model.spawn_mode_active):
                # Inicializar posición del panel Properties a la derecha del Picker
                prop_view = c.properties_controller.view
                if prop_view.draggable_panel.pos is None:
                    pick_view = c.picker_controller.view
                    px, py = pick_view.x, pick_view.y
                    pw, _ = pick_view.draggable_panel.surface.get_size()
                    prop_view.draggable_panel.pos = (px + pw + margin, py)
                c.properties_controller.draw(screen)
        # Highlight hovered player/NPC in delete mode
        if self.controller.model.delete_mode_active:
            mx, my = pygame.mouse.get_pos()
            _, rect = find_clickable_entity_rect_at(self.controller.game, mx, my)
            if rect is not None:
                # Fondo semitransparente rojo
                overlay = pygame.Surface(rect.size, pygame.SRCALPHA)
                overlay.fill((255, 0, 0, 80))
                screen.blit(overlay, rect.topleft)
                # Borde rojo del asset escalado
                pygame.draw.rect(screen, (255, 0, 0), rect, 2)
        # Overlay para spawn de entidades
        if self.controller.model.spawn_mode_active:
            mx, my = pygame.mouse.get_pos()
            if self.controller.model.spawn_entity_type is None:
                msg = "Selecciona entidad en el picker"
            else:
                msg = f"Haz clic en el mapa para colocar '{self.controller.model.spawn_entity_type}'"
            surf = self.controller.font.render(msg, True, (255, 255, 0))
            screen.blit(surf, (mx + 10, my + 10))
        # Overlay para delete de entidades
        if self.controller.model.delete_mode_active:
            mx, my = pygame.mouse.get_pos()
            msg = "Haz clic sobre la entidad para eliminarla"
            surf = self.controller.font.render(msg, True, (255, 0, 0))
            screen.blit(surf, (mx + 10, my + 10))
        