from pygame import Surface, Rect
import pygame

from .fonts import Fonts
from .colors import Palette


class DialogsView:
    """Centralized rendering for confirmation dialogs."""

    def __init__(self, fonts: Fonts, palette: Palette) -> None:
        self.fonts = fonts
        self.palette = palette

    def render(self, screen: Surface, state) -> None:
        if state.confirm_delete_zone and state.pending_delete_zone:
            self._draw_generic_dialog(
                screen,
                f"Eliminar zona {state.pending_delete_zone}?",
                yes_callback_attr="confirm_yes_rect",
                no_callback_attr="confirm_no_rect",
                state=state,
            )

        if state.confirm_paint_tiles and state.pending_paint_tiles_zone:
            self._draw_generic_dialog(
                screen,
                f"Pintar tiles de zona {state.pending_paint_tiles_zone}?",
                yes_callback_attr="confirm_paint_yes_rect",
                no_callback_attr="confirm_paint_no_rect",
                state=state,
            )

        if state.confirm_clear_colliders and state.pending_clear_colliders_zone:
            self._draw_generic_dialog(
                screen,
                f"Vaciar colliders de zona {state.pending_clear_colliders_zone}?",
                yes_callback_attr="confirm_clear_colliders_yes_rect",
                no_callback_attr="confirm_clear_colliders_no_rect",
                state=state,
            )

        if state.confirm_paint_colliders and state.pending_paint_colliders_zone:
            self._draw_generic_dialog(
                screen,
                f"Pintar colliders de zona {state.pending_paint_colliders_zone}?",
                yes_callback_attr="confirm_paint_colliders_yes_rect",
                no_callback_attr="confirm_paint_colliders_no_rect",
                state=state,
            )

        if state.confirm_add_zone and state.pending_add_zone_coords:
            tx, ty = state.pending_add_zone_coords
            self._draw_generic_dialog(
                screen,
                f"Agregar zona en ({tx},{ty})?",
                yes_callback_attr="confirm_add_yes_rect",
                no_callback_attr="confirm_add_no_rect",
                state=state,
            )

    def _draw_generic_dialog(
        self, screen: Surface, message: str, yes_callback_attr: str, no_callback_attr: str, state
    ) -> None:
        sw, sh = screen.get_size()
        text_surf = self.fonts.medium.render(message, True, self.palette.text)
        box_w = text_surf.get_width() + 20
        box_h = text_surf.get_height() + 60
        box_x = (sw - box_w) // 2
        box_y = (sh - box_h) // 2
        box_rect = Rect(box_x, box_y, box_w, box_h)

        pygame.draw.rect(screen, self.palette.dialog_bg, box_rect)
        pygame.draw.rect(screen, self.palette.dialog_border, box_rect, 2)
        screen.blit(text_surf, (box_x + 10, box_y + 10))

        yes_w, yes_h = 60, 30
        yes_x = box_x + 10
        yes_y = box_y + box_h - yes_h - 10
        yes_rect = Rect(yes_x, yes_y, yes_w, yes_h)
        pygame.draw.rect(screen, self.palette.yes_bg, yes_rect)
        pygame.draw.rect(screen, self.palette.dialog_border, yes_rect, 2)
        yes_surf = self.fonts.medium.render("Sí", True, self.palette.text)
        screen.blit(yes_surf, (yes_rect.centerx - yes_surf.get_width() // 2, yes_rect.centery - yes_surf.get_height() // 2))
        setattr(state, yes_callback_attr, yes_rect)

        no_w, no_h = 60, 30
        no_x = yes_rect.right + 10
        no_y = yes_y
        no_rect = Rect(no_x, no_y, no_w, no_h)
        pygame.draw.rect(screen, self.palette.no_bg, no_rect)
        pygame.draw.rect(screen, self.palette.dialog_border, no_rect, 2)
        no_surf = self.fonts.medium.render("No", True, self.palette.text)
        screen.blit(no_surf, (no_rect.centerx - no_surf.get_width() // 2, no_rect.centery - no_surf.get_height() // 2))
        setattr(state, no_callback_attr, no_rect)
