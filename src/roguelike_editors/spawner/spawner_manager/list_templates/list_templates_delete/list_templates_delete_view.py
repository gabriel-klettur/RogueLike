from __future__ import annotations
import pygame


class ListTemplatesDeleteView:
    """Renders the delete confirmation modal and stores hit-test rects."""

    def __init__(self) -> None:
        self.confirm_rect = None
        self.confirm_yes_rect = None
        self.confirm_no_rect = None

    def render_modal(self, delete_model, screen, panel_rect) -> None:
        # Clear when not visible or no panel available
        if not getattr(delete_model, 'confirm_visible', False) or panel_rect is None:
            self.confirm_rect = None
            self.confirm_yes_rect = None
            self.confirm_no_rect = None
            return
        try:
            # Dim panel area
            overlay = pygame.Surface(panel_rect.size, pygame.SRCALPHA)
            overlay.fill((0, 0, 0, 120))
            screen.blit(overlay, panel_rect.topleft)
            # Dialog metrics
            dw, dh = 280, 120
            dx = panel_rect.left + (panel_rect.width - dw) // 2
            dy = panel_rect.top + (panel_rect.height - dh) // 2
            self.confirm_rect = pygame.Rect(dx, dy, dw, dh)
            # Dialog surface
            dlg = pygame.Surface((dw, dh), pygame.SRCALPHA)
            dlg.fill((30, 30, 30, 240))
            pygame.draw.rect(dlg, (160, 160, 160), dlg.get_rect(), 2, border_radius=4)
            # Message text (simple wrap)
            try:
                font = pygame.font.SysFont(None, 18)
                msg = getattr(delete_model, 'confirm_text', '') or '¿Confirmar eliminación?'
                lines = []
                words = msg.split()
                line = ''
                max_w = dw - 20
                for w in words:
                    test = (line + ' ' + w).strip()
                    if font.size(test)[0] <= max_w:
                        line = test
                    else:
                        if line:
                            lines.append(line)
                        line = w
                if line:
                    lines.append(line)
                ty = 12
                for ln in lines[:3]:
                    text_s = font.render(ln, True, (240, 240, 240))
                    dlg.blit(text_s, (10, ty))
                    ty += text_s.get_height() + 4
            except Exception:
                pass
            # Buttons
            bw, bh = 84, 26
            spacing = 16
            by = dh - bh - 12
            bx_yes = (dw - (2 * bw + spacing)) // 2
            bx_no = bx_yes + bw + spacing
            yes_rect_local = pygame.Rect(bx_yes, by, bw, bh)
            no_rect_local = pygame.Rect(bx_no, by, bw, bh)
            pygame.draw.rect(dlg, (60, 120, 60), yes_rect_local, border_radius=3)
            pygame.draw.rect(dlg, (30, 60, 30), yes_rect_local, 1, border_radius=3)
            pygame.draw.rect(dlg, (120, 60, 60), no_rect_local, border_radius=3)
            pygame.draw.rect(dlg, (60, 30, 30), no_rect_local, 1, border_radius=3)
            try:
                btn_font = pygame.font.SysFont(None, 18)
                yes_t = btn_font.render("Sí", True, (240, 240, 240))
                no_t = btn_font.render("No", True, (240, 240, 240))
                dlg.blit(yes_t, (yes_rect_local.x + (bw - yes_t.get_width()) // 2, yes_rect_local.y + (bh - yes_t.get_height()) // 2))
                dlg.blit(no_t, (no_rect_local.x + (bw - no_t.get_width()) // 2, no_rect_local.y + (bh - no_t.get_height()) // 2))
            except Exception:
                pass
            # Blit dialog
            screen.blit(dlg, (dx, dy))
            # Store screen-space rects
            self.confirm_yes_rect = pygame.Rect(dx + yes_rect_local.x, dy + yes_rect_local.y, bw, bh)
            self.confirm_no_rect = pygame.Rect(dx + no_rect_local.x, dy + no_rect_local.y, bw, bh)
            # Block input under modal
            try:
                from roguelike_ui.ui_blocker import register_blocker
                register_blocker(self.confirm_rect)
            except Exception:
                pass
        except Exception:
            # Ensure rects cleared on failure
            self.confirm_rect = None
            self.confirm_yes_rect = None
            self.confirm_no_rect = None


__all__ = ["ListTemplatesDeleteView"]
