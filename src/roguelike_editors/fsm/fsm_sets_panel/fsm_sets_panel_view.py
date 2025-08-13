from __future__ import annotations


class FsmSetsPanelView:
    def __init__(self) -> None:
        self.panel_rect = None

    def render(self, model, screen, *, anchor=(20, 120)):
        if not getattr(model, "visible", False):
            return None
        # TODO: integrate PickerPanel; for now allocate a small rect
        try:
            import pygame  # type: ignore
            x, y = anchor
            self.panel_rect = pygame.Rect(x, y, 300, 240)
            # Draw simple panel bg and items
            surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            surf.fill((20, 20, 20, 220))
            border = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
            pygame.draw.rect(border, (90, 90, 90), border.get_rect(), 2)
            surf.blit(border, (0, 0))
            try:
                font = pygame.font.SysFont(None, 20)
                # Title
                title_font = pygame.font.SysFont(None, 22)
                title = title_font.render("FSM Sets", True, (240, 240, 240))
                surf.blit(title, (10, 6))
                y_off = 28
                for i, item in enumerate(getattr(model, 'items', [])[:10]):
                    row_y = y_off + i * 20
                    # Highlight row if hovered/selected
                    if model.selected_index == i:
                        pygame.draw.rect(surf, (60, 100, 160, 160), pygame.Rect(6, row_y - 2, self.panel_rect.width - 12, 20))
                    elif model.hovered_index == i:
                        pygame.draw.rect(surf, (60, 60, 60, 100), pygame.Rect(6, row_y - 2, self.panel_rect.width - 12, 20))
                    color = (255, 255, 255) if model.selected_index == i else (230, 230, 230)
                    text = font.render(f"• {item}", True, color)
                    surf.blit(text, (10, row_y))
            except Exception:
                pass
            screen.blit(surf, self.panel_rect.topleft)
            # Register blocker to suppress gameplay input under the panel
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.panel_rect)
            except Exception:
                pass
        except Exception:
            self.panel_rect = None
        return self.panel_rect


__all__ = ["FsmSetsPanelView"]
