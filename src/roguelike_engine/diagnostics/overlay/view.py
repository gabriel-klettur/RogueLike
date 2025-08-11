import pygame
from typing import List, Tuple

from .model import DiagnosticsOverlayModel


class DiagnosticsOverlayView:
    def __init__(self):
        self._fonts: dict[tuple[str, int, bool], pygame.font.Font] = {}
        self._text_cache: dict[str, pygame.Surface] = {}

    def _get_font(self, name: str, size: int, bold: bool = False) -> pygame.font.Font:
        key = (name, size, bold)
        if key not in self._fonts:
            self._fonts[key] = pygame.font.SysFont(name, size, bold=bold)
        return self._fonts[key]

    def line_height(self, model: DiagnosticsOverlayModel) -> int:
        font = self._get_font(model.font_name, model.font_size)
        return font.get_height() + model.padding_y * 2 + model.spacing

    def rebuild_panel(
        self,
        model: DiagnosticsOverlayModel,
        position: Tuple[int, int],
        lines: List[Tuple[str, str]],
        label_w: int,
        value_w: int,
    ) -> None:
        font = self._get_font(model.font_name, model.font_size)
        bold_font = self._get_font(model.font_name, model.font_size, bold=True)
        line_h = self.line_height(model)
        total_h = line_h * len(lines)
        total_w = label_w + value_w + model.padding_x * 2 + 8

        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        surf.fill(model.bg_color)

        model.line_keys = []
        y = 0
        for left, right in lines:
            is_header = left.strip().endswith(':')
            # Render label
            cache_label = f"{('HL' if is_header else 'L')}:{left}|color:{(255, 255, 0) if is_header else model.text_color}"
            if cache_label not in self._text_cache:
                if is_header:
                    self._text_cache[cache_label] = bold_font.render(left, True, (255, 255, 0))
                else:
                    self._text_cache[cache_label] = font.render(left, True, model.text_color)
            surf_l = self._text_cache[cache_label]
            surf.blit(surf_l, (model.padding_x, y + model.padding_y))
            # Store a normalized key for interaction: headers store just the group id + ':'
            if is_header:
                disp = left.strip()[:-1]  # remove trailing ':'
                if '(' in disp:
                    disp = disp.split('(')[0].strip()
                group_id = disp.split('.')[0]
                model.line_keys.append(f"{group_id}:")
            else:
                model.line_keys.append(left.strip())

            # Render value
            if right:
                cache_val = f"{('HV' if is_header else 'R')}:{right}|color:{(255, 255, 0) if is_header else model.value_color}"
                if cache_val not in self._text_cache:
                    if is_header:
                        self._text_cache[cache_val] = bold_font.render(right, True, (255, 255, 0))
                    else:
                        self._text_cache[cache_val] = font.render(right, True, model.value_color)
                surf_r = self._text_cache[cache_val]
                surf.blit(surf_r, (model.padding_x + label_w + 8, y + model.padding_y))
            y += line_h

        model.panel_surf = surf
        model.panel_rect = surf.get_rect(topleft=position)
        model.label_w = label_w
        model.value_w = value_w
