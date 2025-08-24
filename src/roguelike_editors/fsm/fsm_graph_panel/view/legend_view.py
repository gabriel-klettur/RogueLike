from __future__ import annotations
from typing import Any


def draw_legend(model: Any, screen: Any, x: int, y: int, w: int, h: int, view: Any) -> None:
    try:
        import pygame  # type: ignore
    except Exception:
        return None
    try:
        legend_items = [
            ((160, 80, 200), "Damage"),
            ((220, 100, 180), "Alert"),
            ((90, 200, 220), "Interrupt/External"),
        ]
        lfont = pygame.font.SysFont(None, 16)
        small_font = pygame.font.SysFont(None, 14)
        swatch_w, swatch_h = 14, 8
        gap_x, gap_y = 8, 6
        margin = 8
        if getattr(model, 'legend_collapsed', False):
            # Collapsed pill with a [+] button
            label = "Legend"
            txt = lfont.render(label, True, (210, 210, 215))
            btn_w = txt.get_height() + 6
            btn_h = txt.get_height() + 2
            box_w = btn_w + 6 + txt.get_width() + gap_x
            box_h = max(btn_h, txt.get_height()) + gap_y
            box_x = x + w - margin - box_w
            box_y = y + h - margin - box_h
            bg = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
            bg.fill((20, 20, 24, 230))
            pygame.draw.rect(bg, (95, 95, 105), bg.get_rect(), 1)
            # Button [+]
            btn_rect_local = pygame.Rect(gap_x//2, (box_h - btn_h)//2, btn_w, btn_h)
            pygame.draw.rect(bg, (95, 95, 105), btn_rect_local, border_radius=3)
            plus = small_font.render("+", True, (235, 235, 240))
            pr = plus.get_rect(center=btn_rect_local.center)
            bg.blit(plus, pr)
            # Label
            bg.blit(txt, (btn_rect_local.right + 6, (box_h - txt.get_height())//2))
            # Composite
            screen.blit(bg, (box_x, box_y))
            # Store rects (screen-space)
            view.legend_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            view.legend_button_rect = pygame.Rect(box_x + btn_rect_local.left, box_y + btn_rect_local.top, btn_w, btn_h)
        else:
            # Expanded panel with a minimize button [−]
            header = lfont.render("Legend (special)", True, (200, 200, 210))
            max_item_w = 0
            item_h = max(swatch_h, lfont.get_height())
            for color, label in legend_items:
                tw = lfont.size(label)[0]
                max_item_w = max(max_item_w, swatch_w + 6 + tw)
            # Minimize button size
            btn_w = 18
            btn_h = 16
            # Box size
            box_w = max(header.get_width() + btn_w + 6, max_item_w) + gap_x * 2
            box_h = header.get_height() + gap_y + len(legend_items) * (item_h + 2) + gap_y
            # Position bottom-right
            box_x = x + w - margin - box_w
            box_y = y + h - margin - box_h
            bg = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
            bg.fill((20, 20, 24, 230))
            pygame.draw.rect(bg, (95, 95, 105), bg.get_rect(), 1)
            # Header and minimize button
            bg.blit(header, (gap_x, gap_y - 2))
            btn_rect_local = pygame.Rect(box_w - gap_x - btn_w, gap_y - 2, btn_w, btn_h)
            pygame.draw.rect(bg, (95, 95, 105), btn_rect_local, border_radius=3)
            minus = small_font.render("-", True, (235, 235, 240))
            mr = minus.get_rect(center=btn_rect_local.center)
            bg.blit(minus, mr)
            # Items
            iy = gap_y + header.get_height()
            for color, label in legend_items:
                pygame.draw.rect(bg, color, pygame.Rect(gap_x, iy + (item_h - swatch_h)//2, swatch_w, swatch_h))
                txt = lfont.render(label, True, (210, 210, 215))
                bg.blit(txt, (gap_x + swatch_w + 6, iy - 1))
                iy += item_h + 2
            # Composite
            screen.blit(bg, (box_x, box_y))
            # Store rects (screen-space)
            view.legend_rect = pygame.Rect(box_x, box_y, box_w, box_h)
            view.legend_button_rect = pygame.Rect(box_x + btn_rect_local.left, box_y + btn_rect_local.top, btn_w, btn_h)
    except Exception:
        # Non-fatal if we can't render legend
        view.legend_rect = None
        view.legend_button_rect = None
