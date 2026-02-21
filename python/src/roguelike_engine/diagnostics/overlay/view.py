import pygame
import re
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

    def rebuild_minimized(self, model: DiagnosticsOverlayModel, position: Tuple[int, int]) -> None:
        font = self._get_font(model.font_name, model.font_size, bold=True)
        title = "Diagnostics"
        title_surf = font.render(title, True, model.text_color)
        # Layout: [ title ][ spacer ][ restore button ]
        btn_w = max(20, title_surf.get_height())
        btn_h = max(20, title_surf.get_height())
        padding = model.padding_x
        w = max(120, title_surf.get_width() + padding * 3 + btn_w)
        h = max(btn_h + model.padding_y * 2, self.line_height(model))
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill(model.bg_color)
        # Draw title
        surf.blit(title_surf, (padding, (h - title_surf.get_height()) // 2))
        # Draw restore button (▢)
        btn_x = w - padding - btn_w
        btn_y = (h - btn_h) // 2
        btn_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        pygame.draw.rect(surf, (220, 220, 220), btn_rect, border_radius=4)
        sym_font = self._get_font(model.font_name, max(12, min(18, model.font_size)), bold=True)
        sym_surf = sym_font.render("▢", True, (30, 30, 30))
        surf.blit(sym_surf, (btn_x + (btn_w - sym_surf.get_width()) // 2, btn_y + (btn_h - sym_surf.get_height()) // 2))
        # Update model rects
        model.panel_surf = surf
        model.panel_rect = surf.get_rect(topleft=position)
        model.header_rect = pygame.Rect(model.panel_rect.left, model.panel_rect.top, w, h)
        model.btn_restore_rect = pygame.Rect(model.panel_rect.left + btn_rect.left, model.panel_rect.top + btn_rect.top, btn_w, btn_h)
        model.minimized_height = h

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

        # Clamp dimensions to avoid out-of-memory surfaces
        screen = pygame.display.get_surface()
        max_w = getattr(model, 'max_surface_width', 2000)
        max_h = getattr(model, 'max_surface_height', 8000)
        if screen is not None:
            sw, sh = screen.get_size()
            # No exceder pantalla + margen razonable
            max_w = min(max_w, max(64, sw - position[0] + 50))
            max_h = min(max_h, max(1 * line_h, sh - position[1] + 200))
        total_w = max(64, min(total_w, max_w))
        total_h = max(line_h, min(total_h, max_h))

        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        surf.fill(model.bg_color)

        model.line_keys = []
        y = 0
        used_label_w = min(label_w, total_w // 2)
        for idx, (left, right) in enumerate(lines):
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
            # Store a normalized key for interaction: headers store the full group id + ':'
            if is_header:
                disp = left.strip()[:-1]  # remove trailing ':'
                if '(' in disp:
                    disp = disp.split('(')[0].strip()
                # Remove visual indicator if present
                disp = re.sub(r'^[▶▼]\s*', '', disp)
                # Extract numeric dotted id if present; else use first token
                m = re.match(r"^(\d+(?:\.\d+)*)\b", disp)
                if m:
                    group_id = m.group(1)
                else:
                    group_id = disp.split()[0] if disp else ''
                model.line_keys.append(f"{group_id}:")
            else:
                model.line_keys.append(left.strip())

            # Render value
            if right:
                # Determine per-line value color: headers use yellow, items use override or default
                if is_header:
                    val_color = (255, 255, 0)
                else:
                    # Guard against mismatched lengths
                    if 0 <= idx < len(getattr(model, 'value_colors', [])) and model.value_colors[idx] is not None:
                        val_color = model.value_colors[idx]  # type: ignore[assignment]
                    else:
                        val_color = model.value_color
                cache_val = f"{('HV' if is_header else 'R')}:{right}|color:{val_color}"
                if cache_val not in self._text_cache:
                    if is_header:
                        self._text_cache[cache_val] = bold_font.render(right, True, (255, 255, 0))
                    else:
                        self._text_cache[cache_val] = font.render(right, True, val_color)
                surf_r = self._text_cache[cache_val]
                # Alinear el valor a la derecha del panel cuando haya espacio suficiente;
                # si no, mantenerlo inmediatamente después de la etiqueta.
                x_right = total_w - model.padding_x - surf_r.get_width()
                x_min = model.padding_x + used_label_w + 8
                val_x = max(x_min, x_right)
                surf.blit(surf_r, (val_x, y + model.padding_y))
            y += line_h

        model.panel_surf = surf
        model.panel_rect = surf.get_rect(topleft=position)
        model.label_w = used_label_w
        model.value_w = min(value_w, total_w - model.label_w - model.padding_x * 2 - 8)

    def rebuild_toolbar(self, model: DiagnosticsOverlayModel) -> tuple[pygame.Surface, pygame.Rect]:
        # Build a simple vertical toolbar with header and buttons, anchored bottom-right
        font = self._get_font(model.font_name, max(12, model.font_size))
        bold = self._get_font(model.font_name, max(12, model.font_size), bold=True)

        # Content definition: list of (key, display_name)
        items = [
            ("spell_collision", "SpellCollision"),
            ("npc_attack", "NpcAttack"),
            ("hitbox", "Hitbox"),
            ("patrol", "Patrol"),
            ("defend_area", "DefendArea"),
            ("telegraph", "Telegraph"),
            ("windup_outline", "WindupOutline"),
            ("trail", "Trail"),
            ("building_collision", "BuildingCollision"),
        ]

        # Compute sizes
        label = "Debug Tools"
        title_surf = bold.render(label, True, model.text_color)
        btn_h = max(22, font.get_height() + 8)
        btn_w = 0
        btn_g = 6
        pad = 8
        statestr = {True: "mostrando", False: "oculto"}
        # Measure widest button
        for key, name in items:
            text = f"{name} - {statestr.get(bool(model.toolbar_toggles.get(key, True)), 'oculto')}"
            tw, th = font.size(text)
            btn_w = max(btn_w, tw + 16)
        width = max(180, title_surf.get_width() + pad * 2, btn_w + pad * 2)
        # Height depends on minimized
        if model.toolbar_minimized:
            height = title_surf.get_height() + pad * 2
        else:
            height = title_surf.get_height() + pad * 2 + btn_g
            height += len(items) * (btn_h + btn_g)
        surf = pygame.Surface((width, height), pygame.SRCALPHA)
        surf.fill(model.bg_color)

        # Position bottom-right
        screen = pygame.display.get_surface()
        left = model.toolbar_margin
        top = model.toolbar_margin
        if screen is not None and getattr(model, 'toolbar_anchor_bottom_right', True):
            sw, sh = screen.get_size()
            left = max(0, sw - width - model.toolbar_margin)
            top = max(0, sh - height - model.toolbar_margin)

        # Header and minimize button
        surf.blit(title_surf, (pad, pad))
        btn_size = max(18, title_surf.get_height())
        min_rect = pygame.Rect(width - pad - btn_size, pad, btn_size, btn_size)
        pygame.draw.rect(surf, (220, 220, 220), min_rect, border_radius=4)
        sym = bold.render("–" if not model.toolbar_minimized else "+", True, (30, 30, 30))
        surf.blit(sym, (min_rect.left + (btn_size - sym.get_width()) // 2, min_rect.top + (btn_size - sym.get_height()) // 2))

        # Store rects on model
        rect = pygame.Rect(left, top, width, height)
        model.toolbar_rect = rect
        model.toolbar_header_rect = pygame.Rect(rect.left, rect.top, width, title_surf.get_height() + pad * 2)
        model.toolbar_btn_min_rect = pygame.Rect(rect.left + min_rect.left, rect.top + min_rect.top, btn_size, btn_size)
        model.toolbar_buttons.clear()

        # Buttons (only if expanded)
        if not model.toolbar_minimized:
            y = title_surf.get_height() + pad * 2 + btn_g
            for key, name in items:
                text = f"{name} - {statestr.get(bool(model.toolbar_toggles.get(key, True)), 'oculto')}"
                bx = pad
                by = y
                bw = width - pad * 2
                bh = btn_h
                brect = pygame.Rect(bx, by, bw, bh)
                # Visual
                pygame.draw.rect(surf, (240, 240, 240), brect, border_radius=4)
                if model.toolbar_toggles.get(key, True):
                    pygame.draw.rect(surf, (60, 180, 75), brect, width=2, border_radius=4)
                else:
                    pygame.draw.rect(surf, (160, 160, 160), brect, width=2, border_radius=4)
                ts = font.render(text, True, (30, 30, 30))
                surf.blit(ts, (bx + 8, by + (bh - ts.get_height()) // 2))
                # Save absolute rect (in screen coords)
                abs_rect = pygame.Rect(rect.left + brect.left, rect.top + brect.top, bw, bh)
                model.toolbar_buttons[key] = abs_rect
                y += bh + btn_g

        return surf, rect
