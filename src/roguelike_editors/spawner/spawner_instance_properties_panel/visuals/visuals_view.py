from __future__ import annotations

from typing import Tuple

try:
    import pygame  # type: ignore
except Exception:  # pragma: no cover
    pygame = None  # type: ignore

from roguelike_ui.widgets.hover import draw_hover
from .visuals_ui import (
    draw_folder_button,
    draw_eye_button,
    draw_clear_button,
)


class VisualsView:
    """Responsible for rendering the Visuals table inside Instance Properties.

    It draws into the parent panel surface and updates rect caches in the
    VisualsModel for hit-testing. Returns the total vertical space used by
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
        m.visuals_browse_rects = []
        m.visuals_eye_rects = []
        m.visuals_clear_rects = []
        m.visuals_state_rects = []
        m.visuals_row_rects = []

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
                    m.visuals_browse_rects.append(z)
                    m.visuals_eye_rects.append(z)
                    m.visuals_clear_rects.append(z)
                    m.visuals_state_rects.append(z)
                    m.visuals_row_rects.append(z)
                continue

            col1_x, col2_x, col3_x = 10, 210, 310
            t1 = font.render(str(state), True, (230, 230, 230))
            t2 = font.render(str(inst_id), True, (230, 230, 230))
            # Template cell
            template_rect = pygame.Rect(col3_x, ry - 1, width - col3_x - 10, row_h - 2)
            control_h = template_rect.height - 4
            # Controls order (right to left): browse, eye, clear
            browse_rect = pygame.Rect(template_rect.right - 18, template_rect.y + 2, 16, control_h)
            eye_rect = pygame.Rect(browse_rect.left - 18, template_rect.y + 2, 16, control_h)
            clear_rect = pygame.Rect(eye_rect.left - 18, template_rect.y + 2, 16, control_h)
            m.visuals_template_rects.append(template_rect)
            m.visuals_browse_rects.append(browse_rect)
            m.visuals_eye_rects.append(eye_rect)
            m.visuals_clear_rects.append(clear_rect)
            # State cell rect
            state_rect = pygame.Rect(col1_x, ry - 1, (col2_x - col1_x) - 4, row_h - 2)
            m.visuals_state_rects.append(state_rect)
            # Full row rect (left-right padding like header line)
            row_rect = pygame.Rect(8, ry - 1, width - 16, row_h - 2)
            m.visuals_row_rects.append(row_rect)

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
                # 'browse' button
                draw_folder_button(surf, browse_rect)
                # 'eye' button
                visible = True
                try:
                    visible = bool(pc.is_visual_building_visible(str(state)))
                except Exception:
                    visible = True
                draw_eye_button(surf, eye_rect, visible)
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
                # Folder
                draw_folder_button(surf, browse_rect)
                # Eye
                draw_eye_button(surf, eye_rect, visible)
                # Clear (X)
                draw_clear_button(surf, clear_rect)

            # Hover highlight (orange border) over full row
            try:
                if m.hover_row_index is not None and int(m.hover_row_index) == j:
                    pygame.draw.rect(surf, (255, 160, 64), row_rect, 2)
            except Exception:
                pass

        return visuals_total_h


__all__ = ["VisualsView"]
