from __future__ import annotations


class SpawnerListView:
    def __init__(self) -> None:
        self.panel_rect = None

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, 'visible', True):
            return None
        try:
            import pygame  # type: ignore
            x, y = anchor
            width = 360
            height = 260
            self.panel_rect = pygame.Rect(x, y, width, height)
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            pygame.draw.rect(surf, (90, 90, 90), surf.get_rect(), 2)
            # Header
            try:
                title_font = pygame.font.SysFont(None, 22)
                font = pygame.font.SysFont(None, 20)
                title = title_font.render("Spawners", True, (240, 240, 240))
                surf.blit(title, (10, 6))
                y_off = 28
                # Rows
                for i, item in enumerate(getattr(model, 'items', [])[:11]):
                    row_y = y_off + i * 20
                    row_rect_local = pygame.Rect(6, row_y - 2, width - 12, 20)
                    if model.selected_index == i:
                        pygame.draw.rect(surf, (60, 100, 160, 160), row_rect_local)
                    elif model.hovered_index == i:
                        pygame.draw.rect(surf, (60, 60, 60, 100), row_rect_local)
                    color = (255, 255, 255) if model.selected_index == i else (230, 230, 230)
                    text = font.render(item, True, color)
                    surf.blit(text, (10, row_y))
                    # Yellow outline for hover/selection
                    if getattr(model, 'hovered_index', None) == i or getattr(model, 'selected_index', None) == i:
                        pygame.draw.rect(surf, (255, 220, 60), row_rect_local, 2)
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            # Block gameplay input under panel
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["SpawnerListView"]
