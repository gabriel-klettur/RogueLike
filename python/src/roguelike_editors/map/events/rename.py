from __future__ import annotations

import logging
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.buildings.utils.save_buildings_to_json import save_buildings_split

logger = logging.getLogger(__name__)


def handle_renaming_keys(ev: pygame.event.Event, state, controller, manager) -> bool:
    if ev.key == pygame.K_RETURN:
        old_zone = state.renaming_zone
        new_name = state.rename_input.strip()
        logger.debug(f"[MapEditor] renaming (Enter) {old_zone} -> {new_name}")
        success = controller.rename_zone(old_zone, new_name)
        if success:
            for b in manager.game.buildings.buildings:
                if getattr(b, "zone", None) == old_zone:
                    b.zone = new_name
                    logger.debug(
                        f"[MapEditor] building {b} zone updated from {old_zone} to {new_name}"
                    )
            save_buildings_split(
                manager.game.buildings.buildings,
                z_state=manager.game.z_state,
                zone_offsets=global_map_settings.zone_offsets,
            )
            logger.debug("[MapEditor] persisted buildings split files after rename")
            state.selected_zone = new_name
        else:
            logger.info(f"[MapEditor] rename aborted for {old_zone} -> {new_name}")
        state.renaming_zone = None
        state.rename_input = ""
        pygame.key.set_repeat()
        return True

    if ev.key == pygame.K_BACKSPACE:
        state.rename_input = state.rename_input[:-1]
        return True

    if getattr(ev, "unicode", "") and ev.unicode.isprintable():
        state.rename_input += ev.unicode
        return True

    return False


def handle_renaming_click(ev: pygame.event.Event, state, controller, manager) -> bool:
    if state.rename_accept_rect and state.rename_accept_rect.collidepoint(ev.pos):
        old_zone = state.renaming_zone
        new_name = state.rename_input.strip()
        logger.debug(f"[MapEditor] accept rename click {old_zone} -> {new_name}")
        success = controller.rename_zone(old_zone, new_name)
        if success:
            for b in manager.game.buildings.buildings:
                if getattr(b, "zone", None) == old_zone:
                    b.zone = new_name
                    logger.debug(
                        f"[MapEditor] building {b} zone updated from {old_zone} to {new_name}"
                    )
            save_buildings_split(
                manager.game.buildings.buildings,
                z_state=manager.game.z_state,
                zone_offsets=global_map_settings.zone_offsets,
            )
            logger.debug("[MapEditor] persisted buildings split files after rename")
            state.selected_zone = new_name
        else:
            logger.info(f"[MapEditor] rename aborted for {old_zone} -> {new_name}")
    state.renaming_zone = None
    state.rename_input = ""
    state.rename_input_rect = None
    state.rename_accept_rect = None
    pygame.key.set_repeat()
    return True
