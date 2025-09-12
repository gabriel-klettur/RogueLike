from __future__ import annotations

from typing import Tuple

try:
    import pygame  # type: ignore
except Exception:  # pragma: no cover
    pygame = None  # type: ignore

from roguelike_ui.widgets.hover import draw_hover


class VisualizerView:
    """Responsible for rendering the Visuals table inside Instance Properties.

    It draws into the parent panel surface and updates rect caches in the
    VisualizerModel for hit-testing. Returns the total vertical space used by
    the visuals section (spacing + title + header + rows).
    """

    def __init__(self, controller) -> None:
        self.controller = controller

    def render_table(
        self,
        surf: "pygame.Surface",
        panel_rect: "pygame.Rect",
        *,
        title_font,
        font,
        width: int,
        row_h: int,
        y_start_visuals: int,
        viewport_top: int,
        viewport_bottom: int,
    ) -> int:
        c = self.controller
        pc = c.parent
        m = c.model

        # Prepare rows and reset rects
        visuals_rows = pc.get_visuals_rows()
        m.visuals_template_rects = []
        m.visuals_plus_rects = []
        m.visuals_browse_rects = []
        m.visuals_eye_rects = []
        m.visuals_state_rects = []

        # Geometry
        visuals_title_h = row_h
        visuals_header_h = row_h
        visuals_rows_h = (len(visuals_rows) if len(visuals_rows) > 0 else 1) * row_h
        visuals_spacing = 8
        visuals_total_h = visuals_spacing + visuals_title_h + visuals_header_h + visuals_rows_h

        if pygame is None:
            return visuals_total_h

        # Title
        if not (y_start_visuals + row_h < viewport_top or y_start_visuals > viewport_bottom):
            vis_title = title_font.render("Visuals", True, (240, 240, 180))
            surf.blit(vis_title, (10, y_start_visuals))

        # Header
        y_header = y_start_visuals + row_h
        if not (y_header + row_h < viewport_top or y_header > viewport_bottom):
            col1_x, col2_x, col3_x = 10, 210, 310
            hdr1 = font.render("State", True, (180, 220, 255))
            hdr2 = font.render("Instancia", True, (180, 220, 255))
            hdr3 = font.render("Template", True, (180, 220, 255))
            surf.blit(hdr1, (col1_x, y_header))
            surf.blit(hdr2, (col2_x, y_header))
            surf.blit(hdr3, (col3_x, y_header))
            pygame.draw.line(surf, (120, 120, 120), (8, y_header + row_h - 2), (width - 8, y_header + row_h - 2), 1)

        # Rows
        y_rows = y_header + row_h
        if len(visuals_rows) == 0:
            if not (y_rows + row_h < viewport_top or y_rows > viewport_bottom):
                txt = font.render("(sin visuals)", True, (160, 160, 160))
                surf.blit(txt, (10, y_rows))
            return visuals_total_h

        editing_state = getattr(pc.model, 'visuals_editing_state', None)
        for j, (state, inst_id, tpl_id) in enumerate(visuals_rows):
            ry = y_rows + j * row_h
            # Keep arrays aligned when clipped
            if ry + row_h < viewport_top or ry > viewport_bottom:
                if pygame:
                    z = pygame.Rect(0, 0, 0, 0)
                    m.visuals_template_rects.append(z)
                    m.visuals_plus_rects.append(z)
                    m.visuals_browse_rects.append(z)
                    m.visuals_eye_rects.append(z)
                    m.visuals_state_rects.append(z)
                continue

            col1_x, col2_x, col3_x = 10, 210, 310
            t1 = font.render(str(state), True, (230, 230, 230))
            t2 = font.render(str(inst_id), True, (230, 230, 230))
            # Template cell
            template_rect = pygame.Rect(col3_x, ry - 1, width - col3_x - 10, row_h - 2)
            control_h = template_rect.height - 4
            plus_rect = pygame.Rect(template_rect.right - 18, template_rect.y + 2, 16, control_h)
            browse_rect = pygame.Rect(plus_rect.left - 18, template_rect.y + 2, 16, control_h)
            eye_rect = pygame.Rect(browse_rect.left - 18, template_rect.y + 2, 16, control_h)
            m.visuals_template_rects.append(template_rect)
            m.visuals_plus_rects.append(plus_rect)
            m.visuals_browse_rects.append(browse_rect)
            m.visuals_eye_rects.append(eye_rect)
            # State cell rect
            state_rect = pygame.Rect(col1_x, ry - 1, (col2_x - col1_x) - 4, row_h - 2)
            m.visuals_state_rects.append(state_rect)

            # Draw label cells
            surf.blit(t1, (col1_x, ry))
            surf.blit(t2, (col2_x, ry))

            # Editing state path
            vti = getattr(c.model, 'text_input', None)
            if editing_state is not None and str(editing_state) == str(state) and vti is not None and getattr(vti, 'active', False):
                pygame.draw.rect(surf, (40, 40, 40), template_rect)
                ok, _ = pc.get_visual_input_validation(str(state))
                border_col = (120, 120, 120) if ok else (200, 80, 80)
                pygame.draw.rect(surf, border_col, template_rect, 1)
                # Hidden overlay if toggled off
                try:
                    if not pc.is_visual_building_visible(str(state)):
                        draw_hover(surf, template_rect, color=(120, 40, 40, 70))
                except Exception:
                    pass
                # Draw text input
                if vti is not None:
                    vti.draw(surf, template_rect.x + 4, template_rect.y + 2, color=(255, 255, 255))
                # '+' button
                pygame.draw.rect(surf, (60, 60, 60), plus_rect)
                pygame.draw.rect(surf, (150, 150, 150), plus_rect, 1)
                cx, cy = plus_rect.centerx, plus_rect.centery
                pygame.draw.line(surf, (120, 220, 120), (cx - 4, cy), (cx + 4, cy), 2)
                pygame.draw.line(surf, (120, 220, 120), (cx, cy - 4), (cx, cy + 4), 2)
                # 'browse' button
                pygame.draw.rect(surf, (60, 60, 60), browse_rect)
                pygame.draw.rect(surf, (150, 150, 150), browse_rect, 1)
                bx, by = browse_rect.x + 3, browse_rect.y + 3
                pygame.draw.rect(surf, (230, 200, 120), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 0)
                pygame.draw.rect(surf, (160, 130, 60), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 1)
                pygame.draw.rect(surf, (230, 200, 120), (bx + 2, by + 2, 8, 6), 0)
                # 'eye' button
                visible = True
                try:
                    visible = bool(pc.is_visual_building_visible(str(state)))
                except Exception:
                    visible = True
                pygame.draw.rect(surf, (60, 60, 60), eye_rect)
                pygame.draw.rect(surf, (150, 150, 150), eye_rect, 1)
                ex, ey = eye_rect.centerx, eye_rect.centery
                pygame.draw.ellipse(surf, (220, 220, 220), (eye_rect.x + 3, eye_rect.y + 4, eye_rect.w - 6, eye_rect.h - 8), 1)
                pygame.draw.circle(surf, (220, 220, 220) if visible else (120, 120, 120), (ex, ey), 3)
                if not visible:
                    pygame.draw.line(surf, (200, 80, 80), (eye_rect.left + 3, eye_rect.bottom - 3), (eye_rect.right - 3, eye_rect.top + 3), 2)
            else:
                # Render template id as text; N/A in amber, dim if hidden
                visible = True
                try:
                    visible = bool(pc.is_visual_building_visible(str(state)))
                except Exception:
                    visible = True
                if not visible:
                    try:
                        draw_hover(surf, template_rect, color=(120, 40, 40, 70))
                    except Exception:
                        pass
                base_c = (230, 230, 230) if str(tpl_id).upper() != 'N/A' else (220, 180, 120)
                c3 = base_c if visible else (150, 150, 150)
                t3 = font.render(str(tpl_id), True, c3)
                surf.blit(t3, (col3_x, ry))
                # '+' button
                pygame.draw.rect(surf, (60, 60, 60), plus_rect)
                pygame.draw.rect(surf, (150, 150, 150), plus_rect, 1)
                cx, cy = plus_rect.centerx, plus_rect.centery
                pygame.draw.line(surf, (120, 220, 120), (cx - 4, cy), (cx + 4, cy), 2)
                pygame.draw.line(surf, (120, 220, 120), (cx, cy - 4), (cx, cy + 4), 2)
                # Folder
                pygame.draw.rect(surf, (60, 60, 60), browse_rect)
                pygame.draw.rect(surf, (150, 150, 150), browse_rect, 1)
                bx, by = browse_rect.x + 3, browse_rect.y + 3
                pygame.draw.rect(surf, (230, 200, 120), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 0)
                pygame.draw.rect(surf, (160, 130, 60), (bx, by + 4, browse_rect.w - 6, browse_rect.h - 8), 1)
                pygame.draw.rect(surf, (230, 200, 120), (bx + 2, by + 2, 8, 6), 0)
                # Eye
                pygame.draw.rect(surf, (60, 60, 60), eye_rect)
                pygame.draw.rect(surf, (150, 150, 150), eye_rect, 1)
                ex, ey = eye_rect.centerx, eye_rect.centery
                pygame.draw.ellipse(surf, (220, 220, 220), (eye_rect.x + 3, eye_rect.y + 4, eye_rect.w - 6, eye_rect.h - 8), 1)
                pygame.draw.circle(surf, (220, 220, 220) if visible else (120, 120, 120), (ex, ey), 3)
                if not visible:
                    pygame.draw.line(surf, (200, 80, 80), (eye_rect.left + 3, eye_rect.bottom - 3), (eye_rect.right - 3, eye_rect.top + 3), 2)

        return visuals_total_h


__all__ = ["VisualizerView"]
