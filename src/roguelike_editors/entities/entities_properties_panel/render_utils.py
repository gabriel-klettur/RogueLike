import pygame
from pygame import Surface
from roguelike_ui.widgets.hover import draw_hover


def draw_background(screen: Surface, x: int, y: int, w: int, h: int) -> None:
    info_surf = pygame.Surface((w, h), pygame.SRCALPHA)
    info_surf.fill((0, 0, 0, 200))
    screen.blit(info_surf, (x, y))


def truncate_text(font: pygame.font.Font, text: str, max_width: int) -> str:
    if font.size(text)[0] <= max_width:
        return text
    text = text.rstrip()
    while font.size(text + "...")[0] > max_width and text:
        text = text[:-1]
    return text + "..."


def draw_editing_indicator(
    screen: Surface,
    font: pygame.font.Font,
    model,
    blink_interval: int,
    font_h: int,
) -> None:
    if model.editing_property:
        for rect, key in model.property_entries:
            if key == model.editing_property:
                er = rect.inflate(4, 0)
                pygame.draw.rect(screen, (128, 0, 128), er, 2)
                t = pygame.time.get_ticks()
                if (t % blink_interval) < (blink_interval // 2):
                    prefix = f"{key}: "
                    caret_x = er.x + font.size(prefix + model.editing_text[: model.editing_cursor])[0]
                    caret_y = er.y
                    pygame.draw.line(
                        screen, (255, 255, 255), (caret_x, caret_y), (caret_x, caret_y + font_h), 2
                    )
                break
    elif model.focused_property:
        for rect, key in model.property_entries:
            if key == model.focused_property:
                hl_rect = rect.inflate(4, 0)
                pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                break


def draw_confirm_button(
    screen: Surface,
    font: pygame.font.Font,
    model,
    px: int,
    py: int,
    panel_w: int,
    panel_h: int,
    pad: int,
    font_h: int,
) -> None:
    btn_text = "Confirm"
    text_surf = font.render(btn_text, True, (255, 255, 255))
    btn_h = font_h + 6
    btn_w = panel_w - pad * 2
    btn_x = px + pad
    btn_y = py + panel_h - pad - btn_h
    rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
    pygame.draw.rect(screen, (0, 140, 0), rect)
    pygame.draw.rect(screen, (255, 255, 255), rect, 2)
    tx = rect.x + (rect.w - text_surf.get_width()) // 2
    ty = rect.y + (rect.h - text_surf.get_height()) // 2
    screen.blit(text_surf, (tx, ty))
    model.confirm_button_rect = rect


def draw_entity_type_selector(
    screen: Surface,
    font: pygame.font.Font,
    model,
    px: int,
    py: int,
    pad: int,
    font_h: int,
    panel_w: int,
) -> int:
    tx = px + pad
    ty = py + pad
    label = "Type of Entity"
    label_surf = font.render(label, True, (255, 255, 255))
    screen.blit(label_surf, (tx, ty))
    value = getattr(model, "add_system_entity_type", "Hostile")
    value_text = str(value)
    value_surf = font.render(value_text, True, (0, 0, 0))
    cb_pad_x = 8
    cb_w = max(120, value_surf.get_width() + cb_pad_x * 2)
    cb_h = font_h + 6
    cb_x = tx + label_surf.get_width() + pad
    cb_y = ty - 2
    rect = pygame.Rect(cb_x, cb_y, min(cb_w, panel_w - (cb_x - px) - pad), cb_h)
    pygame.draw.rect(screen, (200, 200, 200), rect)
    pygame.draw.rect(screen, (255, 255, 255), rect, 2)
    text_x = rect.x + (rect.w - value_surf.get_width()) // 2
    text_y = rect.y + (rect.h - value_surf.get_height()) // 2
    screen.blit(value_surf, (text_x, text_y))
    model.entity_type_rect = rect
    consumed_h = max(label_surf.get_height(), rect.h) + pad
    return consumed_h


def draw_properties(
    screen: Surface,
    font: pygame.font.Font,
    model,
    lines: list[str],
    px: int,
    py: int,
    pad: int,
    font_h: int,
    panel_w: int,
) -> None:
    content_start_y = py + pad
    prev_clip = screen.get_clip()
    screen.set_clip(pygame.Rect(px + pad, content_start_y, panel_w - pad * 2, model.available_height))
    tx = px + pad
    ty = py + pad - model.scroll_offset
    model.property_entries.clear()

    for i, line in enumerate(lines):
        is_header = (i == 0) and (": " not in line)
        if is_header:
            text = truncate_text(font, line, panel_w - pad * 2)
            txt_surf = font.render(text, True, (255, 255, 0))
            screen.blit(txt_surf, (tx, ty))
            ty += font_h + 2
            continue
        parts = line.split(": ", 1)
        if len(parts) != 2:
            continue
        key, val_str = parts[0], parts[1]
        key_text = f"{key}: "
        key_surf = font.render(truncate_text(font, key_text, panel_w - pad * 2), True, (255, 255, 255))
        color = (128, 0, 128) if val_str == "None" else (255, 255, 0)
        val_surf = font.render(
            truncate_text(font, val_str, panel_w - pad * 2 - key_surf.get_width()), True, color
        )
        rect = pygame.Rect(tx, ty, key_surf.get_width() + val_surf.get_width(), font_h)
        model.property_entries.append((rect, key))
        if key == model.hovered_property:
            draw_hover(screen, rect)
        screen.blit(key_surf, (tx, ty))
        screen.blit(val_surf, (tx + key_surf.get_width(), ty))
        ty += font_h + 2

    screen.set_clip(prev_clip)
