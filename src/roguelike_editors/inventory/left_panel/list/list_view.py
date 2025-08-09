import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from types import SimpleNamespace


class ListView:
    """
    Vista para la lista del panel izquierdo (scroll + highlights).
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        self.scroll_panel = ScrollPanel(font, margin=margin)
        self.panel_rect = pygame.Rect(0, 0, 0, 0)

    def draw(self, surface: pygame.Surface, model, base_rect: pygame.Rect, items: list):
        """
        Dibuja la lista scrollable y highlights. Devuelve dict con panel_rect y list_rect.
        """
        results = {}
        # Dibujar fondo semitransparente del panel
        panel_surf = pygame.Surface((base_rect.width, base_rect.height), pygame.SRCALPHA)
        panel_surf.fill((50, 50, 50, 150))
        surface.blit(panel_surf, base_rect.topleft)
        # Borde del panel
        pygame.draw.rect(surface, (255, 255, 255), base_rect, 2)
        # Guardar rect del panel para eventos y highlights
        self.panel_rect = base_rect
        results['panel_rect'] = self.panel_rect

        # Dibujar lista scrollable
        self.scroll_panel.set_items(items)
        self.scroll_panel.draw(surface, self.panel_rect)
        results['list_rect'] = self.panel_rect

        # Highlight permanente para monsters
        if model.current_category == 'monsters' and model.selected_eid:
            line_h = self.font.get_linesize()
            y0 = self.panel_rect.y - self.scroll_panel.scroll_offset
            # Encontrar índice raíz del grupo
            for idx, line in enumerate(items):
                if not line.startswith(' ') and str(model.selected_eid) == line.split()[0]:
                    start_idx = idx
                    end_idx = start_idx + 1
                    while end_idx < len(items) and items[end_idx].startswith(' '):
                        end_idx += 1
                    group_height = (end_idx - start_idx) * line_h
                    r = pygame.Rect(self.panel_rect.x, y0 + start_idx * line_h, self.panel_rect.width, group_height)
                    pygame.draw.rect(surface, (255, 255, 0), r, 3)
                    break

        # Highlight on hover para monsters
        mx, my = pygame.mouse.get_pos()
        if model.current_category == 'monsters' and self.panel_rect.collidepoint(mx, my):
            line_h = self.font.get_linesize()
            idx = (my - self.panel_rect.y + self.scroll_panel.scroll_offset) // line_h
            if 0 <= idx < len(items):
                y0 = self.panel_rect.y - self.scroll_panel.scroll_offset
                # Determinar inicio y fin de grupo
                start_idx = idx
                while start_idx > 0 and items[start_idx].startswith(' '):
                    start_idx -= 1
                end_idx = start_idx + 1
                while end_idx < len(items) and items[end_idx].startswith(' '):
                    end_idx += 1
                group_height = (end_idx - start_idx) * line_h
                group_r = pygame.Rect(self.panel_rect.x, y0 + start_idx * line_h, self.panel_rect.width, group_height)
                pygame.draw.rect(surface, (255, 255, 0), group_r, 2)
                # Si hover sobre Pos, dibujar borde naranja en la línea de Pos
                if items[idx].lstrip().startswith('Pos:'):
                    pos_r = pygame.Rect(self.panel_rect.x, y0 + idx * line_h, self.panel_rect.width, line_h)
                    pygame.draw.rect(surface, (255, 165, 0), pos_r, 2)

        # ---------------------------------------------
        # Player + Show Default: hover y selección fija
        # ---------------------------------------------
        # Resaltar selección fija (clase) si existe
        if model.current_category == 'player' and getattr(model, 'editing_side', 'active') == 'default':
            line_h = self.font.get_linesize()
            y0 = self.panel_rect.y - self.scroll_panel.scroll_offset
            selected_cls = getattr(model, 'selected_default_player_class', None)
            if selected_cls:
                # Buscar el grupo raíz cuya línea empiece por "Class: <name>"
                for idx, line in enumerate(items):
                    if not line.startswith(' '):
                        root = line.strip()
                        if root.startswith('Class:'):
                            # Extraer nombre de clase y normalizar (hasta '|')
                            try:
                                cls_name = root.split('Class:')[1].strip()
                                if '|' in cls_name:
                                    cls_name = cls_name.split('|')[0].strip()
                            except Exception:
                                cls_name = None
                            if cls_name == selected_cls:
                                # Medir altura del grupo (hasta la siguiente raíz)
                                end_idx = idx + 1
                                while end_idx < len(items) and items[end_idx].startswith(' '):
                                    end_idx += 1
                                group_height = (end_idx - idx) * line_h
                                r = pygame.Rect(self.panel_rect.x, y0 + idx * line_h, self.panel_rect.width, group_height)
                                pygame.draw.rect(surface, (255, 255, 0), r, 3)
                                break

            # Resaltar hover del grupo bajo el ratón
            if self.panel_rect.collidepoint(mx, my):
                idx = (my - self.panel_rect.y + self.scroll_panel.scroll_offset) // line_h
                if 0 <= idx < len(items):
                    # Determinar inicio del grupo (línea raíz no indentada)
                    start_idx = idx
                    while start_idx > 0 and items[start_idx].startswith(' '):
                        start_idx -= 1
                    end_idx = start_idx + 1
                    while end_idx < len(items) and items[end_idx].startswith(' '):
                        end_idx += 1
                    group_height = (end_idx - start_idx) * line_h
                    group_r = pygame.Rect(self.panel_rect.x, y0 + start_idx * line_h, self.panel_rect.width, group_height)
                    pygame.draw.rect(surface, (255, 255, 0), group_r, 2)

        return results
