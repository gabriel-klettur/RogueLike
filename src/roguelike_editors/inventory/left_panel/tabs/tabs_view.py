import pygame


class TabsView:
    """
    Vista para las pestañas de categoría en el panel izquierdo.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.tab_rects = []
        # Posición base configurable (por defecto como antes)
        self.base_x = 10
        self.base_y = 40
        # Pestañas laterales (Show Default/Active) controladas desde editor_view
        self.show_side_tabs = True
        self.active_side = 'active'  # 'active' | 'default'
        # Borde derecho disponible para alinear pestañas laterales
        self.right_edge = None

    def set_side_tabs(self, active_side: str, visible: bool):
        """Configura el estado y visibilidad de las pestañas secundarias (Default/Active)."""
        self.active_side = active_side
        self.show_side_tabs = visible

    def set_base_pos(self, x: int, y: int):
        self.base_x = x
        self.base_y = y

    def set_right_edge(self, right_x: int):
        """Define el borde derecho disponible para alinear las pestañas laterales."""
        self.right_edge = right_x

    def draw(self, surface: pygame.Surface, model) -> list:
        """
        Dibuja las pestañas y devuelve la lista de rects con su categoría.
        """
        self.tab_rects = []
        tab_x, tab_y = self.base_x, self.base_y
        # 1) Pestañas de categoría
        for cat in model.categories:
            label = cat.capitalize()
            surf = self.font.render(label, True, (255, 255, 255))
            w, h = surf.get_size()
            padding = 10
            rect = pygame.Rect(tab_x, tab_y, w + padding*2, h + padding//2)
            color = (100, 100, 100) if model.current_category == cat else (50, 50, 50)
            pygame.draw.rect(surface, color, rect)
            pygame.draw.rect(surface, (255, 255, 255), rect, 2)
            if model.current_category == cat:
                pygame.draw.rect(surface, (255, 255, 0), rect, 2)
            surface.blit(surf, (tab_x + padding, tab_y + (rect.height - h)//2))
            self.tab_rects.append((rect, cat))
            tab_x += rect.width + 5

        # 2) Pestañas secundarias (Show Default/Show Active) a la derecha, ocultas en 'map'
        if self.show_side_tabs and getattr(model, 'current_category', None) in ('player', 'monsters', 'hostile'):
            padding = 10
            side_gap = 5
            # Preparar superficies para conocer anchos
            txt_def = self.font.render("Show Default", True, (255, 255, 255))
            w_def, h_def = txt_def.get_size()
            txt_act = self.font.render("Show Active", True, (255, 255, 255))
            w_act, h_act = txt_act.get_size()
            def_w = w_def + padding * 2
            act_w = w_act + padding * 2
            side_total_w = def_w + side_gap + act_w

            # Calcular x inicial para alinear a la derecha si es posible
            if self.right_edge is not None:
                desired_start_x = self.right_edge - side_total_w
                # Si hay espacio entre pestañas de categoría y las laterales, alinearlas a la derecha
                place_right = desired_start_x >= tab_x + 5
                side_start_x = desired_start_x if place_right else tab_x
            else:
                side_start_x = tab_x

            # Show Default
            def_rect = pygame.Rect(side_start_x, tab_y, def_w, h_def + padding // 2)
            def_fill = (100, 100, 100) if self.active_side == 'default' else (50, 50, 50)
            pygame.draw.rect(surface, def_fill, def_rect)
            pygame.draw.rect(surface, (255, 255, 255), def_rect, 2)
            if self.active_side == 'default':
                pygame.draw.rect(surface, (255, 255, 0), def_rect, 2)
            surface.blit(txt_def, (def_rect.x + padding, def_rect.y + (def_rect.height - h_def)//2))
            self.tab_rects.append((def_rect, 'show_default'))

            # Show Active, a continuación
            act_x = def_rect.right + side_gap
            act_rect = pygame.Rect(act_x, tab_y, act_w, h_act + padding // 2)
            act_fill = (100, 100, 100) if self.active_side == 'active' else (50, 50, 50)
            pygame.draw.rect(surface, act_fill, act_rect)
            pygame.draw.rect(surface, (255, 255, 255), act_rect, 2)
            if self.active_side == 'active':
                pygame.draw.rect(surface, (255, 255, 0), act_rect, 2)
            surface.blit(txt_act, (act_rect.x + padding, act_rect.y + (act_rect.height - h_act)//2))
            self.tab_rects.append((act_rect, 'show_active'))

        return self.tab_rects
