from __future__ import annotations

try:
    import pygame  # type: ignore
except Exception:  # pragma: no cover
    pygame = None  # type: ignore

# Basic palette for visuals controls (subtle, readable on dark bg)
COLOR_BTN_BG = (60, 60, 60)
COLOR_BTN_BORDER = (150, 150, 150)
COLOR_FOLDER_FILL = (230, 200, 120)
COLOR_FOLDER_BORDER = (160, 130, 60)
COLOR_EYE = (220, 220, 220)
COLOR_EYE_OFF = (120, 120, 120)
COLOR_CLEAR = (220, 120, 120)


def _draw_button_frame(surf: "pygame.Surface", rect: "pygame.Rect") -> None:
    pygame.draw.rect(surf, COLOR_BTN_BG, rect)
    pygame.draw.rect(surf, COLOR_BTN_BORDER, rect, 1)


def draw_folder_button(surf: "pygame.Surface", rect: "pygame.Rect") -> None:
    """Draw a small folder button inside the given rect."""
    if pygame is None:
        return
    _draw_button_frame(surf, rect)
    bx, by = rect.x + 3, rect.y + 3
    inner = (bx, by + 4, rect.w - 6, rect.h - 8)
    pygame.draw.rect(surf, COLOR_FOLDER_FILL, inner, 0)
    pygame.draw.rect(surf, COLOR_FOLDER_BORDER, inner, 1)
    # Folder tab
    pygame.draw.rect(surf, COLOR_FOLDER_FILL, (bx + 2, by + 2, 8, 6), 0)


def draw_eye_button(surf: "pygame.Surface", rect: "pygame.Rect", visible: bool = True) -> None:
    """Draw an eye toggle button; if visible is False, show it crossed out."""
    if pygame is None:
        return
    _draw_button_frame(surf, rect)
    # Eye outline
    pygame.draw.ellipse(surf, COLOR_EYE, (rect.x + 3, rect.y + 4, rect.w - 6, rect.h - 8), 1)
    # Pupil
    ex, ey = rect.centerx, rect.centery
    pygame.draw.circle(surf, COLOR_EYE if visible else COLOR_EYE_OFF, (ex, ey), 3)
    # Cross line when hidden
    if not visible:
        pygame.draw.line(surf, (200, 80, 80), (rect.left + 3, rect.bottom - 3), (rect.right - 3, rect.top + 3), 2)


def draw_clear_button(surf: "pygame.Surface", rect: "pygame.Rect") -> None:
    """Draw a clear (X) button inside the given rect."""
    if pygame is None:
        return
    _draw_button_frame(surf, rect)
    cx, cy = rect.centerx, rect.centery
    pygame.draw.line(surf, COLOR_CLEAR, (cx - 4, cy - 4), (cx + 4, cy + 4), 2)
    pygame.draw.line(surf, COLOR_CLEAR, (cx - 4, cy + 4), (cx + 4, cy - 4), 2)


__all__ = [
    "draw_folder_button",
    "draw_eye_button",
    "draw_clear_button",
]
