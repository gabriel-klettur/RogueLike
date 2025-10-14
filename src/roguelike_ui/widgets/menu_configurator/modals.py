from __future__ import annotations

from typing import Any, Dict, List, Optional

import pygame

from roguelike_ui.services.formatting import format_key_label
from .utils import key_const_from_code, format_action_friendly


def flash_message(renderer, screen: pygame.Surface, lines: List[Any], ms: int = 750) -> None:
    """Show a temporary message using the renderer style."""
    clock = pygame.time.Clock()
    elapsed = 0
    while elapsed < ms:
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                return
        renderer.draw_message(screen, lines)
        pygame.display.flip()
        dt = clock.tick(60)
        elapsed += dt


def _draw_modal_with_buttons(
    renderer,
    screen: pygame.Surface,
    lines: List[Any],
    buttons: List[str],
    *,
    hover_index: Optional[int] = None,
    redraw: bool = True,
) -> Dict[str, Any]:
    """Draw a modal like draw_message with a centered row of buttons.

    Returns dict with panel_rect and list of button_rects in screen coordinates.
    If redraw=False, does not repaint; only computes and returns rects per current layout.
    """
    # measure
    max_w = 0
    for line in lines:
        if isinstance(line, dict):
            txt = line.get("text", "")
            bold = bool(line.get("bold", False))
            prev = renderer.font.get_bold()
            renderer.font.set_bold(bold)
            tw, _ = renderer.font.size(txt)
            renderer.font.set_bold(prev)
        else:
            tw, _ = renderer.font.size(str(line))
        max_w = max(max_w, tw)

    w = renderer.padding_x * 2 + max_w
    rows_h = (len(lines) or 1) * renderer.line_height + max(0, (len(lines) - 1)) * (renderer.item_gap - 2)
    rows_h += renderer.item_gap + renderer.line_height  # buttons row
    h = renderer.padding_y * 2 + rows_h

    sw, sh = screen.get_size()
    w = min(w, int(sw * 0.9))
    h = min(h, int(sh * 0.6))

    x = (sw - w) // 2
    y = (sh - h) // 2
    panel_rect = pygame.Rect(x, y, w, h)

    # buttons rects
    gap = max(16, renderer.item_gap)
    padding_btn_x = 16
    btn_h = renderer.line_height
    labels_w = [renderer.font.size(t)[0] for t in buttons]
    btn_ws = [lw + padding_btn_x * 2 for lw in labels_w]
    total_btn_w = sum(btn_ws) + gap * (len(buttons) - 1 if buttons else 0)

    if w < total_btn_w + renderer.padding_x * 2:
        w = min(total_btn_w + renderer.padding_x * 2, int(sw * 0.95))
        x = (sw - w) // 2
        panel_rect = pygame.Rect(x, y, w, h)

    start_x = x + (w - total_btn_w) // 2
    btn_y = y + h - renderer.padding_y - btn_h
    button_rects = []
    cx = start_x
    for bw in btn_ws:
        button_rects.append(pygame.Rect(cx, btn_y, bw, btn_h))
        cx += bw + gap

    if not redraw:
        return {"panel_rect": panel_rect, "button_rects": button_rects}

    # overlay
    overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
    overlay.fill(renderer.overlay_color)
    screen.blit(overlay, (0, 0))

    # shadow
    sx, sy = renderer.shadow_offset
    shadow_rect = panel_rect.move(sx, sy)
    shadow_surf = pygame.Surface((shadow_rect.width, shadow_rect.height), pygame.SRCALPHA)
    pygame.draw.rect(shadow_surf, (0, 0, 0, 110), shadow_surf.get_rect(), border_radius=renderer.radius + 2)
    screen.blit(shadow_surf, shadow_rect.topleft)

    # panel
    panel = pygame.Surface((w, h), pygame.SRCALPHA)
    color = (*renderer.panel_bg, renderer.panel_alpha)
    pygame.draw.rect(panel, color, panel.get_rect(), border_radius=renderer.radius)

    # text lines
    ty = renderer.padding_y
    for line in lines:
        if isinstance(line, dict):
            txt = line.get("text", "")
            color_line = line.get("color", renderer.text_color)
            bold = bool(line.get("bold", False))
            prev = renderer.font.get_bold()
            renderer.font.set_bold(bold)
            t = renderer.font.render(txt, True, color_line)
            renderer.font.set_bold(prev)
        else:
            t = renderer.font.render(str(line), True, renderer.text_color)
        ly = ty + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (renderer.padding_x, ly))
        ty += renderer.line_height + (renderer.item_gap - 2)

    # buttons paint
    for i, rect in enumerate(button_rects):
        label = buttons[i]
        if label == "CANCELAR":
            bg = (206, 64, 64)
            br = (240, 96, 96)
        elif label == "ACEPTAR":
            bg = (60, 160, 95)
            br = (100, 200, 140)
        elif label == "BORRAR":
            bg = (210, 170, 60)
            br = (240, 200, 90)
        else:
            bg = (50, 52, 58)
            br = (180, 185, 195)

        local_rect = pygame.Rect(rect.x - x, rect.y - y, rect.width, rect.height)
        if hover_index == i:
            glow = pygame.Surface((local_rect.width + 8, local_rect.height + 8), pygame.SRCALPHA)
            pygame.draw.rect(glow, (*br, 60), glow.get_rect(), border_radius=10)
            panel.blit(glow, (local_rect.x - 4, local_rect.y - 4))

        pygame.draw.rect(panel, bg, local_rect, border_radius=10)
        border_color = br
        pygame.draw.rect(panel, border_color, local_rect, width=2, border_radius=10)

        tt = renderer.font.render(label, True, (255, 255, 255))
        tx = local_rect.x + (local_rect.width - tt.get_width()) // 2
        ty2 = local_rect.y + (local_rect.height - tt.get_height()) // 2
        panel.blit(tt, (tx, ty2))

    screen.blit(panel, panel_rect.topleft)
    return {"panel_rect": panel_rect, "button_rects": button_rects}


def prompt_key(renderer, screen: pygame.Surface, config, action: str, *, slot: str = "keyboard_a") -> None:
    pretty = format_action_friendly(action, slot_hint=slot)
    buttons = ["BORRAR", "CANCELAR", "ACEPTAR"]
    hovered: Optional[int] = None
    candidate_value: Optional[str] = None
    candidate_label: Optional[str] = None

    waiting = True
    while waiting:
        for event in pygame.event.get():
            if event.type == pygame.KEYDOWN:
                key_const = key_const_from_code(event.key)
                candidate_value = key_const or f"K_{pygame.key.name(event.key).upper()}"
                candidate_label = format_key_label(candidate_value)
            elif event.type == pygame.MOUSEMOTION:
                lines = [f"Pulsa una tecla para {pretty}"]
                if candidate_label:
                    lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                    lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                layout = _draw_modal_with_buttons(renderer, screen, lines, buttons, redraw=False)
                hovered = None
                for idx, rect in enumerate(layout["button_rects"]):
                    if rect.collidepoint(event.pos):
                        hovered = idx
                        break
            elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                lines = [f"Pulsa una tecla para {pretty}"]
                if candidate_label:
                    lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                    lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                layout = _draw_modal_with_buttons(renderer, screen, lines, buttons, redraw=False)
                for idx, rect in enumerate(layout["button_rects"]):
                    if rect.collidepoint(event.pos):
                        label = buttons[idx]
                        if label == "BORRAR":
                            config.set_binding(action, "")
                            if hasattr(config, "save"):
                                config.save()
                            waiting = False
                        elif label == "CANCELAR":
                            waiting = False
                        elif label == "ACEPTAR":
                            if candidate_value:
                                config.set_key(action, candidate_value)
                                if hasattr(config, "save"):
                                    config.save()
                            waiting = False
                        break
            elif event.type == pygame.QUIT:
                waiting = False
                break

        lines = [f"Pulsa una tecla para {pretty}"]
        if candidate_label:
            lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
            lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
        _draw_modal_with_buttons(renderer, screen, lines, buttons, hover_index=hovered, redraw=True)
        pygame.display.flip()


def prompt_mouse(renderer, screen: pygame.Surface, config, action: str) -> None:
    pretty = action.replace("_", " ").title()
    buttons = ["BORRAR", "CANCELAR", "ACEPTAR"]
    hovered: Optional[int] = None
    candidate_value: Optional[str] = None
    candidate_label: Optional[str] = None

    waiting = True
    while waiting:
        for event in pygame.event.get():
            if event.type == pygame.MOUSEMOTION:
                lines = [f"Haz click para asignar ratón a {pretty}"]
                if candidate_label:
                    lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                    lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                layout = _draw_modal_with_buttons(renderer, screen, lines, buttons, redraw=False)
                hovered = None
                for idx, rect in enumerate(layout["button_rects"]):
                    if rect.collidepoint(event.pos):
                        hovered = idx
                        break
            if event.type == pygame.MOUSEBUTTONDOWN:
                btn = event.button
                if btn == 1:
                    # check modal buttons first
                    lines = [f"Haz click para asignar ratón a {pretty}"]
                    if candidate_label:
                        lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                        lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                    layout = _draw_modal_with_buttons(renderer, screen, lines, buttons, redraw=False)
                    clicked = None
                    for idx, rect in enumerate(layout["button_rects"]):
                        if rect.collidepoint(event.pos):
                            clicked = buttons[idx]
                            break
                    if clicked == "BORRAR":
                        config.set_binding(action, "")
                        if hasattr(config, "save"):
                            config.save()
                        waiting = False
                        _draw_modal_with_buttons(renderer, screen, lines, buttons, hover_index=hovered, redraw=True)
                        pygame.display.flip()
                        continue
                    elif clicked == "CANCELAR":
                        waiting = False
                        _draw_modal_with_buttons(renderer, screen, lines, buttons, hover_index=hovered, redraw=True)
                        pygame.display.flip()
                        continue
                    elif clicked == "ACEPTAR":
                        if candidate_value:
                            config.set_key(action, candidate_value)
                            if hasattr(config, "save"):
                                config.save()
                        waiting = False
                        _draw_modal_with_buttons(renderer, screen, lines, buttons, hover_index=hovered, redraw=True)
                        pygame.display.flip()
                        continue
                # assign mouse button
                mname = None
                if btn == 1:
                    mname = "M_LEFT"
                elif btn == 2:
                    mname = "M_MIDDLE"
                elif btn == 3:
                    mname = "M_RIGHT"
                elif btn == 8:
                    mname = "M_X1"
                elif btn == 9:
                    mname = "M_X2"
                elif btn in (4, 5, 6, 7):
                    flash_message(renderer, screen, ["La rueda del ratón no es asignable"], ms=500)
                    continue
                if mname:
                    candidate_value = mname
                    candidate_label = format_key_label(mname)
            elif event.type == pygame.QUIT:
                waiting = False
                break

        lines = [f"Haz click para asignar ratón a {pretty}"]
        if candidate_label:
            lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
            lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
        _draw_modal_with_buttons(renderer, screen, lines, buttons, hover_index=hovered, redraw=True)
        pygame.display.flip()
