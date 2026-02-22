import pygame


class MinimapEvents:
    """
    Encapsula el manejo de eventos del minimapa (hover/click en botones).
    """

    def handle_event(self, model, event: pygame.event.Event, screen: pygame.Surface) -> bool:
        et = getattr(event, 'type', None)
        if et not in (pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN):
            return False

        # Hit-test área del minimapa
        rect = model.last_rect or self.get_rect(screen, model)
        pos = getattr(event, 'pos', None)
        if not pos or not rect.collidepoint(*pos):
            if et == pygame.MOUSEMOTION:
                model.btn_hover = None
            return False

        # Hover dentro del minimapa
        if et == pygame.MOUSEMOTION:
            model.btn_hover = None
            for key, brect in model.btn_rects.items():
                r = pygame.Rect(rect.x + brect.x, rect.y + brect.y, brect.w, brect.h)
                if r.collidepoint(*pos):
                    model.btn_hover = key
                    break
            return False

        # Click izquierdo para toggles
        if et == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            for key, brect in model.btn_rects.items():
                r = pygame.Rect(rect.x + brect.x, rect.y + brect.y, brect.w, brect.h)
                if r.collidepoint(*pos):
                    if key == 'tiles':
                        model.show_tiles = not model.show_tiles
                    elif key == 'buildings':
                        model.show_buildings = not model.show_buildings
                    elif key == 'zones':
                        model.show_zones = not model.show_zones
                    elif key == 'entities':
                        model.show_entities = not model.show_entities
                    return True
        return False

    @staticmethod
    def get_rect(screen: pygame.Surface, model) -> pygame.Rect:
        return pygame.Rect((screen.get_width() - model.width - model.pad_x, model.pad_y), (model.width, model.height))
