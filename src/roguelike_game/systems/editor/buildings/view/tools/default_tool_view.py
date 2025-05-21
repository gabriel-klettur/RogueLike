# Path: src/roguelike_game/systems/editor/buildings/view/tools/default_tool_view.py
import pygame

class DefaultToolView:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def render_reset_handle(self, screen, building, camera):
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        # Botón proporcional al ancho (mín 15, máx 65)
        handle_size = max(15, min(65, int(w * 0.10)))
        font = pygame.font.SysFont("arial", int(handle_size * 0.6), bold=True)

        # Obtener posición del mouse
        mouse_pos = pygame.mouse.get_pos()

        # Botón rojo de eliminar
        delete_rect = pygame.Rect(
            x + w - 3 * handle_size,
            y,
            handle_size,
            handle_size
        )
        is_hover_delete = delete_rect.collidepoint(mouse_pos)
        pygame.draw.rect(screen, (220, 40, 40), delete_rect)  # rojo
        pygame.draw.rect(screen, (0, 0, 0), delete_rect, 2)   # borde negro
        if is_hover_delete:
            pygame.draw.rect(screen, (255, 255, 0), delete_rect, 4)  # borde amarillo grueso
        # Dibuja una X blanca
        pygame.draw.line(screen, (255,255,255), delete_rect.topleft, delete_rect.bottomright, 3)
        pygame.draw.line(screen, (255,255,255), delete_rect.topright, delete_rect.bottomleft, 3)
        # Letra 'E' centrada
        e_text = font.render('E', True, (255,255,255))
        e_rect = e_text.get_rect(center=delete_rect.center)
        screen.blit(e_text, e_rect)

        # Botón blanco de reset (default)
        reset_rect = pygame.Rect(
            x + w - 2 * handle_size,
            y,
            handle_size,
            handle_size
        )
        is_hover_reset = reset_rect.collidepoint(mouse_pos)
        pygame.draw.rect(screen, (255, 255, 255), reset_rect)  # blanco
        pygame.draw.rect(screen, (0, 0, 0), reset_rect, 2)      # borde negro
        if is_hover_reset:
            pygame.draw.rect(screen, (0, 255, 255), reset_rect, 4)  # borde cian grueso
        # Letra 'D' centrada
        d_text = font.render('D', True, (0,0,0))
        d_rect = d_text.get_rect(center=reset_rect.center)
        screen.blit(d_text, d_rect)

        # Botón azul de resize
        resize_rect = pygame.Rect(
            x + w - handle_size,
            y,
            handle_size,
            handle_size
        )
        is_hover_resize = resize_rect.collidepoint(mouse_pos)
        pygame.draw.rect(screen, (80, 120, 255), resize_rect)  # azul
        pygame.draw.rect(screen, (0, 0, 0), resize_rect, 2)    # borde negro
        if is_hover_resize:
            pygame.draw.rect(screen, (255, 0, 255), resize_rect, 4)  # borde magenta grueso
        # Debug visual: círculo amarillo grueso
        pygame.draw.ellipse(screen, (255,255,0), resize_rect, 5)        
        # Letra 'R' centrada y grande, en amarillo neón
        font_r = pygame.font.SysFont("arial", int(handle_size * 0.8), bold=True)
        r_text = font_r.render('R', True, (255,255,0))
        r_rect = r_text.get_rect(center=resize_rect.center)
        screen.blit(r_text, r_rect)

    def get_delete_handle_rect(self, building, camera):
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        # Dynamic handle size matching render_reset_handle
        dyn_size = max(15, min(65, int(w * 0.10)))
        return pygame.Rect(
            x + w - 3 * dyn_size,
            y,
            dyn_size,
            dyn_size
        )

    def get_reset_handle_rect(self, building, camera):
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        dyn_size = max(15, min(65, int(w * 0.10)))
        return pygame.Rect(
            x + w - 2 * dyn_size,
            y,
            dyn_size,
            dyn_size
        )