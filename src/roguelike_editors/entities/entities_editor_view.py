import pygame

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
        widget = c.toolbar_view.widget
        margin = 8
        # Si se seleccionó alguna herramienta de entidades, mostrar panels
        if active in ('entities_on_map', 'entities_on_system'):
            rect = widget.icon_rects.get('entities_on_map')

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
            cam = self.controller.game.camera
            # Recorrer entidades válidas con Sprite y Position
            ecs = self.controller.game.ecs.ecs_world
            sprites = ecs.components.get('Sprite', {})
            positions = ecs.components.get('Position', {})
            scale_map = ecs.components.get('Scale', {})
            player_tags = ecs.components.get('PlayerTagComponent', {})
            npc_tags = ecs.components.get('NPCTagComponent', {})
            for eid, sprite_comp in sprites.items():
                # Filtrar solo jugadores/NPCs
                if eid not in positions or (eid not in player_tags and eid not in npc_tags):
                    continue
                pos = positions[eid]
                # Coordenadas en pantalla donde se dibuja el sprite
                sx, sy = cam.apply((pos.x, pos.y))
                # Calcular escala total (entidad + cámara)
                entity_scale = getattr(scale_map.get(eid), 'scale', 1.0)
                scale_factor = entity_scale * cam.zoom
                # Obtener sprite escalado
                scaled_img = pygame.transform.rotozoom(sprite_comp.image, 0, scale_factor)
                rect = scaled_img.get_rect()
                rect.topleft = (int(sx), int(sy))
                if rect.collidepoint(mx, my):
                    # Fondo semitransparente rojo
                    overlay = pygame.Surface(rect.size, pygame.SRCALPHA)
                    overlay.fill((255, 0, 0, 80))
                    screen.blit(overlay, rect.topleft)
                    # Borde rojo del asset escalado
                    pygame.draw.rect(screen, (255, 0, 0), rect, 2)
                    break
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
        